import AppKit
import CryptoKit
import Security

struct UpdateFeed: Decodable {
    struct Asset: Decodable { let file: String; let sha256: String; let signature: String }
    let version: String
    let notes: String?
    let macos: Asset?
}

enum UpdateCheck {
    static let publicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKtDuOT/afBlIb3eaZ6ZP9bh7+RuAQY1rDfINQoO/ci8ZZ+/aq0Ne50DxBTTHC9X1fmXeXHIAaUVxZwyW9b49zw=="
    static let team = "29GTPRH5R3"
    static let bundleID = "de.webschwarz.chesscoach"
    static let feedURL = URL(string: "https://github.com/Kilian-Schwarz/chess-coach/releases/latest/download/update.json")!
    static let releasesURL = URL(string: "https://github.com/Kilian-Schwarz/chess-coach/releases/latest")!

    static func downloadURL(version: String, file: String) -> URL? {
        URL(string: "https://github.com/Kilian-Schwarz/chess-coach/releases/download/v\(version)/\(file)")
    }

    static func isValid(_ feed: UpdateFeed) -> Bool {
        feed.version.range(of: #"^\d+\.\d+\.\d+$"#, options: .regularExpression) != nil
            && (feed.macos?.file.range(of: #"^ChessCoach-[0-9.]+-macos\.zip$"#, options: .regularExpression) != nil || feed.macos == nil)
    }

    static func isNewer(_ a: String, than b: String) -> Bool {
        func parts(_ v: String) -> [Int] { (v.split(separator: "-").first ?? "").split(separator: ".").map { Int($0) ?? 0 } }
        let x = parts(a), y = parts(b)
        for i in 0..<max(x.count, y.count) {
            let l = i < x.count ? x[i] : 0, r = i < y.count ? y[i] : 0
            if l != r { return l > r }
        }
        return false
    }

    static func acceptable(shortVersion: String?, bundleID id: String?, feedVersion: String, current: String) -> Bool {
        shortVersion == feedVersion && id == bundleID && isNewer(feedVersion, than: current)
    }

    static func verify(_ data: Data, sha256: String, signature: String, publicKey: String = publicKey) -> Bool {
        guard SHA256.hash(data: data).map({ String(format: "%02x", $0) }).joined() == sha256.lowercased(),
              let keyData = Data(base64Encoded: publicKey), let sigData = Data(base64Encoded: signature),
              let key = try? P256.Signing.PublicKey(derRepresentation: keyData),
              let sig = try? P256.Signing.ECDSASignature(derRepresentation: sigData) else { return false }
        return key.isValidSignature(sig, for: data)
    }

    static func isTeamSigned(_ url: URL) -> Bool {
        var code: SecStaticCode?
        var requirement: SecRequirement?
        let text = "anchor apple generic and certificate leaf[subject.OU] = \"\(team)\" and identifier \"\(bundleID)\""
        let flags = SecCSFlags(rawValue: kSecCSCheckAllArchitectures | kSecCSStrictValidate | kSecCSCheckNestedCode)
        guard SecStaticCodeCreateWithPath(url as CFURL, [], &code) == errSecSuccess, let code,
              SecRequirementCreateWithString(text as CFString, [], &requirement) == errSecSuccess, let requirement else { return false }
        return SecStaticCodeCheckValidity(code, flags, requirement) == errSecSuccess
    }

    static func stage(zip file: URL, asset: UpdateFeed.Asset, version: String, current: String, beside parent: URL) async throws -> URL? {
        guard verify(try Data(contentsOf: file), sha256: asset.sha256, signature: asset.signature) else {
            Log.write("Update \(version) rejected: signature mismatch")
            return nil
        }
        let dir = parent.appendingPathComponent(".ChessCoachUpdate-\(version)")
        try? FileManager.default.removeItem(at: dir)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        try await unzip(file, to: dir)
        guard let app = try FileManager.default.contentsOfDirectory(at: dir, includingPropertiesForKeys: nil).first(where: { $0.pathExtension == "app" }),
              let info = Bundle(url: app)?.infoDictionary,
              acceptable(shortVersion: info["CFBundleShortVersionString"] as? String, bundleID: info["CFBundleIdentifier"] as? String,
                         feedVersion: version, current: current),
              isTeamSigned(app) else {
            Log.write("Update \(version) rejected: version, identifier or code signature")
            try? FileManager.default.removeItem(at: dir)
            return nil
        }
        return app
    }

    private static func unzip(_ zip: URL, to dir: URL) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            let p = Process()
            p.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
            p.arguments = ["-x", "-k", zip.path, dir.path]
            p.terminationHandler = { continuation.resume(with: $0.terminationStatus == 0 ? .success(()) : .failure(URLError(.cannotDecodeContentData))) }
            do { try p.run() } catch { continuation.resume(throwing: error) }
        }
    }
}

@MainActor
final class Updater: ObservableObject {
    @Published private(set) var canCheck = true
    private var stagedVersion: String?
    private var loggedMissingFeed = false
    private var current: String { Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0" }

    var automaticallyUpdates: Bool {
        get { UserDefaults.standard.object(forKey: "autoUpdate") as? Bool ?? true }
        set { objectWillChange.send(); UserDefaults.standard.set(newValue, forKey: "autoUpdate") }
    }

    init() {
        Task { [weak self] in
            try? await Task.sleep(for: .seconds(20))
            while !Task.isCancelled {
                await self?.check(manual: false)
                try? await Task.sleep(for: .seconds(86400))
            }
        }
    }

    func checkNow() { Task { await check(manual: true) } }

    private func fetchFeed() async throws -> UpdateFeed? {
        let (data, response) = try await URLSession.shared.data(from: UpdateCheck.feedURL)
        let code = (response as? HTTPURLResponse)?.statusCode ?? 0
        if code == 404 {
            if !loggedMissingFeed { loggedMissingFeed = true; Log.write("No update feed published yet") }
            return nil
        }
        guard code == 200 else { throw URLError(.badServerResponse) }
        guard let feed = try? JSONDecoder().decode(UpdateFeed.self, from: data), UpdateCheck.isValid(feed) else { throw URLError(.cannotParseResponse) }
        return feed
    }

    private func check(manual: Bool) async {
        guard canCheck else { return }
        canCheck = false
        defer { canCheck = true }
        do {
            guard let feed = try await fetchFeed(), UpdateCheck.isNewer(feed.version, than: current) else {
                if manual { _ = ask(String(localized: "Chess Coach is up to date"), String(localized: "Version \(current) is the latest version."), nil) }
                return
            }
            if stagedVersion == feed.version {
                if manual { _ = ask(String(localized: "Update installed"), String(localized: "Version \(feed.version) is ready. Restart to use it."), nil) }
                return
            }
            let parent = Bundle.main.bundleURL.deletingLastPathComponent()
            guard let asset = feed.macos, manual || automaticallyUpdates, UpdateCheck.isTeamSigned(Bundle.main.bundleURL),
                  FileManager.default.isWritableFile(atPath: parent.path) else {
                offerDownload(feed.version, manual: manual)
                return
            }
            try await install(asset, version: feed.version, parent: parent, manual: manual)
        } catch {
            Log.write("Update check failed: \(error.localizedDescription)")
            if manual { _ = ask(String(localized: "The update check failed"), error.localizedDescription, nil) }
        }
    }

    private func install(_ asset: UpdateFeed.Asset, version: String, parent: URL, manual: Bool) async throws {
        guard let url = UpdateCheck.downloadURL(version: version, file: asset.file) else { throw URLError(.badURL) }
        let (file, _) = try await URLSession.shared.download(from: url)
        guard let app = try await UpdateCheck.stage(zip: file, asset: asset, version: version, current: current, beside: parent) else {
            if manual { _ = ask(String(localized: "The update check failed"), String(localized: "The downloaded update could not be verified."), nil) }
            return
        }
        stagedVersion = version
        Log.write("Update \(version) staged")
        let restart = manual && ask(String(localized: "Update installed"), String(localized: "Version \(version) is ready. Restart to use it."), String(localized: "Restart now"))
        swap(in: app, relaunch: restart)
    }

    private func swap(in app: URL, relaunch: Bool) {
        let script = "while kill -0 \(ProcessInfo.processInfo.processIdentifier) 2>/dev/null; do sleep 0.3; done; [ -d \"$2\" ] || exit 1; "
            + "rm -rf \"$1.old\"; mv \"$1\" \"$1.old\" || exit 1; "
            + "if mv \"$2\" \"$1\"; then rm -rf \"$1.old\" \"$3\"; [ \"$4\" = 1 ] && /usr/bin/open \"$1\"; else mv \"$1.old\" \"$1\"; fi"
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", script, "sh", Bundle.main.bundleURL.path, app.path, app.deletingLastPathComponent().path, relaunch ? "1" : "0"]
        try? p.run()
        if relaunch { NSApp.terminate(nil) }
    }

    private func offerDownload(_ version: String, manual: Bool) {
        let defaults = UserDefaults.standard
        guard manual || defaults.string(forKey: "offeredUpdate") != version else { return }
        defaults.set(version, forKey: "offeredUpdate")
        if ask(String(localized: "Update available"), String(localized: "Version \(version) is available."), String(localized: "Open Releases Page")) {
            NSWorkspace.shared.open(UpdateCheck.releasesURL)
        }
    }

    private func ask(_ title: String, _ text: String, _ accept: String?) -> Bool {
        NSApp.activate(ignoringOtherApps: true)
        let a = NSAlert()
        a.messageText = title
        a.informativeText = text
        if let accept {
            a.addButton(withTitle: accept)
            a.addButton(withTitle: String(localized: "Later"))
        }
        return a.runModal() == .alertFirstButtonReturn
    }
}
