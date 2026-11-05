import Foundation

struct RectStabilizer {
    enum Outcome { case same, changed, pending }

    static let confirmFrames = 2

    private(set) var accepted: BoardRect?
    private var candidate: BoardRect?
    private var candidateFrames = 0

    static func tolerance(for size: Double) -> Double { max(3, size * 0.015) }

    static func near(_ a: BoardRect, _ b: BoardRect) -> Bool {
        let tol = tolerance(for: a.size)
        return abs(a.x - b.x) <= tol && abs(a.y - b.y) <= tol && abs(a.size - b.size) <= tol
    }

    mutating func accept(_ rect: BoardRect) -> (rect: BoardRect, changed: Bool) {
        let snapped = BoardRect(x: rect.x.rounded(), y: rect.y.rounded(), size: rect.size.rounded())
        if let a = accepted, RectStabilizer.near(a, snapped) { return (a, false) }
        accepted = snapped
        candidate = nil
        candidateFrames = 0
        return (snapped, true)
    }

    mutating func propose(_ rect: BoardRect, matchesColors: Bool) -> (rect: BoardRect, outcome: Outcome) {
        let snapped = BoardRect(x: rect.x.rounded(), y: rect.y.rounded(), size: rect.size.rounded())
        guard let a = accepted else { return (accept(snapped).rect, .changed) }
        if RectStabilizer.near(a, snapped) {
            candidate = nil
            candidateFrames = 0
            return (a, .same)
        }
        if let c = candidate, RectStabilizer.near(c, snapped) { candidateFrames += 1 } else { candidate = snapped; candidateFrames = 1 }
        guard candidateFrames >= RectStabilizer.confirmFrames, matchesColors else { return (a, .pending) }
        return (accept(snapped).rect, .changed)
    }

    mutating func reset() {
        accepted = nil
        candidate = nil
        candidateFrames = 0
    }
}

struct RelearnGuard {
    static let badFrames = 3

    private var frames = 0
    private var rect: BoardRect?

    var count: Int { frames }

    mutating func reset() { frames = 0; rect = nil }

    mutating func observe(bad: Bool, rect current: BoardRect, required: Int = RelearnGuard.badFrames) -> Bool {
        guard bad else { reset(); return false }
        if let r = rect, r == current { frames += 1 } else { rect = current; frames = 1 }
        return frames >= required
    }
}
