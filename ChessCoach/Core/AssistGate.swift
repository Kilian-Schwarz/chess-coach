import Foundation

struct AssistGate: Equatable {
    private(set) var tainted = false

    var counts: Bool { !tainted }

    mutating func begin(assisted: Bool) { tainted = assisted }

    mutating func observe(assisted: Bool, started: Bool) {
        if assisted { tainted = true } else if !started { tainted = false }
    }

    @discardableResult
    func record(_ analysis: MoveAnalysis, hint: Int, into profile: inout PlayerProfile) -> Bool {
        guard counts else { return false }
        profile.apply(analysis, gainWeight: Curriculum.gainWeight(hintLevel: hint))
        profile.recordHintUse(level: hint)
        return true
    }

    func add(_ puzzle: Puzzle?, to set: inout PuzzleSet) {
        guard counts, let puzzle else { return }
        _ = set.add(puzzle)
    }

    @discardableResult
    func complete(_ profile: inout PlayerProfile, summary: SideSummary) -> Double? {
        guard counts else { return nil }
        return profile.completeGame(summary: summary)
    }
}
