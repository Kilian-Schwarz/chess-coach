import Foundation

enum MoveRating: String {
    case best, good, inaccuracy, mistake, blunder

    static func from(loss: Int, wasBest: Bool) -> MoveRating {
        if wasBest || loss <= 15 { return .best }
        if loss <= 60 { return .good }
        if loss <= 130 { return .inaccuracy }
        if loss <= 300 { return .mistake }
        return .blunder
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
                       beforeResult before: EngineResult, afterResult after: EngineResult?) -> MoveReview {
        let playedValue: Int, playedScore: Score
        if let after, !after.bestMove.isEmpty {
            playedValue = -after.value
            playedScore = after.score.negated
        } else if next.isInCheck(mover.opposite) {
            playedValue = 10_000; playedScore = .checkmate
        } else {
            playedValue = 0; playedScore = .stalemate
        }
        let best = UCIMove(before.bestMove)
        let loss = max(0, before.value - playedValue)
        return MoveReview(before: prev, played: played, best: best, bestScore: before.score, playedScore: playedScore,
                          rating: MoveRating.from(loss: loss, wasBest: best?.uci == played.uci), loss: loss,
                          replyMoves: after?.lines.first?.pv ?? [], player: mover)
    }

    static func cleanAnswer(_ text: String) -> String {
        var t = text
        while let start = t.range(of: "<think>") {
            let end = t.range(of: "</think>", range: start.upperBound..<t.endIndex)?.upperBound ?? t.endIndex
            t.removeSubrange(start.lowerBound..<end)
        }
        if let end = t.range(of: "</think>") { t.removeSubrange(t.startIndex..<end.upperBound) }
        return t.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
