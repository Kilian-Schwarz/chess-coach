import Foundation

var failures = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; print("FAIL: \(msg())") }
}

let data = try! Data(contentsOf: URL(fileURLWithPath: "Tools/testdata/vectors.json"))
let vectors = try! JSONSerialization.jsonObject(with: data) as! [String: Any]

let movegen = vectors["movegen"] as! [String: Any]
for case let item as [String: Any] in movegen["perft"] as! [Any] {
    let fen = item["fen"] as! String, name = item["name"] as! String
    guard let pos = ChessPosition(fen: fen) else { check(false, "FEN \(fen)"); continue }
    for (i, expected) in (item["counts"] as! [Int]).enumerated() {
        let t0 = Date()
        let n = pos.perft(i + 1)
        check(n == expected, "perft \(name) depth \(i + 1): \(n) instead of \(expected)")
        print(String(format: "perft %@ d%d = %d (%.2f s)", name, i + 1, n, Date().timeIntervalSince(t0)))
    }
    check(pos.fen.split(separator: " ").prefix(4).joined(separator: " ") == fen.split(separator: " ").prefix(4).joined(separator: " "), "FEN round trip \(name): \(pos.fen)")
}

if let sanVectors = vectors["san"] as? [String: Any] {
    for case let c as [String: Any] in sanVectors["generate"] as? [Any] ?? [] {
        let fen = c["fen"] as! String, uci = c["uci"] as! String
        guard let pos = ChessPosition(fen: fen), let m = UCIMove(uci) else { check(false, "SAN vector \(c)"); continue }
        check(SAN.san(m, in: pos) == c["san"] as! String, "SAN \(uci) in \(fen): \(SAN.san(m, in: pos)) instead of \(c["san"]!)")
        if let g = c["german"] as? String {
            let shown = SAN.localize(SAN.san(m, in: pos), pieces: "KDTLS", castleShort: "0-0", castleLong: "0-0-0")
            check(shown == g, "SAN German letters \(uci): \(shown) instead of \(g)")
        }
        check(SAN.parse(c["san"] as! String, in: pos) == m, "SAN parse \(c["san"]!) in \(fen)")
        if let g = c["german"] as? String { check(SAN.parse(g, in: pos, pieces: "KDTLS") == m, "SAN parse German letters \(g)") }
    }
    for case let c as [String: Any] in sanVectors["parseOnly"] as? [Any] ?? [] {
        let fen = c["fen"] as! String, text = c["text"] as! String
        let pos = ChessPosition(fen: fen)!
        let got = SAN.parse(text, in: pos, pieces: (c["german"] as? Bool) == true ? "KDTLS" : nil)?.uci
        check(got == (c["uci"] as? String), "SAN parse \(text): \(got ?? "nil") instead of \(c["uci"] ?? "nil")")
    }
}

for case let c as [String: Any] in (movegen["results"] as? [Any]) ?? [] {
    let pos = ChessPosition(fen: c["fen"] as! String)!
    let r = pos.result
    let expected = c["result"] as! String
    let got: String
    switch r { case .none: got = "none"; case .checkmate(let w): got = w == .white ? "mate-white" : "mate-black"
    case .stalemate: got = "stalemate"; case .insufficientMaterial: got = "material"; case .fiftyMoveRule: got = "fifty" }
    check(got == expected, "result \(pos.fen): \(got) instead of \(expected)")
}

print(failures == 0 ? "movegen/san OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
