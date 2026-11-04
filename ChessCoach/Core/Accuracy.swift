import Foundation

enum PlayerLevel: String, Codable, CaseIterable {
    case beginner, intermediate, strong

    func title(_ loc: Loc = .display) -> String {
        switch self {
        case .beginner: loc("level.beginner", "Beginner")
        case .intermediate: loc("level.intermediate", "Intermediate")
        case .strong: loc("level.strong", "Strong")
        }
    }

    static func from(rating: Double) -> PlayerLevel {
        if rating < 1200 { return .beginner }
        if rating <= 1800 { return .intermediate }
        return .strong
    }
}

enum Accuracy {
    static let mateValue = 10_000
    static let maxLoss = 1000

    static func winChance(cp: Double) -> Double {
        50 + 50 * (2 / (1 + exp(-0.00368208 * cp)) - 1)
    }

    static func winChance(cp: Int) -> Double { winChance(cp: Double(cp)) }

    static func moveAccuracy(winBefore: Double, winAfter: Double) -> Double {
        min(100, max(0, 103.1668 * exp(-0.04354 * (winBefore - winAfter)) - 3.1669))
    }

    static func moveAccuracy(valueBefore: Int, valueAfter: Int) -> Double {
        moveAccuracy(winBefore: winChance(cp: valueBefore), winAfter: winChance(cp: valueAfter))
    }

    static func cpLoss(valueBefore: Int, valueAfter: Int) -> Int {
        min(maxLoss, max(0, valueBefore - valueAfter))
    }

    static func gameAccuracy(_ moveAccuracies: [Double]) -> Double {
        moveAccuracies.isEmpty ? 0 : moveAccuracies.reduce(0, +) / Double(moveAccuracies.count)
    }

    static func acpl(_ losses: [Int]) -> Double {
        losses.isEmpty ? 0 : Double(losses.map { min(maxLoss, max(0, $0)) }.reduce(0, +)) / Double(losses.count)
    }

    private static let performanceTable: [(acpl: Double, rating: Double)] = [
        (10, 2400), (20, 2100), (35, 1800), (50, 1600), (70, 1400), (100, 1200), (150, 1000), (250, 800),
    ]

    static func performanceRating(acpl: Double) -> Double {
        let t = performanceTable
        if acpl <= t[0].acpl { return t[0].rating }
        if acpl > t[t.count - 1].acpl { return 600 }
        for i in 1..<t.count where acpl <= t[i].acpl {
            let (a0, r0) = t[i - 1], (a1, r1) = t[i]
            return r0 + (r1 - r0) * (acpl - a0) / (a1 - a0)
        }
        return 600
    }

    static func ratingDelta(rating: Double, performance: Double, gameNumber: Int) -> Double {
        let k = gameNumber <= 10 ? 0.5 : 0.25
        return min(150, max(-150, k * (performance - rating) / 400 * 100))
    }
}
