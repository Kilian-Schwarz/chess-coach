import Foundation

struct CastlingRights: Equatable {
    var whiteKing = false, whiteQueen = false, blackKing = false, blackQueen = false

    static let all = CastlingRights(whiteKing: true, whiteQueen: true, blackKing: true, blackQueen: true)
    static let none = CastlingRights()

    var isEmpty: Bool { !(whiteKing || whiteQueen || blackKing || blackQueen) }

    var fen: String {
        let s = (whiteKing ? "K" : "") + (whiteQueen ? "Q" : "") + (blackKing ? "k" : "") + (blackQueen ? "q" : "")
        return s.isEmpty ? "-" : s
    }

    init(whiteKing: Bool = false, whiteQueen: Bool = false, blackKing: Bool = false, blackQueen: Bool = false) {
        self.whiteKing = whiteKing; self.whiteQueen = whiteQueen
        self.blackKing = blackKing; self.blackQueen = blackQueen
    }

    init(fen: String) {
        self.init(whiteKing: fen.contains("K"), whiteQueen: fen.contains("Q"),
                  blackKing: fen.contains("k"), blackQueen: fen.contains("q"))
    }

    func limited(to board: Placement) -> CastlingRights {
        var r = self
        if board.squares[0][4] != "K" { r.whiteKing = false; r.whiteQueen = false }
        if board.squares[7][4] != "k" { r.blackKing = false; r.blackQueen = false }
        if board.squares[0][7] != "R" { r.whiteKing = false }
        if board.squares[0][0] != "R" { r.whiteQueen = false }
        if board.squares[7][7] != "r" { r.blackKing = false }
        if board.squares[7][0] != "r" { r.blackQueen = false }
        return r
    }
}

enum GameResult: Equatable {
    case checkmate(winner: PieceColor)
    case stalemate
    case insufficientMaterial
    case fiftyMoveRule

    var pgn: String {
        switch self {
        case .checkmate(let w): return w == .white ? "1-0" : "0-1"
        default: return "1/2-1/2"
        }
    }

    func title(_ loc: Loc = .display) -> String {
        switch self {
        case .checkmate(let w): loc("result.checkmate", "Checkmate, \(w.title(loc)) wins")
        case .stalemate: loc("result.stalemate", "Stalemate")
        case .insufficientMaterial: loc("result.insufficientMaterial", "Draw by insufficient material")
        case .fiftyMoveRule: loc("result.fiftyMoveRule", "Draw by the 50-move rule")
        }
    }
}

struct ChessPosition: Equatable {
    var board: Placement
    var turn: PieceColor
    var castling: CastlingRights
    var enPassantFile: Int?
    var halfmoveClock: Int
    var fullmoveNumber: Int

    init(board: Placement, turn: PieceColor, castling: CastlingRights, enPassantFile: Int? = nil,
         halfmoveClock: Int = 0, fullmoveNumber: Int = 1) {
        self.board = board; self.turn = turn; self.castling = castling
        self.enPassantFile = enPassantFile
        self.halfmoveClock = halfmoveClock; self.fullmoveNumber = fullmoveNumber
    }

    static let startFEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
    static var start: ChessPosition { ChessPosition(fen: startFEN)! }

    init?(fen: String) {
        let f = fen.split(separator: " ").map(String.init)
        guard f.count >= 2 else { return nil }
        let rows = f[0].split(separator: "/").map(String.init)
        guard rows.count == 8 else { return nil }
        var b = Placement()
        for (i, row) in rows.enumerated() {
            let rank = 7 - i
            var file = 0
            for ch in row {
                if let n = ch.wholeNumberValue { file += n }
                else {
                    guard file < 8, "kqrbnpKQRBNP".contains(ch) else { return nil }
                    b.squares[rank][file] = ch
                    file += 1
                }
            }
            guard file == 8 else { return nil }
        }
        board = b
        guard f[1] == "w" || f[1] == "b" else { return nil }
        turn = f[1] == "w" ? .white : .black
        castling = f.count > 2 ? CastlingRights(fen: f[2]) : .none
        enPassantFile = nil
        if f.count > 3, f[3].count == 2, let ch = f[3].first, let idx = "abcdefgh".firstIndex(of: ch) {
            enPassantFile = "abcdefgh".distance(from: "abcdefgh".startIndex, to: idx)
        }
        halfmoveClock = f.count > 4 ? Int(f[4]) ?? 0 : 0
        fullmoveNumber = f.count > 5 ? max(1, Int(f[5]) ?? 1) : 1
    }

    var fen: String {
        let placement = board.fen(sideToMove: turn).split(separator: " ")[0]
        var ep = "-"
        if let file = enPassantFile {
            let rank = turn == .white ? 5 : 2
            if legalMoves().contains(where: { $0.toFile == file && $0.toRank == rank && isEnPassant($0) }) {
                ep = UCIMove.squareName(file, rank)
            }
        }
        return "\(placement) \(turn == .white ? "w" : "b") \(castling.fen) \(ep) \(halfmoveClock) \(fullmoveNumber)"
    }

    var nullMove: ChessPosition {
        var p = self
        p.turn = turn.opposite
        p.enPassantFile = nil
        p.halfmoveClock += 1
        if turn == .black { p.fullmoveNumber += 1 }
        return p
    }

    func isEnPassant(_ m: UCIMove) -> Bool {
        guard let p = board.squares[m.fromRank][m.fromFile], p == "P" || p == "p" else { return false }
        return m.fromFile != m.toFile && board.squares[m.toRank][m.toFile] == nil
    }

    private func pseudoMoves() -> [UCIMove] {
        var out: [UCIMove] = []
        out.reserveCapacity(48)
        let sq = board.squares
        let white = turn == .white
        for r in 0..<8 { for f in 0..<8 {
            guard let p = sq[r][f], (p.isUppercase) == white else { continue }
            let kind = Character(p.lowercased())
            func add(_ tf: Int, _ tr: Int) { out.append(UCIMove(fromFile: f, fromRank: r, toFile: tf, toRank: tr)) }
            func step(_ dirs: [(Int, Int)], slide: Bool) {
                for (df, dr) in dirs {
                    var tf = f + df, tr = r + dr
                    while (0..<8).contains(tf) && (0..<8).contains(tr) {
                        if let t = sq[tr][tf] {
                            if t.isUppercase != white { add(tf, tr) }
                            break
                        }
                        add(tf, tr)
                        if !slide { break }
                        tf += df; tr += dr
                    }
                }
            }
            switch kind {
            case "n": step([(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)], slide: false)
            case "b": step([(1, 1), (1, -1), (-1, 1), (-1, -1)], slide: true)
            case "r": step([(1, 0), (-1, 0), (0, 1), (0, -1)], slide: true)
            case "q": step([(1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1)], slide: true)
            case "k":
                step([(1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1)], slide: false)
                let home = white ? 0 : 7
                if r == home && f == 4 && !board.isAttacked(file: 4, rank: home, by: turn.opposite) {
                    let canKing = white ? castling.whiteKing : castling.blackKing
                    let canQueen = white ? castling.whiteQueen : castling.blackQueen
                    let rook: Character = white ? "R" : "r"
                    if canKing, sq[home][7] == rook, sq[home][5] == nil, sq[home][6] == nil,
                       !board.isAttacked(file: 5, rank: home, by: turn.opposite) { add(6, home) }
                    if canQueen, sq[home][0] == rook, sq[home][1] == nil, sq[home][2] == nil, sq[home][3] == nil,
                       !board.isAttacked(file: 3, rank: home, by: turn.opposite) { add(2, home) }
                }
            default:
                let dir = white ? 1 : -1, startRank = white ? 1 : 6, lastRank = white ? 7 : 0
                func addPawn(_ tf: Int, _ tr: Int) {
                    if tr == lastRank {
                        for promo in ["q", "r", "b", "n"] as [Character] {
                            out.append(UCIMove(fromFile: f, fromRank: r, toFile: tf, toRank: tr, promotion: promo))
                        }
                    } else { add(tf, tr) }
                }
                let one = r + dir
                guard (0..<8).contains(one) else { break }
                if sq[one][f] == nil {
                    addPawn(f, one)
                    if r == startRank, sq[one + dir][f] == nil { add(f, one + dir) }
                }
                for df in [-1, 1] where (0..<8).contains(f + df) {
                    if let t = sq[one][f + df], t.isUppercase != white { addPawn(f + df, one) }
                    else if sq[one][f + df] == nil, let ep = enPassantFile, ep == f + df, one == (white ? 5 : 2),
                            sq[r][f + df] == (white ? Character("p") : Character("P")) { add(f + df, one) }
                }
            }
        } }
        return out
    }

    func legalMoves() -> [UCIMove] {
        pseudoMoves().filter { m in !board.applying(m).isInCheck(turn) }
    }

    func isLegal(_ m: UCIMove) -> Bool { legalMoves().contains(m) }

    func applying(_ m: UCIMove) -> ChessPosition {
        var p = self
        let piece = board.squares[m.fromRank][m.fromFile]
        let isPawn = piece == "P" || piece == "p"
        let capture = board.squares[m.toRank][m.toFile] != nil || isEnPassant(m)
        p.board = board.applying(m)
        p.turn = turn.opposite
        if turn == .black { p.fullmoveNumber += 1 }
        p.halfmoveClock = (isPawn || capture) ? 0 : halfmoveClock + 1
        p.enPassantFile = (isPawn && abs(m.toRank - m.fromRank) == 2) ? m.fromFile : nil
        if piece == "K" { p.castling.whiteKing = false; p.castling.whiteQueen = false }
        if piece == "k" { p.castling.blackKing = false; p.castling.blackQueen = false }
        for (f, r) in [(m.fromFile, m.fromRank), (m.toFile, m.toRank)] {
            if (f, r) == (7, 0) { p.castling.whiteKing = false }
            if (f, r) == (0, 0) { p.castling.whiteQueen = false }
            if (f, r) == (7, 7) { p.castling.blackKing = false }
            if (f, r) == (0, 7) { p.castling.blackQueen = false }
        }
        return p
    }

    var isCheck: Bool { board.isInCheck(turn) }

    var hasInsufficientMaterial: Bool {
        var minors: [(Character, Int)] = []
        for r in 0..<8 { for f in 0..<8 {
            guard let p = board.squares[r][f] else { continue }
            switch Character(p.lowercased()) {
            case "k": continue
            case "b", "n": minors.append((Character(p.lowercased()), (r + f) % 2))
            default: return false
            }
        } }
        if minors.count <= 1 { return true }
        return minors.allSatisfy { $0.0 == "b" && $0.1 == minors[0].1 }
    }

    var result: GameResult? {
        if legalMoves().isEmpty { return isCheck ? .checkmate(winner: turn.opposite) : .stalemate }
        if hasInsufficientMaterial { return .insufficientMaterial }
        if halfmoveClock >= 100 { return .fiftyMoveRule }
        return nil
    }

    func perft(_ depth: Int) -> Int {
        if depth == 0 { return 1 }
        let moves = legalMoves()
        if depth == 1 { return moves.count }
        return moves.reduce(0) { $0 + applying($1).perft(depth - 1) }
    }
}
