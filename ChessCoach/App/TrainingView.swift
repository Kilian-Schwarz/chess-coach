import SwiftUI

@MainActor
final class TrainingSession: ObservableObject {
    enum Phase: Equatable { case asking, correct, wrong }

    let app: AppModel
    @Published var queue: [Puzzle] = []
    @Published var index = 0
    @Published var phase = Phase.asking
    @Published var hintLevel = 0
    @Published var hints: HintSet?
    @Published var hintBusy = false
    @Published var ideaText: String?
    @Published var current: ChessPosition?
    @Published var retry = false
    @Published var note: String?
    @Published var refutation: String?
    @Published var replying = false
    @Published var failReason: String?
    @Published var lessons: [String: PuzzleLesson] = [:]
    @Published var solved = 0
    @Published var attempted = 0
    private var ideaTask: Task<Void, Never>?
    private var prepared = Set<String>()
    private var upgraded = Set<String>()
    private var loadedFor: String?
    private var recorded = false
    private var nudged = false
    private var shownAt = Date()
    private var ply = 0
    static let maxPlies = 4

    init(app: AppModel) { self.app = app }

    var store: LearningStore { app.store }
    var profile: PlayerProfile { store.active }
    var puzzle: Puzzle? { queue.indices.contains(index) ? queue[index] : nil }
    var position: ChessPosition? { puzzle.flatMap { ChessPosition(fen: $0.fen) } }
    var lesson: PuzzleLesson? { puzzle.flatMap { lessons[$0.id] } }

    func load(force: Bool = false) {
        guard force || loadedFor != store.activeId else { return }
        loadedFor = store.activeId
        let set = store.puzzles(for: store.activeId).puzzles
        queue = PuzzleScheduler.todaysSelection(for: profile, puzzles: set, priority: app.trainingPriority)
        app.trainingPriority = []
        index = 0
        solved = 0
        attempted = 0
        resetPuzzleState()
    }

    private func resetPuzzleState() {
        phase = .asking
        hintLevel = 0
        hints = nil
        ideaText = nil
        note = nil
        refutation = nil
        failReason = nil
        current = position
        retry = false
        replying = false
        recorded = false
        nudged = false
        shownAt = Date()
        ply = 0
        ideaTask?.cancel()
        prepareLessons()
    }

    var stats: PuzzleStats { PuzzleScheduler.stats(store.puzzles(for: store.activeId).puzzles) }
    var themeProgress: [ThemeProgress] { PuzzleCoach.progress(store.puzzles(for: store.activeId).puzzles) }

    private func prepareLessons() {
        for i in [index, index + 1] where queue.indices.contains(i) {
            let p = queue[i]
            guard prepared.insert(p.id).inserted else { continue }
            lessons[p.id] = PuzzleCoach.lesson(p)
            Task { await refine(p.id) }
        }
    }

    private func currentPuzzle(_ id: String) -> Puzzle? {
        store.puzzles(for: store.activeId).puzzles.first { $0.id == id } ?? queue.first { $0.id == id }
    }

    private func refine(_ id: String) async {
        guard var p = currentPuzzle(id) else { return }
        if PuzzleCoach.needsThemeCheck(p), let result = await app.engineAnalysis(fen: p.fen, ms: 300),
           let theme = PuzzleCoach.classify(p, result: result) {
            applyTheme(theme, to: id)
            guard let fresh = currentPuzzle(id) else { return }
            p = fresh
            lessons[id] = PuzzleCoach.lesson(p)
        }
        guard app.llmConfigured, upgraded.insert(id).inserted,
              let request = PuzzleCoach.whyRequest(p, context: profile.promptContext, style: profile.nextExplanationStyle().style, language: Language.englishName),
              let raw = try? await app.completeText(request, onText: { _ in }), let pos = ChessPosition(fen: p.fen) else { return }
        let checked = FactCheck.clean(Coach.cleanAnswer(raw), allowed: FactCheck.allowed(positions: [pos], lines: [p.bestLine]))
        guard let text = checked.text, !text.isEmpty, checked.removed == 0, var lesson = lessons[id] else { return }
        lesson.why = text
        lessons[id] = lesson
    }

    private func applyTheme(_ theme: HintTheme, to id: String) {
        store.updatePuzzles(for: store.activeId) { set in
            if let i = set.puzzles.firstIndex(where: { $0.id == id }) { set.puzzles[i].theme = theme.rawValue; set.puzzles[i].themeChecked = true }
        }
        if let i = queue.firstIndex(where: { $0.id == id }) { queue[i].theme = theme.rawValue; queue[i].themeChecked = true }
    }

    private func record(_ uci: String, hintLevel level: Int) -> Bool {
        guard var p = puzzle else { return false }
        let pid = store.activeId
        let correct = p.attempt(move: uci, hintLevel: level, learningSpeed: profile.learningSpeed)
        queue[index] = p
        store.updatePuzzles(for: pid) { set in
            if let i = set.puzzles.firstIndex(where: { $0.id == p.id }) { set.puzzles[i] = p }
        }
        let all = store.puzzles(for: pid).puzzles
        let explained = store.explainedStyle(profileId: pid, theme: p.theme)
        store.update(pid) { prof in
            prof.adjustLearningSpeed(puzzles: all)
            if let style = explained { prof.recordPuzzleAfterExplanation(usedStyle: style, correct: correct, assisted: level > 0) }
        }
        recorded = true
        attempted += 1
        if correct && level == 0 { solved += 1 }
        return correct
    }

    func submit(_ move: UCIMove) {
        Task { await process(move) }
    }

    private func process(_ move: UCIMove) async {
        guard phase == .asking, !replying, let pos = current, pos.isLegal(move), let p = puzzle else { return }
        replying = true
        var ok: Bool
        if ply == 0 {
            ok = p.accepts(move.uci)
            if !ok, await app.acceptsAlternative(puzzle: p, move: move),
               let fresh = store.puzzles(for: store.activeId).puzzles.first(where: { $0.id == p.id }) {
                queue[index] = fresh
                ok = true
            }
            if !recorded { _ = record(move.uci, hintLevel: hintLevel) }
        } else {
            ok = move.uci == p.bestLine[ply]
        }
        guard ok else {
            retry = true
            await refute(pos.applying(move), puzzle: p)
            replying = false
            return
        }
        retry = false
        refutation = nil
        failReason = nil
        current = pos.applying(move)
        let followsLine = ply > 0 || move.uci == p.bestLine.first
        if followsLine, ply + 2 < TrainingSession.maxPlies, p.bestLine.indices.contains(ply + 2),
           let reply = UCIMove(p.bestLine[ply + 1]), current?.isLegal(reply) == true {
            replyAndContinue(reply)
        } else {
            replying = false
            phase = .correct
        }
    }

    private func refute(_ after: ChessPosition, puzzle p: Puzzle) async {
        refutation = nil
        failReason = nil
        guard ply == 0, let result = await app.engineAnalysis(fen: after.fen, ms: 300), let pv = result.lines.first?.pv, !pv.isEmpty else { return }
        refutation = after.board.describeLine(pv)
        failReason = PuzzleCoach.failReason(p, after: after, refutation: pv)
    }

    private func replyAndContinue(_ reply: UCIMove) {
        hints = nil
        hintLevel = 0
        note = nil
        ideaTask?.cancel()
        Task {
            try? await Task.sleep(for: .milliseconds(500))
            guard replying, let pos = current else { return }
            current = pos.applying(reply)
            ply += 2
            replying = false
        }
    }

    func showSolution() {
        guard phase == .asking else { return }
        if !recorded { _ = record("", hintLevel: 4) }
        replying = false
        current = position
        retry = false
        refutation = nil
        failReason = nil
        phase = .wrong
    }

    func next() {
        index += 1
        resetPuzzleState()
    }

    func hint() async {
        guard phase == .asking, !replying, let pos = current, hintLevel < 4 else { return }
        if Curriculum.needsThinkDelay(level: hintLevel, elapsed: Date().timeIntervalSince(shownAt),
                                      seconds: app.adaptation(for: profile.id).thinkSeconds, nudged: nudged) {
            nudged = true
            note = String(localized: "Take a moment to think, then press again.")
            return
        }
        note = nil
        if hints == nil {
            hintBusy = true
            defer { hintBusy = false }
            guard var result = await app.engineAnalysis(fen: pos.fen, ms: 600) else { return }
            if ply == 0, let p = puzzle {
                if let i = result.lines.firstIndex(where: { p.accepts($0.move) }) {
                    var lines = result.lines
                    let line = lines.remove(at: i)
                    lines.insert(line, at: 0)
                    result = EngineResult(fen: result.fen, bestMove: line.move, lines: lines)
                } else if let first = p.acceptedMoves.first {
                    let line = EngineLine(move: first, scoreCP: result.lines.first?.scoreCP, mateIn: result.lines.first?.mateIn, pv: [first])
                    result = EngineResult(fen: result.fen, bestMove: first, lines: [line] + result.lines)
                }
            }
            hints = Hints.build(placement: pos.board, player: pos.turn, result: result, themeOverride: ply == 0 ? puzzle.map(PuzzleCoach.theme) : nil)
        }
        guard let hints else { return }
        hintLevel = hintLevel == 0 ? Curriculum.firstHintLevel : hintLevel + 1
        if hintLevel == 3 { startIdea(hints, position: pos) }
    }

    private func startIdea(_ h: HintSet, position pos: ChessPosition) {
        let fallback = PuzzleCoach.hint3(h, position: pos)
        ideaText = fallback
        ideaTask?.cancel()
        guard app.llmConfigured else { return }
        let allowed = FactCheck.allowed(positions: [pos], lines: [[h.move.uci]])
        let level = hintLevel
        ideaTask = app.stream(Coach.hintRequest(h, context: profile.promptContext, language: Language.englishName), update: { [weak self] text, done in
            guard let self, self.hintLevel == level, self.hints?.move == h.move else { return }
            if !done {
                if !text.isEmpty, !Hints.revealsTarget(text, move: h.move) { self.ideaText = text }
                return
            }
            let checked = Hints.revealsTarget(text, move: h.move) ? nil : FactCheck.clean(text, allowed: allowed).text
            self.ideaText = checked ?? fallback
        }, failed: { _ in })
    }

    var solutionText: String {
        guard let p = puzzle, let pos = position, let first = p.acceptedMoves.first, let m = UCIMove(first) else { return "" }
        return pos.board.describeMove(m)
    }
}

struct TrainingView: View {
    @ObservedObject var app: AppModel
    @ObservedObject var store: LearningStore
    @StateObject private var session: TrainingSession

    init(app: AppModel) {
        self.app = app
        self.store = app.store
        _session = StateObject(wrappedValue: TrainingSession(app: app))
    }

    var body: some View {
        Group {
            if let puzzle = session.puzzle, let start = session.position {
                HStack(alignment: .top, spacing: 24) {
                    GeometryReader { geo in
                        let side = min(geo.size.width, geo.size.height)
                        board(puzzle, start)
                            .frame(width: side, height: side)
                            .frame(width: geo.size.width, height: geo.size.height)
                    }
                    sidebar(start)
                        .frame(width: 300)
                }
                .padding(20)
            } else {
                empty(finished: !session.queue.isEmpty)
            }
        }
        .frame(minWidth: 800, minHeight: 580)
        .navigationTitle("Training")
        .onAppear { session.load() }
        .onChange(of: store.activeId) { _, _ in session.load() }
        .onChange(of: app.trainingPriority) { _, ids in if !ids.isEmpty { session.load(force: true) } }
    }

    private func board(_ puzzle: Puzzle, _ start: ChessPosition) -> some View {
        let pos = session.current ?? start
        let moves = pos.legalMoves()
        let hinted = session.hints.map(\.move)
        let highlight: (file: Int, rank: Int)? = (session.hintLevel == 2 || session.hintLevel == 3) ? hinted.map { ($0.fromFile, $0.fromRank) } : nil
        let shown: UCIMove? = session.phase == .wrong ? puzzle.acceptedMoves.first.flatMap { UCIMove($0) } : (session.hintLevel >= 4 ? hinted : nil)
        return ChessBoardView(
            placement: pos.board,
            whiteAtBottom: start.turn == .white,
            highlight: highlight,
            arrow: shown.map { (from: ($0.fromFile, $0.fromRank), to: ($0.toFile, $0.toRank)) },
            interactive: session.phase == .asking && !session.replying,
            onMove: { session.submit($0) },
            legal: { ff, fr, tf, tr in moves.contains { $0.fromFile == ff && $0.fromRank == fr && $0.toFile == tf && $0.toRank == tr } },
            canSelect: { f, r in moves.contains { $0.fromFile == f && $0.fromRank == r } })
            .id(session.index)
    }

    private func sidebar(_ start: ChessPosition) -> some View {
        VStack(alignment: .leading, spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Task \(session.index + 1) of \(session.queue.count)").font(.title2.bold())
                if let p = session.puzzle, session.phase != .asking { Text(PuzzleCoach.theme(p).title()).font(.caption.bold()).foregroundStyle(.tint) }
                Text(start.turn == .white ? "Find the best move for White." : "Find the best move for Black.")
                if let p = session.puzzle, session.phase == .asking {
                    Text(PuzzleCoach.question(PuzzleCoach.theme(p)))
                    Text(PuzzleCoach.source(p)).font(.callout).foregroundStyle(.secondary)
                }
                Text("Click or drag a piece to play your move.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            ScrollView { feedback.frame(maxWidth: .infinity, alignment: .leading) }
            Spacer(minLength: 0)
            if session.phase == .asking { actions } else {
                Button("Next task") { session.next() }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .keyboardShortcut(.defaultAction)
            }
            Divider()
            statsView
        }
    }

    @ViewBuilder
    private var feedback: some View {
        switch session.phase {
        case .asking:
            VStack(alignment: .leading, spacing: 8) {
                if session.retry {
                    Label("Not quite, try again.", systemImage: "arrow.uturn.backward.circle.fill").foregroundStyle(.orange).font(.headline)
                }
                if let r = session.refutation { Text("After that move: \(r)").font(.callout).foregroundStyle(.secondary) }
                if let f = session.failReason { Text(f).font(.callout) }
                hintText
            }
        case .correct:
            VStack(alignment: .leading, spacing: 8) {
                Label("Correct", systemImage: "checkmark.circle.fill").foregroundStyle(.green).font(.title3)
                lessonView
                if session.hintLevel > 0 { Text("You used a hint, so the task comes back at the same interval.").font(.callout).foregroundStyle(.secondary) }
            }
        case .wrong:
            VStack(alignment: .leading, spacing: 8) {
                Label("Not quite", systemImage: "xmark.circle.fill").foregroundStyle(.orange).font(.title3)
                Text("Better was: \(session.solutionText)")
                lessonView
                Text("The task starts again tomorrow.").font(.callout).foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder
    private var lessonView: some View {
        if let l = session.lesson {
            VStack(alignment: .leading, spacing: 4) {
                Text("Why it works").font(.headline)
                if l.why.isEmpty { ProgressView().controlSize(.small) } else { Text(l.why) }
                Text("Habit to remember").font(.headline).padding(.top, 4)
                Text(l.habit)
            }
        }
    }

    @ViewBuilder
    private var hintText: some View {
        if let h = session.hints, session.hintLevel > 0 {
            VStack(alignment: .leading, spacing: 4) {
                Text("Hint \(session.hintLevel) of 4").font(.caption).foregroundStyle(.secondary)
                switch session.hintLevel {
                case 1: Text(h.stage1)
                case 2: Text(h.stage2)
                case 3:
                    if let t = session.ideaText { Text(t) } else { ProgressView().controlSize(.small) }
                default: Text(h.stage4)
                }
            }
        }
    }

    private var actions: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button {
                Task { await session.hint() }
            } label: {
                if session.hintBusy { ProgressView().controlSize(.small) } else { Text("Show hint") }
            }
            .disabled(session.hintLevel >= 4 || session.hintBusy || session.replying)
            if let n = session.note { Text(n).font(.callout).foregroundStyle(.secondary) }
            HStack {
                Button("Skip") { session.next() }
                Button("Show solution") { session.showSolution() }
            }
        }
    }

    private var statsView: some View {
        let s = session.stats
        let p = session.profile
        return VStack(alignment: .leading, spacing: 4) {
            Text("Solved today: \(session.solved) of \(session.attempted)")
            Text("Mastered: \(s.mastered) of \(s.total)")
            if let q = s.recentSuccessRate { Text("Recent success rate: \(Int((q * 100).rounded())) % (goal 70–85 %)") }
            Text("Learning pace: ×\(p.learningSpeed, specifier: "%.1f")")
            ForEach(session.themeProgress.prefix(4), id: \.theme) { Text($0.text()) }
        }
        .font(.callout)
        .foregroundStyle(.secondary)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder
    private func empty(finished: Bool) -> some View {
        let s = session.stats
        VStack(spacing: 16) {
            if finished {
                ContentUnavailableView {
                    Label("Done for today", systemImage: "checkmark.seal")
                } description: {
                    Text("You solved \(session.solved) of \(session.attempted) tasks. Learning pace: ×\(session.profile.learningSpeed, specifier: "%.1f").")
                }
            } else {
                ContentUnavailableView {
                    Label("No tasks due", systemImage: "tray")
                } description: {
                    Text(s.total == 0 ? "Mistakes from your games become tasks here." : "Everything due is done. \(s.mastered) of \(s.total) tasks mastered.")
                }
            }
            Button("Reload") { session.load(force: true) }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
