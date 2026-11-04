import Foundation

var failures = 0
func check(_ ok: Bool, _ msg: String) { if !ok { print("FAIL: \(msg)"); failures += 1 } }

let root = FileManager.default.currentDirectoryPath
guard let book = Openings(path: root + "/ChessCoach/Resources/openings.tsv") else { print("openings.tsv cannot be loaded"); exit(1) }
check(book.count > 3000, "too few entries: \(book.count)")

func epds(_ moves: [String]) -> [String] {
    var p = Placement()
    let back: [Character] = Array("RNBQKBNR")
    for f in 0..<8 {
        p.squares[0][f] = back[f]; p.squares[1][f] = "P"; p.squares[6][f] = "p"
        p.squares[7][f] = Character(back[f].lowercased())
    }
    var rights: Set<Character> = ["K", "Q", "k", "q"]
    var side = PieceColor.white
    var out: [String] = []
    for s in moves {
        let m = UCIMove(s)!
        let piece = p.squares[m.fromRank][m.fromFile]!
        if (piece == "P" || piece == "p"), m.fromFile != m.toFile, p.squares[m.toRank][m.toFile] == nil {
            p.squares[m.fromRank][m.toFile] = nil
        }
        if (piece == "K" || piece == "k"), abs(m.toFile - m.fromFile) == 2 {
            let rf = m.toFile > m.fromFile ? 7 : 0, nf = m.toFile > m.fromFile ? 5 : 3
            p.squares[m.fromRank][nf] = p.squares[m.fromRank][rf]; p.squares[m.fromRank][rf] = nil
        }
        p.squares[m.toRank][m.toFile] = m.promotion.map { piece.isUppercase ? Character($0.uppercased()) : $0 } ?? piece
        p.squares[m.fromRank][m.fromFile] = nil
        for (f, r, c) in [(4, 0, "KQ"), (7, 0, "K"), (0, 0, "Q"), (4, 7, "kq"), (7, 7, "k"), (0, 7, "q")] as [(Int, Int, String)] {
            if (m.fromFile == f && m.fromRank == r) || (m.toFile == f && m.toRank == r) { c.forEach { rights.remove($0) } }
        }
        side = side.opposite
        let cs = String("KQkq".filter { rights.contains($0) })
        out.append(Openings.epd(placement: p, sideToMove: side, castling: cs,
                                enPassant: Openings.enPassantTarget(placement: p, lastMove: m)))
    }
    return out
}

func name(_ moves: [String]) -> OpeningInfo? { book.longestName(along: epds(moves)) }

let vecURL = URL(fileURLWithPath: root + "/Tools/testdata/vectors.json")
if let data = try? Data(contentsOf: vecURL),
   let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
   let cases = obj["openings"] as? [[String: Any]] {
    for c in cases {
        let moves = (c["moves"] as! String).split(separator: " ").map(String.init)
        let hit = name(moves)
        check(hit?.eco == c["eco"] as? String && hit?.name == c["name"] as? String,
              "\(c["moves"] as! String): expected \(c["eco"]!) \(c["name"]!), got \(hit?.display ?? "nil")")
    }
    check(cases.count >= 5, "too few vectors")
} else { check(false, "vectors.json: key openings is missing") }

let a = name("e2e4 e7e5 g1f3 b8c6 f1c4".split(separator: " ").map(String.init))
let b = name("g1f3 b8c6 e2e4 e7e5 f1c4".split(separator: " ").map(String.init))
check(a != nil && a == b, "Italian Game by transposition")
check(a?.display == "Italian Game (C50)", "Italian Game: \(a?.display ?? "nil")")

let c = name("e2e4 e7e5 g1f3 b8c6 f1c4 a7a6 a2a3 h7h6".split(separator: " ").map(String.init))
check(c != nil, "longest name along the history")

let ep = epds("e2e4 a7a6 e4e5 d7d5".split(separator: " ").map(String.init))
check(ep[0].hasSuffix(" -"), "no en passant square after 1.e4")
check(ep[3].hasSuffix(" d6"), "expected en passant d6: \(ep[3])")

check(epds(["e2e4"])[0] == "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq -", "EPD 1.e4: \(epds(["e2e4"])[0])")

if failures == 0 { print("openings: all tests OK"); exit(0) } else { exit(1) }
