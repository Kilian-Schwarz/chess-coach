import AppKit

@MainActor
enum MoveToApplications {
    private static let declinedKey = "declinedMoveToApplications"

    static func offerIfNeeded() {
        let bundle = Bundle.main
        let path = bundle.bundleURL.path
        guard bundle.object(forInfoDictionaryKey: "CCOfficialBuild") as? String == "YES",
              !InstallLocation.isInstalled(path),
              !UserDefaults.standard.bool(forKey: declinedKey) else { return }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = String(localized: "Move Chess Coach to the Applications folder?")
        alert.informativeText = String(localized: "Chess Coach is running from outside the Applications folder. Moving it there keeps it in one place and lets it update itself.")
        alert.addButton(withTitle: String(localized: "Move to Applications"))
        alert.addButton(withTitle: String(localized: "Not now"))
        guard alert.runModal() == .alertFirstButtonReturn else {
            UserDefaults.standard.set(true, forKey: declinedKey)
            return
        }
        Task {
            do { try await move(from: bundle.bundleURL) } catch {
                Log.write("Move to Applications failed: \(error.localizedDescription)")
                let failure = NSAlert()
                failure.messageText = String(localized: "Chess Coach could not be moved")
                failure.informativeText = error.localizedDescription
                failure.runModal()
            }
        }
    }

    private static func move(from source: URL) async throws {
        let fileManager = FileManager.default
        let directory = URL(fileURLWithPath: InstallLocation.targetDirectory(isWritable: fileManager.isWritableFile(atPath:)))
        try fileManager.createDirectory(at: directory, withIntermediateDirectories: true)
        let destination = directory.appendingPathComponent(source.lastPathComponent)
        let staging = directory.appendingPathComponent(".\(source.lastPathComponent).new")
        try? fileManager.removeItem(at: staging)
        try await run("/usr/bin/ditto", ["--noqtn", source.path, staging.path])
        let bundleID = Bundle.main.bundleIdentifier ?? ""
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).filter { $0 != .current }
        others.forEach { $0.terminate() }
        for _ in 0..<30 where others.contains(where: { !$0.isTerminated }) { try await Task.sleep(for: .milliseconds(100)) }
        if fileManager.fileExists(atPath: destination.path) { try fileManager.trashItem(at: destination, resultingItemURL: nil) }
        try fileManager.moveItem(at: staging, to: destination)
        Log.write("Moved to \(destination.path)")
        relaunch(destination, discarding: source)
    }

    private static func relaunch(_ destination: URL, discarding source: URL) {
        var cleanup = ":"
        var volume = ""
        if InstallLocation.isOnDiskImage(source.path) {
            volume = "/" + source.pathComponents.dropFirst().prefix(2).joined(separator: "/")
            cleanup = "/usr/bin/hdiutil detach -quiet \"$1\" >/dev/null 2>&1"
        } else if !InstallLocation.isTranslocated(source.path) {
            try? FileManager.default.trashItem(at: source, resultingItemURL: nil)
        }
        let script = "while kill -0 \(ProcessInfo.processInfo.processIdentifier) 2>/dev/null; do sleep 0.2; done; /usr/bin/open \"$0\"; \(cleanup)"
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        process.arguments = ["-c", script, destination.path, volume]
        try? process.run()
        NSApp.terminate(nil)
    }

    private static func run(_ tool: String, _ arguments: [String]) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            let process = Process()
            process.executableURL = URL(fileURLWithPath: tool)
            process.arguments = arguments
            process.terminationHandler = { continuation.resume(with: $0.terminationStatus == 0 ? .success(()) : .failure(CocoaError(.fileWriteUnknown))) }
            do { try process.run() } catch { continuation.resume(throwing: error) }
        }
    }
}
