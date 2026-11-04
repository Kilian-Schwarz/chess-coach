import AppKit
import SwiftUI

@MainActor
extension AppModel {
    func forgetHints() {
        hintCtx = nil
        hintFen = nil
        hintLevel = 0
    }

    func clearHints() {
        forgetHints()
        threatCard = nil
        panel.model.canHint = false
        panel.model.canExplain = reviewCard != nil
    }

    func defaultHintLevel(for profile: PlayerProfile?) -> Int {
        guard let p = profile else { return 1 }
        let rate = PuzzleScheduler.recentSuccessRate(store.puzzles(for: p.id).puzzles)
        return Curriculum.adaptation(level: p.level, focus: p.focusSkill, skills: p.skills, successRate: rate).defaultHintLevel
    }

    func nextHint() {
        guard isActive, let ctx = hintCtx, !effectiveShowBest, hintLevel < panel.model.hintMax else { return }
        hintLevel = hintLevel == 0 ? max(1, defaultHintLevel(for: store.profile(id: ctx.profileId))) : hintLevel + 1
        usedHint[ctx.fen] = max(usedHint[ctx.fen] ?? 0, hintLevel)
        lastChangeAt = Date()
        renderHints(ctx)
    }

    func toggleSideToMove() {
        guard isActive, let pos = record.current else { return }
        sideOverride = (sideOverride ?? pos.turn).opposite
        suggestionCard = nil
        tasks[.suggestion]?.cancel()
        forgetHints()
        invalidate()
    }

    func screenPoint(_ move: UCIMove, whiteAtBottom: Bool) -> (from: (col: Int, row: Int), to: (col: Int, row: Int)) {
        (Placement.screenSquare(file: move.fromFile, rank: move.fromRank, whiteAtBottom: whiteAtBottom),
         Placement.screenSquare(file: move.toFile, rank: move.toRank, whiteAtBottom: whiteAtBottom))
    }

    func renderHints(_ ctx: HintContext) {
        let level = effectiveShowBest ? 4 : hintLevel
        let move = ctx.hints.move
        let sq = screenPoint(move, whiteAtBottom: ctx.whiteAtBottom)
        let prefix = duoTraining ? ctx.who + ": " : ""
        panel.model.canExplain = level >= 3 || reviewCard != nil
        var state = OverlayState()

        let newThreatCard: CoachCard? = (showThreats ? ctx.threat : nil).map {
            CoachCard(id: ctx.fen + "|threat", kind: .threat, title: String(localized: "Threat"), text: $0.text)
        }
        if threatCard != newThreatCard { threatCard = newThreatCard }
        if showThreats, let t = ctx.threat {
            let tp = screenPoint(t.move, whiteAtBottom: ctx.whiteAtBottom)
            state.threatArrow = .init(from: tp.from, to: tp.to)
        }

        switch level {
        case 0:
            suggestion = String(localized: "Think for yourself · ⌥⌘H for a hint")
            state.hint = prefix + String(localized: "Your move · think for yourself")
            setHintCard(id: ctx.fen + "|h0", title: String(localized: "\(ctx.who) to move"),
                        lines: [String(localized: "Think for yourself. “Next hint” (⌥⌘H) reveals more step by step.")])
        case 1:
            suggestion = String(localized: "Hint 1 of 4")
            state.hint = prefix + String(localized: "Hint 1: topic")
            setHintCard(id: ctx.fen + "|h1", title: String(localized: "Hint 1: topic"), text: ctx.hints.stage1)
        case 2:
            suggestion = String(localized: "Hint 2 of 4")
            state.highlight = .init(col: sq.from.col, row: sq.from.row)
            state.partnerHighlight = ctx.partner
            state.hint = prefix + String(localized: "Hint 2: this piece")
            setHintCard(id: ctx.fen + "|h2", title: String(localized: "Hint 2: piece"), lines: [ctx.hints.stage2])
        case 3:
            suggestion = String(localized: "Hint 3 of 4")
            state.highlight = .init(col: sq.from.col, row: sq.from.row)
            state.partnerHighlight = ctx.partner
            state.hint = prefix + String(localized: "Hint 3: idea")
            renderIdea(ctx)
        default:
            suggestion = ctx.score.isEmpty ? ctx.text : "\(ctx.text)  (\(ctx.score))"
            state.arrow = .init(from: sq.from, to: sq.to, partner: ctx.partner)
            state.label = "\(prefix)\(ctx.text)\(ctx.score.isEmpty ? "" : " · " + ctx.score)"
            renderSolution(ctx)
        }
        overlay.show(boardInScreen: ctx.boardRect, state: state)
        updatePanelCards()
    }

    func setHintCard(id: String, title: String, lines: [String] = [], text: String = "") {
        guard suggestionCard?.id != id else { return }
        tasks[.suggestion]?.cancel()
        suggestionCard = CoachCard(id: id, kind: .hint, title: title, lines: lines, text: text)
    }

    func renderIdea(_ ctx: HintContext) {
        let id = ctx.fen + "|idea"
        guard suggestionCard?.id != id else { return }
        tasks[.suggestion]?.cancel()
        var card = CoachCard(id: id, kind: .hint, title: String(localized: "Hint 3: idea"))
        if let cached = ideaCache[ctx.fen] { card.text = cached }
        else if !llmConfigured { card.text = ctx.hints.stage3Fallback }
        else { card.isLoading = true }
        suggestionCard = card
        if card.isLoading { prefetchIdea(ctx) }
    }

    func prefetchIdea(_ ctx: HintContext) {
        guard llmConfigured, ideaCache[ctx.fen] == nil, ideaFen != ctx.fen else { return }
        ideaTask?.cancel()
        ideaFen = ctx.fen
        let context = store.profile(id: ctx.profileId)?.promptContext ?? PlayerContext.neutral
        let fen = ctx.fen, move = ctx.hints.move, fallback = ctx.hints.stage3Fallback
        let show: @MainActor (AppModel, String, Bool) -> Void = { model, text, done in
            guard model.suggestionCard?.id == fen + "|idea" else { return }
            model.suggestionCard?.text = text
            model.suggestionCard?.isLoading = !done
        }
        ideaTask = stream(Coach.hintRequest(ctx.hints, context: context, language: Language.englishName), update: { [weak self] text, done in
            guard let self else { return }
            let safe = Hints.revealsTarget(text, move: move) ? nil : text
            if done {
                let final = safe ?? fallback
                self.ideaCache[fen] = final
                show(self, final, true)
            } else if let safe {
                show(self, safe, false)
            }
        }, failed: { [weak self] _ in
            guard let self else { return }
            self.ideaCache[fen] = fallback
            show(self, fallback, true)
        })
    }

    func renderSolution(_ ctx: HintContext) {
        let id = ctx.fen
        guard suggestionCard?.id != id else { return }
        let title = duoTraining ? ctx.who + ": " + ctx.text : String(localized: "Suggestion: \(ctx.text)")
        var card = CoachCard(id: id, kind: .suggestion, title: title, lines: alternatives(ctx.result, placement: ctx.position.board))
        tasks[.suggestion]?.cancel()
        guard explainSuggestions else { suggestionCard = card; return }
        guard llmConfigured else {
            card.note = String(localized: "Set up an AI server in the settings (⌘,) for explanations.")
            suggestionCard = card
            return
        }
        suggestionCard = card
        let pvs = ctx.result.lines.map(\.pv)
        startExplanation(.suggestion, ExplainJob(
            cardId: id, profileId: ctx.profileId, themeKey: ctx.hints.theme.rawValue,
            allowed: { FactCheck.allowed(positions: [ctx.position], lines: pvs) },
            build: { c, style in
                Coach.suggestionRequest(placement: ctx.position.board, player: ctx.toMove, fen: ctx.fen,
                                        result: ctx.result, context: c, style: style, language: Language.englishName)
            }))
    }

    func alternatives(_ r: EngineResult, placement: Placement) -> [String] {
        let alts = r.lines.dropFirst().compactMap { line -> String? in
            guard let m = UCIMove(line.move) else { return nil }
            return "\(placement.describeMove(m)) (\(line.scoreText))"
        }
        var out = [String(localized: "Evaluation: \(r.scoreText)")]
        if !alts.isEmpty { out.append(String(localized: "Also possible: \(alts.joined(separator: ", "))")) }
        return out
    }
}
