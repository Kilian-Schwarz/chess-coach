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

struct Pin: Equatable {
    let piece: Character
    let file: Int
    let rank: Int
    let pinner: Character
    let pinnerFile: Int
    let pinnerRank: Int
    let target: Character
    let targetFile: Int
    let targetRank: Int

    var absolute: Bool { Character(target.lowercased()) == "k" }
    var square: String { UCIMove.squareName(file, rank) }
    var pinnerSquare: String { UCIMove.squareName(pinnerFile, pinnerRank) }
    var targetSquare: String { UCIMove.squareName(targetFile, targetRank) }

    func text(_ loc: Loc = .facts) -> String {
        let owner = Placement.color(of: piece).title(loc), enemy = Placement.color(of: pinner).title(loc)
        let me = "\(owner) \(PieceInfo.name(piece, loc)) \(square)", by = "\(enemy) \(PieceInfo.name(pinner, loc)) \(pinnerSquare)"
        let against = "\(owner) \(PieceInfo.name(target, loc)) \(targetSquare)"
        return absolute
            ? "\(me) is pinned against \(against) by \(by): it cannot leave the line without exposing the king"
            : "\(me) is pinned against \(against) by \(by): moving it would lose the queen"
    }
}

struct PieceAt: Equatable {
    let piece: Character
    let square: String

    func text(_ loc: Loc = .display) -> String { "\(PieceInfo.name(piece, loc)) \(square)" }

    func phrase(_ loc: Loc = .display, accusative: Bool = false) -> String {
        loc("piece.on", "\(PieceInfo.the(piece, loc, accusative: accusative)) on \(square)")
    }
}

struct XRay: Equatable {
    let slider: PieceAt
    let blocker: PieceAt
    let target: PieceAt
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

    static func the(_ p: Character, _ loc: Loc = .display, accusative: Bool = false) -> String {
        switch (Character(p.lowercased()), accusative) {
        case ("k", false): loc("piece.king.the", "the king")
        case ("k", true): loc("piece.king.theAcc", "the king")
        case ("q", false): loc("piece.queen.the", "the queen")
        case ("q", true): loc("piece.queen.theAcc", "the queen")
        case ("r", false): loc("piece.rook.the", "the rook")
        case ("r", true): loc("piece.rook.theAcc", "the rook")
        case ("b", false): loc("piece.bishop.the", "the bishop")
        case ("b", true): loc("piece.bishop.theAcc", "the bishop")
        case ("n", false): loc("piece.knight.the", "the knight")
        case ("n", true): loc("piece.knight.theAcc", "the knight")
        case (_, false): loc("piece.pawn.the", "the pawn")
        case (_, true): loc("piece.pawn.theAcc", "the pawn")
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

    func pins(of color: PieceColor) -> [Pin] {
        var out: [Pin] = []
        let dirs: [(Int, Int)] = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)]
        for tr in 0..<8 { for tf in 0..<8 {
            guard let target = squares[tr][tf], Placement.color(of: target) == color else { continue }
            let kind = Character(target.lowercased())
            guard kind == "k" || kind == "q" else { continue }
            for (df, dr) in dirs {
                let diagonal = df != 0 && dr != 0
                var f = tf + df, r = tr + dr
                var shield: (Character, Int, Int)?
                while (0..<8).contains(f) && (0..<8).contains(r) {
                    if let p = squares[r][f] {
                        if shield == nil {
                            guard Placement.color(of: p) == color, kind == "k" || PieceInfo.value(p) < PieceInfo.value(target) else { break }
                            shield = (p, f, r)
                        } else {
                            let k = Character(p.lowercased())
                            let slides = k == "q" ? kind != "q" : (diagonal ? k == "b" : k == "r")
                            if Placement.color(of: p) != color, slides, let s = shield {
                                out.append(Pin(piece: s.0, file: s.1, rank: s.2, pinner: p, pinnerFile: f, pinnerRank: r,
                                               target: target, targetFile: tf, targetRank: tr))
                            }
                            break
                        }
                    }
                    f += df; r += dr
                }
            }
        } }
        return out
    }

    func unpinned(by move: UCIMove, color: PieceColor) -> [Pin] {
        let after = applying(move).pins(of: color)
        return pins(of: color).filter { pin in
            !(pin.file == move.fromFile && pin.rank == move.fromRank) && !after.contains(pin)
        }
    }

    func xrays(file: Int, rank: Int) -> [XRay] {
        guard let p = piece(file, rank) else { return [] }
        let kind = Character(p.lowercased())
        var dirs: [(Int, Int)] = []
        if kind == "r" || kind == "q" { dirs += [(1, 0), (-1, 0), (0, 1), (0, -1)] }
        if kind == "b" || kind == "q" { dirs += [(1, 1), (1, -1), (-1, 1), (-1, -1)] }
        var out: [XRay] = []
        for (df, dr) in dirs {
            var f = file + df, r = rank + dr
            var blocker: (Character, Int, Int)?
            while (0..<8).contains(f) && (0..<8).contains(r) {
                if let q = squares[r][f] {
                    if blocker == nil {
                        guard Placement.color(of: q) == Placement.color(of: p) else { break }
                        blocker = (q, f, r)
                    } else {
                        if Placement.color(of: q) != Placement.color(of: p), let b = blocker {
                            out.append(XRay(slider: PieceAt(piece: p, square: UCIMove.squareName(file, rank)),
                                            blocker: PieceAt(piece: b.0, square: UCIMove.squareName(b.1, b.2)),
                                            target: PieceAt(piece: q, square: UCIMove.squareName(f, r))))
                        }
                        break
                    }
                }
                f += df; r += dr
            }
        }
        return out
    }

    func discoveredTargets(by move: UCIMove, color: PieceColor) -> [XRay] {
        let after = applying(move)
        let defenders = after.attackers(of: color.opposite)
        var out: [XRay] = []
        for r in 0..<8 { for f in 0..<8 {
            guard let p = squares[r][f], Placement.color(of: p) == color, "brq".contains(Character(p.lowercased())),
                  !(f == move.fromFile && r == move.fromRank) else { continue }
            let known = Set(attacks(file: f, rank: r).map { $0.0 * 8 + $0.1 })
            for (tf, tr) in after.attacks(file: f, rank: r) where !known.contains(tf * 8 + tr) {
                guard let t = after.piece(tf, tr), Placement.color(of: t) != color else { continue }
                guard Character(t.lowercased()) == "k" || PieceInfo.value(t) > PieceInfo.value(p) || defenders[tr][tf].isEmpty else { continue }
                out.append(XRay(slider: PieceAt(piece: p, square: UCIMove.squareName(f, r)),
                                blocker: PieceAt(piece: piece(move.fromFile, move.fromRank) ?? p, square: UCIMove.squareName(move.fromFile, move.fromRank)),
                                target: PieceAt(piece: t, square: UCIMove.squareName(tf, tr))))
            }
        } }
        return out
    }

    func createdPins(by move: UCIMove, against color: PieceColor) -> [Pin] {
        let before = pins(of: color)
        return applying(move).pins(of: color).filter { !before.contains($0) }
    }

    func material(_ color: PieceColor) -> Int {
        squares.joined().reduce(0) { sum, p in
            guard let p, Placement.color(of: p) == color, Character(p.lowercased()) != "k" else { return sum }
            return sum + PieceInfo.value(p)
        }
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
