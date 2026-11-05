import AppKit

@MainActor
final class SystemActivity {
    private(set) var locked = false
    private(set) var screensaver = false
    private(set) var displaysAsleep = false
    private(set) var systemAsleep = false
    var onChange: (() -> Void)?
    var onActivate: (() -> Void)?

    var isPaused: Bool { locked || screensaver || displaysAsleep || systemAsleep }
    var reason: String {
        if locked { return String(localized: "screen locked") }
        if screensaver { return String(localized: "screen saver") }
        return String(localized: "display asleep")
    }

    private var tokens: [(NotificationCenter, NSObjectProtocol)] = []

    func start() {
        guard tokens.isEmpty else { return }
        let session = CGSessionCopyCurrentDictionary() as? [String: Any]
        locked = (session?["CGSSessionScreenIsLocked"] as? Bool) ?? false
        let dc = DistributedNotificationCenter.default()
        let ws = NSWorkspace.shared.notificationCenter
        observe(dc, "com.apple.screenIsLocked") { $0.locked = true }
        observe(dc, "com.apple.screenIsUnlocked") { $0.locked = false }
        observe(dc, "com.apple.screensaver.didstart") { $0.screensaver = true }
        observe(dc, "com.apple.screensaver.didstop") { $0.screensaver = false }
        observe(ws, NSWorkspace.screensDidSleepNotification.rawValue) { $0.displaysAsleep = true }
        observe(ws, NSWorkspace.screensDidWakeNotification.rawValue) { $0.displaysAsleep = false }
        observe(ws, NSWorkspace.willSleepNotification.rawValue) { $0.systemAsleep = true }
        observe(ws, NSWorkspace.didWakeNotification.rawValue) { $0.systemAsleep = false }
        for name in [NSWorkspace.didActivateApplicationNotification, NSWorkspace.activeSpaceDidChangeNotification] {
            let token = ws.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.onActivate?() }
            }
            tokens.append((ws, token))
        }
    }

    func stop() {
        for (center, token) in tokens { center.removeObserver(token) }
        tokens.removeAll()
        locked = false; screensaver = false; displaysAsleep = false; systemAsleep = false
    }

    private func observe(_ center: NotificationCenter, _ name: String, _ change: @escaping (SystemActivity) -> Void) {
        let token = center.addObserver(forName: Notification.Name(name), object: nil, queue: .main) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                change(self)
                self.onChange?()
            }
        }
        tokens.append((center, token))
    }
}
