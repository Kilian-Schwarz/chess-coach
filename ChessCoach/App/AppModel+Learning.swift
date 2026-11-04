import AppKit
import Foundation

@MainActor
extension AppModel {
    func showFairPlayNotice(key: String) {
        guard !defaults.bool(forKey: key) else { return }
        defaults.set(true, forKey: key)
        Task { @MainActor in
            let alert = NSAlert()
            alert.messageText = String(localized: "Fair play")
            alert.informativeText = String(localized: "Using hints or engine suggestions in rated online games against humans breaks the rules of chess.com, Lichess and similar sites and can get your account banned. Use Chess Coach for games against bots, unrated games, analysis and training.")
            alert.addButton(withTitle: String(localized: "OK"))
            alert.runModal()
        }
    }

    func trainPuzzles(_ ids: [String]) {
        trainingPriority = ids
        open("training")
    }

    func setFocus(_ skill: Skill, report: GameReport) {
        guard let pid = perspective(of: report).profile?.id else { return }
        store.update(pid) { $0.pinFocus(skill) }
    }

    func showThreat() {
        guard let ctx = hintCtx else { return }
        threatRequested.insert(ctx.fen)
        renderHints(ctx)
    }

    func threatVisible(_ ctx: HintContext) -> Bool {
        guard showThreats else { return false }
        let auto = store.profile(id: ctx.profileId)?.showsThreatAutomatically ?? true
        return effectiveShowBest || hintLevel >= Curriculum.firstHintLevel || auto || threatRequested.contains(ctx.fen)
    }

    func adaptation(for profileId: String?) -> Adaptation {
        guard let p = store.profile(id: profileId) else {
            return Curriculum.adaptation(level: .beginner, focus: .pieceSafety, skills: [:], successRate: nil)
        }
        let rate = PuzzleScheduler.recentSuccessRate(store.puzzles(for: p.id).puzzles)
        return Curriculum.adaptation(level: p.level, focus: p.focusSkill, skills: p.skills, successRate: rate)
    }

    func acceptsAlternative(puzzle p: Puzzle, move: UCIMove) async -> Bool {
        guard let pos = ChessPosition(fen: p.fen), pos.isLegal(move),
              let best = await engineAnalysis(fen: p.fen, ms: 300),
              let after = await engineAnalysis(fen: pos.applying(move).fen, ms: 300) else { return false }
        guard Puzzle.isAcceptableAlternative(bestValue: best.value, playedValue: -after.value) else { return false }
        store.updatePuzzles(for: store.activeId) { set in
            if let i = set.puzzles.firstIndex(where: { $0.id == p.id }) { set.puzzles[i].acceptAlternative(move.uci) }
        }
        return true
    }
}
