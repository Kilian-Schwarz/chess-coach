import Foundation

struct HintStats: Codable, Equatable {
    var levelUses: [Int] = [0, 0, 0, 0, 0]
}

struct RatingEntry: Codable, Equatable {
    var date: Date
    var rating: Double
    var performance: Double
    var weight: Double? = nil
}

struct PlayerProfile: Codable, Equatable, Identifiable {
    static let currentSchemaVersion = 1
    static let placementTotal = 3
    static let startRating = 1100.0
    static let alpha = 0.15
    static let minSkillObservations = 10
    static let focusLeaveSkill = 0.8
    static let focusEnterSkill = 0.6
    static let minGameMoves = 10
    static let defaultGameWeight = 20.0

    static var defaultName: String {
        Loc.display("profile.defaultName", "Player")
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
    var skillCounts: [String: Int] = [:]
    var mistakeCounts: [String: Int] = [:]
    var mistakeExamples: [String: [String]]? = nil
    var focus: String
    var learningSpeed: Double = 1.0
    var learningSpeedAdjustedAt: Date? = nil
    var pinnedFocus: String? = nil
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
        skillCounts = try c.decodeIfPresent([String: Int].self, forKey: .skillCounts) ?? [:]
        mistakeCounts = try c.decodeIfPresent([String: Int].self, forKey: .mistakeCounts) ?? [:]
        mistakeExamples = try c.decodeIfPresent([String: [String]].self, forKey: .mistakeExamples)
        focus = try c.decodeIfPresent(String.self, forKey: .focus) ?? Curriculum.focus(skills: s).rawValue
        learningSpeed = try c.decodeIfPresent(Double.self, forKey: .learningSpeed) ?? 1.0
        learningSpeedAdjustedAt = try c.decodeIfPresent(Date.self, forKey: .learningSpeedAdjustedAt)
        pinnedFocus = try c.decodeIfPresent(String.self, forKey: .pinnedFocus)
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

    func observationCount(_ skill: Skill) -> Int { skillCounts[skill.rawValue] ?? 0 }

    func hasEnoughData(_ skill: Skill) -> Bool { observationCount(skill) >= PlayerProfile.minSkillObservations }

    var showsThreatAutomatically: Bool { Curriculum.showsThreatAutomatically(gamesPlayed: gamesPlayed, skills: skills) }

    var supportFaded: Bool { Curriculum.supportFaded(skills: skills, counts: skillCounts) }

    mutating func observe(_ skill: Skill, value: Double, gainWeight: Double = 1) {
        let v = min(1, max(0, value))
        let key = skill.rawValue
        let old = skills[key] ?? 0.5
        let n = skillCounts[key] ?? 0
        var a = max(PlayerProfile.alpha, 1 / Double(n + 2))
        if v > old { a *= gainWeight }
        skills[key] = (1 - a) * old + a * v
        skillCounts[key] = n + 1
    }

    mutating func apply(_ analysis: MoveAnalysis, gainWeight: Double = 1) {
        for o in analysis.observations { observe(o.skill, value: o.value, gainWeight: gainWeight) }
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

    mutating func updateFocus() {
        if let pinned = pinnedFocus { focus = pinned; return }
        let current = focusSkill
        if Curriculum.value(skills, current) >= PlayerProfile.focusLeaveSkill {
            focus = Curriculum.focus(skills: skills).rawValue
        } else if let earlier = Curriculum.path.prefix(while: { $0.skill != current })
            .first(where: { Curriculum.value(skills, $0.skill) < PlayerProfile.focusEnterSkill }) {
            focus = earlier.skill.rawValue
        }
    }

    mutating func pinFocus(_ skill: Skill) {
        pinnedFocus = skill.rawValue
        focus = skill.rawValue
    }

    mutating func adjustLearningSpeed(puzzles: [Puzzle], now: Date = Date(), calendar: Calendar = .current) {
        if let last = learningSpeedAdjustedAt, calendar.isDate(last, inSameDayAs: now) { return }
        guard PuzzleScheduler.reviewAttempts(puzzles).count >= PuzzleScheduler.minReviewsForSpeed else { return }
        learningSpeed = PuzzleScheduler.adjustedLearningSpeed(current: learningSpeed, puzzles: puzzles)
        learningSpeedAdjustedAt = now
    }

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
    mutating func completeGame(performance: Double, weight: Double? = nil, assistedShare: Double = 0, date: Date = Date()) -> Double {
        gamesPlayed += 1
        pinnedFocus = nil
        if isPlacing {
            placementGames += 1
            let w = weight ?? PlayerProfile.defaultGameWeight
            var sum = performance * w
            var total = w
            for e in ratingHistory.prefix(placementGames - 1) {
                let ew = e.weight ?? PlayerProfile.defaultGameWeight
                sum += e.performance * ew
                total += ew
            }
            rating = sum / total
        } else {
            var delta = Accuracy.ratingDelta(rating: rating, performance: performance, gameNumber: gamesPlayed)
            if delta > 0 { delta *= 1 - min(1, max(0, assistedShare)) }
            rating += delta
        }
        level = PlayerLevel.from(rating: rating)
        ratingHistory.append(RatingEntry(date: date, rating: rating, performance: performance, weight: weight))
        return rating
    }

    @discardableResult
    mutating func completeGame(summary s: SideSummary, date: Date = Date()) -> Double? {
        guard s.countsForRating else { return nil }
        return completeGame(performance: s.performance, weight: Double(s.moves), assistedShare: s.assistedShare ?? 0, date: date)
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
