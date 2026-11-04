import Foundation

struct PuzzleAttempt: Codable, Equatable {
    var date: Date
    var correct: Bool
    var hintLevel: Int
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
    mutating func attempt(move uci: String, hintLevel: Int = 0, now: Date = Date(), learningSpeed: Double = 1.0) -> Bool {
        let correct = accepts(uci)
        attempts.append(PuzzleAttempt(date: now, correct: correct, hintLevel: hintLevel))
        let speed = min(1.5, max(0.6, learningSpeed))
        if !correct {
            box = 1
        } else if hintLevel == 0 {
            if box >= 5 { mastered = true } else { box += 1 }
        }
        let days = Puzzle.intervalsDays[min(5, max(1, box)) - 1] * speed
        dueAt = now.addingTimeInterval(days * 86_400)
        return correct
    }
}

struct PuzzleSet: Codable, Equatable {
    var schemaVersion: Int = 1
    var puzzles: [Puzzle] = []

    @discardableResult
    mutating func add(_ p: Puzzle) -> Bool {
        guard !puzzles.contains(where: { $0.fen == p.fen }) else { return false }
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
    static let recentWindow = 30

    static func recentSuccessRate(_ puzzles: [Puzzle]) -> Double? {
        let all = puzzles.flatMap(\.attempts).sorted { $0.date < $1.date }.suffix(recentWindow)
        guard !all.isEmpty else { return nil }
        return Double(all.filter(\.correct).count) / Double(all.count)
    }

    static func adjustedLearningSpeed(current: Double, puzzles: [Puzzle]) -> Double {
        let n = puzzles.flatMap(\.attempts).count
        guard n >= 10, let q = recentSuccessRate(puzzles) else { return current }
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
                                calendar: Calendar = .current) -> [Puzzle] {
        func focusFirst(_ a: Puzzle, _ b: Puzzle) -> Bool {
            let fa = a.skill == focus, fb = b.skill == focus
            if fa != fb { return fa }
            return a.dueAt < b.dueAt
        }
        let live = puzzles.filter { !$0.mastered && $0.dueAt <= now }
        let due = live.filter { !$0.isNew }.sorted(by: focusFirst)
        let startedToday = puzzles.filter { p in p.attempts.first.map { calendar.isDate($0.date, inSameDayAs: now) } ?? false }.count
        let room = max(0, maxNew - startedToday)
        let fresh = Array(live.filter(\.isNew).sorted(by: focusFirst).prefix(room))
        return due + fresh
    }
}
