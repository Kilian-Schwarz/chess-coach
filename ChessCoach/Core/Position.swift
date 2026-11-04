import Foundation

enum PieceColor {
    case white, black

    var opposite: PieceColor { self == .white ? .black : .white }

    func title(_ loc: Loc = .display) -> String {
        self == .white ? loc("color.white", "White") : loc("color.black", "Black")
    }
}

struct Placement: Equatable {
    var squares: [[Character?]] = Array(repeating: Array(repeating: nil, count: 8), count: 8)

    init(screen: [[Character?]], whiteAtBottom: Bool) {
        for r in 0..<8 { for c in 0..<8 {
            let (file, rank) = Placement.chessSquare(col: c, row: r, whiteAtBottom: whiteAtBottom)
            squares[rank][file] = screen[r][c]
        } }
    }

    init() {}

    static func chessSquare(col: Int, row: Int, whiteAtBottom: Bool) -> (file: Int, rank: Int) {
        whiteAtBottom ? (col, 7 - row) : (7 - col, row)
    }

    static func screenSquare(file: Int, rank: Int, whiteAtBottom: Bool) -> (col: Int, row: Int) {
        whiteAtBottom ? (file, 7 - rank) : (7 - file, rank)
    }

    static func color(of p: Character) -> PieceColor { p.isUppercase ? .white : .black }

    func count(_ p: Character) -> Int { squares.joined().filter { $0 == p }.count }
    func missingPieces(in other: Placement) -> Int {
        var missing = 0
        for r in 0..<8 { for f in 0..<8 {
            guard let o = other.squares[r][f] else { if squares[r][f] != nil { missing += 1 }; continue }
            if squares[r][f] != o { return 0 }
        } }
        return missing
    }

    var pieceCount: Int { squares.joined().compactMap { $0 }.count }

    var isStartPosition: Bool {
        let back: [Character] = Array("RNBQKBNR")
        for f in 0..<8 {
            if squares[0][f] != back[f] || squares[1][f] != "P" || squares[6][f] != "p"
                || squares[7][f] != Character(back[f].lowercased()) { return false }
        }
        return (2...5).allSatisfy { r in squares[r].allSatisfy { $0 == nil } }
    }

    func fen(sideToMove: PieceColor) -> String {
        var rows: [String] = []
        for rank in stride(from: 7, through: 0, by: -1) {
            var row = "", empty = 0
            for file in 0..<8 {
                if let p = squares[rank][file] {
                    if empty > 0 { row += String(empty); empty = 0 }
                    row.append(p)
                } else { empty += 1 }
            }
            if empty > 0 { row += String(empty) }
            rows.append(row)
        }
        var castling = ""
        if squares[0][4] == "K" {
            if squares[0][7] == "R" { castling += "K" }
            if squares[0][0] == "R" { castling += "Q" }
        }
        if squares[7][4] == "k" {
            if squares[7][7] == "r" { castling += "k" }
            if squares[7][0] == "r" { castling += "q" }
        }
        if castling.isEmpty { castling = "-" }
        return "\(rows.joined(separator: "/")) \(sideToMove == .white ? "w" : "b") \(castling) - 0 1"
    }

    func problem(sideToMove: PieceColor) -> String? {
        guard count("K") == 1, count("k") == 1 else { return Loc.display("problem.kings", "Kings not clearly recognised") }
        if squares[0].contains(where: { $0 == "P" || $0 == "p" }) || squares[7].contains(where: { $0 == "P" || $0 == "p" }) {
            return Loc.display("problem.backRankPawn", "Pawn on the back rank detected")
        }
        if count("P") > 8 || count("p") > 8 { return Loc.display("problem.tooManyPawns", "Too many pawns detected") }
        for (color, letters) in [(PieceColor.white, Array("QRBNP")), (.black, Array("qrbnp"))] {
            let total = squares.joined().compactMap { $0 }.filter { Placement.color(of: $0) == color }.count
            if total > 16 {
                return color == .white
                    ? Loc.display("problem.tooManyWhite", "Too many white pieces detected")
                    : Loc.display("problem.tooManyBlack", "Too many black pieces detected")
            }
            let promoted = max(0, count(letters[0]) - 1) + max(0, count(letters[1]) - 2)
                + max(0, count(letters[2]) - 2) + max(0, count(letters[3]) - 2)
            if count(letters[4]) + promoted > 8 { return Loc.display("problem.impossibleMaterial", "Impossible piece count, check the recognition") }
        }
        if isInCheck(sideToMove.opposite) { return Loc.display("problem.opponentInCheck", "The side not to move is in check") }
        return nil
    }

    func kingSquare(_ color: PieceColor) -> (Int, Int)? {
        let k: Character = color == .white ? "K" : "k"
        for r in 0..<8 { for f in 0..<8 where squares[r][f] == k { return (f, r) } }
        return nil
    }

    func isInCheck(_ color: PieceColor) -> Bool {
        guard let (f, r) = kingSquare(color) else { return false }
        return isAttacked(file: f, rank: r, by: color.opposite)
    }

    func isAttacked(file: Int, rank: Int, by attacker: PieceColor) -> Bool {
        func piece(_ f: Int, _ r: Int) -> Character? {
            guard (0..<8).contains(f), (0..<8).contains(r) else { return nil }
            return squares[r][f]
        }
        func isOwn(_ p: Character?, _ letters: String) -> Bool {
            guard let p, Placement.color(of: p) == attacker else { return false }
            return letters.contains(Character(p.lowercased()))
        }
        let dir = attacker == .white ? -1 : 1
        for df in [-1, 1] where isOwn(piece(file + df, rank + dir), "p") { return true }
        for (df, dr) in [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)]
            where isOwn(piece(file + df, rank + dr), "n") { return true }
        for df in -1...1 { for dr in -1...1 where !(df == 0 && dr == 0) {
            if isOwn(piece(file + df, rank + dr), "k") { return true }
        } }
        let lines: [((Int, Int), String)] = [((1, 0), "rq"), ((-1, 0), "rq"), ((0, 1), "rq"), ((0, -1), "rq"),
                                              ((1, 1), "bq"), ((1, -1), "bq"), ((-1, 1), "bq"), ((-1, -1), "bq")]
        for ((df, dr), letters) in lines {
            var f = file + df, r = rank + dr
            while (0..<8).contains(f) && (0..<8).contains(r) {
                if let p = squares[r][f] {
                    if isOwn(p, letters) { return true }
                    break
                }
                f += df; r += dr
            }
        }
        return false
    }

    func mover(from previous: Placement) -> PieceColor? {
        var colors = Set<String>()
        var changed = 0
        for r in 0..<8 { for f in 0..<8 where squares[r][f] != previous.squares[r][f] {
            changed += 1
            if let p = squares[r][f] { colors.insert(Placement.color(of: p) == .white ? "w" : "b") }
        } }
        guard changed >= 2, changed <= 4, colors.count == 1 else { return nil }
        return colors.first == "w" ? .white : .black
    }
}

struct UCIMove: Equatable {
    let fromFile: Int, fromRank: Int, toFile: Int, toRank: Int
    let promotion: Character?

    init(fromFile: Int, fromRank: Int, toFile: Int, toRank: Int, promotion: Character? = nil) {
        self.fromFile = fromFile; self.fromRank = fromRank
        self.toFile = toFile; self.toRank = toRank
        self.promotion = promotion
    }

    init?(_ s: String) {
        let c = Array(s)
        guard c.count >= 4,
              let ff = "abcdefgh".firstIndex(of: c[0]), let fr = c[1].wholeNumberValue,
              let tf = "abcdefgh".firstIndex(of: c[2]), let tr = c[3].wholeNumberValue else { return nil }
        fromFile = "abcdefgh".distance(from: "abcdefgh".startIndex, to: ff)
        toFile = "abcdefgh".distance(from: "abcdefgh".startIndex, to: tf)
        fromRank = fr - 1; toRank = tr - 1
        promotion = c.count > 4 ? c[4] : nil
    }

    static func squareName(_ f: Int, _ r: Int) -> String { "\(Array("abcdefgh")[f])\(r + 1)" }
}
