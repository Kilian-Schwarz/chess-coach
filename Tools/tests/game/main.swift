import Foundation

var failures = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; if failures < 40 { print("FAIL: \(msg())") } }
}

let data = try! Data(contentsOf: URL(fileURLWithPath: "Tools/testdata/vectors.json"))
let vectors = try! JSONSerialization.jsonObject(with: data) as! [String: Any]
let games = (vectors["game"] as! [String: Any])["games"] as! [[String: Any]]

func placement(_ fen: String) -> Placement { ChessPosition(fen: fen)!.board }

for g in games {
    let name = g["name"] as! String
    let moves = g["moves"] as! [String], sans = g["san"] as! [String], fens = g["fen"] as! [String]
    let result = g["result"] as! String

    var rec = GameRecord()
    var u = rec.update(placement: placement(ChessPosition.startFEN), whiteAtBottom: true)
    check(u.change == .newGame, "\(name): start should begin a new game")
    for i in 0..<moves.count {
        u = rec.update(placement: placement(fens[i]), whiteAtBottom: true)
        check(u.change == .moves && u.newMoves.count == 1, "\(name) move \(i + 1): change \(u.change)")
        guard let m = u.lastMove else { break }
        check(m.uci == moves[i], "\(name) move \(i + 1): \(m.uci) instead of \(moves[i])")
        check(m.san == sans[i], "\(name) move \(i + 1): SAN \(m.san) instead of \(sans[i])")
        check(u.fen == fens[i], "\(name) move \(i + 1): FEN \(u.fen) instead of \(fens[i])")
        check(m.sanDisplay == SAN.localize(sans[i]), "\(name) move \(i + 1): display \(m.sanDisplay)")
    }
    check(u.result?.pgn == (result == "*" ? nil : result) || (result == "*" && u.result?.pgn != "1-0" && u.result?.pgn != "0-1"),
          "\(name): result \(String(describing: u.result)) instead of \(result)")
    let pgn = rec.pgn(white: "A", black: "B", date: Date(timeIntervalSince1970: 0))
    check(pgn.contains("[Result \""), "\(name): PGN header")
    check(pgn.contains("1. \(sans[0])"), "\(name): PGN move text")
    if name == "fools_mate" { check(pgn.contains("1. f3 e5 2. g4 Qh4# 0-1"), "PGN fool's mate:\n\(pgn)") }

    var rec2 = GameRecord()
    rec2.update(placement: placement(ChessPosition.startFEN), whiteAtBottom: true)
    var i = 1
    while i < moves.count {
        u = rec2.update(placement: placement(fens[i]), whiteAtBottom: true)
        check(u.change == .moves && u.newMoves.count == 2, "\(name) resync at ply \(i + 1): \(u.change) with \(u.newMoves.count) moves")
        if u.fen != fens[i] { check(false, "\(name) resync FEN \(u.fen) instead of \(fens[i])") }
        i += 2
    }
    check(rec2.moves.count == (moves.count % 2 == 0 ? moves.count : moves.count - 1), "\(name): move count after resync \(rec2.moves.count)")
}

do {
    let g = games.first { $0["name"] as? String == "scholars_mate" }!
    let fens = g["fen"] as! [String]
    var rec = GameRecord()
    rec.update(placement: placement(ChessPosition.startFEN), whiteAtBottom: true)
    for f in fens.prefix(4) { rec.update(placement: placement(f), whiteAtBottom: true) }
    var u = rec.update(placement: placement(fens[2]), whiteAtBottom: true)
    check(u.change == .takeback && rec.moves.count == 3, "takeback: \(u.change), \(rec.moves.count) moves")
    u = rec.update(placement: placement(fens[3]), whiteAtBottom: true)
    check(u.change == .moves && u.fen == fens[3], "continue after takeback")
}

do {
    var rec = GameRecord()
    rec.update(placement: placement(ChessPosition.startFEN), whiteAtBottom: true)
    rec.update(placement: placement("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"), whiteAtBottom: true)
    let jump = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQ1RK1 b kq - 0 5"
    var u = rec.update(placement: placement(jump), whiteAtBottom: true)
    check(u.change == .resync && u.estimated, "resync segment: \(u.change)")
    check(u.fen.contains(" kq "), "castling rights estimated: \(u.fen)")
    check(rec.earlierSegments.count == 1, "earlier segments")
    u = rec.update(placement: placement("r1bq1rk1/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQ1RK1 w - - 1 6"), whiteAtBottom: true)
    check(u.change == .moves && u.lastMove?.san == "O-O" && u.fen.hasPrefix("r1bq1rk1/") , "castling in the segment: \(u.lastMove?.san ?? "-")")
    u = rec.update(placement: placement(ChessPosition.startFEN), whiteAtBottom: true)
    check(u.change == .newGame && rec.moves.isEmpty && !u.estimated, "new game")
}

do {
    var rec = GameRecord()
    let b = placement("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 5")
    var u = rec.update(placement: b, whiteAtBottom: true, hintedMover: .black)
    check(u.change == .firstSight && u.sideToMove == .white && u.estimated, "first sight with hint")
    u = rec.update(placement: b, whiteAtBottom: true, hintedMover: .white)
    check(u.change == .unchanged && u.sideToMove == .black, "hint corrects the side")
    u = rec.update(placement: placement("r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 6"), whiteAtBottom: true)
    check(u.change == .moves && u.lastMove?.mover == .black, "move after hint: \(u.change)")
    var rec2 = GameRecord()
    rec2.update(placement: b, whiteAtBottom: false)
    let afterWhite = placement("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPPB1PPP/RN1QK2R b KQkq - 1 5")
    u = rec2.update(placement: afterWhite, whiteAtBottom: false)
    check(u.change == .moves && u.lastMove?.mover == .white && u.sideToMove == .black, "side corrected afterwards: \(u.change)")
}

do {
    var rec = GameRecord()
    rec.update(placement: placement(ChessPosition.startFEN), whiteAtBottom: true)
    for u in ["e2e4", "e7e5", "e1e2", "e8e7", "e2e1", "e7e8"] {
        rec.update(placement: placement(rec.current!.applying(UCIMove(u)!).fen), whiteAtBottom: true)
    }
    check(rec.current!.castling.isEmpty && rec.current!.fen.contains(" - - 4 4"), "rights after king walks: \(rec.current!.fen)")
}

do {
    let p = ChessPosition(fen: "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3")!
    check(p.fen.contains(" d6 "), "en passant visible: \(p.fen)")
    check(p.nullMove.fen == "rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 1 3", "null move: \(p.nullMove.fen)")
}

print(failures == 0 ? "game OK (\(games.count) games)" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
