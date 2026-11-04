import Foundation

struct OpeningInfo: Equatable {
    let eco: String
    let name: String
    var display: String { Loc.display("opening.display", "\(name) (\(eco))") }
}

final class Openings {
    private(set) var table: [String: OpeningInfo] = [:]

    var count: Int { table.count }

    init(tsv: String) {
        for line in tsv.split(whereSeparator: \.isNewline) {
            let cols = line.split(separator: "\t", omittingEmptySubsequences: false)
            guard cols.count >= 3 else { continue }
            table[String(cols[0])] = OpeningInfo(eco: String(cols[1]), name: String(cols[2]))
        }
    }

    convenience init?(path: String) {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8) else { return nil }
        self.init(tsv: text)
    }

    convenience init?(bundle: Bundle = .main) {
        guard let url = bundle.url(forResource: "openings", withExtension: "tsv") else { return nil }
        self.init(path: url.path)
    }

    func longestName(along epds: [String]) -> OpeningInfo? {
        epds.reversed().lazy.compactMap { self.table[$0] }.first
    }

    static func epd(placement: Placement, sideToMove: PieceColor, castling: String, enPassant: String?) -> String {
        let fenFields = placement.fen(sideToMove: sideToMove).split(separator: " ")
        let c = castling.isEmpty ? "-" : castling
        return "\(fenFields[0]) \(fenFields[1]) \(c) \(enPassant ?? "-")"
    }

    static func enPassantTarget(placement: Placement, lastMove m: UCIMove) -> String? {
        guard let piece = placement.squares[m.toRank][m.toFile], piece == "P" || piece == "p",
              m.fromFile == m.toFile, abs(m.toRank - m.fromRank) == 2 else { return nil }
        let enemy: Character = piece == "P" ? "p" : "P"
        for df in [-1, 1] {
            let nf = m.toFile + df
            if (0..<8).contains(nf), placement.squares[m.toRank][nf] == enemy {
                return UCIMove.squareName(m.toFile, (m.toRank + m.fromRank) / 2)
            }
        }
        return nil
    }
}
