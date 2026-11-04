import Foundation

enum SAN {
    private static let letters = Array("KQRBN")

    static var displayPieces: String { Loc.display("san.pieces", "KQRBN") }

    static func core(_ m: UCIMove, in pos: ChessPosition) -> String {
        guard let piece = pos.board.squares[m.fromRank][m.fromFile] else { return m.uci }
        let kind = Character(piece.uppercased())
        let to = UCIMove.squareName(m.toFile, m.toRank)
        let capture = pos.board.squares[m.toRank][m.toFile] != nil || pos.isEnPassant(m)
        if kind == "K" && abs(m.toFile - m.fromFile) == 2 { return m.toFile > m.fromFile ? "O-O" : "O-O-O" }
        var s = ""
        if kind == "P" {
            if capture { s += String(Array("abcdefgh")[m.fromFile]) + "x" }
            s += to
            if let promo = m.promotion { s += "=" + String(promo).uppercased() }
            return s
        }
        s += String(kind)
        let others = pos.legalMoves().filter {
            $0.toFile == m.toFile && $0.toRank == m.toRank
                && !($0.fromFile == m.fromFile && $0.fromRank == m.fromRank)
                && pos.board.squares[$0.fromRank][$0.fromFile] == piece
        }
        if !others.isEmpty {
            let sameFile = others.contains { $0.fromFile == m.fromFile }
            let sameRank = others.contains { $0.fromRank == m.fromRank }
            if !sameFile { s += String(Array("abcdefgh")[m.fromFile]) }
            else if !sameRank { s += String(m.fromRank + 1) }
            else { s += UCIMove.squareName(m.fromFile, m.fromRank) }
        }
        if capture { s += "x" }
        return s + to
    }

    static func san(_ m: UCIMove, in pos: ChessPosition) -> String {
        var s = core(m, in: pos)
        let after = pos.applying(m)
        if after.isCheck { s += after.legalMoves().isEmpty ? "#" : "+" }
        return s
    }

    static func localize(_ san: String, pieces: String = SAN.displayPieces,
                         castleShort: String = Loc.display("san.castleShort", "O-O"),
                         castleLong: String = Loc.display("san.castleLong", "O-O-O")) -> String {
        if san.hasPrefix("O-O-O") { return castleLong + san.dropFirst(5) }
        if san.hasPrefix("O-O") { return castleShort + san.dropFirst(3) }
        let table = Dictionary(zip(letters, pieces)) { first, _ in first }
        return String(san.map { table[$0] ?? $0 })
    }

    static func delocalize(_ text: String, pieces: String = SAN.displayPieces) -> String {
        let table = Dictionary(zip(pieces, letters)) { first, _ in first }
        var chars = Array(text)
        if let f = chars.first, let e = table[f] { chars[0] = e }
        if let i = chars.firstIndex(of: "="), i + 1 < chars.count, let e = table[chars[i + 1]] { chars[i + 1] = e }
        return String(chars)
    }

    static func parse(_ text: String, in pos: ChessPosition, pieces: String? = nil) -> UCIMove? {
        var t = text.trimmingCharacters(in: .whitespaces)
        t = t.replacingOccurrences(of: "e.p.", with: "").replacingOccurrences(of: "0", with: "O")
        while let l = t.last, "+#!? ".contains(l) { t.removeLast() }
        if let pieces { t = delocalize(t, pieces: pieces) }
        guard !t.isEmpty else { return nil }
        let legal = pos.legalMoves()
        let exact = legal.filter { core($0, in: pos) == t }
        if exact.count == 1 { return exact[0] }
        func norm(_ s: String) -> String { s.replacingOccurrences(of: "x", with: "").replacingOccurrences(of: "=", with: "") }
        let loose = legal.filter { norm(core($0, in: pos)) == norm(t) }
        if loose.count == 1 { return loose[0] }
        let chars = Array(norm(t))
        if chars.count == 3, "KQRBN".contains(chars[0]) {
            let dest = String(chars[1...])
            let cands = legal.filter {
                pos.board.squares[$0.fromRank][$0.fromFile].map { Character($0.uppercased()) } == chars[0]
                    && UCIMove.squareName($0.toFile, $0.toRank) == dest
            }
            if cands.count == 1 { return cands[0] }
        }
        if chars.count == 5, "KQRBN".contains(chars[0]) {
            let from = String(chars[1...2]), dest = String(chars[3...])
            let cands = legal.filter {
                pos.board.squares[$0.fromRank][$0.fromFile].map { Character($0.uppercased()) } == chars[0]
                    && UCIMove.squareName($0.fromFile, $0.fromRank) == from
                    && UCIMove.squareName($0.toFile, $0.toRank) == dest
            }
            if cands.count == 1 { return cands[0] }
        }
        return nil
    }
}
