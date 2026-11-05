import Foundation

struct OrientationTracker {
    static let evidenceFrames = 5

    private(set) var whiteAtBottom: Bool?
    private var contrary = 0

    var hasEvidence: Bool { contrary > 0 }

    mutating func set(_ value: Bool?) {
        whiteAtBottom = value
        contrary = 0
    }

    static func impossible(_ screen: [[Character?]], whiteAtBottom: Bool) -> Bool {
        let placement = Placement(screen: screen, whiteAtBottom: whiteAtBottom)
        return placement.problem(sideToMove: .white) != nil && placement.problem(sideToMove: .black) != nil
    }

    mutating func resolve(guess: Bool?, screen: [[Character?]], isStart: Bool) -> (whiteAtBottom: Bool, reason: String?) {
        guard let current = whiteAtBottom else {
            whiteAtBottom = guess
            return (guess ?? true, nil)
        }
        if isStart, let guess, guess != current {
            set(guess)
            return (guess, "start position seen with the other orientation")
        }
        if Self.impossible(screen, whiteAtBottom: current), !Self.impossible(screen, whiteAtBottom: !current) {
            contrary += 1
            if contrary >= Self.evidenceFrames {
                set(!current)
                return (!current, "position impossible in the current orientation for \(Self.evidenceFrames) frames")
            }
        } else {
            contrary = 0
        }
        return (current, nil)
    }
}
