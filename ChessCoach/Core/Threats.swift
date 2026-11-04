import Foundation

enum Threats {
    static let threshold = 150

    struct Threat: Equatable {
        let move: UCIMove
        let line: [String]
        let gain: Int
        let mateIn: Int?
        let board: Placement

        func text(_ loc: Loc = .display) -> String {
            let mate = (mateIn ?? 0) > 0
            let move = board.describeMove(move, loc, check: !mate)
            return mate ? loc("threat.mate", "Threat: \(move), mate in \(mateIn ?? 0)") : loc("threat.text", "Threat: \(move)")
        }

        var text: String { text() }

        func isParried(by answer: UCIMove) -> Bool {
            let after = board.applying(answer)
            let from = (move.fromFile, move.fromRank), to = (move.toFile, move.toRank)
            if after.piece(from.0, from.1) != board.piece(from.0, from.1) { return true }
            if let victim = board.piece(to.0, to.1), after.piece(to.0, to.1) != victim { return true }
            return !after.attacks(file: from.0, rank: from.1).contains { $0 == to }
        }
    }

    static func nullMoveFEN(_ pos: ChessPosition) -> String? {
        guard !pos.isCheck, pos.board.problem(sideToMove: pos.turn) == nil else { return nil }
        let n = pos.nullMove
        guard n.board.problem(sideToMove: n.turn) == nil else { return nil }
        return n.fen
    }

    static func detect(board: Placement, currentValue: Int, opponent: EngineResult, threshold: Int = Threats.threshold) -> Threat? {
        guard let line = opponent.lines.first, let move = UCIMove(opponent.bestMove), board.piece(move.fromFile, move.fromRank) != nil,
              opponent.value + currentValue >= threshold else { return nil }
        return Threat(move: move, line: line.pv, gain: min(opponent.value + currentValue, 20_000), mateIn: line.mateIn, board: board)
    }

    static func detect(in pos: ChessPosition, current: EngineResult, opponent: EngineResult, threshold: Int = Threats.threshold) -> Threat? {
        pos.isCheck ? nil : detect(board: pos.board, currentValue: current.value, opponent: opponent, threshold: threshold)
    }
}
