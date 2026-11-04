import Foundation

enum StyleLength: String, Codable, CaseIterable, Comparable {
    case short, medium, long

    private var order: Int { self == .short ? 0 : self == .medium ? 1 : 2 }
    static func < (a: StyleLength, b: StyleLength) -> Bool { a.order < b.order }

    var maxTokens: Int { switch self { case .short: 120; case .medium: 220; case .long: 400 } }
    var maxWords: Int { switch self { case .short: 25; case .medium: 45; case .long: 110 } }
    var shorter: StyleLength? { self == .long ? .medium : self == .medium ? .short : nil }
    var longer: StyleLength? { self == .short ? .medium : self == .medium ? .long : nil }
}

enum StyleDepth: String, Codable, CaseIterable {
    case principle, concrete
    var other: StyleDepth { self == .principle ? .concrete : .principle }
}

struct ExplanationStyle: Codable, Hashable {
    var length: StyleLength = .medium
    var depth: StyleDepth = .principle
    var examples: Bool = false
    var tone: String = "auto"

    var key: String { "\(length.rawValue)|\(depth.rawValue)" }
    var maxTokens: Int { length.maxTokens }

    var neighbors: [ExplanationStyle] {
        var out: [ExplanationStyle] = []
        if let s = length.shorter { var n = self; n.length = s; out.append(n) }
        if let l = length.longer { var n = self; n.length = l; out.append(n) }
        var d = self; d.depth = depth.other; out.append(d)
        return out
    }

    static let shortPrinciple = ExplanationStyle(length: .short)
}

enum ExplanationFeedback: String, Codable {
    case tooLong, tooShort, helpful
    case askedAgain
    case collapsedImmediately
}

struct StyleScore: Codable, Equatable {
    var mean: Double = 0.5
    var n: Int = 0
    var puzzleCorrect: Int = 0
    var puzzleTotal: Int = 0

    var combined: Double {
        guard puzzleTotal >= 3 else { return mean }
        return 0.7 * mean + 0.3 * Double(puzzleCorrect) / Double(puzzleTotal)
    }
}

struct LearningStyle: Codable, Equatable {
    static let alpha = 0.25
    static let explorationRate = 0.10
    static let switchMargin = 0.05

    var length: StyleLength
    var depth: StyleDepth
    var examples: Bool
    var tone: String
    var scores: [String: StyleScore] = [:]

    init(start: ExplanationStyle = ExplanationStyle()) {
        length = start.length; depth = start.depth; examples = start.examples; tone = start.tone
    }

    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        length = try c.decodeIfPresent(StyleLength.self, forKey: .length) ?? .medium
        depth = try c.decodeIfPresent(StyleDepth.self, forKey: .depth) ?? .principle
        examples = try c.decodeIfPresent(Bool.self, forKey: .examples) ?? false
        tone = try c.decodeIfPresent(String.self, forKey: .tone) ?? "auto"
        scores = try c.decodeIfPresent([String: StyleScore].self, forKey: .scores) ?? [:]
    }

    var current: ExplanationStyle { ExplanationStyle(length: length, depth: depth, examples: examples, tone: tone) }

    func score(_ s: ExplanationStyle) -> StyleScore { scores[s.key] ?? StyleScore() }

    func styleForNextExplanation<G: RandomNumberGenerator>(using rng: inout G) -> (style: ExplanationStyle, explored: Bool) {
        if Double.random(in: 0..<1, using: &rng) < LearningStyle.explorationRate,
           let n = current.neighbors.randomElement(using: &rng) { return (n, true) }
        return (current, false)
    }

    func styleForNextExplanation() -> (style: ExplanationStyle, explored: Bool) {
        var g = SystemRandomNumberGenerator()
        return styleForNextExplanation(using: &g)
    }

    private mutating func nudge(_ s: ExplanationStyle, reward: Double, weight: Double = 1) {
        var sc = scores[s.key] ?? StyleScore()
        sc.mean += LearningStyle.alpha * weight * (reward - sc.mean)
        sc.n += 1
        scores[s.key] = sc
    }

    mutating func record(_ f: ExplanationFeedback, used: ExplanationStyle) {
        switch f {
        case .helpful: nudge(used, reward: 1)
        case .tooLong, .collapsedImmediately:
            let w = f == .tooLong ? 1.0 : 0.5
            if let s = used.length.shorter {
                nudge(used, reward: 0, weight: w)
                var n = used; n.length = s
                nudge(n, reward: 0.8, weight: w)
            }
        case .tooShort, .askedAgain:
            let w = f == .tooShort ? 1.0 : 0.5
            if let l = used.length.longer {
                nudge(used, reward: 0, weight: w)
                var n = used; n.length = l
                nudge(n, reward: 0.8, weight: w)
            }
        }
        reselect()
    }

    mutating func recordPuzzle(after used: ExplanationStyle, correct: Bool) {
        var sc = scores[used.key] ?? StyleScore()
        sc.puzzleTotal += 1
        if correct { sc.puzzleCorrect += 1 }
        scores[used.key] = sc
        reselect()
    }

    private mutating func reselect() {
        let cur = score(current).combined
        var best = current, bestScore = cur
        for l in StyleLength.allCases { for d in StyleDepth.allCases {
            let s = ExplanationStyle(length: l, depth: d, examples: examples, tone: tone)
            let v = score(s).combined
            if v > bestScore + 1e-9 && v > cur + LearningStyle.switchMargin { best = s; bestScore = v }
        } }
        length = best.length; depth = best.depth
    }
}

extension PlayerProfile {
    func nextExplanationStyle() -> (style: ExplanationStyle, explored: Bool) { style.styleForNextExplanation() }

    mutating func recordFeedback(_ f: ExplanationFeedback, usedStyle: ExplanationStyle) { style.record(f, used: usedStyle) }

    mutating func recordPuzzleAfterExplanation(usedStyle: ExplanationStyle, correct: Bool) {
        style.recordPuzzle(after: usedStyle, correct: correct)
    }
}
