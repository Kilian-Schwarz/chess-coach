import Foundation

struct RectStabilizer {
    private(set) var accepted: BoardRect?

    static func tolerance(for size: Double) -> Double { max(3, size * 0.015) }

    mutating func accept(_ rect: BoardRect) -> (rect: BoardRect, changed: Bool) {
        let snapped = BoardRect(x: rect.x.rounded(), y: rect.y.rounded(), size: rect.size.rounded())
        if let a = accepted {
            let tol = RectStabilizer.tolerance(for: a.size)
            if abs(a.x - snapped.x) <= tol, abs(a.y - snapped.y) <= tol, abs(a.size - snapped.size) <= tol { return (a, false) }
        }
        accepted = snapped
        return (snapped, true)
    }

    mutating func reset() { accepted = nil }
}
