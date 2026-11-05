import Foundation
import CoreGraphics
import ImageIO

var failures = 0
extension RGBImage {
    init(width: Int, height: Int, pixels: [UInt8]) { self.width = width; self.height = height; self.pixels = pixels }
}
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; print("FAIL: \(msg())") }
}

func skip(unchanged: Bool = true, settled: Bool = true, review: Bool = false, force: Bool = false, held: Bool = false,
          soon: Bool = false, disagree: Bool = false, evidence: Bool = false) -> Bool {
    TickGate.canSkip(unchanged: unchanged, settled: settled, pendingReview: review, forceResync: force, mouseHeld: held,
                     confirmSoon: soon, recordDisagrees: disagree, orientationEvidence: evidence)
}
check(skip(), "plain static screen is skipped")
check(!skip(unchanged: false) && !skip(settled: false) && !skip(review: true), "changed, unsettled or reviewing is not skipped")
check(!skip(held: true), "mouse held after release must not be skipped")
check(!skip(soon: true), "confirm soon must not be skipped")
check(!skip(force: true), "forced resync must run on a static screen")
check(!skip(disagree: true), "record and screen disagreement must run on a static screen")
check(!skip(evidence: true), "orientation evidence must accumulate on a static screen")

let a = CGRect(x: 100, y: 100, width: 800, height: 600)
check(WindowMove.change(from: a, to: a) == .none, "same frame")
check(WindowMove.change(from: a, to: a.offsetBy(dx: 0.4, dy: -0.4)) == .none, "sub-pixel jitter is ignored")
check(WindowMove.change(from: a, to: a.offsetBy(dx: 120, dy: -30)) == .moved(dx: 120, dy: -30), "moved window")
check(WindowMove.change(from: a, to: CGRect(x: 90, y: 100, width: 810, height: 600)) == .resized, "left edge resize")

var names: [String] = []
for i in 0..<70 { names.append(String(format: "20260101-000000-%03d.png", i)); names.append(String(format: "20260101-000000-%03d.txt", i)) }
let stale = FrameRetention.stale(names, keepFrames: 60)
check(stale.count == 20, "10 oldest pairs removed, got \(stale.count)")
check(Set(names).subtracting(stale).count == 120, "60 frames (png + txt) remain")
check(stale.allSatisfy { $0.contains("-00") && Int($0.dropFirst(16).prefix(3))! < 10 }, "oldest are removed")

var screen = [[Character?]](repeating: [Character?](repeating: nil, count: 8), count: 8)
screen[5][1] = "k"; screen[6][0] = "P"; screen[6][2] = "p"; screen[7][3] = "K"
var flip = OrientationTracker()
flip.set(true)
check(OrientationTracker.impossible(screen, whiteAtBottom: true) && !OrientationTracker.impossible(screen, whiteAtBottom: false), "orientation fixture is contrary")
_ = flip.resolve(guess: nil, screen: screen, isStart: false)
check(flip.hasEvidence, "one contrary frame is evidence")
for _ in 0..<(OrientationTracker.evidenceFrames - 1) { _ = flip.resolve(guess: nil, screen: screen, isStart: false) }
check(flip.whiteAtBottom == false && !flip.hasEvidence, "flipped after the evidence frames")

var rec = GameRecord()
let mated = ChessPosition(fen: "rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3")!
_ = rec.update(placement: mated.board, whiteAtBottom: true)
check(rec.current?.result != nil, "fixture is a decided position")
check(rec.decidedResult == nil, "estimated first sight never ends the game")

let blank = RGBImage(width: 300, height: 300, pixels: [UInt8](repeating: 128, count: 300 * 300 * 4))
var stabilizer = RectStabilizer()
let off = RegionObserver.observe(blank, origin: (x: 0, y: 0), expected: BoardRect(x: 200, y: 200, size: 200), templates: PieceTemplates(), colors: nil, stabilizer: &stabilizer)
if case .failed = off {} else { check(false, "board outside the crop must fail") }

let py = Process()
py.executableURL = URL(fileURLWithPath: "/usr/bin/env")
py.arguments = ["python3", "Tools/make_board.py", "build/testboards"]
py.standardOutput = FileHandle.nullDevice
try! py.run(); py.waitUntilExit()
guard let src = CGImageSourceCreateWithURL(URL(fileURLWithPath: "build/testboards/big_start.png") as CFURL, nil),
      let cg = CGImageSourceCreateImageAtIndex(src, 0, nil), let big = RGBImage(cgImage: cg) else { fatalError("fixture") }
let canvasW = 5120, canvasH = 2880
var canvas = [UInt8](repeating: 40, count: canvasW * canvasH * 4)
let ox = 1500, oy = 700
for y in 0..<big.height { for x in 0..<big.width {
    let s = (y * big.width + x) * 4, d = ((y + oy) * canvasW + x + ox) * 4
    for k in 0..<4 { canvas[d + k] = big.pixels[s + k] }
} }
let frame = RGBImage(width: canvasW, height: canvasH, pixels: canvas)
let t0 = Date()
let found = BoardScanner.scan(frame, templates: PieceTemplates())
let ms = Date().timeIntervalSince(t0) * 1000
print("5120x2880 full-screen scan: \(Int(ms)) ms")
check(found != nil, "board found on the 5K frame")
check(ms < 300, "5K scan took \(Int(ms)) ms")

print(failures == 0 ? "capturegate OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
