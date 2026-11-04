import Foundation

struct HintStats: Codable, Equatable {
    var levelUses: [Int] = [0, 0, 0, 0, 0]
}

struct RatingEntry: Codable, Equatable {
    var date: Date
    var rating: Double
    var performance: Double
}

struct PlayerProfile: Codable, Equatable, Identifiable {
    static let currentSchemaVersion = 1
    static let placementTotal = 3
    static let startRating = 1100.0
    static let alpha = 0.15

    static var defaultName: String {
        let full = NSFullUserName()
        return full.isEmpty ? Loc.display("profile.defaultName", "Player") : full
    }

    var schemaVersion: Int = PlayerProfile.currentSchemaVersion
    var id: String
    var name: String
    var createdAt: Date
    var gamesPlayed: Int = 0
    var placementGames: Int = 0
    var rating: Double = PlayerProfile.startRating
    var level: PlayerLevel = .beginner
    var skills: [String: Double]
    var mistakeCounts: [String: Int] = [:]
    var mistakeExamples: [String: [String]]? = nil
    var focus: String
    var learningSpeed: Double = 1.0
    var hintStats = HintStats()
    var ratingHistory: [RatingEntry] = []
    var style = LearningStyle()

    init(id: String = UUID().uuidString.lowercased(), name: String, createdAt: Date = Date(), startStyle: ExplanationStyle = ExplanationStyle()) {
        style = LearningStyle(start: startStyle)
        self.id = id
        self.name = name
        self.createdAt = createdAt
        var s: [String: Double] = [:]
        for k in Skill.allCases { s[k.rawValue] = 0.5 }
        skills = s
        focus = Curriculum.focus(skills: s).rawValue
    }

    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        schemaVersion = try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1
        id = try c.decode(String.self, forKey: .id)
        name = try c.decode(String.self, forKey: .name)
        createdAt = try c.decodeIfPresent(Date.self, forKey: .createdAt) ?? Date()
        gamesPlayed = try c.decodeIfPresent(Int.self, forKey: .gamesPlayed) ?? 0
        placementGames = try c.decodeIfPresent(Int.self, forKey: .placementGames) ?? 0
        rating = try c.decodeIfPresent(Double.self, forKey: .rating) ?? PlayerProfile.startRating
        level = try c.decodeIfPresent(PlayerLevel.self, forKey: .level) ?? PlayerLevel.from(rating: rating)
        var s = try c.decodeIfPresent([String: Double].self, forKey: .skills) ?? [:]
        for k in Skill.allCases where s[k.rawValue] == nil { s[k.rawValue] = 0.5 }
        skills = s
        mistakeCounts = try c.decodeIfPresent([String: Int].self, forKey: .mistakeCounts) ?? [:]
        mistakeExamples = try c.decodeIfPresent([String: [String]].self, forKey: .mistakeExamples)
        focus = try c.decodeIfPresent(String.self, forKey: .focus) ?? Curriculum.focus(skills: s).rawValue
        learningSpeed = try c.decodeIfPresent(Double.self, forKey: .learningSpeed) ?? 1.0
        hintStats = try c.decodeIfPresent(HintStats.self, forKey: .hintStats) ?? HintStats()
        if hintStats.levelUses.count < 5 { hintStats.levelUses += Array(repeating: 0, count: 5 - hintStats.levelUses.count) }
        ratingHistory = try c.decodeIfPresent([RatingEntry].self, forKey: .ratingHistory) ?? []
        style = try c.decodeIfPresent(LearningStyle.self, forKey: .style) ?? LearningStyle()
    }

    var isPlacing: Bool { placementGames < PlayerProfile.placementTotal }

    func placementText(_ loc: Loc = .display) -> String? {
        isPlacing ? loc("profile.placement", "Placement in progress: game \(placementGames + 1) of \(PlayerProfile.placementTotal)") : nil
    }

    var placementText: String? { placementText() }

    var focusSkill: Skill { Skill(rawValue: focus) ?? .pieceSafety }

    mutating func observe(_ skill: Skill, value: Double) {
        let v = min(1, max(0, value))
        let old = skills[skill.rawValue] ?? 0.5
        skills[skill.rawValue] = (1 - PlayerProfile.alpha) * old + PlayerProfile.alpha * v
    }

    mutating func apply(_ analysis: MoveAnalysis) {
        for o in analysis.observations { observe(o.skill, value: o.value) }
        if let m = analysis.mistakeType, let example = analysis.example { recordMistake(m, example: example) }
        updateFocus()
    }

    mutating func recordMistake(_ type: MistakeType, example: String? = nil) {
        mistakeCounts[type.rawValue, default: 0] += 1
        guard let example else { return }
        var all = mistakeExamples ?? [:]
        all[type.rawValue] = Array(((all[type.rawValue] ?? []) + [example]).suffix(5))
        mistakeExamples = all
    }

    mutating func recordHintUse(level: Int) {
        let i = min(4, max(0, level))
        hintStats.levelUses[i] += 1
    }

    mutating func updateFocus() { focus = Curriculum.focus(skills: skills).rawValue }

    func topMistakes(_ n: Int = 3) -> [(type: MistakeType, count: Int)] {
        let list = mistakeCounts.compactMap { k, v in MistakeType(rawValue: k).flatMap { v > 0 ? (type: $0, count: v) : nil } }
        return Array(list.sorted { $0.count != $1.count ? $0.count > $1.count : $0.type.rawValue < $1.type.rawValue }.prefix(n))
    }

    var averageHintLevel: Double? {
        let uses = Array(hintStats.levelUses.dropFirst())
        let used = uses.reduce(0, +)
        guard used > 0 else { return nil }
        return Double(uses.enumerated().reduce(0) { $0 + ($1.offset + 1) * $1.element }) / Double(used)
    }

    @discardableResult
    mutating func completeGame(performance: Double, date: Date = Date()) -> Double {
        gamesPlayed += 1
        if isPlacing {
            placementGames += 1
            let perfs = ratingHistory.prefix(placementGames - 1).map(\.performance) + [performance]
            rating = perfs.reduce(0, +) / Double(perfs.count)
        } else {
            rating += Accuracy.ratingDelta(rating: rating, performance: performance, gameNumber: gamesPlayed)
        }
        level = PlayerLevel.from(rating: rating)
        ratingHistory.append(RatingEntry(date: date, rating: rating, performance: performance))
        return rating
    }

    var promptContext: PlayerContext {
        PlayerContext(level: level, focus: focusSkill,
                      typicalMistakes: topMistakes(3).map { $0.type.title(.facts) })
    }
}

struct PlayerContext: Equatable {
    var level: PlayerLevel
    var focus: Skill?
    var typicalMistakes: [String] = []

    static let neutral = PlayerContext(level: .intermediate, focus: nil)
}
