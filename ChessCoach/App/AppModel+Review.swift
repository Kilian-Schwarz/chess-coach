import Foundation

@MainActor
extension AppModel {
    func handle(_ update: GameUpdate) {
        switch update.change {
        case .newGame:
            gameId = UUID().uuidString.lowercased()
            gameFinished = false
            countedProfiles = []
            overlayEnd = nil
            usedHint = [:]
            gameMistakes = []
            assist.begin(assisted: effectiveShowBest || effectiveAlwaysMyTurn)
            threatRequested = []
            hintNudged = []
            hintFirstSeen = [:]
            reviewCard = nil
            reportCard = nil
        case .moves:
            if gameFinished, let ended = overlayEnd, ended.id == gameId {
                overlayEnd = nil
                if update.newMoves.first?.fenBefore == ended.fen {
                    retractEnd(ended.id)
                } else {
                    gameId = UUID().uuidString.lowercased()
                    countedProfiles = []
                    assist.begin(assisted: effectiveShowBest || effectiveAlwaysMyTurn)
                }
            }
            gameFinished = false
        default:
            break
        }
        syncEntries()
        panel.model.canFinish = entries.count >= 2 && !gameFinished
        if let c = reviewCard, let ply = c.reviewPly,
           !(record.moves.indices.contains(ply - 1) && c.id == "\(record.moves[ply - 1].fenBefore)|\(record.moves[ply - 1].uci)") {
            reviewSeq += 1
            tasks[.review]?.cancel()
            jobs[.review] = nil
            reviewCard = nil
        }
        if update.gap, reviewMoves {
            reviewSeq += 1
            tasks[.review]?.cancel()
            jobs[.review] = nil
            reviewCard = CoachCard(id: "gap|\(reviewSeq)", kind: .review,
                                   title: String(localized: "Moves missed – the next move will be rated again"))
        }
        updatePanelCards()
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
        if assist.tainted { lines.append(String(localized: "Not counted for your profile")) }
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

    func retractEnd(_ id: String) {
        Log.write("Overlay end retracted: the game continues")
        completeTask?.cancel()
        completeTask = nil
        summaryTasks[id]?.cancel()
        summaryTasks[id] = nil
        summaryDraft[id] = nil
        recapProgress = nil
        reportCard = nil
        store.removeGame(id: id)
        if lastReport?.id == id { lastReport = store.games.first }
        if recapSelection == id {
            recapSelection = nil
            closeWindow("report")
        }
        refreshActions()
    }

    func endGameNow() { finishGame(GameEnd(reason: .manual, result: "*"), minPlies: 2) }

    func finishGame(_ end: GameEnd, minPlies: Int = GameEndDetector.minPlies, open: Bool = true) {
        guard !gameFinished, entries.count >= minPlies else { return }
        gameFinished = true
        panel.model.canFinish = false
        let decided = record.current?.result.flatMap(GameEndDetector.end(for:))
        let final = end.knownResult ? end : GameEnd(reason: end.reason, result: decided?.result ?? "*")
        Log.write("Game end: \(final.reason.rawValue), result \(final.result), \(entries.count) plies")
        let whiteProfile = profileForReport(.white), blackProfile = profileForReport(.black)
        let snapshot = GameSnapshot(
            id: gameId, end: final, date: record.startedAt, entries: entries, opening: openingLine,
            whiteProfile: whiteProfile, blackProfile: blackProfile,
            pgn: record.pgn(white: store.profile(id: whiteProfile)?.name ?? "?", black: store.profile(id: blackProfile)?.name ?? "?",
                            opening: openingLine, result: final.knownResult ? final.result : nil),
            hints: usedHint, mistakes: gameMistakes, assisted: assist.tainted)
        overlayEnd = end.reason == .overlay ? (gameId, record.current?.fen ?? "") : nil
        completeTask = Task { await complete(snapshot, open: open, activate: end.reason == .manual) }
    }

    func observeEnd(_ view: BoardView, result: GameResult? = nil, isStart: Bool = false) {
        guard !gameFinished else { return }
        guard let end = gameEnd.observe(view, plies: record.moves.count, result: result, isStart: isStart, at: Date()) else { return }
        finishGame(end, minPlies: 1, open: !end.silent)
    }

    func profileForReport(_ color: PieceColor) -> String? {
        guard let me = lastMe else { return nil }
        return profileId(for: color, me: me)
    }

    func showRecap(_ id: String? = nil, activate: Bool = true) {
        recapSelection = id ?? lastReport?.id ?? store.games.first?.id
        open("report", activate: activate)
    }

    func reviewedMove(_ e: MoveEntry, ply: Int, ms: Int?, hints: [String: Int] = [:]) async -> ReviewedMove? {
        let p = e.played
        var before = e.before ?? analysisCache[p.fenBefore]?.result
        if before == nil, let ms, p.before.board.problem(sideToMove: p.mover) == nil {
            before = await analysis(p.fenBefore, ms: ms, priority: false, forced: true)
        }
        guard let before else { return nil }
        let valueAfter: Int
        switch p.after.result {
        case .checkmate?: valueAfter = Accuracy.mateValue
        case .some: valueAfter = 0
        case nil:
            var after = e.after ?? analysisCache[p.fenAfter]?.result
            if after == nil, let ms, p.after.board.problem(sideToMove: p.after.turn) == nil {
                after = await analysis(p.fenAfter, ms: ms, priority: false, forced: true)
            }
            if let after { valueAfter = -after.value } else if ms != nil { valueAfter = before.value } else { return nil }
        }
        return ReviewedMove(color: p.mover == .white ? "w" : "b", uci: p.uci, san: p.san, fenBefore: p.fenBefore,
                            valueBefore: before.value, valueAfter: valueAfter, bestUci: UCIMove(before.bestMove)?.uci, ply: ply,
                            hint: hints[p.fenBefore])
    }

    func complete(_ snap: GameSnapshot, open: Bool, activate: Bool) async {
        var reviewed: [ReviewedMove?] = []
        for (i, e) in snap.entries.enumerated() { reviewed.append(await reviewedMove(e, ply: i + 1, ms: nil, hints: snap.hints)) }
        func report() -> GameReport {
            var r = GameReport.build(moves: reviewed.compactMap { $0 }, id: snap.id, date: snap.date, whiteProfileId: snap.whiteProfile,
                                     blackProfileId: snap.blackProfile, opening: snap.opening)
            r.result = snap.end.result
            r.endReason = snap.end.reason.rawValue
            r.engineAssisted = snap.assisted ? true : nil
            r.summary = store.games.first { $0.id == snap.id }?.summary
            let view = perspective(of: r)
            r.takeaways = Takeaway.build(mistakes: gameId == snap.id ? gameMistakes : snap.mistakes, moves: r.moves, color: view.color,
                                         focus: view.profile?.focusSkill ?? .pieceSafety)
            return r
        }
        let missing = reviewed.indices.filter { reviewed[$0] == nil }
        var current = report()
        guard !Task.isCancelled else { return }
        if !reviewed.allSatisfy({ $0 == nil }) {
            store.save(game: current, pgn: snap.pgn)
            lastReport = current
        }
        refreshActions()
        if open && openRecap { showRecap(snap.id, activate: activate) }
        if !missing.isEmpty {
            recapProgress = RecapProgress(id: snap.id, done: 0, total: missing.count)
            for (n, i) in missing.enumerated() {
                reviewed[i] = await reviewedMove(snap.entries[i], ply: i + 1, ms: 200, hints: snap.hints)
                guard !Task.isCancelled else { return }
                recapProgress = RecapProgress(id: snap.id, done: n + 1, total: missing.count)
                if n % 4 == 3 || n == missing.count - 1 {
                    current = report()
                    lastReport = current
                    store.save(game: current, pgn: snap.pgn)
                }
            }
            recapProgress = nil
        }
        guard !Task.isCancelled, !reviewed.allSatisfy({ $0 == nil }) else { return }
        let final = report()
        for (color, pid) in [("w", snap.whiteProfile), ("b", snap.blackProfile)] {
            guard !snap.assisted, let pid, final.side(color).countsForRating, !countedProfiles.contains(snap.id + pid) else { continue }
            countedProfiles.insert(snap.id + pid)
            store.update(pid) { $0.completeGame(summary: final.side(color)) }
        }
        store.save(game: final, pgn: snap.pgn)
        lastReport = final
        refreshActions()
        updateInfoLines()
        reportCard = CoachCard(id: "report|" + snap.id, kind: .report, title: String(localized: "Game finished"),
                               text: String(localized: "The evaluation is ready. Open it from the buttons below or the menu."))
        writeSummary(for: final)
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
            guard var updated = self.store.games.first(where: { $0.id == report.id }) else { return }
            updated.summary = text
            self.store.save(game: updated, pgn: self.store.pgn(for: updated))
            if self.lastReport?.id == updated.id { self.lastReport = updated }
        }, failed: { [weak self] _ in self?.summaryDraft[report.id] = nil })
    }

    func inRecord(_ p: PlayedMove) -> Bool { record.moves.contains { $0.fenBefore == p.fenBefore && $0.uci == p.uci } }

    func displayedReviewCard() -> CoachCard? {
        guard var c = reviewCard else { return nil }
        if let ply = c.reviewPly, ply < record.moves.count, let t = c.staleTitle { c.title = t }
        return c
    }

    func startReview(_ moves: [PlayedMove], me: PieceColor) {
        reviewSeq += 1
        let seq = reviewSeq
        Task { [weak self] in
            for m in moves { await self?.reviewOwnMove(m, me: me, seq: seq) }
        }
    }

    func reviewOwnMove(_ played: PlayedMove, me: PieceColor, seq: Int) async {
        let started = Date()
        let pid = profileId(for: played.mover, me: me)
        guard played.before.board.problem(sideToMove: played.mover) == nil,
              let before = await analysis(played.fenBefore, ms: 300) else { return }
        var after: EngineResult?
        if played.after.result == nil, played.after.board.problem(sideToMove: played.after.turn) == nil {
            after = await analysis(played.fenAfter, ms: 300)
        }
        guard inRecord(played) else { return }
        let id = played.fenBefore + played.uci
        guard let review = Coach.review(before: played.before.board, played: played.move, after: played.after.board,
                                        by: played.mover, beforeResult: before, afterResult: after,
                                        gameOver: played.after.result != nil) else { return }
        let cardId = "\(played.fenBefore)|\(played.uci)"
        let who = playerName(played.mover, me: me)
        if reviewMoves, seq == reviewSeq {
            var lines = [String(localized: "Played: \(review.playedText) (\(review.playedScore.text()))")]
            if review.rating != .best, let bestText = review.bestText {
                lines.append(String(localized: "Better: \(bestText) (\(before.scoreText))"))
            }
            let rating = review.rating.title()
            let san = played.sanDisplay
            let mine = String(localized: "Your move")
            var card = CoachCard(id: cardId, kind: .review, title: "\(duoTraining ? who : mine) (\(san)): \(rating)",
                                 rating: review.rating, lines: lines)
            card.reviewPly = (record.moves.firstIndex { $0.fenBefore == played.fenBefore && $0.uci == played.uci } ?? -1) + 1
            card.reviewSan = san
            if !duoTraining { card.staleTitle = "\(String(localized: "Your last move")) (\(san)): \(rating)" }
            reviewCard = card
            Log.write("Review shown for \(played.san) \(Int(Date().timeIntervalSince(started) * 1000)) ms after the move was confirmed")
        }
        var nullResult: EngineResult?
        if let nullFen = Threats.nullMoveFEN(played.before) { nullResult = await analysis(nullFen, ms: 300, priority: false) }
        guard inRecord(played) else { return }
        let firstTime = entries.first { $0.id == id }.map { !$0.processed } ?? false
        setEntry(id) { $0.before = before; $0.after = after ?? $0.after; $0.triedBefore = true; $0.triedAfter = true; $0.processed = true }
        let outcome = MoveAnalyzer.analyze(MoveAnalysisInput(review: review, beforeResult: before, afterResult: after,
                                                             threatResult: nullResult, moveNumber: played.moveNumber))
        if let pid, firstTime, assist.counts {
            let hint = usedHint[played.fenBefore] ?? 0
            let gate = assist
            store.update(pid) { gate.record(outcome, hint: hint, into: &$0) }
            let level = store.profile(id: pid)?.level ?? .beginner
            let puzzle = Puzzle.make(review: review, beforeResult: before, mistakeType: outcome.mistakeType,
                                     theme: outcome.theme, level: level)
            if puzzle != nil { store.updatePuzzles(for: pid) { gate.add(puzzle, to: &$0) } }
            if let type = outcome.mistakeType {
                let puzzleId = store.puzzles(for: pid).puzzles.first { $0.fen == review.before.fen(sideToMove: review.player) }?.id
                gameMistakes.append(GameMistake(ply: record.moves.firstIndex { $0.fenBefore == played.fenBefore && $0.uci == played.uci }.map { $0 + 1 } ?? 0,
                                                color: played.mover == .white ? "w" : "b", type: type.rawValue, loss: outcome.loss,
                                                example: outcome.example, puzzleId: puzzleId))
            }
            updateInfoLines()
        }
        guard reviewMoves, seq == reviewSeq, reviewCard?.id == cardId else { return }
        let positions = [played.before, played.after]
        let pvs = before.lines.map(\.pv) + (after?.lines.map(\.pv) ?? [])
        startExplanation(.review, ExplainJob(
            cardId: cardId, profileId: pid, themeKey: outcome.theme?.rawValue,
            allowed: { FactCheck.allowed(positions: positions, lines: pvs) },
            build: { ctx, style in Coach.reviewRequest(review, context: ctx, style: style, language: Language.englishName) }))
    }
}
