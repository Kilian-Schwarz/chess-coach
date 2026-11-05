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

func sq(_ name: String) -> (file: Int, rank: Int) {
    let c = Array(name)
    return ("abcdefgh".firstIndex(of: c[0]).map { "abcdefgh".distance(from: "abcdefgh".startIndex, to: $0) }!, c[1].wholeNumberValue! - 1)
}
func names(_ s: [(file: Int, rank: Int)]) -> [String] { s.map { UCIMove.squareName($0.file, $0.rank) }.sorted() }

struct Fixture {
    var name: String
    var start: String
    var mid: String
    var expectedPlacement: String
    var marked: [String]
    var checks: [String]
    var mover: PieceColor?
}

func run(_ fx: Fixture) {
    print("--- \(fx.name)")
    let start = load(fx.start), mid = load(fx.mid)
    guard let b1 = BoardDetector.detect(start) else { check(false, "\(fx.name): no board in the start image"); return }
    var templates = PieceTemplates()
    let s1 = BoardScanner.analyze(start, board: b1, templates: templates)
    check(s1.looksLikeStart, "\(fx.name): start position not recognised")
    guard s1.looksLikeStart else { return }
    let whiteBottom = templates.learnStart(s1.features)
    check(templates.squareColors != nil, "\(fx.name): square colours not learned")

    let startMark = SideToMove.detect(scan: s1, whiteAtBottom: whiteBottom, templates: templates)
    check(startMark.marked.isEmpty && startMark.mover == nil, "\(fx.name): start image has a marking \(names(startMark.marked))")

    guard let res = BoardScanner.scan(mid, templates: templates) else { check(false, "\(fx.name): no board in the image"); return }
    let guess = BoardScanner.guessWhiteAtBottom(res.screen)
    let wb = guess ?? whiteBottom
    let placement = Placement(screen: res.screen, whiteAtBottom: wb)
    let got = String(placement.fen(sideToMove: .white).split(separator: " ")[0])
    check(got == fx.expectedPlacement, "\(fx.name): placement \(got) instead of \(fx.expectedPlacement)")
    check(wb == whiteBottom, "\(fx.name): orientation \(wb) instead of \(whiteBottom)")

    let mark = SideToMove.detect(scan: res, whiteAtBottom: wb, templates: templates)
    check(names(mark.marked) == fx.marked.sorted(), "\(fx.name): marked \(names(mark.marked)) instead of \(fx.marked.sorted())")
    check(names(mark.checkSquares) == fx.checks.sorted(), "\(fx.name): check \(names(mark.checkSquares)) instead of \(fx.checks.sorted())")
    check(mark.mover == fx.mover, "\(fx.name): mover \(String(describing: mark.mover)) instead of \(String(describing: fx.mover))")
    print("marked \(names(mark.marked)), check \(names(mark.checkSquares)), mover \(String(describing: mark.mover?.title(.facts)))")

    let noLearn = SideToMove.detect(scan: res, whiteAtBottom: wb, templates: PieceTemplates())
    check(names(noLearn.marked) == fx.marked.sorted() && noLearn.mover == fx.mover, "\(fx.name): without learned colours \(names(noLearn.marked))")

    if let data = try? JSONEncoder().encode(templates),
       var obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
        obj["squareColors"] = nil
        if var t = obj["templates"] as? [String: [[String: Any]]] {
            for k in t.keys { t[k] = t[k]!.map { var d = $0; d["bg"] = nil; return d } }
            obj["templates"] = t
        }
        let old = try! JSONSerialization.data(withJSONObject: obj)
        let decoded = try? JSONDecoder().decode(PieceTemplates.self, from: old)
        check(decoded != nil && decoded!.squareColors == nil, "\(fx.name): old pieces.json cannot be loaded")
        if let d = decoded, let again = BoardScanner.scan(mid, templates: d) {
            check(again.screen == res.screen, "\(fx.name): recognition with old pieces.json differs")
        }
    }
}

let boards = "build/testboards"
let py = Process()
py.executableURL = URL(fileURLWithPath: "/usr/bin/env")
py.arguments = ["python3", "Tools/make_board.py", boards]
py.standardOutput = FileHandle.nullDevice
try! py.run(); py.waitUntilExit()
check(py.terminationStatus == 0, "make_board.py failed")

let synthMid = (try! String(contentsOfFile: "\(boards)/expected.txt", encoding: .utf8)).trimmingCharacters(in: .whitespacesAndNewlines)
run(Fixture(name: "synthetic white", start: "\(boards)/start_white.png", mid: "\(boards)/mid_white.png",
            expectedPlacement: synthMid, marked: ["d2", "d3"], checks: [], mover: .white))
run(Fixture(name: "synthetic black", start: "\(boards)/start_black.png", mid: "\(boards)/mid_black.png",
            expectedPlacement: synthMid, marked: [], checks: [], mover: nil))

let td = "Tools/testdata"
let expected = (try! String(contentsOfFile: "\(td)/expected.txt", encoding: .utf8)).split(separator: "\n").reduce(into: [String: String]()) { d, l in
        let p = l.split(separator: " "); if p.count == 2 { d[String(p[0])] = String(p[1]) } }
check(expected.count == 2, "expected.txt: \(expected.count) entries")
let marks: [String: (marked: [String], checks: [String])] = [
    "lichess_check_f7.png": (["f3", "e5"], ["f7"]),
    "lichess_mate_g8.png": (["f7"], ["g8"]),
]
for (file, fen) in expected.sorted(by: { $0.key < $1.key }) {
    let m = marks[file]!
    run(Fixture(name: file, start: "\(td)/lichess_start_black.png", mid: "\(td)/\(file)", expectedPlacement: fen,
                marked: m.marked, checks: m.checks, mover: .white))
}

func placement(of res: ScanResult, whiteBottom: Bool) -> String {
    String(Placement(screen: res.screen, whiteAtBottom: whiteBottom).fen(sideToMove: .white).split(separator: " ")[0])
}

func runEarly(_ name: String, image: String, expected: String, whiteBottom: Bool) {
    print("--- early \(name)")
    let img = load(image)
    guard let rect = BoardDetector.detect(img) else { check(false, "\(name): no board"); return }
    var templates = PieceTemplates()
    let scan = BoardScanner.analyze(img, board: rect, templates: templates)
    check(!scan.looksLikeStart, "\(name): taken for the start position")
    let t0 = Date()
    let wb = templates.learnEarly(scan.features)
    print("search \(Int(Date().timeIntervalSince(t0) * 1000)) ms")
    check(wb == whiteBottom, "\(name): orientation \(String(describing: wb)) instead of \(whiteBottom)")
    guard wb != nil, let res = BoardScanner.scan(img, templates: templates) else { return }
    let got = placement(of: res, whiteBottom: whiteBottom)
    check(got == expected, "\(name): placement \(got) instead of \(expected)")
}

func runStartFixture(_ name: String, image: String, whiteBottom: Bool) {
    print("--- start \(name)")
    let img = load(image)
    guard let rect = BoardDetector.detect(img) else { check(false, "\(name): no board"); return }
    var templates = PieceTemplates()
    let scan = BoardScanner.analyze(img, board: rect, templates: templates)
    check(scan.looksLikeStart, "\(name): start position not recognised")
    check(templates.learnStart(scan.features) == whiteBottom, "\(name): orientation")
    guard let res = BoardScanner.scan(img, templates: templates) else { return }
    let got = placement(of: res, whiteBottom: whiteBottom)
    check(got == startPlacement, "\(name): placement \(got)")
}

func runOrientation() {
    print("--- orientation tracker")
    let start = Array("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR".split(separator: "/")).map { row -> [Character?] in
        row.flatMap { ch -> [Character?] in ch.isNumber ? Array(repeating: nil, count: ch.wholeNumberValue!) : [ch] }
    }
    let flipped: [[Character?]] = start.reversed().map { Array($0.reversed()) }
    var t = OrientationTracker()
    check(t.resolve(guess: true, screen: start, isStart: true).whiteAtBottom && t.whiteAtBottom == true, "first guess adopted")
    for _ in 0..<30 { check(t.resolve(guess: false, screen: start, isStart: false).whiteAtBottom, "a wrong guess must not flip a settled game") }
    let seen = t.resolve(guess: false, screen: flipped, isStart: true)
    check(!seen.whiteAtBottom && seen.reason != nil, "a start position with the other orientation switches")
}
runOrientation()

func runDragFixture(start: String, image: String) {
    print("--- drag fixture: rings and dots over pieces")
    let startImg = load(start)
    guard let rect = BoardDetector.detect(startImg) else { check(false, "drag: no board in the start image"); return }
    var templates = PieceTemplates()
    templates.learnStart(BoardScanner.analyze(startImg, board: rect, templates: templates).features)
    let res = BoardScanner.analyze(load(image), board: BoardRect(x: 18, y: 66, size: 1136), templates: templates)
    check(res.screen[2][2] == "n", "drag: knight under the ring on c6 read as \(String(describing: res.screen[2][2]))")
    check(res.screen[1][5] == "p", "drag: pawn under the ring on f7 read as \(String(describing: res.screen[1][5]))")
    check(res.screen[1][4] == "b" && res.screen[0][4] == "k", "drag: pieces next to the markings")
    for (r, c) in [(1, 3), (4, 5), (5, 6)] { check(res.screen[r][c] == nil, "drag: dot on row \(r) col \(c) read as a piece \(String(describing: res.screen[r][c]))") }
    guard let pos = ChessPosition(fen: "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQR1K1 w kq - 7 9") else { check(false, "drag: fen"); return }
    var lifted = pos.board
    lifted.squares[4][4] = nil
    lifted.squares[3][6] = "N"
    guard let piece = LiftedPiece.detect(last: pos, current: lifted, held: true) else { check(false, "drag: lifted knight not found"); return }
    check(piece.square == "e5" && piece.piece == "N", "drag: lifted \(piece.square) \(piece.piece)")
    let targets: Set<String> = ["c4", "d7", "g6", "g4", "d3", "f3", "c6", "f7"]
    let unsafe = piece.unsafeSquares(in: pos).map(\.square)
    check(Set(unsafe).isSubset(of: targets), "drag: unsafe squares \(unsafe) are not knight targets")
    check(LiftedPiece.detect(last: pos, current: lifted) == nil, "drag: a finished move is not a lift")
    var wrongSquare = pos.board
    wrongSquare.squares[4][4] = nil
    wrongSquare.squares[3][1] = "N"
    check(LiftedPiece.detect(last: pos, current: wrongSquare, held: true)?.square == "e5", "drag: the hover square must not decide the lifted piece")
}

func runEndgameFixture(start: String, image: String) {
    print("--- endgame orientation")
    let startImg = load(start)
    guard let rect = BoardDetector.detect(startImg) else { check(false, "endgame: no board in the start image"); return }
    var templates = PieceTemplates()
    templates.learnStart(BoardScanner.analyze(startImg, board: rect, templates: templates).features)
    let img = load(image)
    guard let res = BoardScanner.scan(img, templates: templates) else { check(false, "endgame: no board"); return }
    let got = placement(of: res, whiteBottom: true)
    check(got == "7r/5Kp1/8/p7/Pk1b2P1/1P6/8/8", "endgame: read with white at the bottom \(got)")
    var tracker = OrientationTracker()
    tracker.set(true)
    for i in 0..<20 {
        let r = tracker.resolve(guess: false, screen: res.screen, isStart: false)
        check(r.whiteAtBottom && r.reason == nil, "endgame: orientation flipped on frame \(i) by a wrong guess")
    }
}

let startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"
runEarly("d4", image: "\(boards)/early_d4_white.png", expected: "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", whiteBottom: true)
runEarly("e4 e5", image: "\(boards)/early_e4e5_black.png", expected: "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR", whiteBottom: false)
runEarly("Nf3 Nc6", image: "\(boards)/early_nf3nc6_white.png", expected: "r1bqkbnr/pppppppp/2n5/8/8/5N2/PPPPPPPP/RNBQKB1R", whiteBottom: true)
runEarly("four plies", image: "\(boards)/early_four_plies_black.png", expected: "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R", whiteBottom: false)

func runRelearn(_ name: String, oldTheme: String, image: String, expected: String, whiteBottom: Bool) {
    print("--- relearn \(name)")
    let old = load(oldTheme)
    guard let oldRect = BoardDetector.detect(old) else { check(false, "\(name): no board in the old theme"); return }
    var templates = PieceTemplates()
    templates.learnStart(BoardScanner.analyze(old, board: oldRect, templates: templates).features)
    let img = load(image)
    guard let scan = BoardScanner.scan(img, templates: templates) else { check(false, "\(name): no board"); return }
    print("old templates read \(placement(of: scan, whiteBottom: whiteBottom)), badly: \(templates.fitsBadly(scan))")
    check(templates.fitsBadly(scan), "\(name): bad fit not detected")
    guard let found = templates.learnEarlyPosition(scan.features) else { check(false, "\(name): relearn failed"); return }
    check(found.whiteAtBottom == whiteBottom, "\(name): orientation")
    check(found.position.turn == .white, "\(name): side to move \(found.position.turn)")
    guard let res = BoardScanner.scan(img, templates: templates) else { return }
    let got = placement(of: res, whiteBottom: whiteBottom)
    check(got == expected, "\(name): placement \(got) instead of \(expected)")
    check(!templates.fitsBadly(res), "\(name): still a bad fit after relearning")
}

func runRelearnKnown() {
    print("--- relearn from the tracked position")
    let midFen = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R"
    let themed = "\(boards)/themed_mid_white.png"
    let script = "import sys; sys.path.insert(0, 'Tools'); import make_board as m; m.LIGHT=(240,217,181); m.DARK=(181,136,99); " +
        "m.GLYPH_SOLID=dict(m.GLYPH_OUTLINE); m.render('\(midFen)', True, '\(themed)')"
    let proc = Process()
    proc.executableURL = URL(fileURLWithPath: "/usr/bin/env")
    proc.arguments = ["python3", "-c", script]
    try! proc.run(); proc.waitUntilExit()
    check(proc.terminationStatus == 0, "themed render failed")
    let start = load("\(boards)/start_white.png")
    guard let rect = BoardDetector.detect(start), let tracked = ChessPosition(fen: midFen + " w kq - 0 7") else { check(false, "relearn known: setup"); return }
    var templates = PieceTemplates()
    templates.learnStart(BoardScanner.analyze(start, board: rect, templates: templates).features)
    let img = load(themed)
    guard let scan = BoardScanner.scan(img, templates: templates) else { check(false, "relearn known: no board"); return }
    check(templates.fitsBadly(scan), "relearn known: bad fit not detected, read \(placement(of: scan, whiteBottom: true))")
    var wrong = tracked.board
    wrong.squares[4][4] = nil
    var t0 = PieceTemplates()
    check(t0.learnKnown(scan.features, board: wrong, preferWhiteAtBottom: true) == nil, "relearn known: occupancy mismatch accepted")
    var t = PieceTemplates()
    check(t.learnKnown(scan.features, board: tracked.board, preferWhiteAtBottom: nil) == true, "relearn known: orientation")
    guard let res = BoardScanner.scan(img, templates: t) else { return }
    let got = placement(of: res, whiteBottom: true)
    check(got == midFen, "relearn known: placement \(got)")
    check(!t.fitsBadly(res), "relearn known: still a bad fit")
}

runRelearnKnown()

let privateDir = "intern/fixtures"
if FileManager.default.fileExists(atPath: privateDir) {
    runStartFixture("chess.com", image: "\(privateDir)/chesscom_start_white.png", whiteBottom: true)
    runStartFixture("lichess", image: "\(privateDir)/lichess_start_white.png", whiteBottom: true)
    runRelearn("chess.com custom theme", oldTheme: "\(privateDir)/chesscom_start_white.png", image: "\(privateDir)/chesscom_theme_d4_nf6_white.png",
               expected: "rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR", whiteBottom: true)
    runEarly("World Chess d4 with arrow", image: "\(privateDir)/worldchess_d4_black_arrow.png",
             expected: "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", whiteBottom: false)
    runDragFixture(start: "\(privateDir)/chesscom_start_white.png", image: "\(privateDir)/chesscom_drag_knight_e5_over_g4.png")
    runEndgameFixture(start: "\(privateDir)/chesscom_start_white.png", image: "\(privateDir)/chesscom_endgame_flipped_orientation.png")
} else {
    print("private fixtures absent, skipped")
}

print(failures == 0 ? "recognition OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
