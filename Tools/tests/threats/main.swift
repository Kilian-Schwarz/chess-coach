import Foundation

var failures = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; print("FAIL: \(msg())") }
}

let data = try! Data(contentsOf: URL(fileURLWithPath: "Tools/testdata/vectors.json"))
let vectors = try! JSONSerialization.jsonObject(with: data) as! [String: Any]
let t = vectors["threats"] as! [String: Any]
check(Threats.threshold == (t["threshold"] as! Int), "threshold")

for case let c as [String: Any] in t["nullMove"] as! [Any] {
    let pos = ChessPosition(fen: c["fen"] as! String)!
    let got = Threats.nullMoveFEN(pos)
    check(got == c["nullFen"] as? String, "null move \(c["fen"]!): \(got ?? "nil") instead of \(c["nullFen"] ?? "nil")")
}

func line(_ best: String, _ r: [String: Any]) -> EngineLine {
    EngineLine(move: best, scoreCP: r["cp"] as? Int, mateIn: r["mate"] as? Int, pv: [best])
}
for case let c as [String: Any] in t["detect"] as! [Any] {
    let pos = ChessPosition(fen: c["fen"] as! String)!
    let best = c["opponentBest"] as! String
    let cur = EngineResult(fen: pos.fen, bestMove: "e2e4", lines: [line("e2e4", c["current"] as! [String: Any])])
    let opp = EngineResult(fen: "", bestMove: best, lines: [line(best, c["opponent"] as! [String: Any])])
    let th = Threats.detect(in: pos, current: cur, opponent: opp)
    check((th != nil) == (c["threat"] as! Bool), "threat \(c): \(String(describing: th))")
    if let txt = c["text"] as? String { check(th?.text == txt, "text \(th?.text ?? "nil") instead of \(txt)") }
}

if let path = StockfishEngine.findBinary(), let engine = try? StockfishEngine(path: path) {
    for case let c as [String: Any] in t["engine"] as! [Any] {
        let fen = c["fen"] as! String
        let pos = ChessPosition(fen: fen)!
        guard let nullFen = Threats.nullMoveFEN(pos),
              let cur = await engine.analyze(fen: pos.fen, movetimeMs: 400),
              let opp = await engine.analyze(fen: nullFen, movetimeMs: 400) else { check(false, "Engine \(fen)"); continue }
        let th = Threats.detect(in: pos, current: cur, opponent: opp)
        print("\(fen): \(th?.text ?? "no threat") (\(cur.scoreText) / \(opp.scoreText))")
        check((th != nil) == (c["threat"] as! Bool), "engine threat \(fen): \(th?.text ?? "nil")")
        if let s = c["contains"] as? String { check(th?.text.contains(s) == true, "text contains \(s): \(th?.text ?? "nil")") }
    }
    engine.stop()
} else { check(false, "Stockfish not found") }

print(failures == 0 ? "threats OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
