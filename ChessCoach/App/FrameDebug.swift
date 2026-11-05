import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

enum FrameDebug {
    static let maxFrames = 60
    private static var counter = 0
    private static let queue = DispatchQueue(label: "chesscoach.frames")
    private static let directory = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/ChessCoach/frames", isDirectory: true)
    private static let formatter: DateFormatter = {
        let f = DateFormatter(); f.dateFormat = "yyyyMMdd-HHmmss-SSS"; return f
    }()

    static var enabled: Bool { UserDefaults.standard.bool(forKey: "debugFrames") }

    static func save(_ image: CGImage, reason: String) {
        guard enabled else { return }
        counter += 1
        let name = formatter.string(from: Date()) + String(format: "-%03d", counter % 1000)
        queue.async {
            let fm = FileManager.default
            try? fm.createDirectory(at: directory, withIntermediateDirectories: true)
            let png = directory.appendingPathComponent(name + ".png")
            if let dest = CGImageDestinationCreateWithURL(png as CFURL, UTType.png.identifier as CFString, 1, nil) {
                CGImageDestinationAddImage(dest, image, nil)
                CGImageDestinationFinalize(dest)
            }
            try? Data((reason + "\n").utf8).write(to: directory.appendingPathComponent(name + ".txt"))
            let names = ((try? fm.contentsOfDirectory(atPath: directory.path)) ?? [])
            for old in FrameRetention.stale(names, keepFrames: maxFrames) { try? fm.removeItem(at: directory.appendingPathComponent(old)) }
        }
    }
}
