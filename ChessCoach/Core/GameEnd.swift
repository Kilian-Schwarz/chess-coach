import Foundation

enum GameEndReason: String {
    case checkmate, stalemate, overlay, boardGone, newStart, manual
}

struct GameEnd: Equatable {
    let reason: GameEndReason
    let result: String
    var silent = false
    var knownResult: Bool { result != "*" }
}

enum BoardView {
    case readable, covered, gone
}

struct BoardColors: Equatable {
    var light: [Double]
    var dark: [Double]

    static func learn(_ features: [[SquareFeature]]) -> BoardColors {
        BoardColors(light: BoardReadability.parityMedian(features, 1), dark: BoardReadability.parityMedian(features, 0))
    }
}

enum BoardReadability {
    static let coveredLimit = 4
    static let deviation = 40.0
    static let distance = 40.0
    static let ringTolerance = 45.0
    static let ringShare = 0.6

    static func bg(_ f: SquareFeature) -> [Double]? {
        guard let b = f.bg, b.count == 3 else { return nil }
        return b.map(Double.init)
    }

    static func parityMedian(_ features: [[SquareFeature]], _ parity: Int) -> [Double] {
        var cols: [[Double]] = [[], [], []]
        for r in 0..<8 { for c in 0..<8 where (r + c) % 2 == parity {
            guard let b = bg(features[r][c]) else { continue }
            for k in 0..<3 { cols[k].append(b[k]) }
        } }
        return cols.map { $0.isEmpty ? 0 : $0.sorted()[$0.count / 2] }
    }

    static func luminance(_ c: [Double]) -> Double { 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2] }

    static func dist(_ a: [Double], _ b: [Double]) -> Double {
        ((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2])).squareRoot()
    }

    static func coveredSquares(_ features: [[SquareFeature]]) -> Int {
        let refs = [parityMedian(features, 0), parityMedian(features, 1)]
        var covered = 0
        for r in 0..<8 { for c in 0..<8 {
            guard let px = bg(features[r][c]) else { continue }
            let ref = refs[(r + c) % 2]
            if abs(luminance(ref) - luminance(px)) >= deviation && dist(ref, px) >= distance { covered += 1 }
        } }
        return covered
    }

    static func isReadable(_ features: [[SquareFeature]]) -> Bool { coveredSquares(features) < coveredLimit }

    static func ringMatch(_ features: [[SquareFeature]], _ colors: BoardColors) -> Double {
        var total = 0, hits = 0
        for r in 0..<8 { for c in 0..<8 where r == 0 || r == 7 || c == 0 || c == 7 {
            total += 1
            guard let px = bg(features[r][c]) else { continue }
            if dist(px, (r + c) % 2 == 1 ? colors.light : colors.dark) <= ringTolerance { hits += 1 }
        } }
        return Double(hits) / Double(total)
    }

    static let verifyShare = 0.6

    enum Verification { case matched, covered, failed }

    static func matchShare(_ features: [[SquareFeature]], _ colors: BoardColors) -> Double {
        var hits = 0
        for r in 0..<8 { for c in 0..<8 {
            guard let px = bg(features[r][c]) else { continue }
            if dist(px, (r + c) % 2 == 1 ? colors.light : colors.dark) <= ringTolerance { hits += 1 }
        } }
        return Double(hits) / 64
    }

    static func verify(_ features: [[SquareFeature]], colors: BoardColors?) -> Verification {
        guard let colors else { return .failed }
        if matchShare(features, colors) >= verifyShare { return .matched }
        return ringMatch(features, colors) >= ringShare ? .covered : .failed
    }

    static func view(_ features: [[SquareFeature]], colors: BoardColors?) -> BoardView {
        if isReadable(features) { return .readable }
        guard let colors else { return .gone }
        return ringMatch(features, colors) >= ringShare ? .covered : .gone
    }
}

struct GameEndDetector {
    static let minPlies = 6
    static let coveredSeconds = 3.0
    static let goneSeconds = 600.0

    private var coveredSince: Date?
    private var goneSince: Date?

    mutating func reset() { coveredSince = nil; goneSince = nil }

    mutating func observe(_ view: BoardView, plies: Int, result: GameResult?, isStart: Bool, at now: Date) -> GameEnd? {
        if view != .covered { coveredSince = nil }
        if view != .gone { goneSince = nil }
        switch view {
        case .readable:
            if plies >= 1, let result { return Self.end(for: result) }
            if isStart, plies >= Self.minPlies { return GameEnd(reason: .newStart, result: "*") }
        case .covered:
            if coveredSince == nil { coveredSince = now }
            if plies >= Self.minPlies, let since = coveredSince, now.timeIntervalSince(since) >= Self.coveredSeconds {
                coveredSince = nil
                return GameEnd(reason: .overlay, result: "*")
            }
        case .gone:
            if goneSince == nil { goneSince = now }
            if plies >= Self.minPlies, let since = goneSince, now.timeIntervalSince(since) >= Self.goneSeconds {
                goneSince = nil
                return GameEnd(reason: .boardGone, result: "*", silent: true)
            }
        }
        return nil
    }

    static func end(for result: GameResult) -> GameEnd? {
        switch result {
        case .checkmate: GameEnd(reason: .checkmate, result: result.pgn)
        case .stalemate: GameEnd(reason: .stalemate, result: result.pgn)
        default: nil
        }
    }
}
