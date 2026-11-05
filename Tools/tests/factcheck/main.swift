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

check(FactCheck.clean("The queen on d1 is attacking the rook on a8.", allowed: allowed).text == nil, "is attacking claim checked")
check(FactCheck.clean("The queen on d1 hits the rook on a8.", allowed: allowed).text == nil, "hits claim checked")
check(FactCheck.clean("The queen on d1 puts pressure on the rook on a8.", allowed: allowed).text == nil, "pressure claim checked")
check(FactCheck.clean("The bishop on b5 is attacking the knight on c6.", allowed: allowed).text != nil, "true is attacking claim kept")
check(FactCheck.clean("Your rook on e1 threatens the queen on e6 right now.", allowed: allowed).text == nil, "attack blocked by the knight is rejected in the current position")
check(FactCheck.clean("After Nxc6, the rook on e1 attacks the queen on e6.", allowed: allowed).text != nil, "attack after a named move kept")
check(FactCheck.clean("After Nxc6, the rook on e1 attacks the queen on a8.", allowed: allowed).text == nil, "false attack after a named move removed")
check(FactCheck.clean("When the knight moves, the rook on e1 attacks the queen on e6.", allowed: allowed).text != nil, "conditional discovered attack kept")
check(FactCheck.clean("Der Turm auf e1 greift den Bauern auf f7 an.", allowed: allowed).text == nil, "declined German name examined")
check(FactCheck.clean("Der Läufer auf b5 greift den Springer auf c6 an.", allowed: allowed).text != nil, "true German attack kept")
check(FactCheck.clean("Nxc6 gives check and wins the knight.", allowed: allowed).text == nil, "false check prose removed")
check(FactCheck.clean("Nxc6 is checkmate.", allowed: allowed).text == nil, "false mate prose removed")
check(FactCheck.clean("That is checkmate.", allowed: allowed).text == nil, "mate claim without a move removed")
check(FactCheck.clean("Check whether your knight is pinned.", allowed: allowed).text != nil, "imperative check kept")
let pinPos = ChessPosition(fen: "r1b1kbnr/pp2qppp/2n5/1B1pN3/3P4/8/PPP2PPP/RNBQK2R w KQkq - 0 7")!
let pinAllowed = FactCheck.allowed(positions: [pinPos], lines: [["e1g1"]])
check(FactCheck.clean("Bxc6+ gives check.", allowed: pinAllowed).text != nil, "true check prose kept")
let mateFEN = ChessPosition(fen: "6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1")!
let mateAllowed = FactCheck.allowed(positions: [mateFEN], lines: [["a1a8"]])
check(FactCheck.clean("Ra8 is checkmate.", allowed: mateAllowed).text != nil, "true mate prose kept")
check(FactCheck.clean("The move gives check.", allowed: mateAllowed).text != nil, "check claim backed by the first line kept")
let hidden = FactCheck.withholding("Castling is wise. Play O-O now. The pin on e5 matters. Move to g1.", move: UCIMove("e1g1")!, san: "O-O")
check(hidden.text == "Castling is wise. The pin on e5 matters." && hidden.removed == 2, "withholding drops the best move: \(hidden.text ?? "nil")")
let endgame = ChessPosition(fen: "8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/6K1 b - - 0 1")!
let endAllowed = FactCheck.allowed(positions: [endgame], lines: [])
let rxa2 = SAN.san(UCIMove("f2a2")!, in: endgame)
check(FactCheck.clean("Black plays Rxa2 and wins a pawn.", allowed: endAllowed).text != nil, "plain capture kept")
let wrongSuffix = rxa2.hasSuffix("+") ? "Rxa2#" : "Rxa2+"
check(FactCheck.clean("Black plays \(wrongSuffix) next.", allowed: endAllowed).text == nil, "wrong check suffix removed (\(rxa2))")
check(FactCheck.clean("Black plays \(rxa2) next.", allowed: endAllowed).text != nil, "computed suffix kept")
print(failures == 0 ? "factcheck OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
