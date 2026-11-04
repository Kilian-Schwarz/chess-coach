import Foundation

@MainActor
extension AppModel {
    func handle(_ update: GameUpdate) {
        switch update.change {
        case .newGame:
            gameId = UUID().uuidString.lowercased()
            gameFinished = false
            countedProfiles = []
            usedHint = [:]
            reviewCard = nil
            reportCard = nil
        case .moves:
            gameFinished = false
        default:
            break
        }
        syncEntries()
        openingLine = openingName()
        updateInfoLines()
        if entries.contains(where: { !($0.triedBefore && $0.triedAfter) }) { startWorker() }
    }

    func syncEntries() {
        let moves = record.moves
        var keep = 0
        while keep < min(entries.count, moves.count),
              entries[keep].played.fenBefore == moves[keep].fenBefore, entries[keep].played.uci == moves[keep].uci { keep += 1 }
        if keep < entries.count { entries.removeSubrange(keep...) }
        for m in moves[keep...] { entries.append(MoveEntry(played: m)) }
        movesInGame = entries.count
    }

    func setEntry(_ id: String, _ change: (inout MoveEntry) -> Void) {
        guard let i = entries.firstIndex(where: { $0.id == id }) else { return }
        change(&entries[i])
    }

    func openingName() -> String? {
        guard let book = store.openings else { return nil }
        let epds = record.positions.map { $0.fen.split(separator: " ").prefix(4).joined(separator: " ") }
        return book.longestName(along: epds)?.display
    }

    func updateInfoLines() {
        var lines: [String] = []
        if let o = openingLine { lines.append(String(localized: "Opening: \(o)")) }
        if let t = store.active.placementText() { lines.append(t) }
        panel.model.infoLines = lines
    }

    func startWorker() {
        guard worker == nil else { return }
        worker = Task { [weak self] in
            while let self, !Task.isCancelled, let next = self.entries.first(where: { !($0.triedBefore && $0.triedAfter) }) {
                await self.fill(next)
            }
            self?.worker = nil
        }
    }

    func fill(_ e: MoveEntry) async {
        var before = e.before, after = e.after
        let p = e.played
        if !e.triedBefore, p.before.board.problem(sideToMove: p.mover) == nil {
            before = await analysis(p.fenBefore, ms: 300, priority: false)
        }
        if !e.triedAfter, p.after.result == nil, p.after.board.problem(sideToMove: p.after.turn) == nil {
            after = await analysis(p.fenAfter, ms: 300, priority: false)
        }
        setEntry(e.id) {
            $0.before = before ?? $0.before
            $0.after = after ?? $0.after
            $0.triedBefore = true
            $0.triedAfter = true
        }
    }

    func endGameNow() { finishGame() }

    func finishGame() {
        guard !gameFinished, entries.count >= 6 else { return }
        gameFinished = true
        let whiteProfile = profileForReport(.white), blackProfile = profileForReport(.black)
        let snapshot = GameSnapshot(
            id: gameId, date: record.startedAt, entries: entries, opening: openingLine,
            whiteProfile: whiteProfile, blackProfile: blackProfile,
            pgn: record.pgn(white: store.profile(id: whiteProfile)?.name ?? "?", black: store.profile(id: blackProfile)?.name ?? "?",
                            opening: openingLine))
        Task { await complete(snapshot) }
    }

    func profileForReport(_ color: PieceColor) -> String? {
        guard let me = lastMe else { return nil }
        return profileId(for: color, me: me)
    }

    func complete(_ snap: GameSnapshot) async {
        var reviewed: [ReviewedMove] = []
        for e in snap.entries {
            let p = e.played
            var before = e.before ?? analysisCache[p.fenBefore]?.result
            if before == nil, p.before.board.problem(sideToMove: p.mover) == nil { before = await analysis(p.fenBefore, ms: 300, priority: false) }
            guard let before else { continue }
            let valueAfter: Int
            switch p.after.result {
            case .checkmate?: valueAfter = Accuracy.mateValue
            case .some: valueAfter = 0
            case nil:
                var after = e.after ?? analysisCache[p.fenAfter]?.result
                if after == nil, p.after.board.problem(sideToMove: p.after.turn) == nil { after = await analysis(p.fenAfter, ms: 300, priority: false) }
                valueAfter = after.map { -$0.value } ?? before.value
            }
            reviewed.append(ReviewedMove(color: p.mover == .white ? "w" : "b", uci: p.uci, san: p.san, fenBefore: p.fenBefore,
                                         valueBefore: before.value, valueAfter: valueAfter,
                                         bestUci: UCIMove(before.bestMove)?.uci))
        }
        guard !reviewed.isEmpty else { return }
        let report = GameReport.build(moves: reviewed, id: snap.id, date: snap.date, whiteProfileId: snap.whiteProfile,
                                      blackProfileId: snap.blackProfile, opening: snap.opening)
        for (color, pid) in [("w", snap.whiteProfile), ("b", snap.blackProfile)] {
            guard let pid, report.side(color).moves > 0, !countedProfiles.contains(snap.id + pid) else { continue }
            countedProfiles.insert(snap.id + pid)
            store.update(pid) { $0.completeGame(performance: report.side(color).performance) }
        }
        store.save(game: report, pgn: snap.pgn)
        lastReport = report
        refreshActions()
        updateInfoLines()
        reportCard = CoachCard(id: "report|" + snap.id, kind: .report, title: String(localized: "Game finished"),
                               text: String(localized: "The evaluation is ready. Open it from the buttons below or the menu."))
        writeSummary(for: report)
    }

    func perspective(of report: GameReport) -> (color: String, profile: PlayerProfile?) {
        if let w = report.whiteProfileId, w == store.activeId { return ("w", store.profile(id: w)) }
        if let b = report.blackProfileId, b == store.activeId { return ("b", store.profile(id: b)) }
        if let w = report.whiteProfileId { return ("w", store.profile(id: w)) }
        if let b = report.blackProfileId { return ("b", store.profile(id: b)) }
        return ("w", nil)
    }

    func writeSummary(for report: GameReport) {
        guard llmConfigured else { return }
        let (color, profile) = perspective(of: report)
        let request = report.summaryRequest(for: color, context: profile?.promptContext ?? .neutral, language: Language.englishName)
        summaryTasks[report.id]?.cancel()
        summaryDraft[report.id] = ""
        summaryTasks[report.id] = stream(request, update: { [weak self] text, done in
            guard let self else { return }
            self.summaryDraft[report.id] = text
            guard done else { return }
            self.summaryDraft[report.id] = nil
            guard !text.isEmpty else { return }
            var updated = self.store.games.first { $0.id == report.id } ?? report
            updated.summary = text
            self.store.save(game: updated, pgn: self.store.pgn(for: updated))
            if self.lastReport?.id == updated.id { self.lastReport = updated }
        }, failed: { [weak self] _ in self?.summaryDraft[report.id] = nil })
    }

    func reviewOwnMove(_ played: PlayedMove, me: PieceColor) async {
        let pid = profileId(for: played.mover, me: me)
        guard played.before.board.problem(sideToMove: played.mover) == nil,
              let before = await analysis(played.fenBefore) else { return }
        var after: EngineResult?
        if played.after.result == nil, played.after.board.problem(sideToMove: played.after.turn) == nil {
            after = await analysis(played.fenAfter)
        }
        let id = played.fenBefore + played.uci
        let firstTime = entries.first { $0.id == id }.map { !$0.processed } ?? false
        setEntry(id) { $0.before = before; $0.after = after ?? $0.after; $0.triedBefore = true; $0.triedAfter = true; $0.processed = true }
        let review = Coach.review(before: played.before.board, played: played.move, after: played.after.board,
                                  by: played.mover, beforeResult: before, afterResult: after)
        var nullResult: EngineResult?
        if let nullFen = Threats.nullMoveFEN(played.before) { nullResult = await analysis(nullFen, ms: 300) }
        let outcome = MoveAnalyzer.analyze(MoveAnalysisInput(review: review, beforeResult: before, afterResult: after,
                                                             threatResult: nullResult, moveNumber: played.moveNumber))
        if let pid, firstTime {
            let hint = usedHint[played.fenBefore] ?? 0
            store.update(pid) { $0.apply(outcome); $0.recordHintUse(level: hint) }
            let level = store.profile(id: pid)?.level ?? .beginner
            if let puzzle = Puzzle.make(review: review, beforeResult: before, mistakeType: outcome.mistakeType,
                                        theme: outcome.theme, level: level) {
                store.updatePuzzles(for: pid) { $0.add(puzzle) }
            }
            updateInfoLines()
        }
        guard reviewMoves else { return }
        let who = playerName(played.mover, me: me)
        var lines = [String(localized: "Played: \(review.playedText) (\(review.playedScore.text()))")]
        if review.rating != .best, let bestText = review.bestText {
            lines.append(String(localized: "Better: \(bestText) (\(before.scoreText))"))
        }
        let cardId = "\(played.fenBefore)|\(played.uci)"
        reviewCard = CoachCard(id: cardId, kind: .review,
                               title: "\(duoTraining ? who : String(localized: "Your move")): \(review.rating.title())",
                               rating: review.rating, lines: lines)
        let positions = [played.before, played.after]
        let pvs = before.lines.map(\.pv) + (after?.lines.map(\.pv) ?? [])
        startExplanation(.review, ExplainJob(
            cardId: cardId, profileId: pid, themeKey: outcome.theme?.rawValue,
            allowed: { FactCheck.allowed(positions: positions, lines: pvs) },
            build: { ctx, style in Coach.reviewRequest(review, context: ctx, style: style, language: Language.englishName) }))
    }
}
