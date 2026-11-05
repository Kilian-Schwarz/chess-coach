import Foundation

enum FrameRetention {
    static func stale(_ names: [String], keepFrames: Int) -> [String] {
        let stems = Set(names.map { ($0 as NSString).deletingPathExtension }).sorted()
        let keep = Set(stems.suffix(keepFrames))
        return names.filter { !keep.contains(($0 as NSString).deletingPathExtension) }
    }
}
