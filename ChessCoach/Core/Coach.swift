import Foundation

enum MoveRating: String {
    case best, good, inaccuracy, mistake, blunder

    static let bestDrop = 2.0
    static let inaccuracyDrop = 10.0
    static let mistakeDrop = 20.0
    static let blunderDrop = 30.0

    static func from(winBefore: Double, winAfter: Double, wasBest: Bool) -> MoveRating {
        let drop = winBefore - winAfter
        if wasBest || drop <= bestDrop { return .best }
        if drop < inaccuracyDrop { return .good }
        if drop < mistakeDrop { return .inaccuracy }
        if drop < blunderDrop { return .mistake }
        return .blunder
    }

    static func from(valueBefore: Int, valueAfter: Int, wasBest: Bool) -> MoveRating {
        from(winBefore: Accuracy.winChance(cp: valueBefore), winAfter: Accuracy.winChance(cp: valueAfter), wasBest: wasBest)
    }

    func title(_ loc: Loc = .display) -> String {
        switch self {
        case .best: loc("rating.best", "Best move")
        case .good: loc("rating.good", "Good move")
        case .inaccuracy: loc("rating.inaccuracy", "Inaccuracy")
        case .mistake: loc("rating.mistake", "Mistake")
        case .blunder: loc("rating.blunder", "Blunder")
        }
    }

    var symbol: String {
        switch self {
        case .best: "star.circle.fill"
        case .good: "checkmark.circle.fill"
        case .inaccuracy: "exclamationmark.circle.fill"
        case .mistake: "questionmark.circle.fill"
        case .blunder: "xmark.octagon.fill"
        }
    }
}

struct MoveReview {
    let before: Placement
    let played: UCIMove
    let best: UCIMove?
    let bestScore: Score
    let playedScore: Score
    let rating: MoveRating
    let loss: Int
    let replyMoves: [String]
    let player: PieceColor

    func playedText(_ loc: Loc = .display) -> String { before.describeMove(played, loc) }
    func bestText(_ loc: Loc = .display) -> String? { best.map { before.describeMove($0, loc) } }
    func replyLine(_ loc: Loc = .display) -> String { before.applying(played).describeLine(replyMoves, maxMoves: 4, loc) }
    var playedText: String { playedText() }
    var bestText: String? { bestText() }
    var replyLine: String { replyLine() }
}

enum Coach {
    static func review(before prev: Placement, played: UCIMove, after next: Placement, by mover: PieceColor,
                       beforeResult before: EngineResult, afterResult after: EngineResult?, gameOver: Bool = false) -> MoveReview? {
        let playedValue: Int, playedScore: Score
        if let after, !after.bestMove.isEmpty {
            playedValue = -after.value
            playedScore = after.score.negated
        } else if after != nil || gameOver {
            if next.isInCheck(mover.opposite) { playedValue = Accuracy.mateValue; playedScore = .checkmate }
            else { playedValue = 0; playedScore = .stalemate }
        } else {
            return nil
        }
        let best = UCIMove(before.bestMove)
        let loss = max(0, before.value - playedValue)
        return MoveReview(before: prev, played: played, best: best, bestScore: before.score, playedScore: playedScore,
                          rating: MoveRating.from(valueBefore: before.value, valueAfter: playedValue, wasBest: best?.uci == played.uci), loss: loss,
                          replyMoves: after?.lines.first?.pv ?? [], player: mover)
    }

    static func cleanAnswer(_ text: String) -> String {
        var t = text
        while let start = t.range(of: "<think>") {
            let end = t.range(of: "</think>", range: start.upperBound..<t.endIndex)?.upperBound ?? t.endIndex
            t.removeSubrange(start.lowerBound..<end)
        }
        if let end = t.range(of: "</think>") { t.removeSubrange(t.startIndex..<end.upperBound) }
        let tag = "<think>"
        for k in stride(from: tag.count - 1, through: 1, by: -1) where t.hasSuffix(String(tag.prefix(k))) {
            t.removeLast(k)
            break
        }
        return t.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
