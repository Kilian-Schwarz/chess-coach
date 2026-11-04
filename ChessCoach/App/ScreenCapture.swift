import AppKit
import ScreenCaptureKit

struct CapturedDisplay {
    let displayID: CGDirectDisplayID
    let frame: CGRect
    let image: CGImage
    var origin: CGPoint = .zero
}

enum ScreenCapture {
    static var hasPermission: Bool { CGPreflightScreenCaptureAccess() }

    static func requestPermission() { _ = CGRequestScreenCaptureAccess() }

    static func openPrivacySettings() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture") {
            NSWorkspace.shared.open(url)
        }
    }

    private static var filters: [CGDirectDisplayID: (filter: SCContentFilter, width: Int, height: Int)] = [:]

    static func captureRegion(displayID: CGDirectDisplayID, frame: CGRect, rect: CGRect) async throws -> CapturedDisplay? {
        guard let cached = filters[displayID] else { return nil }
        let bounds = CGRect(x: 0, y: 0, width: cached.width, height: cached.height)
        let r = rect.integral.intersection(bounds)
        guard r.width >= 120, r.height >= 120 else { return nil }
        let config = SCStreamConfiguration()
        config.sourceRect = r
        config.width = Int(r.width)
        config.height = Int(r.height)
        config.showsCursor = false
        config.captureResolution = .nominal
        let image = try await SCScreenshotManager.captureImage(contentFilter: cached.filter, configuration: config)
        return CapturedDisplay(displayID: displayID, frame: frame, image: image, origin: r.origin)
    }

    static func captureDisplays(preferred: CGDirectDisplayID?) async throws -> [CapturedDisplay] {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
        let me = content.applications.filter { $0.bundleIdentifier == Bundle.main.bundleIdentifier }
        var displays = content.displays
        if let preferred, let i = displays.firstIndex(where: { $0.displayID == preferred }) {
            displays.insert(displays.remove(at: i), at: 0)
        }
        var result: [CapturedDisplay] = []
        for d in displays {
            let filter = SCContentFilter(display: d, excludingApplications: me, exceptingWindows: [])
            filters[d.displayID] = (filter, d.width, d.height)
            let config = SCStreamConfiguration()
            config.width = d.width
            config.height = d.height
            config.showsCursor = false
            config.captureResolution = .nominal
            let image = try await SCScreenshotManager.captureImage(contentFilter: filter, configuration: config)
            result.append(CapturedDisplay(displayID: d.displayID, frame: d.frame, image: image))
        }
        return result
    }
}
