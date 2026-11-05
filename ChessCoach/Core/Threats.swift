import Foundation

enum Threats {
    static let threshold = 150

    struct Threat: Equatable {
        let move: UCIMove
        let line: [String]
        let gain: Int
        let mateIn: Int?
        let board: Placement
        var lost: [PieceAt] = []
        var attacked: [PieceAt] = []
        var pinned: [PieceAt] = []

        func reason(_ loc: Loc = .display) -> String? {
            func list(_ items: [PieceAt]) -> String { items.map { $0.text(loc) }.joined(separator: ", ") }
            if (mateIn ?? 0) > 0 { return nil }
            if !lost.isEmpty { return loc("threat.reason.lose", "You would lose: \(list(lost)).") }
            if !pinned.isEmpty { return loc("threat.reason.pinned", "\(list(pinned)) would be attacked and could not move away.") }
            if !attacked.isEmpty { return loc("threat.reason.attack", "It would attack: \(list(attacked)).") }
            return nil
        }

        func text(_ loc: Loc = .display) -> String {
            let mate = (mateIn ?? 0) > 0
            let move = board.describeMove(move, loc, check: !mate)
            let head = mate ? loc("threat.mate", "Threat: \(move), mate in \(mateIn ?? 0)") : loc("threat.text", "Threat: \(move)")
            guard let reason = reason(loc) else { return head }
            return head + ". " + reason
        }

        var text: String { text() }

        func isParried(by answer: UCIMove) -> Bool {
            let after = board.applying(answer)
            let from = (move.fromFile, move.fromRank), to = (move.toFile, move.toRank)
            if after.piece(from.0, from.1) != board.piece(from.0, from.1) { return true }
            if let victim = board.piece(to.0, to.1), after.piece(to.0, to.1) != victim { return true }
            if board.piece(to.0, to.1) == nil {
                if after.piece(to.0, to.1) != nil { return true }
                if Character((board.piece(from.0, from.1) ?? " ").lowercased()) == "p" { return false }
            }
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
        let mover = Placement.color(of: board.piece(move.fromFile, move.fromRank) ?? "p")
        let me = mover.opposite
        var threat = Threat(move: move, line: line.pv, gain: min(opponent.value + currentValue, 20_000), mateIn: line.mateIn, board: board)
        let start = ChessPosition(board: board, turn: mover, castling: .none)
        var concrete = (line.mateIn ?? 0) > 0
        if start.isLegal(move) {
            if let o = PositionFacts.outcome(pv: line.pv, from: start, me: me) {
                var seen = Set<String>()
                threat.lost = o.captures.filter { Placement.color(of: $0.victim) == me && seen.insert($0.square).inserted }
                    .map { PieceAt(piece: $0.victim, square: $0.square) }
                if o.delta <= -1 && !threat.lost.isEmpty { concrete = true }
            }
            let after = board.applying(move)
            let known = Set(board.hanging(of: me).map(\.square))
            let pins = after.pins(of: me)
            for h in after.hanging(of: me) where !known.contains(h.square) {
                let at = PieceAt(piece: h.piece, square: h.square)
                if pins.contains(where: { $0.file == h.file && $0.rank == h.rank }) { threat.pinned.append(at) } else { threat.attacked.append(at) }
                concrete = true
            }
        }
        return concrete ? threat : nil
    }

    static func detect(in pos: ChessPosition, current: EngineResult, opponent: EngineResult, threshold: Int = Threats.threshold) -> Threat? {
        pos.isCheck ? nil : detect(board: pos.board, currentValue: current.value, opponent: opponent, threshold: threshold)
    }
}
