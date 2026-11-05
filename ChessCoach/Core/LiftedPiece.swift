import Foundation

struct LiftedPiece: Equatable {
    let file: Int
    let rank: Int
    let piece: Character

    var square: String { UCIMove.squareName(file, rank) }

    static func detect(last: ChessPosition, current: Placement, held: Bool = false) -> LiftedPiece? {
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
        if !held, last.legalMoves().contains(where: { last.applying($0).board == current }) { return nil }
        return LiftedPiece(file: f, rank: r, piece: p)
    }
}

struct UnsafeSquare: Equatable {
    enum Reason: Equatable {
        case attacked(by: Character, square: String)
        case leavesHanging(square: String, piece: Character)
    }

    let file: Int
    let rank: Int
    let reason: Reason

    var square: String { UCIMove.squareName(file, rank) }

    func text(piece: Character, _ loc: Loc = .display) -> String {
        let name = PieceInfo.name(piece, loc), sq = square
        switch reason {
        case .attacked(let by, let from):
            return loc("lift.taken", "Careful: \(name) on \(sq) could be taken by \(PieceInfo.name(by, loc)) on \(from).")
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
        var out: [UnsafeSquare] = []
        for m in pos.legalMoves() where m.fromFile == file && m.fromRank == rank {
            let after = pos.board.applying(m)
            guard let placed = after.piece(m.toFile, m.toRank) else { continue }
            let gain = pos.isEnPassant(m) ? 1 : pos.board.piece(m.toFile, m.toRank).map(PieceInfo.value) ?? 0
            var reason: UnsafeSquare.Reason?
            if Character(placed.lowercased()) != "k", let lost = after.exchangeLoss(file: m.toFile, rank: m.toRank, color: color), lost.loss > gain {
                reason = .attacked(by: lost.attacker.0, square: UCIMove.squareName(lost.attacker.1, lost.attacker.2))
            }
            if reason == nil {
                for r in 0..<8 { for f in 0..<8 {
                    guard let other = after.piece(f, r), Placement.color(of: other) == color, Character(other.lowercased()) != "k",
                          !(f == m.toFile && r == m.toRank),
                          (after.exchangeLoss(file: f, rank: r, color: color)?.loss ?? 0) > 0,
                          (pos.board.exchangeLoss(file: f, rank: r, color: color)?.loss ?? 0) == 0 else { continue }
                    reason = .leavesHanging(square: UCIMove.squareName(f, r), piece: other)
                } }
            }
            if let reason { out.append(UnsafeSquare(file: m.toFile, rank: m.toRank, reason: reason)) }
        }
        var seen = Set<String>()
        return out.filter { seen.insert($0.square).inserted }
    }
}

extension Placement {
    func exchangeLoss(file: Int, rank: Int, color: PieceColor) -> (loss: Int, attacker: (Character, Int, Int))? {
        guard let target = piece(file, rank) else { return nil }
        let attackers = self.attackers(of: color.opposite)[rank][file].sorted { PieceInfo.value($0.0) < PieceInfo.value($1.0) }
        guard let first = attackers.first else { return nil }
        let defenders = self.attackers(of: color)[rank][file].map { PieceInfo.value($0.0) }.sorted()
        func exchange(_ onSquare: Int, _ attacking: [Int], _ defending: [Int]) -> Int {
            guard let a = attacking.first else { return 0 }
            return max(0, onSquare - exchange(a, defending, Array(attacking.dropFirst())))
        }
        let loss = exchange(PieceInfo.value(target), attackers.map { PieceInfo.value($0.0) }, defenders)
        return loss > 0 ? (loss, first) : nil
    }
}
