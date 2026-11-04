import Foundation

struct SquareColors: Codable, Equatable {
    var even: [Int]
    var odd: [Int]

    static func learn(from features: [[SquareFeature]]) -> SquareColors? {
        func median(_ parity: Int) -> [Int]? {
            var cols: [[Int]] = []
            for r in 2...5 { for c in 0..<8 where (r + c) % 2 == parity {
                if let bg = features[r][c].bg { cols.append(bg) }
            } }
            guard cols.count >= 4 else { return nil }
            return (0..<3).map { k in cols.map { $0[k] }.sorted()[cols.count / 2] }
        }
        guard let e = median(0), let o = median(1) else { return nil }
        return SquareColors(even: e, odd: o)
    }
}

struct LastMoveMarking {
    var marked: [(file: Int, rank: Int)]
    var checkSquares: [(file: Int, rank: Int)]
    var mover: PieceColor?
    var sideToMove: PieceColor? { mover?.opposite }
}

enum SideToMove {
    static let markThreshold = 40

    static func detect(features: [[SquareFeature]], screen: [[Character?]], whiteAtBottom: Bool,
                       learned: SquareColors? = nil) -> LastMoveMarking {
        var reference: [[Int]] = []
        for parity in 0...1 {
            var cols: [[Int]] = []
            for r in 0..<8 { for c in 0..<8 where (r + c) % 2 == parity {
                if let bg = features[r][c].bg { cols.append(bg) }
            } }
            guard cols.count >= 16 else { return LastMoveMarking(marked: [], checkSquares: [], mover: nil) }
            let median = (0..<3).map { k in cols.map { $0[k] }.sorted()[cols.count / 2] }
            if let l = learned.map({ parity == 0 ? $0.even : $0.odd }) {
                let near = cols.filter { dist($0, l) <= markThreshold }.count
                reference.append(near * 2 >= cols.count ? l : median)
            } else { reference.append(median) }
        }

        var marked: [(file: Int, rank: Int)] = [], checks: [(file: Int, rank: Int)] = []
        var colors = Set<Bool>()
        for r in 0..<8 { for c in 0..<8 {
            guard let bg = features[r][c].bg else { continue }
            let ref = reference[(r + c) % 2]
            guard dist(bg, ref) > markThreshold else { continue }
            let sq = Placement.chessSquare(col: c, row: r, whiteAtBottom: whiteAtBottom)
            let dRG = (bg[0] - bg[1]) - (ref[0] - ref[1])
            if dRG > 35 && bg[1] - ref[1] < -25 { checks.append(sq); continue }
            marked.append(sq)
            if let p = screen[r][c] { colors.insert(p.isUppercase) }
        } }
        var mover: PieceColor?
        if (1...4).contains(marked.count), colors.count == 1 { mover = colors.first! ? .white : .black }
        return LastMoveMarking(marked: marked, checkSquares: checks, mover: mover)
    }

    static func detect(scan: ScanResult, whiteAtBottom: Bool, templates: PieceTemplates) -> LastMoveMarking {
        detect(features: scan.features, screen: scan.screen, whiteAtBottom: whiteAtBottom, learned: templates.squareColors)
    }

    private static func dist(_ a: [Int], _ b: [Int]) -> Int { abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2]) }
}
