import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

enum FrameDebug {
    static let maxFiles = 60
    private static let queue = DispatchQueue(label: "chesscoach.frames")
    private static let directory = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/ChessCoach/frames", isDirectory: true)
    private static let formatter: DateFormatter = {
        let f = DateFormatter(); f.dateFormat = "yyyyMMdd-HHmmss-SSS"; return f
    }()

    static var enabled: Bool { UserDefaults.standard.bool(forKey: "debugFrames") }

    static func save(_ image: CGImage, reason: String) {
        guard enabled else { return }
        let name = formatter.string(from: Date())
        queue.async {
            let fm = FileManager.default
            try? fm.createDirectory(at: directory, withIntermediateDirectories: true)
            let png = directory.appendingPathComponent(name + ".png")
            if let dest = CGImageDestinationCreateWithURL(png as CFURL, UTType.png.identifier as CFString, 1, nil) {
                CGImageDestinationAddImage(dest, image, nil)
                CGImageDestinationFinalize(dest)
            }
            try? Data((reason + "\n").utf8).write(to: directory.appendingPathComponent(name + ".txt"))
            let files = ((try? fm.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? []).sorted { $0.lastPathComponent < $1.lastPathComponent }
            for old in files.dropLast(maxFiles) { try? fm.removeItem(at: old) }
        }
    }
}
