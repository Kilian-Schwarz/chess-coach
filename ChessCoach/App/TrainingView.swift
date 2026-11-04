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
    @Published var shown: Placement?
    @Published var solved = 0
    @Published var attempted = 0
    private var ideaTask: Task<Void, Never>?
    private var loadedFor: String?

    init(app: AppModel) { self.app = app }

    var store: LearningStore { app.store }
    var profile: PlayerProfile { store.active }
    var puzzle: Puzzle? { queue.indices.contains(index) ? queue[index] : nil }
    var position: ChessPosition? { puzzle.flatMap { ChessPosition(fen: $0.fen) } }

    func load(force: Bool = false) {
        guard force || loadedFor != store.activeId else { return }
        loadedFor = store.activeId
        let set = store.puzzles(for: store.activeId).puzzles
        queue = PuzzleScheduler.todaysSelection(set, focus: profile.focusSkill)
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
        shown = nil
        ideaTask?.cancel()
    }

    var stats: PuzzleStats { PuzzleScheduler.stats(store.puzzles(for: store.activeId).puzzles) }

    var defaultHint: Int {
        let p = profile
        let rate = PuzzleScheduler.recentSuccessRate(store.puzzles(for: p.id).puzzles)
        return Curriculum.adaptation(level: p.level, focus: p.focusSkill, skills: p.skills, successRate: rate).defaultHintLevel
    }

    func submit(_ move: UCIMove) {
        guard phase == .asking, var p = puzzle, let pos = position, pos.isLegal(move) else { return }
        let pid = store.activeId
        let correct = p.attempt(move: move.uci, hintLevel: hintLevel, learningSpeed: profile.learningSpeed)
        queue[index] = p
        store.updatePuzzles(for: pid) { set in
            if let i = set.puzzles.firstIndex(where: { $0.id == p.id }) { set.puzzles[i] = p }
        }
        let all = store.puzzles(for: pid).puzzles
        let explained = store.explainedStyle(profileId: pid, theme: p.theme)
        store.update(pid) { prof in
            prof.learningSpeed = PuzzleScheduler.adjustedLearningSpeed(current: prof.learningSpeed, puzzles: all)
            if let style = explained { prof.recordPuzzleAfterExplanation(usedStyle: style, correct: correct) }
        }
        attempted += 1
        if correct { solved += 1; shown = pos.applying(move).board }
        phase = correct ? .correct : .wrong
    }

    func next() {
        index += 1
        resetPuzzleState()
    }

    func hint() async {
        guard phase == .asking, let p = puzzle, let pos = position, hintLevel < 4 else { return }
        if hints == nil {
            hintBusy = true
            defer { hintBusy = false }
            guard var result = await app.engineAnalysis(fen: p.fen, ms: 600) else { return }
            if let i = result.lines.firstIndex(where: { p.accepts($0.move) }), i > 0 {
                var lines = result.lines
                let line = lines.remove(at: i)
                lines.insert(line, at: 0)
                result = EngineResult(fen: result.fen, bestMove: line.move, lines: lines)
            }
            hints = Hints.build(placement: pos.board, player: pos.turn, result: result)
        }
        guard let hints else { return }
        hintLevel = hintLevel == 0 ? max(1, defaultHint) : hintLevel + 1
        if hintLevel == 3 { startIdea(hints) }
    }

    private func startIdea(_ h: HintSet) {
        ideaText = h.stage3Fallback
        guard app.llmConfigured else { return }
        ideaText = nil
        ideaTask?.cancel()
        ideaTask = Task {
            let request = Coach.hintRequest(h, context: profile.promptContext, language: Language.englishName)
            let text = try? await app.completeText(request) { _ in }
            guard !Task.isCancelled else { return }
            if let text, !text.isEmpty, !Hints.revealsTarget(text, move: h.move) { ideaText = text } else { ideaText = h.stage3Fallback }
        }
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
            if let puzzle = session.puzzle, let pos = session.position {
                HStack(alignment: .top, spacing: 20) {
                    board(puzzle, pos)
                    sidebar(puzzle, pos)
                        .frame(width: 280)
                }
                .padding(20)
            } else if session.queue.isEmpty {
                empty(finished: false)
            } else {
                empty(finished: true)
            }
        }
        .frame(minWidth: 780, minHeight: 520)
        .navigationTitle("Training")
        .onAppear { session.load() }
        .onChange(of: store.activeId) { _, _ in session.load() }
    }

    private func board(_ puzzle: Puzzle, _ pos: ChessPosition) -> some View {
        let m = session.hints.map { $0.move }
        let showSolution = session.phase == .wrong || session.hintLevel >= 4
        let solution = showSolution ? (puzzle.acceptedMoves.first.flatMap { UCIMove($0) } ?? m) : nil
        let highlight: (file: Int, rank: Int)? = (session.hintLevel == 2 || session.hintLevel == 3) ? m.map { ($0.fromFile, $0.fromRank) } : nil
        return ChessBoardView(
            placement: session.shown ?? pos.board,
            whiteAtBottom: pos.turn == .white,
            highlight: highlight,
            arrow: solution.map { (from: ($0.fromFile, $0.fromRank), to: ($0.toFile, $0.toRank)) },
            interactive: session.phase == .asking,
            onMove: { session.submit($0) },
            legal: { ff, fr, tf, tr in pos.legalMoves().contains { $0.fromFile == ff && $0.fromRank == fr && $0.toFile == tf && $0.toRank == tr } })
            .frame(minWidth: 420, minHeight: 420)
    }

    private func sidebar(_ puzzle: Puzzle, _ pos: ChessPosition) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Task \(session.index + 1) of \(session.queue.count)").font(.headline)
                Text(pos.turn == .white ? "Find the best move for White." : "Find the best move for Black.")
                    .foregroundStyle(.secondary)
            }
            switch session.phase {
            case .asking: hintArea
            case .correct:
                Label("Correct", systemImage: "checkmark.circle.fill").foregroundStyle(.green).font(.title3)
                if session.hintLevel > 0 { Text("You used a hint, so the task comes back at the same interval.").font(.callout).foregroundStyle(.secondary) }
            case .wrong:
                Label("Not quite", systemImage: "xmark.circle.fill").foregroundStyle(.orange).font(.title3)
                Text("Better was: \(session.solutionText)")
                Text("The task starts again tomorrow.").font(.callout).foregroundStyle(.secondary)
            }
            Spacer()
            if session.phase != .asking {
                Button("Next task") { session.next() }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
            }
            Divider()
            statsView
        }
    }

    @ViewBuilder
    private var hintArea: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let h = session.hints, session.hintLevel > 0 {
                Text("Hint \(session.hintLevel) of 4").font(.caption).foregroundStyle(.secondary)
                switch session.hintLevel {
                case 1: Text(h.stage1)
                case 2: Text(h.stage2)
                case 3:
                    if let t = session.ideaText { Text(t) } else { ProgressView().controlSize(.small) }
                default: Text(h.stage4)
                }
            }
            Button {
                Task { await session.hint() }
            } label: {
                if session.hintBusy { ProgressView().controlSize(.small) } else { Text("Show hint") }
            }
            .disabled(session.hintLevel >= 4 || session.hintBusy)
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
        }
        .font(.callout)
        .foregroundStyle(.secondary)
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
