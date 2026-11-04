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

let startPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR"
runEarly("d4", image: "\(boards)/early_d4_white.png", expected: "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", whiteBottom: true)
runEarly("e4 e5", image: "\(boards)/early_e4e5_black.png", expected: "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR", whiteBottom: false)
runEarly("Nf3 Nc6", image: "\(boards)/early_nf3nc6_white.png", expected: "r1bqkbnr/pppppppp/2n5/8/8/5N2/PPPPPPPP/RNBQKB1R", whiteBottom: true)
runEarly("four plies", image: "\(boards)/early_four_plies_black.png", expected: "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R", whiteBottom: false)

let privateDir = "intern/fixtures"
if FileManager.default.fileExists(atPath: privateDir) {
    runStartFixture("chess.com", image: "\(privateDir)/chesscom_start_white.png", whiteBottom: true)
    runStartFixture("lichess", image: "\(privateDir)/lichess_start_white.png", whiteBottom: true)
    runEarly("World Chess d4 with arrow", image: "\(privateDir)/worldchess_d4_black_arrow.png",
             expected: "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", whiteBottom: false)
} else {
    print("private fixtures absent, skipped")
}

print(failures == 0 ? "recognition OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
