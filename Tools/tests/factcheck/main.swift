import Foundation

var failures = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; print("FAIL: \(msg())") }
}

let pos = ChessPosition(fen: "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQR1K1 w kq - 7 9")!
let allowed = FactCheck.allowed(positions: [pos], lines: [["e5c6", "b7c6", "e1e6"]])

check(FactCheck.clean("Your rook on e1 threatens the knight.", allowed: allowed).text == nil, "false rook claim removed")
check(FactCheck.clean("The Rook e1 attacks the Knight c6.", allowed: allowed).text == nil, "false rook attack with square removed")
check(FactCheck.clean("Your Bishop b5 attacks the Knight c6.", allowed: allowed).text != nil, "true bishop attack kept")
check(FactCheck.clean("Your Knight e5 attacks the Knight c6 and the pawn on f7.", allowed: allowed).text != nil, "true knight attack kept")
check(FactCheck.clean("The rook on e1 attacks the queen on e6 once the knight moves.", allowed: allowed).text != nil, "conditional claim kept")
check(FactCheck.clean("Play Nxc6 and the rook on e1 attacks the queen on e6.", allowed: allowed).text != nil, "discovered attack after a legal move kept")
check(FactCheck.clean("Your Rook h3 is strong.", allowed: allowed).text == nil, "invented square still rejected")

let endgame = ChessPosition(fen: "8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/6K1 b - - 0 1")!
let endAllowed = FactCheck.allowed(positions: [endgame], lines: [])
let rxa2 = SAN.san(UCIMove("f2a2")!, in: endgame)
check(FactCheck.clean("Black plays Rxa2 and wins a pawn.", allowed: endAllowed).text != nil, "plain capture kept")
let wrongSuffix = rxa2.hasSuffix("+") ? "Rxa2#" : "Rxa2+"
check(FactCheck.clean("Black plays \(wrongSuffix) next.", allowed: endAllowed).text == nil, "wrong check suffix removed (\(rxa2))")
check(FactCheck.clean("Black plays \(rxa2) next.", allowed: endAllowed).text != nil, "computed suffix kept")
print(failures == 0 ? "factcheck OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
