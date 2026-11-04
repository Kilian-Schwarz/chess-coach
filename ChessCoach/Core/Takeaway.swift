import Foundation

struct GameMistake: Codable, Equatable {
    var ply: Int
    var color: String
    var type: String
    var loss: Int
    var example: String?
    var puzzleId: String?
}

struct Takeaway: Codable, Equatable {
    enum Kind: String, Codable { case mistake, positive, habit }

    static let minStreak = 5
    static let maxCount = 3

    var kind: Kind
    var skill: String
    var mistakeType: String? = nil
    var count = 0
    var streak = 0
    var example: String? = nil
    var puzzleIds: [String] = []

    var skillValue: Skill { Skill(rawValue: skill) ?? .middlegame }

    func text(_ loc: Loc = .display) -> String {
        switch kind {
        case .mistake:
            let name = MistakeType(rawValue: mistakeType ?? "")?.title(loc) ?? ""
            guard let example else { return loc("takeaway.mistakeShort", "Most frequent mistake: \(name), \(count)× this game") }
            return loc("takeaway.mistake", "Most frequent mistake: \(name), \(count)× this game. Example: \(example)")
        case .positive:
            return loc("takeaway.positive", "Well done: \(streak) good moves in a row")
        case .habit:
            return loc("takeaway.habit", "Habit for the next game: \(skillValue.summary(loc))")
        }
    }

    static func bestStreak(_ moves: [MoveRecord], color: String) -> Int {
        var best = 0, run = 0
        for m in moves where m.color == color {
            if let r = MoveRating(rawValue: m.rating), r == .best || r == .good { run += 1; best = max(best, run) } else { run = 0 }
        }
        return best
    }

    static func build(mistakes: [GameMistake], moves: [MoveRecord], color: String, focus: Skill) -> [Takeaway] {
        var out: [Takeaway] = []
        let groups = Dictionary(grouping: mistakes.filter { $0.color == color }, by: \.type)
        let ranked = groups.compactMap { key, list in MistakeType(rawValue: key).map { (type: $0, list: list) } }.sorted { a, b in
            if a.list.count != b.list.count { return a.list.count > b.list.count }
            let la = a.list.reduce(0) { $0 + $1.loss }, lb = b.list.reduce(0) { $0 + $1.loss }
            return la != lb ? la > lb : a.type.rawValue < b.type.rawValue
        }
        if let top = ranked.first {
            out.append(Takeaway(kind: .mistake, skill: top.type.skill.rawValue, mistakeType: top.type.rawValue, count: top.list.count,
                                example: top.list.max { $0.loss < $1.loss }?.example, puzzleIds: top.list.compactMap(\.puzzleId)))
        }
        let streak = bestStreak(moves, color: color)
        if streak >= minStreak { out.append(Takeaway(kind: .positive, skill: focus.rawValue, streak: streak)) }
        out.append(Takeaway(kind: .habit, skill: focus.rawValue))
        return Array(out.prefix(maxCount))
    }
}
