import Foundation
import CoreGraphics
import ImageIO

var failures = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; print("FAIL: \(msg())") }
}

func load(_ path: String) -> RGBImage {
    guard let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: path) as CFURL, nil),
          let cg = CGImageSourceCreateImageAtIndex(src, 0, nil), let img = RGBImage(cgImage: cg) else {
        fatalError("image cannot be loaded: \(path)")
    }
    return img
}

let py = Process()
py.executableURL = URL(fileURLWithPath: "/usr/bin/env")
py.arguments = ["python3", "Tools/make_board.py", "build/testboards"]
py.standardOutput = FileHandle.nullDevice
try! py.run(); py.waitUntilExit()
check(py.terminationStatus == 0, "make_board.py failed")
let dir = "build/testboards"

func learn(_ path: String) -> (PieceTemplates, Bool) {
    let img = load(path)
    var t = PieceTemplates()
    let board = BoardDetector.detect(img)!
    let white = t.learnStart(BoardScanner.analyze(img, board: board, templates: t).features)
    return (t, white)
}

struct Seen { var scan: ScanResult; var placement: Placement }

func see(_ img: RGBImage, _ t: PieceTemplates, _ white: Bool) -> Seen? {
    guard let scan = BoardScanner.scan(img, templates: t) else { return nil }
    return Seen(scan: scan, placement: Placement(screen: scan.screen, whiteAtBottom: white))
}

let vectors = try! JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: "Tools/testdata/vectors.json"))) as! [String: Any]
let flow = vectors["gameFlow"] as! [String: Any]

func sq(_ f: Int, _ r: Int) -> String { UCIMove.squareName(f, r) }

print("--- unsafe squares")
for v in flow["unsafe"] as! [[String: Any]] {
    let fen = v["fen"] as! String, from = Array((v["from"] as! String).utf8)
    let pos = ChessPosition(fen: fen)!
    let file = Int(from[0]) - 97, rank = Int(from[1]) - 49
    let lifted = LiftedPiece(file: file, rank: rank, piece: pos.board.squares[rank][file]!)
    var got: [String: String] = [:]
    for u in lifted.unsafeSquares(in: pos) {
        switch u.reason {
        case .undefended: got[u.square] = "undefended"
        case .cheaperAttacker: got[u.square] = "cheaperAttacker"
        case .leavesHanging(let s, _): got[u.square] = "leavesHanging:\(s)"
        }
    }
    check(got == (v["unsafe"] as! [String: String]), "\(fen) \(v["from"]!): \(got)")
}
do {
    let pos = ChessPosition(fen: "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 1")!
    let l = LiftedPiece(file: 2, rank: 3, piece: "B")
    let u = l.unsafeSquares(in: pos)
    check(u.count == 3 && LiftedPiece.summary(u, piece: "B", Loc.facts)?.contains("3 squares") == true, "summary many")
    let one = LiftedPiece(file: 4, rank: 3, piece: "P").unsafeSquares(in: pos)
    check(one.isEmpty, "pawn e4 has no unsafe squares: \(one)")
    let nd = ChessPosition(fen: "4k3/8/3p4/8/4P3/8/8/4K3 w - - 0 1")!
    let s = LiftedPiece.summary(LiftedPiece(file: 4, rank: 3, piece: "P").unsafeSquares(in: nd), piece: "P", Loc.facts)
    check(s == "Careful: Pawn on e5 would be undefended.", "text \(s ?? "nil")")
    check(LiftedPiece(file: 4, rank: 0, piece: "K").unsafeSquares(in: pos).map(\.square) == ["d2"], "king d2 leaves f2 hanging")
    check(LiftedPiece(file: 2, rank: 3, piece: "B").unsafeSquares(in: ChessPosition(fen: pos.fen.replacingOccurrences(of: " w ", with: " b "))!).isEmpty, "wrong side")
}

print("--- lifted piece from placements")
for v in flow["lifted"] as! [[String: Any]] {
    let last = ChessPosition(fen: v["last"] as! String)!
    let cur = ChessPosition(fen: (v["current"] as! String) + " w - - 0 1")!.board
    let got = LiftedPiece.detect(last: last, current: cur)?.square
    check(got == v["lifted"] as? String, "\(v["comment"] as? String ?? "") \(v["current"]!): \(got ?? "nil")")
}

print("--- game end observations")
let ge = flow["gameEnd"] as! [String: Any]
check(GameEndDetector.minPlies == ge["minPlies"] as! Int && GameEndDetector.coveredSeconds == (ge["coveredSeconds"] as! NSNumber).doubleValue
      && GameEndDetector.goneSeconds == (ge["goneSeconds"] as! NSNumber).doubleValue, "thresholds")
for c in ge["cases"] as! [[String: Any]] {
    var det = GameEndDetector()
    let t0 = Date(timeIntervalSince1970: 1_000_000)
    var fired: (Int, GameEnd)?
    for (i, s) in (c["steps"] as! [[String: Any]]).enumerated() {
        let view: BoardView = s["view"] as! String == "readable" ? .readable : (s["view"] as! String == "gone" ? .gone : .covered)
        var result: GameResult?
        switch s["result"] as? String {
        case "checkmate:w": result = .checkmate(winner: .white)
        case "checkmate:b": result = .checkmate(winner: .black)
        case "stalemate": result = .stalemate
        default: break
        }
        let e = det.observe(view, plies: s["plies"] as! Int, result: result, isStart: s["isStart"] as? Bool ?? false,
                            at: t0.addingTimeInterval((s["t"] as! NSNumber).doubleValue))
        if let e, fired == nil { fired = (i, e) }
    }
    let name = c["name"] as! String
    if let want = c["end"] as? [String: Any] {
        check(fired?.0 == want["step"] as? Int && fired?.1.reason.rawValue == want["reason"] as? String && fired?.1.result == want["result"] as? String && fired?.1.silent == want["silent"] as? Bool ?? false,
              "\(name): got \(String(describing: fired))")
    } else {
        check(fired == nil, "\(name): unexpected end \(String(describing: fired))")
    }
}

print("--- synthetic images")
let (templates, white) = learn("\(dir)/start_white.png")
let midFen = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R"
let midPos = ChessPosition(fen: midFen + " w KQkq - 0 1")!
if let none = see(load("\(dir)/popup_none.png"), templates, white) {
    check(none.placement == midPos.board, "plain board read")
    check(BoardReadability.isReadable(none.scan.features), "plain board must be readable (covered \(BoardReadability.coveredSquares(none.scan.features)))")
} else { check(false, "plain board not found") }
let startImg = load("\(dir)/start_white.png")
let startFeatures = BoardScanner.analyze(startImg, board: BoardDetector.detect(startImg)!, templates: templates).features
let colors = BoardColors.learn(startFeatures)
do {
    let plain = load("\(dir)/popup_none.png")
    let rect = BoardDetector.detect(plain)!
    check(BoardReadability.view(BoardScanner.analyze(plain, board: rect, templates: templates).features, colors: colors) == .readable, "plain board view")
    for name in ["popup_blank", "popup_blur"] {
        let img = load("\(dir)/\(name).png")
        let f = BoardScanner.analyze(img, board: rect, templates: templates).features
        check(!BoardReadability.isReadable(f), "\(name) must be unreadable (covered \(BoardReadability.coveredSquares(f)))")
        let ring = BoardReadability.ringMatch(f, colors)
        check(ring >= BoardReadability.ringShare, "\(name) ring must still match (\(ring))")
        check(BoardReadability.view(f, colors: colors) == .covered, "\(name) view")
    }
    let other = BoardScanner.analyze(plain, board: BoardRect(x: 0, y: 0, size: 576), templates: templates).features
    let ring = BoardReadability.ringMatch(other, colors)
    check(ring < BoardReadability.ringShare, "other content must not match the ring (\(ring))")
    check(BoardReadability.view(other, colors: nil) != .covered, "no colours known: never covered")
}
if let s = see(load("\(dir)/lift_dots.png"), templates, white) {
    check(BoardReadability.isReadable(s.scan.features), "dots keep the board readable")
    let lifted = LiftedPiece.detect(last: midPos, current: s.placement)
    check(lifted?.square == "c4", "lift_dots: \(String(describing: lifted?.square)) placement \(s.placement.fen(sideToMove: .white))")
} else { check(false, "lift_dots: no board") }
if let s = see(load("\(dir)/lift_offgrid.png"), templates, white) {
    check(LiftedPiece.detect(last: midPos, current: s.placement)?.square == "c4", "lift_offgrid")
} else { check(false, "lift_offgrid: no board") }
if let s = see(load("\(dir)/lift_dots_only.png"), templates, white) {
    check(LiftedPiece.detect(last: midPos, current: s.placement)?.square == "c4", "lift_dots_only")
    let unsafe = LiftedPiece(file: 2, rank: 3, piece: "B").unsafeSquares(in: midPos).map(\.square).sorted()
    check(unsafe == ["a6", "e6", "f7"], "unsafe from the image position \(unsafe)")
} else { check(false, "lift_dots_only: no board") }

let privateDir = "intern/fixtures"
if FileManager.default.fileExists(atPath: "\(privateDir)/chesscom_gameover_popup_white_mated.png"),
   FileManager.default.fileExists(atPath: "\(privateDir)/chesscom_start_white.png") {
    print("--- private fixture: game over dialog")
    let (t, w) = learn("\(privateDir)/chesscom_start_white.png")
    let img = load("\(privateDir)/chesscom_gameover_popup_white_mated.png")
    let startPrivate = load("\(privateDir)/chesscom_start_white.png")
    let c = BoardColors.learn(BoardScanner.analyze(startPrivate, board: BoardDetector.detect(startPrivate)!, templates: t).features)
    let seen = see(img, t, w)
    let rect = seen?.scan.board ?? BoardRect(x: 33, y: 53, size: 1136)
    let f = BoardScanner.analyze(img, board: rect, templates: t).features
    let ring = BoardReadability.ringMatch(f, c)
    print("dialog frame: scan \(seen == nil ? "nil" : "found"), ring \(ring)")
    check(seen == nil || BoardReadability.view(seen!.scan.features, colors: c) != .readable, "dialog frame must not count as readable")
    check(ring >= BoardReadability.ringShare, "dialog frame keeps the board ring (\(ring))")
    var det = GameEndDetector()
    let t0 = Date(timeIntervalSince1970: 1_000_000)
    var end: GameEnd?
    for i in 0..<5 where end == nil {
        end = det.observe(.covered, plies: 40, result: nil, isStart: false, at: t0.addingTimeInterval(Double(i)))
    }
    check(end?.reason == .overlay && end?.result == "*" && end?.silent == false, "dialog frame ends the game as overlay without a guessed result: \(String(describing: end))")
    let lichess = load("\(privateDir)/lichess_start_white.png")
    let wrongRect = BoardScanner.analyze(lichess, board: rect, templates: t).features
    check(BoardReadability.ringMatch(wrongRect, c) < BoardReadability.ringShare, "other site in the region must not look like the dialog frame")
}

print(failures == 0 ? "gameflow OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
