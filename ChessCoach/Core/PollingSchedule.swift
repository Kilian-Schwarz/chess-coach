import Foundation

enum PollingSchedule {
    static let pausedMs = 3000
    static let confirmMs = 200
    static let activeMs = 400
    static let activeUntil = 3.0
    static let rampSeconds = 12.0
    static let calmMs = 1500
    static let idleAfter = 120.0
    static let idleMs = 4000
    static let searchSteps: [(after: Double, ms: Int)] = [(300, 8000), (120, 4000), (30, 2000), (0, 1000)]
    static let lowPowerFactor = 2

    static func delayMs(paused: Bool, confirmSoon: Bool, tracked: Bool, lostFor: Double, calmFor: Double, lowPower: Bool) -> Int {
        let factor = lowPower ? lowPowerFactor : 1
        if paused { return pausedMs }
        if tracked {
            if confirmSoon { return confirmMs }
            if calmFor < activeUntil { return activeMs }
            if calmFor >= idleAfter { return idleMs * factor }
            return Int(min(Double(calmMs), Double(activeMs) + (calmFor - activeUntil) / rampSeconds * Double(calmMs - activeMs))) * factor
        }
        return (searchSteps.first { lostFor > $0.after }?.ms ?? 1000) * factor
    }
}
