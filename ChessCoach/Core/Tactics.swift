import Foundation

struct HangingPiece: Equatable {
    let piece: Character
    let file: Int
    let rank: Int
    let cheaperAttacker: Bool

    var square: String { UCIMove.squareName(file, rank) }

    func text(_ loc: Loc = .display) -> String {
        let name = PieceInfo.name(piece, loc), sq = square
        return cheaperAttacker
            ? loc("hanging.cheaper", "\(name) \(sq) is attacked by a cheaper piece")
            : loc("hanging.unprotected", "\(name) \(sq) is attacked and unprotected")
    }
}

enum PieceInfo {
    static let values: [Character: Int] = ["k": 100, "q": 9, "r": 5, "b": 3, "n": 3, "p": 1]

    static func name(_ p: Character, _ loc: Loc = .display) -> String {
        switch Character(p.lowercased()) {
        case "k": loc("piece.king", "King")
        case "q": loc("piece.queen", "Queen")
        case "r": loc("piece.rook", "Rook")
        case "b": loc("piece.bishop", "Bishop")
        case "n": loc("piece.knight", "Knight")
        default: loc("piece.pawn", "Pawn")
        }
    }

    static func value(_ p: Character) -> Int { values[Character(p.lowercased())] ?? 0 }
}

extension UCIMove {
    var uci: String {
        "\(UCIMove.squareName(fromFile, fromRank))\(UCIMove.squareName(toFile, toRank))\(promotion.map(String.init) ?? "")"
    }
}

extension Placement {
    func piece(_ f: Int, _ r: Int) -> Character? {
        guard (0..<8).contains(f), (0..<8).contains(r) else { return nil }
        return squares[r][f]
    }

    func applying(_ m: UCIMove) -> Placement {
        var p = self
        guard let piece = squares[m.fromRank][m.fromFile] else { return p }
        let kind = Character(piece.lowercased())
        p.squares[m.fromRank][m.fromFile] = nil
        if kind == "p" && m.fromFile != m.toFile && squares[m.toRank][m.toFile] == nil {
            p.squares[m.fromRank][m.toFile] = nil
        }
        if kind == "k" && abs(m.toFile - m.fromFile) == 2 {
            let (rf, rt) = m.toFile > m.fromFile ? (7, 5) : (0, 3)
            p.squares[m.fromRank][rt] = p.squares[m.fromRank][rf]
            p.squares[m.fromRank][rf] = nil
        }
        var placed = piece
        if let promo = m.promotion { placed = piece.isUppercase ? Character(promo.uppercased()) : promo }
        p.squares[m.toRank][m.toFile] = placed
        return p
    }

    func attacks(file: Int, rank: Int) -> [(Int, Int)] {
        guard let p = piece(file, rank) else { return [] }
        let white = p.isUppercase
        var result: [(Int, Int)] = []
        func onBoard(_ f: Int, _ r: Int) -> Bool { (0..<8).contains(f) && (0..<8).contains(r) }
        func slide(_ dirs: [(Int, Int)]) {
            for (df, dr) in dirs {
                var f = file + df, r = rank + dr
                while onBoard(f, r) {
                    result.append((f, r))
                    if squares[r][f] != nil { break }
                    f += df; r += dr
                }
            }
        }
        switch Character(p.lowercased()) {
        case "p":
            let dr = white ? 1 : -1
            for df in [-1, 1] where onBoard(file + df, rank + dr) { result.append((file + df, rank + dr)) }
        case "n":
            for (df, dr) in [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)]
                where onBoard(file + df, rank + dr) { result.append((file + df, rank + dr)) }
        case "k":
            for df in -1...1 { for dr in -1...1 where !(df == 0 && dr == 0) && onBoard(file + df, rank + dr) {
                result.append((file + df, rank + dr))
            } }
        case "b": slide([(1, 1), (1, -1), (-1, 1), (-1, -1)])
        case "r": slide([(1, 0), (-1, 0), (0, 1), (0, -1)])
        case "q": slide([(1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1)])
        default: break
        }
        return result
    }

    func attackers(of color: PieceColor) -> [[[(Character, Int, Int)]]] {
        var map = Array(repeating: Array(repeating: [(Character, Int, Int)](), count: 8), count: 8)
        for r in 0..<8 { for f in 0..<8 {
            guard let p = squares[r][f], Placement.color(of: p) == color else { continue }
            for (tf, tr) in attacks(file: f, rank: r) { map[tr][tf].append((p, f, r)) }
        } }
        return map
    }

    func hanging(of color: PieceColor) -> [HangingPiece] {
        let enemy = attackers(of: color.opposite), friends = attackers(of: color)
        var out: [HangingPiece] = []
        for r in 0..<8 { for f in 0..<8 {
            guard let p = squares[r][f], Placement.color(of: p) == color, Character(p.lowercased()) != "k" else { continue }
            guard let cheapest = enemy[r][f].map({ PieceInfo.value($0.0) }).min() else { continue }
            if friends[r][f].isEmpty {
                out.append(HangingPiece(piece: p, file: f, rank: r, cheaperAttacker: false))
            } else if cheapest < PieceInfo.value(p) {
                out.append(HangingPiece(piece: p, file: f, rank: r, cheaperAttacker: true))
            }
        } }
        return out
    }

    func hangingPieces(of color: PieceColor, _ loc: Loc = .display) -> [String] {
        hanging(of: color).map { $0.text(loc) }
    }

    func pieceList(_ color: PieceColor) -> String {
        let kinds: [(Character, String, String)] = [("k", "King", "Kings"), ("q", "Queen", "Queens"), ("r", "Rook", "Rooks"),
                                                    ("b", "Bishop", "Bishops"), ("n", "Knight", "Knights"), ("p", "Pawn", "Pawns")]
        return kinds.compactMap { kind, one, many in
            let sqs = (0..<8).flatMap { r in (0..<8).compactMap { f -> String? in
                guard let p = squares[r][f], Placement.color(of: p) == color, Character(p.lowercased()) == kind else { return nil }
                return UCIMove.squareName(f, r)
            } }
            return sqs.isEmpty ? nil : "\(sqs.count == 1 ? one : many) \(sqs.joined(separator: " "))"
        }.joined(separator: ", ")
    }

    func describeMove(_ m: UCIMove, _ loc: Loc = .display, check: Bool = true) -> String {
        guard let p = piece(m.fromFile, m.fromRank) else { return m.uci }
        let from = UCIMove.squareName(m.fromFile, m.fromRank), to = UCIMove.squareName(m.toFile, m.toRank)
        let name = PieceInfo.name(p, loc)
        var text: String
        if Character(p.lowercased()) == "k" && abs(m.toFile - m.fromFile) == 2 {
            text = m.toFile > m.fromFile ? loc("move.castleShort", "short castling") : loc("move.castleLong", "long castling")
        } else if let victim = piece(m.toFile, m.toRank) {
            let target = PieceInfo.name(victim, loc)
            text = loc("move.capture", "\(name) \(from) captures \(target) \(to)")
        } else if Character(p.lowercased()) == "p" && m.fromFile != m.toFile {
            text = loc("move.enPassant", "Pawn \(from) captures en passant on \(to)")
        } else {
            text = loc("move.plain", "\(name) \(from)–\(to)")
        }
        if let promo = m.promotion { text += loc("move.promotes", ", promotes to \(PieceInfo.name(promo, loc))") }
        if check, applying(m).isInCheck(Placement.color(of: p).opposite) { text += loc("move.check", ", check") }
        return text
    }

    func describeLine(_ uciMoves: [String], maxMoves: Int = 6, _ loc: Loc = .display) -> String {
        var pos = self
        var out: [String] = []
        for u in uciMoves.prefix(maxMoves) {
            guard let m = UCIMove(u) else { break }
            out.append(pos.describeMove(m, loc))
            pos = pos.applying(m)
        }
        return out.joined(separator: "; ")
    }
}
