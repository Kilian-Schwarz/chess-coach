import Foundation

struct SquareFeature: Codable {
    static let n = 32
    static let maxPieceChroma = 80
    static let overlayRatio = (low: 0.78, high: 0.93)
    static let overlaySpread = 0.04

    static func isMoveOverlay(_ c: (Int, Int, Int), bg: (Int, Int, Int)) -> Bool {
        guard min(bg.0, bg.1, bg.2) > 40 else { return false }
        let ratios = [Double(c.0) / Double(bg.0), Double(c.1) / Double(bg.1), Double(c.2) / Double(bg.2)]
        guard let lo = ratios.min(), let hi = ratios.max() else { return false }
        return hi - lo <= overlaySpread && lo >= overlayRatio.low && hi <= overlayRatio.high
    }
    var mask: [Bool]
    var lum: [UInt8]
    var fill: Double
    var meanLum: Double
    var bg: [Int]? = nil

    static func extract(_ img: RGBImage, board: BoardRect, col: Int, row: Int) -> SquareFeature {
        let (sx, sy, s) = board.squareRect(col: col, row: row)
        let n = SquareFeature.n

        var rs: [Int] = [], gs: [Int] = [], bs: [Int] = []
        var cr: [Int] = [], cg: [Int] = [], cb: [Int] = []
        let inset = max(1.5, s * 0.06)
        let steps = 24
        for i in 0..<steps {
            let u = Double(i) / Double(steps - 1)
            let t = inset + (s - 2 * inset) * Double(i) / Double(steps - 1)
            let tc = inset + (s - 2 * inset) * (u < 0.5 ? u * 0.56 : 1 - (1 - u) * 0.56)
            for (px, py) in [(sx + t, sy + inset), (sx + t, sy + s - inset), (sx + inset, sy + t), (sx + s - inset, sy + t)] {
                let c = img.rgb(Int(px), Int(py))
                rs.append(c.0); gs.append(c.1); bs.append(c.2)
            }
            for (px, py) in [(sx + tc, sy + inset), (sx + tc, sy + s - inset), (sx + inset, sy + tc), (sx + s - inset, sy + tc)] {
                let c = img.rgb(Int(px), Int(py))
                cr.append(c.0); cg.append(c.1); cb.append(c.2)
            }
        }
        rs.sort(); gs.sort(); bs.sort(); cr.sort(); cg.sort(); cb.sort()
        let bg = (rs[rs.count / 2], gs[gs.count / 2], bs[bs.count / 2])
        let maskBg = (cr[cr.count / 2], cg[cg.count / 2], cb[cb.count / 2])

        var mask = [Bool](repeating: false, count: n * n)
        var lum = [UInt8](repeating: 0, count: n * n)
        var centralHits = 0, centralTotal = 0
        var lumSum = 0.0, lumCount = 0
        let margin = s * 0.04
        let span = s - 2 * margin
        for j in 0..<n {
            for i in 0..<n {
                var hitsInCell = 0, lSum = 0
                for sj in 0..<3 { for si in 0..<3 {
                    let px = sx + margin + (Double(i) + (Double(si) + 0.5) / 3) * span / Double(n)
                    let py = sy + margin + (Double(j) + (Double(sj) + 0.5) / 3) * span / Double(n)
                    let c = img.rgb(Int(px), Int(py))
                    let chroma = max(c.0, c.1, c.2) - min(c.0, c.1, c.2)
                    if colorDistance(c, maskBg) > 45 && chroma <= SquareFeature.maxPieceChroma && !SquareFeature.isMoveOverlay(c, bg: maskBg) { hitsInCell += 1 }
                    lSum += (c.0 * 299 + c.1 * 587 + c.2 * 114) / 1000
                } }
                let isPiece = hitsInCell >= 4
                let l = lSum / 9
                let k = j * n + i
                mask[k] = isPiece
                lum[k] = UInt8(min(255, max(0, l)))
                let central = i >= n / 8 && i < n - n / 8 && j >= n / 8 && j < n - n / 8
                if central {
                    centralTotal += 1
                    if isPiece { centralHits += 1 }
                }
                if isPiece { lumSum += Double(l); lumCount += 1 }
            }
        }
        return SquareFeature(mask: mask, lum: lum,
                             fill: Double(centralHits) / Double(max(centralTotal, 1)),
                             meanLum: lumCount > 0 ? lumSum / Double(lumCount) : 0,
                             bg: [bg.0, bg.1, bg.2])
    }

    func distance(to other: SquareFeature) -> Double {
        var best = Double.infinity
        for dy in -1...1 { for dx in -1...1 {
            best = min(best, distance(to: other, dx: dx, dy: dy))
        } }
        return best
    }

    private func distance(to other: SquareFeature, dx: Int, dy: Int) -> Double {
        let n = SquareFeature.n
        var cost = 0.0
        var union = 0
        mask.withUnsafeBufferPointer { ma in other.mask.withUnsafeBufferPointer { mb in
        lum.withUnsafeBufferPointer { la in other.lum.withUnsafeBufferPointer { lb in
            for j in 0..<n {
                let oj = j + dy
                for i in 0..<n {
                    let oi = i + dx
                    let k = j * n + i
                    let a = ma[k]
                    let inside = oi >= 0 && oi < n && oj >= 0 && oj < n
                    let ko = inside ? oj * n + oi : 0
                    let b = inside ? mb[ko] : false
                    if a || b {
                        union += 1
                        if a && b { cost += min(1, Double(abs(Int(la[k]) - Int(lb[ko]))) / 160.0) }
                        else { cost += 1 }
                    }
                }
            }
        } } } }
        return union == 0 ? 0 : min(cost / Double(union), 2)
    }
}

struct PieceTemplates: Codable {
    var templates: [String: [SquareFeature]] = [:]
    var squareColors: SquareColors? = nil
    var isEmpty: Bool { templates.isEmpty }

    static let occupiedFill = 0.06
    static let maxDistance = 0.42
    static let minFitSquares = 4

    static func startLayout(whiteAtBottom: Bool) -> [[Character]] {
        if whiteAtBottom {
            return [Array("rnbqkbnr"), Array("pppppppp"), [], [], [], [], Array("PPPPPPPP"), Array("RNBQKBNR")]
        } else {
            return [Array("RNBKQBNR"), Array("PPPPPPPP"), [], [], [], [], Array("pppppppp"), Array("rnbkqbnr")]
        }
    }

    static func looksLikeStart(_ features: [[SquareFeature]]) -> Bool {
        for r in 0..<8 {
            for c in 0..<8 {
                let occ = features[r][c].fill > occupiedFill
                if [0, 1, 6, 7].contains(r) != occ { return false }
            }
        }
        return true
    }

    @discardableResult
    mutating func learnStart(_ features: [[SquareFeature]]) -> Bool {
        let top = (features[0] + features[1]).map(\.meanLum).reduce(0, +)
        let bottom = (features[6] + features[7]).map(\.meanLum).reduce(0, +)
        let whiteAtBottom = bottom > top
        let layout = PieceTemplates.startLayout(whiteAtBottom: whiteAtBottom)
        learn(features) { r, c in layout[r].isEmpty ? nil : layout[r][c] }
        return whiteAtBottom
    }

    mutating func learnEarly(_ features: [[SquareFeature]]) -> Bool? {
        learnEarlyPosition(features)?.whiteAtBottom
    }

    mutating func learnEarlyPosition(_ features: [[SquareFeature]]) -> (whiteAtBottom: Bool, position: ChessPosition)? {
        let occupied = features.map { $0.map { $0.fill > PieceTemplates.occupiedFill } }
        let lums = zip(features.joined(), occupied.joined()).filter { $0.1 }.map { $0.0.meanLum }
        guard let lo0 = lums.min(), let hi0 = lums.max(), hi0 - lo0 > 40 else { return nil }
        var lo = lo0, hi = hi0
        for _ in 0..<8 {
            let t = (lo + hi) / 2
            lo = lums.filter { $0 <= t }.reduce(0, +) / Double(max(lums.filter { $0 <= t }.count, 1))
            hi = lums.filter { $0 > t }.reduce(0, +) / Double(max(lums.filter { $0 > t }.count, 1))
        }
        let threshold = (lo + hi) / 2
        let bright = features.map { $0.map { $0.meanLum > threshold } }
        guard let found = EarlyStart.matchPosition(occupied: occupied, bright: bright) else { return nil }
        let board = found.position.board
        learn(features) { r, c in
            let (file, rank) = Placement.chessSquare(col: c, row: r, whiteAtBottom: found.whiteAtBottom)
            return board.squares[rank][file]
        }
        return found
    }

    mutating func learnKnown(_ features: [[SquareFeature]], board: Placement, preferWhiteAtBottom: Bool?) -> Bool? {
        let first = preferWhiteAtBottom ?? true
        for whiteAtBottom in [first, !first] {
            var white: [Double] = [], black: [Double] = []
            var fits = true
            for r in 0..<8 { for c in 0..<8 {
                let (file, rank) = Placement.chessSquare(col: c, row: r, whiteAtBottom: whiteAtBottom)
                let piece = board.squares[rank][file]
                let occupied = features[r][c].fill > PieceTemplates.occupiedFill
                if occupied != (piece != nil) { fits = false }
                if let piece, occupied { if piece.isUppercase { white.append(features[r][c].meanLum) } else { black.append(features[r][c].meanLum) } }
            } }
            guard fits, !white.isEmpty, !black.isEmpty,
                  white.reduce(0, +) / Double(white.count) > black.reduce(0, +) / Double(black.count) else { continue }
            learn(features) { r, c in
                let (file, rank) = Placement.chessSquare(col: c, row: r, whiteAtBottom: whiteAtBottom)
                return board.squares[rank][file]
            }
            return whiteAtBottom
        }
        return nil
    }

    private mutating func learn(_ features: [[SquareFeature]], label: (Int, Int) -> Character?) {
        var t: [String: [SquareFeature]] = [:]
        for r in 0..<8 {
            for c in 0..<8 {
                guard let piece = label(r, c) else { continue }
                let key = String(piece)
                if (key == "p" || key == "P"), (t[key]?.count ?? 0) >= 4 { continue }
                t[key, default: []].append(features[r][c])
            }
        }
        templates = t
        squareColors = SquareColors.learn(from: features)
    }

    func fitsBadly(_ scan: ScanResult) -> Bool {
        guard !isEmpty else { return false }
        let cells = zip(scan.features.joined(), scan.screen.joined()).filter { $0.0.fill > PieceTemplates.occupiedFill }
        guard cells.count >= PieceTemplates.minFitSquares else { return false }
        if cells.filter({ $0.1 == nil }).count * 4 >= cells.count { return true }
        return [true, false].allSatisfy { wb in
            let board = Placement(screen: scan.screen, whiteAtBottom: wb)
            return board.problem(sideToMove: .white) != nil && board.problem(sideToMove: .black) != nil
        }
    }

    func distance(_ f: SquareFeature, to piece: Character) -> Double {
        templates[String(piece)]?.map { f.distance(to: $0) }.min() ?? .infinity
    }

    func classify(_ f: SquareFeature) -> Character? {
        guard f.fill > PieceTemplates.occupiedFill else { return nil }
        var bestKey: String?
        var bestDist = Double.infinity
        for (key, list) in templates {
            for t in list {
                let d = f.distance(to: t)
                if d < bestDist { bestDist = d; bestKey = key }
            }
        }
        guard let k = bestKey, bestDist <= PieceTemplates.maxDistance else { return nil }
        return k.first
    }
}
