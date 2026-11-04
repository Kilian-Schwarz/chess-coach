import Foundation
import SwiftUI

@MainActor
final class LearningStore: ObservableObject {
    let storage = Storage()
    let openings: Openings?
    private let defaults = UserDefaults.standard

    @Published private(set) var profiles: [PlayerProfile]
    @Published var activeId: String { didSet { defaults.set(activeId, forKey: "activeProfile") } }
    @Published var partnerId: String? { didSet { defaults.set(partnerId, forKey: "partnerProfile") } }
    @Published private(set) var games: [GameReport]
    private var puzzleSets: [String: PuzzleSet] = [:]

    init() {
        openings = Openings(bundle: .main)
        if openings == nil { Log.write("openings.tsv missing from the app bundle") }
        var list = storage.loadProfiles()
        if list.isEmpty {
            let first = PlayerProfile(name: PlayerProfile.defaultName, startStyle: ExplanationStyle())
            try? storage.save(profile: first)
            list = [first]
        }
        profiles = list
        let saved = defaults.string(forKey: "activeProfile")
        activeId = list.contains { $0.id == saved } ? saved! : list[0].id
        let partner = defaults.string(forKey: "partnerProfile")
        partnerId = list.contains { $0.id == partner } ? partner : nil
        games = storage.loadGames(limit: 100)
    }

    func profile(id: String?) -> PlayerProfile? {
        guard let id else { return nil }
        return profiles.first { $0.id == id }
    }

    var active: PlayerProfile { profile(id: activeId) ?? profiles[0] }
    var partner: PlayerProfile? { profile(id: partnerId) }

    func update(_ id: String, _ change: (inout PlayerProfile) -> Void) {
        guard let i = profiles.firstIndex(where: { $0.id == id }) else { return }
        var p = profiles[i]
        change(&p)
        profiles[i] = p
        do { try storage.save(profile: p) } catch { Log.write("Saving profile failed: \(error.localizedDescription)") }
    }

    @discardableResult
    func addProfile(name: String) -> PlayerProfile {
        let clean = name.trimmingCharacters(in: .whitespacesAndNewlines)
        let p = PlayerProfile(name: clean.isEmpty ? String(localized: "Player \(profiles.count + 1)") : clean, startStyle: ExplanationStyle())
        profiles.append(p)
        try? storage.save(profile: p)
        return p
    }

    func rename(_ id: String, to name: String) {
        let clean = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { return }
        update(id) { $0.name = clean }
    }

    func resetStyle(_ id: String) {
        update(id) { $0.style = LearningStyle(start: ExplanationStyle()) }
    }

    func puzzles(for id: String) -> PuzzleSet {
        if let s = puzzleSets[id] { return s }
        let s = storage.loadPuzzles(profileId: id)
        puzzleSets[id] = s
        return s
    }

    func updatePuzzles(for id: String, _ change: (inout PuzzleSet) -> Void) {
        var s = puzzles(for: id)
        change(&s)
        puzzleSets[id] = s
        do { try storage.save(puzzles: s, profileId: id) } catch { Log.write("Saving puzzles failed: \(error.localizedDescription)") }
    }

    func save(game: GameReport, pgn: String?) {
        do { try storage.save(game: game, pgn: pgn) } catch { Log.write("Saving game failed: \(error.localizedDescription)") }
        games.removeAll { $0.id == game.id }
        games.insert(game, at: 0)
        games.sort { $0.date > $1.date }
    }

    func pgn(for game: GameReport) -> String? {
        let folder = storage.root.appendingPathComponent("games", isDirectory: true)
        let files = (try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? []
        guard let url = files.first(where: { $0.pathExtension == "pgn" && $0.lastPathComponent.hasSuffix("-\(game.id).pgn") }) else { return nil }
        return try? String(contentsOf: url, encoding: .utf8)
    }

    private var explained: [String: ExplanationStyle] {
        get {
            guard let d = defaults.data(forKey: "explainedStyles") else { return [:] }
            return (try? JSONDecoder().decode([String: ExplanationStyle].self, from: d)) ?? [:]
        }
        set { defaults.set(try? JSONEncoder().encode(newValue), forKey: "explainedStyles") }
    }

    func noteExplained(profileId: String, theme: String, style: ExplanationStyle) {
        var e = explained
        e["\(profileId)|\(theme)"] = style
        if e.count > 60 { e = Dictionary(uniqueKeysWithValues: Array(e).suffix(40)) }
        explained = e
    }

    func explainedStyle(profileId: String, theme: String) -> ExplanationStyle? {
        explained["\(profileId)|\(theme)"]
    }
}
