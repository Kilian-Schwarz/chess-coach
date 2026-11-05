import Foundation

@MainActor
extension AppModel {
    func coachingGrounding(question: String, placement: Placement, toMove: PieceColor) async -> PositionFacts.Grounding {
        guard let ctx = hintCtx else {
            let pos = record.current ?? ChessPosition(board: placement, turn: toMove, castling: .none)
            let result = await analysis(pos.fen, ms: 300, forced: true)
            var grounding = PositionFacts.build(position: pos, result: result, nullResult: nil, threat: nil, revealBest: false)
            grounding.lines.append("Hint rule: the player has not asked for a hint. Do not name or hint at the best move.")
            return grounding
        }
        let level = effectiveShowBest ? 4 : hintLevel
        let key = ctx.fen + "|" + (level >= 1 ? "1" : "0")
        var grounding: PositionFacts.Grounding
        if let cached = factsCache[key] {
            grounding = cached
        } else {
            var nullResult: EngineResult?
            if let nullFen = Threats.nullMoveFEN(ctx.position) { nullResult = await analysis(nullFen, ms: 300, forced: true) }
            grounding = PositionFacts.build(position: ctx.position, result: ctx.result, nullResult: nullResult, threat: ctx.threat, revealBest: level >= 1)
            if factsCache.count > 40 { factsCache.removeAll() }
            factsCache[key] = grounding
        }
        grounding.lines += PositionFacts.hintLines(level: level, hints: ctx.hints, position: ctx.position)
        if let move = PositionFacts.mentionedMove(in: question, position: ctx.position), level >= 1 || move != ctx.hints.move,
           let after = await analysis(ctx.position.applying(move).fen, ms: 300, forced: true) {
            let mentioned = PositionFacts.mentionedLines(position: ctx.position, move: move, after: after, best: ctx.result.lines.first)
            grounding.lines += mentioned.lines
            grounding.positions.append(mentioned.position)
            grounding.pvs.append(mentioned.pv)
        }
        return grounding
    }
}
