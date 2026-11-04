import AppKit
import Combine
import Foundation

extension UserDefaults {
    func flag(_ key: String, _ fallback: Bool) -> Bool { object(forKey: key) as? Bool ?? fallback }
}

@MainActor
extension AppModel {
    func resetPanelPosition() { panel.resetPosition() }

    func lockExpertMode() { expertUnlocked = false }

    var tokenOverPlainHTTP: Bool { !llmToken.isEmpty && llmURL.trimmingCharacters(in: .whitespaces).lowercased().hasPrefix("http://") }

    func persist<T>(_ publisher: Published<T>.Publisher, _ key: String, _ encode: @escaping (T) -> Any = { $0 }) {
        publisher.dropFirst().sink { [defaults] in defaults.set(encode($0), forKey: key) }.store(in: &cancellables)
    }

    func bindPreferences() {
        persist($side, "side") { $0.rawValue }
        persist($thinkMs, "thinkMs")
        persist($alwaysMyTurn, "alwaysMyTurn")
        persist($duoTraining, "duoTraining")
        persist($explainSuggestions, "explainSuggestions")
        persist($showThreats, "showThreats")
        persist($reviewMoves, "reviewMoves")
        persist($expertUnlocked, "expertUnlocked")
        persist($showBestImmediately, "showBestImmediately")
        persist($llmURL, "llmURL")
        persist($llmBase, "llmBase")
        persist($llmModel, "llmModel")
    }

    func relaunch() {
        defaults.set(isActive || needsPermission, forKey: "resumeActive")
        let pid = ProcessInfo.processInfo.processIdentifier
        let path = Bundle.main.bundlePath
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "while kill -0 \(pid) 2>/dev/null; do sleep 0.2; done; /usr/bin/open \"$0\"", path]
        try? p.run()
        NSApplication.shared.terminate(nil)
    }

    func relearnPieces() {
        templates = PieceTemplates()
        Self.saveTemplates(templates)
        resetTracking()
    }

    func loadModels() async {
        TokenStore.save(llmToken)
        llmLoading = true
        llmStatus = String(localized: "Connecting …")
        defer { llmLoading = false }
        do {
            let (base, models) = try await LLMClient.discover(url: llmURL, token: llmToken)
            llmBase = base
            llmModels = models
            if !models.contains(where: { $0.id == llmModel }) { llmModel = models.first?.id ?? "" }
            llmStatus = String(localized: "\(models.count) models found (\(base))")
        } catch {
            llmStatus = error.localizedDescription
        }
    }

    static var templatesURL: URL {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("ChessCoach", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("pieces.json")
    }

    static func loadTemplates() -> PieceTemplates? {
        guard let data = try? Data(contentsOf: templatesURL) else { return nil }
        return try? JSONDecoder().decode(PieceTemplates.self, from: data)
    }

    static func saveTemplates(_ t: PieceTemplates) {
        if let data = try? JSONEncoder().encode(t) { try? data.write(to: templatesURL) }
    }
}
