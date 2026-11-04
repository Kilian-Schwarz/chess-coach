import Foundation

struct SquareFeature: Codable {
    static let n = 32
    static let maxPieceChroma = 80
    var mask: [Bool]
    var lum: [UInt8]
    var fill: Double
    var meanLum: Double
    var bg: [Int]? = nil

    static func extract(_ img: RGBImage, board: BoardRect, col: Int, row: Int) -> SquareFeature {
        let (sx, sy, s) = board.squareRect(col: col, row: row)
        let n = SquareFeature.n

        var rs: [Int] = [], gs: [Int] = [], bs: [Int] = []
        let inset = max(1.5, s * 0.06)
        let steps = 24
        for i in 0..<steps {
            let t = inset + (s - 2 * inset) * Double(i) / Double(steps - 1)
            for (px, py) in [(sx + t, sy + inset), (sx + t, sy + s - inset), (sx + inset, sy + t), (sx + s - inset, sy + t)] {
                let c = img.rgb(Int(px), Int(py))
                rs.append(c.0); gs.append(c.1); bs.append(c.2)
            }
        }
        rs.sort(); gs.sort(); bs.sort()
        let bg = (rs[rs.count / 2], gs[gs.count / 2], bs[bs.count / 2])

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
                    if colorDistance(c, bg) > 45 && chroma <= SquareFeature.maxPieceChroma { hitsInCell += 1 }
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
        var t: [String: [SquareFeature]] = [:]
        for r in [0, 1, 6, 7] {
            for c in 0..<8 {
                let key = String(layout[r][c])
                if (key == "p" || key == "P"), (t[key]?.count ?? 0) >= 4 { continue }
                t[key, default: []].append(features[r][c])
            }
        }
        templates = t
        squareColors = SquareColors.learn(from: features)
        return whiteAtBottom
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
