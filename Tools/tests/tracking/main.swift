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

extension RGBImage {
    init(width: Int, height: Int, pixels: [UInt8]) { self.width = width; self.height = height; self.pixels = pixels }

    func crop(x: Int, y: Int, width w: Int, height h: Int) -> RGBImage {
        var px = [UInt8](repeating: 0, count: w * h * 4)
        for yy in 0..<h { for xx in 0..<w {
            let sx = min(max(x + xx, 0), width - 1), sy = min(max(y + yy, 0), height - 1)
            let a = (sy * width + sx) * 4, b = (yy * w + xx) * 4
            for k in 0..<4 { px[b + k] = pixels[a + k] }
        } }
        return RGBImage(width: w, height: h, pixels: px)
    }
}

let py = Process()
py.executableURL = URL(fileURLWithPath: "/usr/bin/env")
py.arguments = ["python3", "Tools/make_board.py", "build/testboards"]
py.standardOutput = FileHandle.nullDevice
try! py.run(); py.waitUntilExit()
check(py.terminationStatus == 0, "make_board.py failed")
let dir = "build/testboards"

let startImg = load("\(dir)/start_white.png")
var templates = PieceTemplates()
let startBoard = BoardDetector.detect(startImg)!
let whiteBottom = templates.learnStart(BoardScanner.analyze(startImg, board: startBoard, templates: templates).features)

struct Frame { var placement: Placement; var marking: LastMoveMarking }

func frame(_ name: String) -> Frame {
    let res = BoardScanner.scan(load("\(dir)/\(name).png"), templates: templates)!
    return Frame(placement: Placement(screen: res.screen, whiteAtBottom: whiteBottom),
                 marking: SideToMove.detect(scan: res, whiteAtBottom: whiteBottom, templates: templates))
}

@discardableResult
func feed(_ rec: inout GameRecord, _ name: String) -> GameUpdate {
    let f = frame(name)
    return rec.update(placement: f.placement, whiteAtBottom: whiteBottom, hintedMover: f.marking.mover)
}

func squares(_ m: LastMoveMarking) -> Set<String> { Set(m.marked.map { UCIMove.squareName($0.file, $0.rank) }) }

func begin() -> GameRecord {
    var rec = GameRecord()
    let u = feed(&rec, "start_white")
    check(u.change == .newGame, "start should begin a game")
    return rec
}

print("--- calm sequence")
do {
    var rec = begin()
    let expect: [(String, String, PieceColor, Set<String>)] = [
        ("seq_1", "g1f3", .white, ["g1", "f3"]), ("seq_2", "d7d5", .black, ["d7", "d5"]),
        ("seq_3", "g2g3", .white, ["g2", "g3"]), ("seq_4", "c7c5", .black, ["c7", "c5"]),
    ]
    for (img, uci, mover, marked) in expect {
        let f = frame(img)
        check(squares(f.marking) == marked, "\(img): marked \(squares(f.marking))")
        check(f.marking.mover == mover, "\(img): marker mover \(String(describing: f.marking.mover))")
        let u = feed(&rec, img)
        check(u.change == .moves && u.newMoves.count == 1 && u.reviewable.count == 1, "\(img): change \(u.change)")
        check(u.reviewable.first?.uci == uci && u.reviewable.first?.mover == mover, "\(img): reviewable \(u.reviewable.map(\.uci))")
        check(!u.gap && u.log.count == 1 && u.log[0].hasPrefix("Move "), "\(img): log \(u.log)")
        let again = feed(&rec, img)
        check(again.change == .unchanged && again.log.isEmpty && again.reviewable.isEmpty, "\(img): repeated frame \(again.change) \(again.log)")
    }
    check(rec.moves.count == 4, "four moves recorded")
}

print("--- bot answers instantly: two plies between scans")
do {
    var rec = begin()
    feed(&rec, "seq_1")
    let u = feed(&rec, "seq_3")
    check(u.change == .moves && u.newMoves.map(\.uci) == ["d7d5", "g2g3"], "two plies: \(u.newMoves.map(\.uci))")
    check(u.reviewable.map(\.uci) == ["d7d5", "g2g3"] && u.reviewable.last?.mover == .white, "two plies reviewable \(u.reviewable.map(\.uci))")
    check(u.log.first?.hasPrefix("Moves (2 plies") == true, "two plies log \(u.log)")
    check(rec.moves.count == 3, "three moves recorded")
}

print("--- three plies missed")
do {
    var rec = begin()
    feed(&rec, "seq_1")
    let u = feed(&rec, "seq_4")
    check(u.change == .resync && u.gap && u.reviewable.isEmpty && u.newMoves.isEmpty, "gap: \(u.change) gap \(u.gap) reviewable \(u.reviewable.count)")
    check(u.log.first?.hasPrefix("Resync:") == true, "gap log \(u.log)")
    check(rec.moves.count == 1 && rec.breaks == [1] && rec.segmentMoves == 0 && u.estimated, "gap keeps earlier moves and starts an estimated segment")
    let next = feed(&rec, "seq_plain_4")
    check(next.change == .unchanged, "same placement after resync is unchanged")
}

print("--- first sight mid-game")
do {
    var rec = GameRecord()
    let a = feed(&rec, "seq_3")
    check(a.change == .firstSight && a.reviewable.isEmpty && a.estimated, "first sight: \(a.change)")
    let b = feed(&rec, "seq_4")
    check(b.change == .moves && b.reviewable.map(\.uci) == ["c7c5"], "first move after first sight: \(b.change) \(b.reviewable.map(\.uci))")
}

print("--- wrong guess of the side is corrected without a review")
do {
    var rec = GameRecord()
    rec.update(placement: frame("seq_1").placement, whiteAtBottom: true)
    let u = feed(&rec, "seq_2")
    check(u.change == .moves && u.reviewable.isEmpty, "flipped estimate must not be rated: \(u.change) \(u.reviewable.count)")
    check(u.log.first?.hasPrefix("Side override") == true, "side override logged: \(u.log)")
}

print("--- lifted piece during a drag")
do {
    var rec = begin()
    for n in ["seq_1", "seq_2", "seq_3", "seq_4"] { feed(&rec, n) }
    let lift = frame("seq_lift")
    check(squares(lift.marking) == ["f1"] && lift.marking.mover == nil, "a single selected square must not give a mover: \(squares(lift.marking)) \(String(describing: lift.marking.mover))")
    let u = feed(&rec, "seq_lift")
    check(u.change == .unchanged && u.reviewable.isEmpty && rec.moves.count == 4, "lifted piece: \(u.change), \(rec.moves.count) moves")
    check(u.log.first?.hasPrefix("Partial frame ignored") == true, "partial frame logged: \(u.log)")
    let drop = feed(&rec, "seq_plain_4")
    check(drop.change == .unchanged && rec.moves.count == 4 && rec.breaks.isEmpty, "piece put back: \(drop.change)")
}

print("--- highlight only: no move, no side change")
do {
    var rec = begin()
    for n in ["seq_1", "seq_2", "seq_3", "seq_4"] { feed(&rec, n) }
    let before = rec.current
    let u = feed(&rec, "seq_plain_4")
    check(u.change == .unchanged && rec.current == before && rec.moves.count == 4, "plain board after highlighted board: \(u.change)")
}

print("--- marker does not flip a settled game")
do {
    var rec = begin()
    feed(&rec, "seq_1")
    let p = frame("seq_1").placement
    let u = rec.update(placement: p, whiteAtBottom: true, hintedMover: .white)
    check(u.change == .unchanged && u.log.isEmpty && rec.current?.turn == .black, "marker must not change the side of a confirmed game")
}

print("--- take-back")
do {
    var rec = begin()
    for n in ["seq_1", "seq_2", "seq_3"] { feed(&rec, n) }
    let u = feed(&rec, "seq_2")
    check(u.change == .takeback && u.reviewable.isEmpty && !u.gap && rec.moves.count == 2, "take-back: \(u.change), \(rec.moves.count) moves")
    check(u.log.first?.hasPrefix("Take-back") == true, "take-back logged: \(u.log)")
    let again = feed(&rec, "seq_3")
    check(again.change == .moves && again.reviewable.map(\.uci) == ["g2g3"], "move played again after take-back: \(again.change)")
}

print("--- board rectangle jitter")
do {
    var st = RectStabilizer()
    let base = BoardRect(x: 310, y: 140, size: 576)
    let first = st.accept(base)
    check(first.changed && first.rect == base, "first rect accepted")
    var accepted = first.rect
    for (dx, dy, ds) in [(1.0, 0.0, 0.0), (-2.0, 3.0, 1.0), (3.0, -3.0, -3.0), (0.4, 0.6, 2.0), (-1.0, 1.0, 5.0)] {
        let r = st.accept(BoardRect(x: base.x + dx, y: base.y + dy, size: base.size + ds))
        check(!r.changed && r.rect == accepted, "jitter \(dx),\(dy),\(ds) moved the rect to \(r.rect)")
        accepted = r.rect
    }
    let moved = st.accept(BoardRect(x: base.x + 40, y: base.y, size: base.size))
    check(moved.changed && moved.rect.x == 350, "a 40 px move must be followed: \(moved.rect)")
    let resized = st.accept(BoardRect(x: 350, y: 140, size: 640))
    check(resized.changed && resized.rect.size == 640, "a resize must be followed")
    var fresh = RectStabilizer()
    let snapped = fresh.accept(BoardRect(x: 10.4, y: 20.6, size: 100.5))
    check(snapped.rect.x == 10 && snapped.rect.y == 21, "rect snapped to whole pixels: \(snapped.rect)")
}

print("--- region crops of the board")
func regionCheck(_ name: String, _ img: RGBImage) {
    guard let b = BoardDetector.detect(img) else { check(false, "\(name): no board in the full image"); return }
    for margin in [0.12, 0.3] {
        for off in [(0, 0), (1, 0), (0, 1), (-7, 5), (13, -9), (-1, -1)] {
            let m = max(Int(b.size * margin), 40)
            let cx = Int(b.x) - m + off.0, cy = Int(b.y) - m + off.1
            let crop = img.crop(x: cx, y: cy, width: Int(b.size) + 2 * m, height: Int(b.size) + 2 * m)
            guard let d = BoardDetector.detect(crop) else { check(false, "\(name): board lost in crop margin \(margin) offset \(off)"); continue }
            check(abs(d.x + Double(cx) - b.x) <= 3 && abs(d.y + Double(cy) - b.y) <= 3 && abs(d.size - b.size) <= 3,
                  "\(name): crop margin \(margin) offset \(off) found \(d) instead of \(b)")
        }
    }
}
regionCheck("synthetic start", startImg)
regionCheck("synthetic seq_4", load("\(dir)/seq_4.png"))
let privateDir = "intern/fixtures"
if FileManager.default.fileExists(atPath: privateDir) {
    for f in ["chesscom_start_white", "lichess_start_white", "worldchess_d4_black_arrow"] {
        regionCheck(f, load("\(privateDir)/\(f).png"))
    }
}

print("--- verification instead of re-detection")
let bigStart = load("\(dir)/big_start.png")
let bigRect = BoardDetector.detect(bigStart)!
var bigTemplates = PieceTemplates()
bigTemplates.learnStart(BoardScanner.analyze(bigStart, board: bigRect, templates: bigTemplates).features)
let learnedColors = BoardColors.learn(BoardScanner.analyze(bigStart, board: bigRect, templates: bigTemplates).features)
let margin = Int(bigRect.size * 0.2)
let cropX = Int(bigRect.x) - margin, cropY = Int(bigRect.y) - margin
func regionCrop(_ img: RGBImage) -> RGBImage { img.crop(x: cropX, y: cropY, width: Int(bigRect.size) + 2 * margin, height: Int(bigRect.size) + 2 * margin) }
let expectedInCrop = BoardRect(x: bigRect.x - Double(cropX), y: bigRect.y - Double(cropY), size: bigRect.size)
let cropOrigin = (x: Double(cropX), y: Double(cropY))

func tracker() -> RectStabilizer {
    var st = RectStabilizer()
    _ = st.accept(bigRect)
    return st
}

func observe(_ name: String, _ st: inout RectStabilizer, colors: BoardColors? = learnedColors) -> RegionObserver.Result {
    RegionObserver.observe(regionCrop(load("\(dir)/\(name).png")), origin: cropOrigin, expected: expectedInCrop, templates: bigTemplates, colors: colors, stabilizer: &st)
}

func isKept(_ r: RegionObserver.Result) -> Bool {
    switch r {
    case .scanned(_, _, let changed): return !changed
    case .verified, .covered: return true
    case .failed: return false
    }
}

for name in ["big_start", "big_mid", "big_drag", "big_dots", "big_arrows"] {
    var st = tracker()
    let r = observe(name, &st)
    check(isKept(r), "\(name): board lost instead of kept (\(r))")
    check(st.accepted == tracker().accepted, "\(name): rect changed to \(String(describing: st.accepted))")
}

do {
    var st = tracker()
    let blank = RGBImage(width: expectedInCrop.size > 0 ? Int(bigRect.size) + 2 * margin : 1, height: Int(bigRect.size) + 2 * margin,
                         pixels: [UInt8](repeating: 40, count: (Int(bigRect.size) + 2 * margin) * (Int(bigRect.size) + 2 * margin) * 4))
    let r = RegionObserver.observe(blank, origin: cropOrigin, expected: expectedInCrop, templates: bigTemplates, colors: learnedColors, stabilizer: &st)
    if case .failed = r {} else { check(false, "an empty area must fail verification: \(r)") }
    let noColors = RegionObserver.observe(regionCrop(load("\(dir)/big_drag.png")), origin: cropOrigin, expected: expectedInCrop, templates: bigTemplates, colors: nil, stabilizer: &st)
    check(isKept(noColors), "without learned colours the detected grid is still used: \(noColors)")
}

print("--- transient wrong rectangle")
do {
    var st = tracker()
    let wrongRect = observe("big_shifted", &st)
    check(st.accepted == tracker().accepted, "one shifted frame changed the rect to \(String(describing: st.accepted))")
    switch wrongRect {
    case .scanned(_, _, let changed): check(!changed, "a shifted frame was accepted at once")
    default: break
    }
    _ = observe("big_mid", &st)
    _ = observe("big_shifted", &st)
    check(st.accepted == tracker().accepted, "a shifted frame between normal frames changed the rect")
    var other = tracker()
    for i in 0..<5 {
        _ = observe("big_other_theme", &other)
        check(other.accepted == tracker().accepted, "frame \(i): a board with other colours changed the rect")
    }
    var moved = tracker()
    _ = observe("big_shifted", &moved)
    check(moved.accepted == tracker().accepted, "first sight of a moved board must stay pending")
    _ = observe("big_shifted", &moved)
    check(moved.accepted != tracker().accepted, "a board seen twice at a new place must be followed")
    var noColors = tracker()
    _ = observe("big_shifted", &noColors, colors: nil)
    _ = observe("big_shifted", &noColors, colors: nil)
    check(noColors.accepted != tracker().accepted, "twice-confirmed rect without colours must be accepted")
}

print("--- replay: miss, verify, keep")
do {
    var st = tracker()
    var failures = 0, scanned = 0, verified = 0
    for name in ["big_mid", "big_drag", "big_drag", "big_dots", "big_dots", "big_arrows", "big_mid", "big_drag", "big_mid"] {
        switch observe(name, &st) {
        case .failed: failures += 1
        case .verified: verified += 1
        case .scanned: scanned += 1
        case .covered: break
        }
    }
    check(failures == 0, "replay: \(failures) verification failures")
    check(st.accepted == tracker().accepted, "replay: rect drifted")
    print("replay: \(scanned) grid detections, \(verified) verifications")
}

print("--- relearn guard")
do {
    var g = RelearnGuard()
    let a = BoardRect(x: 100, y: 100, size: 800), b = BoardRect(x: 101, y: 100, size: 800)
    check(!g.observe(bad: true, rect: a) && !g.observe(bad: true, rect: a), "two bad frames must not relearn")
    check(g.observe(bad: true, rect: a), "three bad frames at one rect must relearn")
    g.reset()
    check(!g.observe(bad: true, rect: a) && !g.observe(bad: true, rect: b) && !g.observe(bad: true, rect: a), "a changing rect must not relearn")
    g.reset()
    _ = g.observe(bad: true, rect: a); _ = g.observe(bad: true, rect: a)
    check(!g.observe(bad: false, rect: a) && !g.observe(bad: true, rect: a), "a good frame resets the count")
    check(g.observe(bad: true, rect: a, required: 1), "empty templates relearn at once")
}

print("--- stuck record resync")
do {
    var rec = begin()
    for n in ["seq_1", "seq_2", "seq_3", "seq_4"] { feed(&rec, n) }
    let f = frame("seq_lift")
    let partial = rec.update(placement: f.placement, whiteAtBottom: whiteBottom, hintedMover: nil)
    check(partial.change == .unchanged && rec.current?.board != f.placement, "a missing piece is ignored as partial: \(partial.change)")
    let forced = rec.update(placement: f.placement, whiteAtBottom: whiteBottom, hintedMover: nil, forced: true)
    check(forced.change != .unchanged && rec.current?.board == f.placement, "forced update must resync, got \(forced.change)")
}

print("--- resync keeps the game")
do {
    var rec = GameRecord()
    var pos = ChessPosition.start
    _ = rec.update(placement: pos.board, whiteAtBottom: true)
    var played = 0
    while played < 40, let m = pos.legalMoves().first {
        pos = pos.applying(m)
        _ = rec.update(placement: pos.board, whiteAtBottom: true)
        played += 1
    }
    let before = rec.moves.count
    check(before >= 30, "setup: \(before) plies")
    let startedWith = rec.startPosition?.fen
    var odd = pos.board
    var swapped = 0
    for r in 0..<8 { for f in 0..<8 where swapped < 4 && odd.squares[r][f] != nil && (r + f) % 3 == 0 {
        odd.squares[r][f] = nil; swapped += 1
    } }
    let u = rec.update(placement: odd, whiteAtBottom: true)
    check(u.change == .resync, "unreachable board: \(u.change)")
    check(rec.moves.count == before && rec.breaks == [before], "moves kept across the break: \(rec.moves.count) of \(before), breaks \(rec.breaks)")
    check(rec.startPosition?.fen == startedWith, "same game start after the resync")
    let back = rec.update(placement: pos.board, whiteAtBottom: true)
    check(rec.moves.count == before && back.change != .newGame, "returning to the old position keeps all plies: \(rec.moves.count)")
}

print(failures == 0 ? "tracking OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
