import Foundation

struct BoardRect: Equatable {
    var x: Double
    var y: Double
    var size: Double
    var square: Double { size / 8 }

    func squareRect(col: Int, row: Int) -> (x: Double, y: Double, s: Double) {
        (x + Double(col) * square, y + Double(row) * square, square)
    }
}

enum BoardDetector {
    struct Segment { let pos: Int; let start: Int; let end: Int; var length: Int { end - start } }

    static func detect(_ img: RGBImage, minSize: Int = 160, threshold: Int = 36) -> BoardRect? {
        let vertical = segments(img, vertical: true, minLength: minSize, threshold: threshold)
        let horizontal = segments(img, vertical: false, minLength: minSize, threshold: threshold)

        let vIndex = Dictionary(grouping: vertical, by: \.pos)
        let hIndex = Dictionary(grouping: horizontal, by: \.pos)
        var best: (rect: BoardRect, score: Int)?
        func consider(_ rect: BoardRect) {
            let s = rect.square
            let vHits = covered(vIndex, origin: rect.x, step: s, from: rect.y, to: rect.y + rect.size)
            let hHits = covered(hIndex, origin: rect.y, step: s, from: rect.x, to: rect.x + rect.size)
            guard vHits >= 5, hHits >= 5 else { return }
            let score = (vHits + hHits) * 10_000 + Int(rect.size)
            if best == nil || score > best!.score { best = (rect, score) }
        }

        for g in group(vertical) where g.positions.count >= 4 {
            let size = Double(g.end - g.start), s = size / 8
            for p in g.positions { for k in 1...7 {
                consider(BoardRect(x: (p - Double(k) * s).rounded(), y: Double(g.start), size: size))
            } }
        }
        for g in group(horizontal) where g.positions.count >= 4 {
            let size = Double(g.end - g.start), s = size / 8
            for p in g.positions { for k in 1...7 {
                consider(BoardRect(x: Double(g.start), y: (p - Double(k) * s).rounded(), size: size))
            } }
        }
        return best?.rect
    }

    private static func covered(_ index: [Int: [Segment]], origin: Double, step: Double, from: Double, to: Double) -> Int {
        let tol = max(2.5, step * 0.04)
        var n = 0
        for k in 1...7 {
            let expected = origin + Double(k) * step - 0.5
            let lo = Int((expected - tol - 0.5).rounded(.down)), hi = Int((expected + tol + 0.5).rounded(.up))
            var found = false
            for pos in lo...hi {
                if index[pos]?.contains(where: { Double($0.start) <= from + 4 && Double($0.end) >= to - 4 }) == true {
                    found = true; break
                }
            }
            if found { n += 1 }
        }
        return n
    }

    struct Group { var start: Int; var end: Int; var positions: [Double] }

    private static func group(_ segs: [Segment]) -> [Group] {
        var groups: [(start: Int, end: Int, pos: [Int])] = []
        for s in segs.sorted(by: { $0.length > $1.length }) {
            if let i = groups.firstIndex(where: { abs($0.start - s.start) <= 4 && abs($0.end - s.end) <= 4 }) {
                groups[i].pos.append(s.pos)
            } else {
                groups.append((s.start, s.end, [s.pos]))
            }
        }
        return groups.map { g in
            let sorted = g.pos.sorted()
            var clusters: [[Int]] = []
            for p in sorted {
                if let last = clusters.last?.last, p - last <= 2 { clusters[clusters.count - 1].append(p) }
                else { clusters.append([p]) }
            }
            let positions = clusters.map { c in Double(c.reduce(0, +)) / Double(c.count) + 0.5 }
            return Group(start: g.start, end: g.end, positions: positions)
        }
    }

    static func segments(_ img: RGBImage, vertical: Bool, minLength: Int, threshold: Int) -> [Segment] {
        let w = img.width, h = img.height
        let outer = vertical ? w : h, inner = vertical ? h : w
        guard outer > 2, inner > minLength else { return [] }
        let maxGap = 6
        var result: [Segment] = []
        img.pixels.withUnsafeBufferPointer { p in
            for o in 1..<(outer - 1) {
                var runStart = -1, lastHit = -1
                for t in 0..<inner {
                    let a: Int, b: Int
                    if vertical {
                        a = (t * w + o - 1) * 4; b = (t * w + o + 1) * 4
                    } else {
                        a = ((o - 1) * w + t) * 4; b = ((o + 1) * w + t) * 4
                    }
                    let d = abs(Int(p[a]) - Int(p[b])) + abs(Int(p[a + 1]) - Int(p[b + 1])) + abs(Int(p[a + 2]) - Int(p[b + 2]))
                    if d > threshold {
                        if runStart < 0 { runStart = t }
                        lastHit = t
                    } else if runStart >= 0 && t - lastHit > maxGap {
                        if lastHit - runStart >= minLength { result.append(Segment(pos: o, start: runStart, end: lastHit + 1)) }
                        runStart = -1
                    }
                }
                if runStart >= 0 && lastHit - runStart >= minLength {
                    result.append(Segment(pos: o, start: runStart, end: lastHit + 1))
                }
            }
        }
        return result
    }
}
