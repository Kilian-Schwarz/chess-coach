import SwiftUI
import AppKit

@main
struct ChessCoachApp: App {
    @StateObject private var model = AppModel()
    @StateObject private var updater = Updater()

    init() {
        DispatchQueue.main.async { MoveToApplications.offerIfNeeded() }
    }

    var body: some Scene {
        MenuBarExtra {
            MenuContent(model: model, store: model.store, updater: updater)
        } label: {
            StatusLabel(model: model)
        }
        .menuBarExtraStyle(.menu)

        Window("Training", id: "training") { TrainingView(app: model) }
            .defaultSize(width: 1000, height: 700)
            .windowResizability(.contentMinSize)
            .defaultLaunchBehavior(.suppressed)
            .restorationBehavior(.disabled)

        Window("Welcome", id: "welcome") { WelcomeView(app: model) }
            .windowResizability(.contentSize)
            .defaultLaunchBehavior(.suppressed)
            .restorationBehavior(.disabled)

        Window("Evaluation", id: "report") { ReportView(app: model) }
            .defaultLaunchBehavior(.suppressed)
            .restorationBehavior(.disabled)

        Window("Player profile", id: "profile") { ProfileView(app: model) }
            .defaultLaunchBehavior(.suppressed)
            .restorationBehavior(.disabled)

        Settings {
            SettingsView(model: model, updater: updater)
        }
    }
}

struct StatusLabel: View {
    @ObservedObject var model: AppModel
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        Image(systemName: model.isActive ? "checkerboard.rectangle" : "rectangle.slash")
            .accessibilityLabel(model.isActive ? "Chess Coach active" : "Chess Coach off")
            .onChange(of: model.windowRequest) { _, request in
                guard let request else { return }
                if request.activate { NSApp.activate(ignoringOtherApps: true) }
                openWindow(id: request.id)
                DispatchQueue.main.async {
                    let window = NSApp.windows.first { $0.identifier?.rawValue.contains(request.id) == true }
                    if request.activate { window?.makeKeyAndOrderFront(nil) } else { window?.orderFrontRegardless() }
                }
            }
    }
}

struct ExpertControls: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Toggle("Show best move immediately", isOn: $model.showBestImmediately)
        Toggle("Always calculate for me", isOn: $model.alwaysMyTurn)
            .disabled(model.duoTraining)
        Picker("Thinking time", selection: $model.thinkMs) {
            Text("0.3 seconds").tag(300)
            Text("1 second").tag(1000)
            Text("3 seconds").tag(3000)
        }
    }
}

struct MenuContent: View {
    @ObservedObject var model: AppModel
    @ObservedObject var store: LearningStore
    @ObservedObject var updater: Updater
    @Environment(\.openSettings) private var openSettings

    var body: some View {
        if let pending = updater.pending {
            if pending.staged != nil {
                Button("Restart to update to version \(pending.version)") { updater.applyPending() }
            } else {
                Button("Version \(pending.version) available …") { updater.applyPending() }
            }
            Divider()
        }

        Toggle("Active", isOn: $model.isActive)
            .keyboardShortcut("s")

        Divider()

        Text(model.status)
        if let s = model.suggestion { Text(s) }
        if model.needsPermission {
            Text("Chess Coach needs screen recording to see the chessboard.")
            Text("Images are analysed on this Mac only.")
            Button("Allow Screen Recording …") { ScreenCapture.openPrivacySettings() }
            Button("Restart Chess Coach") { model.relaunch() }
        }

        Divider()

        Button("Next hint") { model.nextHint() }
            .keyboardShortcut("h", modifiers: [.control, .option, .command])
        Button("Switch side to move") { model.toggleSideToMove() }
            .keyboardShortcut("t", modifiers: [.control, .option, .command])
        Button("Finish game & show recap") { model.endGameNow() }
            .disabled(model.movesInGame < 2)

        Divider()

        Picker("Profile", selection: $store.activeId) {
            ForEach(store.profiles) { Text($0.name).tag($0.id) }
        }
        if model.duoTraining {
            Picker("Partner profile", selection: $store.partnerId) {
                Text("None").tag(String?.none)
                ForEach(store.profiles.filter { $0.id != store.activeId }) { Text($0.name).tag(Optional($0.id)) }
            }
        }
        Text(store.active.placementText() ?? "\(store.active.level.title()) · \(Int(store.active.rating.rounded()))")
        Text(model.focusLine)
        Button("Training …") { model.open("training") }
        Button("Last recap …") { model.showRecap() }
            .disabled(model.lastReport == nil && store.games.isEmpty)
        Button("Evaluation …") { model.open("report") }
            .disabled(model.lastReport == nil && store.games.isEmpty)
        Button("Player profile …") { model.open("profile") }

        Divider()

        Toggle("Training with a partner (both sides)", isOn: $model.duoTraining)
        Toggle("Explain suggestions", isOn: $model.explainSuggestions)
        Toggle("Rate moves", isOn: $model.reviewMoves)
        Toggle("Show threats", isOn: $model.showThreats)
        Toggle("Hints while dragging", isOn: $model.dragHints)

        Divider()

        Picker("I play", selection: $model.side) {
            ForEach(AppModel.Side.allCases) { Text($0.title).tag($0) }
        }
        if model.expertUnlocked { ExpertControls(model: model) }
        Button("Move panel back next to the board") { model.resetPanelPosition() }
        Button("Relearn pieces") { model.relearnPieces() }
            .disabled(!model.hasTemplates)

        Divider()

        Button("Check for Updates …") { updater.checkNow() }
            .disabled(!updater.canCheck)
        Button("Settings …") {
            NSApp.activate(ignoringOtherApps: true)
            openSettings()
        }
        .keyboardShortcut(",")
        Button("Quit Chess Coach") { NSApplication.shared.terminate(nil) }
            .keyboardShortcut("q")
    }
}

struct SettingsView: View {
    @ObservedObject var model: AppModel
    @ObservedObject var updater: Updater
    @State private var language = Language.stored

    var body: some View {
        Form {
            Section("General") {
                Picker("Language", selection: $language) {
                    Text("System").tag("")
                    ForEach(Language.codes, id: \.self) { Text(Language.nativeName($0)).tag($0) }
                }
                .onChange(of: language) { _, code in Language.store(code) }
                if language != Language.atLaunch {
                    HStack {
                        Text("The language changes after a restart.").font(.callout).foregroundStyle(.secondary)
                        Spacer()
                        Button("Restart now") { model.relaunch() }
                    }
                }
                Toggle("Install updates automatically", isOn: Binding(get: { updater.automaticallyUpdates }, set: { updater.automaticallyUpdates = $0 }))
            }

            Section {
                TextField("Server", text: $model.llmURL, prompt: Text("https://your-server"))
                    .onSubmit { Task { await model.loadModels() } }
                SecureField("Token", text: $model.llmToken, prompt: Text("API key"))
                    .onSubmit { Task { await model.loadModels() } }
                if model.tokenOverPlainHTTP {
                    Label("The server address starts with http://. Your token would be sent unencrypted. Use https:// if you can.", systemImage: "exclamationmark.triangle")
                        .font(.callout).foregroundStyle(.orange)
                }
                HStack {
                    Button("Load models") { Task { await model.loadModels() } }
                        .disabled(model.llmLoading)
                    if model.llmLoading { ProgressView().controlSize(.small) }
                }
                Picker("Model", selection: $model.llmModel) {
                    if model.llmModels.isEmpty { Text("Load models first").tag("") }
                    ForEach(model.llmModels) { Text($0.name).tag($0.id) }
                }
                .disabled(model.llmModels.isEmpty)
                if !model.llmStatus.isEmpty {
                    Text(model.llmStatus).font(.callout).foregroundStyle(.secondary)
                }
            } header: {
                Text("AI for explanations")
            } footer: {
                Text("Open WebUI: enter the site address and an API key (Settings, Account). vLLM or llama.cpp: enter the address with /v1 and leave the token empty. The token is kept in the keychain.")
                    .font(.caption).foregroundStyle(.secondary)
            }

            Section("Training") {
                Toggle("Training with a partner: both sides get suggestions and ratings", isOn: $model.duoTraining)
                Toggle("Explain suggestions", isOn: $model.explainSuggestions)
                Toggle("Rate and explain moves", isOn: $model.reviewMoves)
                Toggle("Show threats", isOn: $model.showThreats)
                Toggle("Hints while dragging", isOn: $model.dragHints)
                Toggle("Open recap after a game", isOn: $model.openRecap)
            }

            if model.expertUnlocked {
                Section("Expert") {
                    ExpertControls(model: model)
                    Button("Lock expert mode") { model.lockExpertMode() }
                }
            }
        }
        .formStyle(.grouped)
        .frame(width: 520)
        .fixedSize(horizontal: false, vertical: true)
        .background(ExpertKeyMonitor { model.expertUnlocked = true })
    }
}

struct ExpertKeyMonitor: NSViewRepresentable {
    var onUnlock: () -> Void

    func makeNSView(context: Context) -> MonitorView { MonitorView() }
    func updateNSView(_ view: MonitorView, context: Context) { view.onUnlock = onUnlock }

    final class MonitorView: NSView {
        var onUnlock: () -> Void = {}
        private var monitor: Any?

        override func viewDidMoveToWindow() {
            super.viewDidMoveToWindow()
            if let m = monitor { NSEvent.removeMonitor(m); monitor = nil }
            guard window != nil else { return }
            monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
                let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
                if let self, event.window === self.window, flags == [.option, .shift, .command], event.keyCode == 14 {
                    self.onUnlock()
                    return nil
                }
                return event
            }
        }

        deinit { if let m = monitor { NSEvent.removeMonitor(m) } }
    }
}
