import Foundation

final class Storage {
    let root: URL
    private let fm = FileManager.default

    static var defaultRoot: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ChessCoach", isDirectory: true)
    }

    init(root: URL = Storage.defaultRoot) { self.root = root }

    static func makeEncoder() -> JSONEncoder {
        let e = JSONEncoder()
        e.dateEncodingStrategy = .iso8601
        e.outputFormatting = [.prettyPrinted, .sortedKeys]
        return e
    }

    static func makeDecoder() -> JSONDecoder {
        let d = JSONDecoder()
        d.dateDecodingStrategy = .iso8601
        return d
    }

    private func dir(_ name: String) throws -> URL {
        let u = root.appendingPathComponent(name, isDirectory: true)
        try fm.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }

    private func write<T: Encodable>(_ value: T, to url: URL) throws {
        try Storage.makeEncoder().encode(value).write(to: url, options: .atomic)
    }

    private func read<T: Decodable>(_ type: T.Type, from url: URL) throws -> T {
        try Storage.makeDecoder().decode(T.self, from: Data(contentsOf: url))
    }

    func save(profile: PlayerProfile) throws {
        try write(profile, to: dir("profiles").appendingPathComponent("\(profile.id).json"))
    }

    func loadProfile(id: String) throws -> PlayerProfile {
        try read(PlayerProfile.self, from: root.appendingPathComponent("profiles/\(id).json"))
    }

    func loadProfiles() -> [PlayerProfile] {
        let folder = root.appendingPathComponent("profiles", isDirectory: true)
        let files = (try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        return files.filter { $0.pathExtension == "json" }
            .compactMap { url -> PlayerProfile? in
                do { return try read(PlayerProfile.self, from: url) } catch {
                    Log.write("Profile file unreadable, moved aside: \(url.lastPathComponent)")
                    try? fm.moveItem(at: url, to: url.appendingPathExtension("corrupt"))
                    return nil
                }
            }
            .sorted { $0.createdAt < $1.createdAt }
    }

    func save(puzzles: PuzzleSet, profileId: String) throws {
        try write(puzzles, to: dir("puzzles").appendingPathComponent("\(profileId).json"))
    }

    func loadPuzzles(profileId: String) -> PuzzleSet {
        (try? read(PuzzleSet.self, from: root.appendingPathComponent("puzzles/\(profileId).json"))) ?? PuzzleSet()
    }

    private static func dayString(_ d: Date) -> String {
        var cal = Calendar(identifier: .gregorian)
        cal.timeZone = TimeZone(identifier: "UTC")!
        let c = cal.dateComponents([.year, .month, .day], from: d)
        return String(format: "%04d-%02d-%02d", c.year!, c.month!, c.day!)
    }

    @discardableResult
    func save(game: GameReport, pgn: String? = nil) throws -> URL {
        let base = "\(Storage.dayString(game.date))-\(game.id)"
        let folder = try dir("games")
        let url = folder.appendingPathComponent(base + ".json")
        try write(game, to: url)
        if let pgn { try pgn.write(to: folder.appendingPathComponent(base + ".pgn"), atomically: true, encoding: .utf8) }
        return url
    }

    func loadGames(limit: Int = 50) -> [GameReport] {
        let folder = root.appendingPathComponent("games", isDirectory: true)
        let files = (try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        return files.filter { $0.pathExtension == "json" }
            .compactMap { try? read(GameReport.self, from: $0) }
            .sorted { $0.date > $1.date }
            .prefix(limit).map { $0 }
    }
}
