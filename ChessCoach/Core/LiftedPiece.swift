import Foundation

struct LiftedPiece: Equatable {
    let file: Int
    let rank: Int
    let piece: Character

    var square: String { UCIMove.squareName(file, rank) }

    static func detect(last: ChessPosition, current: Placement) -> LiftedPiece? {
        var missing: [(Int, Int, Character)] = []
        var disturbed = 0
        for r in 0..<8 { for f in 0..<8 where last.board.squares[r][f] != current.squares[r][f] {
            if let p = last.board.squares[r][f], current.squares[r][f] == nil, Placement.color(of: p) == last.turn {
                missing.append((f, r, p))
            } else {
                disturbed += 1
            }
        } }
        guard missing.count == 1, disturbed <= 1 else { return nil }
        let (f, r, p) = missing[0]
        if last.legalMoves().contains(where: { last.applying($0).board == current }) { return nil }
        return LiftedPiece(file: f, rank: r, piece: p)
    }
}

struct UnsafeSquare: Equatable {
    enum Reason: Equatable {
        case undefended
        case cheaperAttacker
        case leavesHanging(square: String, piece: Character)
    }

    let file: Int
    let rank: Int
    let reason: Reason

    var square: String { UCIMove.squareName(file, rank) }

    func text(piece: Character, _ loc: Loc = .display) -> String {
        let name = PieceInfo.name(piece, loc), sq = square
        switch reason {
        case .undefended: return loc("lift.undefended", "Careful: \(name) on \(sq) would be undefended.")
        case .cheaperAttacker: return loc("lift.cheaper", "Careful: \(name) on \(sq) could be taken by a cheaper piece.")
        case .leavesHanging(let at, let other):
            return loc("lift.leaves", "Careful: \(name) to \(sq) leaves \(PieceInfo.name(other, loc)) on \(at) hanging.")
        }
    }
}

extension LiftedPiece {
    static func summary(_ unsafe: [UnsafeSquare], piece: Character, _ loc: Loc = .display) -> String? {
        guard let first = unsafe.first else { return nil }
        if unsafe.count == 1 { return first.text(piece: piece, loc) }
        return loc("lift.many", "Careful: \(unsafe.count) squares are unsafe for \(PieceInfo.name(piece, loc)), marked in red.")
    }

    func unsafeSquares(in pos: ChessPosition) -> [UnsafeSquare] {
        guard pos.board.squares[rank][file] == piece, Placement.color(of: piece) == pos.turn else { return [] }
        let color = pos.turn
        let hangingBefore = Set(pos.board.hanging(of: color).map(\.square))
        var out: [UnsafeSquare] = []
        for m in pos.legalMoves() where m.fromFile == file && m.fromRank == rank {
            let after = pos.board.applying(m)
            guard let placed = after.piece(m.toFile, m.toRank) else { continue }
            let gain = pos.isEnPassant(m) ? 1 : pos.board.piece(m.toFile, m.toRank).map(PieceInfo.value) ?? 0
            let mine = PieceInfo.value(placed)
            let attackers = after.attackers(of: color.opposite)[m.toRank][m.toFile].map { PieceInfo.value($0.0) }
            let defended = !after.attackers(of: color)[m.toRank][m.toFile].isEmpty
            var reason: UnsafeSquare.Reason?
            if Character(placed.lowercased()) != "k", let cheapest = attackers.min(), gain < mine {
                if !defended { reason = .undefended } else if cheapest < mine { reason = .cheaperAttacker }
            }
            if reason == nil, let h = after.hanging(of: color).first(where: { !hangingBefore.contains($0.square) && $0.square != UCIMove.squareName(m.toFile, m.toRank) }) {
                reason = .leavesHanging(square: h.square, piece: h.piece)
            }
            if let reason { out.append(UnsafeSquare(file: m.toFile, rank: m.toRank, reason: reason)) }
        }
        var seen = Set<String>()
        return out.filter { seen.insert($0.square).inserted }
    }
}
