import Foundation

enum TickGate {
    static func canSkip(unchanged: Bool, settled: Bool, pendingReview: Bool, forceResync: Bool, mouseHeld: Bool,
                        confirmSoon: Bool, recordDisagrees: Bool, orientationEvidence: Bool) -> Bool {
        unchanged && settled && !pendingReview && !forceResync && !mouseHeld && !confirmSoon && !recordDisagrees && !orientationEvidence
    }
}
