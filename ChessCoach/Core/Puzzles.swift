import Foundation

struct PuzzleAttempt: Codable, Equatable {
    var date: Date
    var correct: Bool
    var hintLevel: Int
    var assisted: Bool = false

    var counted: Bool { !assisted }
}

extension PuzzleAttempt {
    init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        date = try c.decode(Date.self, forKey: .date)
        correct = try c.decode(Bool.self, forKey: .correct)
        hintLevel = try c.decode(Int.self, forKey: .hintLevel)
        assisted = try c.decodeIfPresent(Bool.self, forKey: .assisted) ?? (hintLevel > 0)
    }
}

struct Puzzle: Codable, Equatable, Identifiable {
    var id: String
    var fen: String
    var acceptedMoves: [String]
    var bestLine: [String]
    var theme: String
    var mistakeType: String
    var createdAt: Date
    var box: Int = 1
    var dueAt: Date
    var attempts: [PuzzleAttempt] = []
    var mastered: Bool = false

    static let intervalsDays: [Double] = [1, 3, 7, 16, 35]
    static let acceptTolerance = 30
    static let alternativeTolerance = 40

    var isNew: Bool { attempts.isEmpty }
    var skill: Skill { (MistakeType(rawValue: mistakeType) ?? .other).skill }

    static func make(review r: MoveReview, beforeResult: EngineResult, mistakeType: MistakeType?, theme: HintTheme?,
                     level: PlayerLevel, now: Date = Date(), id: String = UUID().uuidString.lowercased()) -> Puzzle? {
        let qualifies = r.rating == .mistake || r.rating == .blunder || (level == .strong && r.rating == .inaccuracy)
        guard qualifies, let top = beforeResult.lines.first else { return nil }
        let accepted = beforeResult.lines.filter { $0.value >= top.value - acceptTolerance && !$0.move.isEmpty }.map(\.move)
        guard !accepted.isEmpty else { return nil }
        return Puzzle(id: id, fen: r.before.fen(sideToMove: r.player), acceptedMoves: accepted, bestLine: top.pv,
                      theme: (theme ?? .improvePiece).rawValue, mistakeType: (mistakeType ?? .other).rawValue,
                      createdAt: now, dueAt: now)
    }

    func accepts(_ uci: String) -> Bool { acceptedMoves.contains(uci.lowercased()) }

    @discardableResult
    mutating func attempt(move uci: String, hintLevel: Int = 0, now: Date = Date(), learningSpeed: Double = 1.0,
                          calendar: Calendar = .current) -> Bool {
        let correct = accepts(uci)
        attempts.append(PuzzleAttempt(date: now, correct: correct, hintLevel: hintLevel, assisted: hintLevel > 0))
        let speed = min(1.5, max(0.6, learningSpeed))
        if !correct {
            box = 1
        } else if hintLevel >= 4 {
            box = max(1, box - 1)
        } else if hintLevel == 0 {
            if box >= 5 { mastered = true } else { box += 1 }
        }
        let days = max(1, Int((Puzzle.intervalsDays[min(5, max(1, box)) - 1] * speed).rounded()))
        dueAt = calendar.startOfDay(for: calendar.date(byAdding: .day, value: days, to: now) ?? now)
        return correct
    }

    static func isAcceptableAlternative(bestValue: Int, playedValue: Int) -> Bool {
        bestValue - playedValue <= alternativeTolerance
    }

    mutating func acceptAlternative(_ uci: String) {
        let move = uci.lowercased()
        if !acceptedMoves.contains(move) { acceptedMoves.append(move) }
    }
}

struct PuzzleSet: Codable, Equatable {
    var schemaVersion: Int = 1
    var puzzles: [Puzzle] = []

    @discardableResult
    mutating func add(_ p: Puzzle) -> Bool {
        if let i = puzzles.firstIndex(where: { $0.fen == p.fen }) {
            puzzles[i].box = 1
            puzzles[i].mastered = false
            puzzles[i].dueAt = p.dueAt
            return false
        }
        puzzles.append(p)
        return true
    }
}

struct PuzzleStats: Equatable {
    var total = 0, mastered = 0, due = 0, new = 0
    var attempts = 0
    var recentSuccessRate: Double?
    var byBox: [Int] = [0, 0, 0, 0, 0]
}

enum PuzzleScheduler {
    static let maxNewPerDay = 10
    static let maxTotalPerDay = 15
    static let recentWindow = 30
    static let minReviewsForSpeed = 10
    static let focusBias = 0.55
    static let maxThemeRun = 3

    static func reviewAttempts(_ puzzles: [Puzzle]) -> [PuzzleAttempt] {
        puzzles.flatMap { $0.attempts.dropFirst().filter(\.counted) }.sorted { $0.date < $1.date }
    }

    static func recentSuccessRate(_ puzzles: [Puzzle]) -> Double? {
        let recent = reviewAttempts(puzzles).suffix(recentWindow)
        guard !recent.isEmpty else { return nil }
        return Double(recent.filter(\.correct).count) / Double(recent.count)
    }

    static func adjustedLearningSpeed(current: Double, puzzles: [Puzzle]) -> Double {
        guard reviewAttempts(puzzles).count >= minReviewsForSpeed, let q = recentSuccessRate(puzzles) else { return current }
        var v = current
        if q > 0.85 { v += 0.1 } else if q < 0.70 { v -= 0.1 }
        return min(1.5, max(0.6, (v * 10).rounded() / 10))
    }

    static func stats(_ puzzles: [Puzzle], now: Date = Date()) -> PuzzleStats {
        var s = PuzzleStats()
        s.total = puzzles.count
        for p in puzzles {
            if p.mastered { s.mastered += 1; continue }
            s.byBox[min(5, max(1, p.box)) - 1] += 1
            if p.dueAt <= now { if p.isNew { s.new += 1 } else { s.due += 1 } }
        }
        s.attempts = puzzles.reduce(0) { $0 + $1.attempts.count }
        s.recentSuccessRate = recentSuccessRate(puzzles)
        return s
    }

    static func todaysSelection(_ puzzles: [Puzzle], focus: Skill?, now: Date = Date(), maxNew: Int = maxNewPerDay,
                                maxTotal: Int = maxTotalPerDay, priority: [String] = [], calendar: Calendar = .current) -> [Puzzle] {
        var g = SystemRandomNumberGenerator()
        return todaysSelection(puzzles, focus: focus, now: now, maxNew: maxNew, maxTotal: maxTotal, priority: priority,
                               calendar: calendar, using: &g)
    }

    static func todaysSelection<G: RandomNumberGenerator>(_ puzzles: [Puzzle], focus: Skill?, now: Date, maxNew: Int, maxTotal: Int,
                                                          priority: [String], calendar: Calendar, using rng: inout G) -> [Puzzle] {
        let first = priority.compactMap { id in puzzles.first { $0.id == id && !$0.mastered } }
        let taken = Set(first.map(\.id))
        let live = puzzles.filter { !$0.mastered && $0.dueAt <= now && !taken.contains($0.id) }
        let reviews = live.filter { !$0.isNew }.sorted { $0.dueAt < $1.dueAt }
        let startedToday = puzzles.filter { p in p.attempts.first.map { calendar.isDate($0.date, inSameDayAs: now) } ?? false }.count
        let room = max(0, maxNew - startedToday)
        let fresh = live.filter(\.isNew).sorted { a, b in
            let fa = a.skill == focus, fb = b.skill == focus
            return fa != fb ? fa : a.createdAt < b.createdAt
        }.prefix(room)
        let capped = Array((reviews + fresh).prefix(max(0, maxTotal - first.count)))
        return first + interleave(capped, focus: focus, using: &rng)
    }

    static func todaysSelection(for profile: PlayerProfile, puzzles: [Puzzle], priority: [String] = [], now: Date = Date(),
                                calendar: Calendar = .current) -> [Puzzle] {
        let a = Curriculum.adaptation(level: profile.level, focus: profile.focusSkill, skills: profile.skills,
                                      successRate: recentSuccessRate(puzzles))
        return todaysSelection(puzzles, focus: profile.focusSkill, now: now, maxNew: a.newPuzzlesPerDay, priority: priority,
                               calendar: calendar)
    }

    static func interleave<G: RandomNumberGenerator>(_ items: [Puzzle], focus: Skill?, using rng: inout G) -> [Puzzle] {
        var pools = [items.filter { focus != nil && $0.skill == focus }, items.filter { focus == nil || $0.skill != focus }]
        var out: [Puzzle] = []
        func blocked(_ p: Puzzle) -> Bool {
            let run = out.suffix(maxThemeRun)
            return run.count == maxThemeRun && run.allSatisfy { $0.theme == p.theme }
        }
        while !pools[0].isEmpty || !pools[1].isEmpty {
            let preferFocus = pools[1].isEmpty || (!pools[0].isEmpty && Double.random(in: 0..<1, using: &rng) < focusBias)
            let candidates = (preferFocus ? [0, 1] : [1, 0]).flatMap { k in pools[k].indices.map { (pool: k, index: $0) } }
            let pick = candidates.first { !blocked(pools[$0.pool][$0.index]) } ?? candidates[0]
            out.append(pools[pick.pool].remove(at: pick.index))
        }
        return out
    }
}
