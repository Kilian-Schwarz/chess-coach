import Foundation

var failures = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    if !ok { failures += 1; print("FAIL: \(msg())") }
}

func d(paused: Bool = false, soon: Bool = false, tracked: Bool = true, lost: Double = 0, calm: Double = 0, low: Bool = false) -> Int {
    PollingSchedule.delayMs(paused: paused, confirmSoon: soon, tracked: tracked, lostFor: lost, calmFor: calm, lowPower: low)
}

check(d(paused: true) == 3000, "paused")
check(d(soon: true) == 200, "confirm soon")
check(d(calm: 1) == 400, "active board")
check(d(calm: 9) > 400 && d(calm: 9) < 1500, "ramp")
check(d(calm: 60) == 1500, "calm capped")
check(d(calm: 120) == 4000 && d(calm: 3600) == 4000, "idle board after two minutes")
check(d(calm: 3600, low: true) == 8000, "low power doubles")
check(d(tracked: false, lost: 5) == 1000 && d(tracked: false, lost: 60) == 2000 && d(tracked: false, lost: 200) == 4000 && d(tracked: false, lost: 400) == 8000, "search backoff")
var last = 0
for c in stride(from: 0.0, to: 200, by: 1) { let v = d(calm: c); if c < 119 { check(v >= last, "monotonic at \(c)") }; last = v }
print(failures == 0 ? "polling OK" : "\(failures) failures")
exit(failures == 0 ? 0 : 1)
