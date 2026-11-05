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

let pinFEN = "r1b1kbnr/pp2qppp/2n5/1B1pN3/3P4/8/PPP2PPP/RNBQK2R w KQkq - 0 7"
let pinPos = ChessPosition(fen: pinFEN)!
check(pinPos.board.problem(sideToMove: .white) == nil, "pin FEN is a legal position")
let whitePins = pinPos.board.pins(of: .white)
check(whitePins.count == 1 && whitePins[0].square == "e5" && whitePins[0].pinnerSquare == "e7" && whitePins[0].targetSquare == "e1" && whitePins[0].absolute,
      "white knight e5 pinned by queen e7: \(whitePins.map { $0.text() })")
let blackPins = pinPos.board.pins(of: .black)
check(blackPins.contains { $0.square == "c6" && $0.pinnerSquare == "b5" && $0.absolute }, "black knight c6 pinned by bishop b5")
check(ChessPosition(fen: ChessPosition.startFEN)!.board.pins(of: .white).isEmpty, "no pins in the start position")
let queenPin = ChessPosition(fen: "4k3/8/8/8/1b6/2N5/3Q4/4K3 w - - 0 1")!
check(queenPin.board.pins(of: .white).contains { $0.square == "c3" && $0.targetSquare == "d2" && !$0.absolute }, "relative pin against the queen")
check(!ChessPosition(fen: "4k3/8/8/8/1q6/2N5/3Q4/4K3 w - - 0 1")!.board.pins(of: .white).contains { $0.targetSquare == "d2" }, "queen does not pin against queen")
check(pinPos.board.unpinned(by: UCIMove("e1g1")!, color: .white).count == 1, "castling unpins the knight")
check(Hints.theme(placement: pinPos.board, best: UCIMove("e1g1")!, player: .white) == .unpin, "theme of castling out of the pin")
check(Hints.stage2Text(piece: "K", square: "e1", castling: true) == "Bring your king to safety: castle.", "castling stage 2 text")
check(Hints.stage2Text(piece: "K", square: "e1", castling: false) == "Move the king on e1.", "king stage 2 text")
for sys in [Coach.systemPrompt, Coach.hintSystemPrompt, Coach.questionSystemPrompt, Coach.summarySystemPrompt] {
    let low = sys.lowercased()
    check(!low.contains("facts") && !low.contains("information provided") && !low.contains(" data"), "prompt wording: \(sys.prefix(60))")
}

if let path = StockfishEngine.findBinary(), let engine = try? StockfishEngine(path: path) {
    if let cur = await engine.analyze(fen: pinPos.fen, movetimeMs: 600), let nullFen = Threats.nullMoveFEN(pinPos),
       let nul = await engine.analyze(fen: nullFen, movetimeMs: 300) {
        let threat = Threats.detect(in: pinPos, current: cur, opponent: nul)
        let g = PositionFacts.build(position: pinPos, result: cur, nullResult: nul, threat: threat, revealBest: true)
        print(g.lines.joined(separator: "\n"))
        let text = g.lines.joined(separator: "\n")
        check(cur.bestMove == "e1g1", "best move is castling: \(cur.bestMove)")
        check(text.contains("Best move per engine: O-O"), "facts contain the best move")
        check(text.contains("Pins:") && text.contains("White Knight e5 is pinned against White King e1 by Black Queen e7"), "facts contain the pin")
        check(text.contains("Opponent's threat") && text.contains("7...f6"), "facts contain the threat consequence")
        check(threat == nil || threat?.reason()?.contains("Knight e5") == true, "threat reason names the knight: \(threat?.text ?? "nil")")
        check(text.contains("pinned against King e1, so it cannot move away"), "facts say the attacked knight cannot move away")
        let hidden = PositionFacts.build(position: pinPos, result: cur, nullResult: nul, threat: threat, revealBest: false)
        check(!hidden.lines.joined(separator: "\n").contains("Best move per engine"), "best move hidden without a hint")
        check(PositionFacts.mentionedMove(in: "why not Bxc6?", position: pinPos) == UCIMove("b5c6"), "mentioned move parsed")
        if let m = UCIMove("b5c6"), let after = await engine.analyze(fen: pinPos.applying(m).fen, movetimeMs: 300) {
            let mention = PositionFacts.mentionedLines(position: pinPos, move: m, after: after, best: cur.lines.first)
            print(mention.lines.joined(separator: "\n"))
            check(mention.lines.first?.contains("worse than the best move") == true || mention.lines.first?.contains("as good") == true, "mentioned move evaluation")
        }
    } else { check(false, "engine analysis of the pin position") }
    engine.stop()
}

let discFEN = "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQR1K1 w kq - 7 9"
let discPos = ChessPosition(fen: discFEN)!
check(Hints.theme(placement: discPos.board, best: UCIMove("e5c6")!, player: .white) == .discovered, "Nxc6 is a discovered attack on the queen")
check(discPos.board.discoveredTargets(by: UCIMove("e5c6")!, color: .white).contains { $0.target.square == "e6" && $0.slider.square == "e1" }, "discovered target e6")
check(Hints.theme(placement: ChessPosition.start.board, best: UCIMove("e2e4")!, player: .white) != .discovered, "no false discovered attack")
let rookBefore = ChessPosition(fen: "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQ1RK1 w kq - 7 9")!
let rookFacts = Coach.moveEffects(before: rookBefore.board, move: UCIMove("f1e1")!, me: .white, label: "The played move")
check(rookFacts.contains { $0.contains("x-rays Black Queen e6") || $0.contains("x-rays Queen e6") } && rookFacts.contains { $0.contains("attacks no enemy piece directly") },
      "rook x-ray grounded: \(rookFacts)")
check(Threats.Threat(move: UCIMove("e6d6")!, line: ["e6d6"], gain: 200, mateIn: nil, board: discPos.board).reason() == nil, "threat without reason has no reason line")
if let path = StockfishEngine.findBinary(), let engine = try? StockfishEngine(path: path) {
    if let cur = await engine.analyze(fen: discPos.fen, movetimeMs: 600), let nullFen = Threats.nullMoveFEN(discPos),
       let nul = await engine.analyze(fen: nullFen, movetimeMs: 300) {
        let th = Threats.detect(in: discPos, current: cur, opponent: nul)
        print("disc threat: \(th?.text ?? "none") best \(cur.bestMove) \(cur.scoreText)")
        check(th == nil || th?.reason() != nil, "a shown threat always has a reason")
        if let hints = Hints.build(placement: discPos.board, player: .white, result: cur, threatResult: nul) {
            print("theme \(hints.theme)")
            check(hints.theme != .improvePiece, "hint theme is tactical: \(hints.theme)")
        }
    }
    engine.stop()
}

let kingFEN = "1B2k2r/p3b1p1/b3pn2/3p3p/3P1B2/2N5/PPP2PPP/R2Q2K1 w - - 0 1"
if let kp = ChessPosition(fen: kingFEN), kp.board.problem(sideToMove: .white) == nil, let path = StockfishEngine.findBinary(), let engine = try? StockfishEngine(path: path) {
    if let cur = await engine.analyze(fen: kp.fen, movetimeMs: 500), let nullFen = Threats.nullMoveFEN(kp), let nul = await engine.analyze(fen: nullFen, movetimeMs: 300) {
        let th = Threats.detect(in: kp, current: cur, opponent: nul)
        print("king fixture: threat \(th?.text ?? "none"), best \(cur.bestMove) \(cur.scoreText)")
        check(th == nil || th?.reason() != nil || (th?.mateIn ?? 0) > 0, "quiet move is not shown as a threat")
        if let h = Hints.build(placement: kp.board, player: .white, result: cur, threatResult: nul), cur.value >= 300 {
            check(h.theme != .improvePiece, "winning position gets a real theme: \(h.theme)")
        }
    }
    engine.stop()
}
check(Hints.theme(placement: ChessPosition.start.board, best: UCIMove("g1f3")!, player: .white, value: 400) == .convert, "large advantage converts")

let rookEnd = ChessPosition(fen: "8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/6K1 b - - 0 1")!
check(rookEnd.board.describeMove(UCIMove("f2a2")!, .facts).hasSuffix(", check"), "discovered check by the bishop is computed")
check(SAN.san(UCIMove("f2a2")!, in: rookEnd) == "Rxa2+", "SAN suffix from the move generator")
let rookQuiet = ChessPosition(fen: "8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/7K b - - 0 1")!
check(!rookQuiet.board.describeMove(UCIMove("f2a2")!, .facts).contains("check") && SAN.san(UCIMove("f2a2")!, in: rookQuiet) == "Rxa2", "no check when the king is on h1")
let rookBest = ChessPosition(fen: "8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/6K1 w - - 0 1")!
let purpose = PositionFacts.purposeLines(position: rookBest, best: UCIMove("h5f5")!)
check(purpose.contains { $0.contains("the f-file") } && purpose.contains { $0.contains("attacks:") && $0.contains("Rook f2") }, "concrete hint purpose: \(purpose)")

print(failures == 0 ? "threats OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
