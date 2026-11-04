enum EarlyStart {
    static let maxPlies = 4

    static func match(occupied: [[Bool]], bright: [[Bool]]) -> (whiteAtBottom: Bool, board: Placement)? {
        var found: (whiteAtBottom: Bool, board: Placement)?
        for whiteAtBottom in [true, false] {
            var occ: UInt64 = 0, white: UInt64 = 0
            for r in 0..<8 { for c in 0..<8 where occupied[r][c] {
                let (file, rank) = Placement.chessSquare(col: c, row: r, whiteAtBottom: whiteAtBottom)
                let bit = UInt64(1) << UInt64(rank * 8 + file)
                occ |= bit
                if bright[r][c] { white |= bit }
            } }
            var hits: [Placement] = []
            search(.start, plies: maxPlies, occ: occ, white: white, hits: &hits)
            for hit in hits {
                if let f = found, f.whiteAtBottom != whiteAtBottom || f.board != hit { return nil }
                found = (whiteAtBottom, hit)
            }
        }
        return found
    }

    private static func masks(_ board: Placement) -> (occ: UInt64, white: UInt64) {
        var occ: UInt64 = 0, white: UInt64 = 0
        for rank in 0..<8 { for file in 0..<8 {
            guard let p = board.squares[rank][file] else { continue }
            let bit = UInt64(1) << UInt64(rank * 8 + file)
            occ |= bit
            if p.isUppercase { white |= bit }
        } }
        return (occ, white)
    }

    private static func search(_ pos: ChessPosition, plies: Int, occ: UInt64, white: UInt64, hits: inout [Placement]) {
        guard plies > 0, (masks(pos.board).occ ^ occ).nonzeroBitCount <= 2 * plies else { return }
        for move in pos.legalMoves() {
            let next = pos.applying(move)
            let m = masks(next.board)
            if m.occ == occ && m.white == white { hits.append(next.board) }
            search(next, plies: plies - 1, occ: occ, white: white, hits: &hits)
        }
    }
}
