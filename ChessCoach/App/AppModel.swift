import AppKit
import Combine
import SwiftUI

@MainActor
final class AppModel: ObservableObject {
    enum Side: String, CaseIterable, Identifiable {
        case auto, white, black
        var id: String { rawValue }
        var title: LocalizedStringResource {
            switch self { case .auto: "Automatic (bottom)"; case .white: "White"; case .black: "Black" }
        }
    }

    @Published var isActive = false { didSet { if isActive != oldValue { isActive ? start() : stop() } } }
    @Published var status = String(localized: "Off")
    @Published var suggestion: String?
    @Published var needsPermission = false
    @Published private(set) var hasTemplates = false

    @Published var side: Side { didSet { resetTracking() } }
    @Published var thinkMs: Int { didSet { analysisCache.removeAll(); invalidate() } }
    @Published var alwaysMyTurn: Bool { didSet { invalidate() } }
    @Published var duoTraining: Bool { didSet { suggestionCard = nil; forgetHints(); invalidate() } }
    @Published var explainSuggestions: Bool { didSet { suggestionCard = nil; invalidate() } }
    @Published var showThreats: Bool { didSet { invalidate() } }
    @Published var reviewMoves: Bool { didSet { if !reviewMoves { reviewCard = nil }; invalidate() } }

    @Published var expertUnlocked: Bool { didSet { analysisCache.removeAll(); suggestionCard = nil; invalidate() } }
    @Published var showBestImmediately: Bool { didSet { suggestionCard = nil; invalidate() } }
    var effectiveShowBest: Bool { expertUnlocked && showBestImmediately }
    var effectiveAlwaysMyTurn: Bool { expertUnlocked && alwaysMyTurn }
    var effectiveThinkMs: Int { expertUnlocked ? thinkMs : 1000 }

    @Published var llmURL: String
    @Published var llmToken: String
    @Published var llmModel: String { didSet { suggestionCard = nil } }
    @Published var llmModels: [LLMModel] = []
    @Published var llmStatus = ""
    @Published var llmLoading = false
    @Published var llmBase: String
    var llmConfigured: Bool { !llmBase.isEmpty && !llmModel.isEmpty }

    let defaults = UserDefaults.standard
    let overlay = OverlayController()
    let panel = CoachPanelController()
    var loopTask: Task<Void, Never>?
    var engine: StockfishEngine?
    var templates = PieceTemplates() { didSet { hasTemplates = !templates.isEmpty } }

    var lastDisplay: CGDirectDisplayID?
    var autoWhiteAtBottom: Bool?
    var pendingScreen: [[Character?]]?
    var stablePlacement: Placement?
    var sideOverride: PieceColor?
    var analysisCache: [String: CachedAnalysis] = [:]
    var failedFens = Set<String>()
    var lastProblemFen: String?
    var askedForPermission = false

    var reviewCard: CoachCard? { didSet { updatePanelCards() } }
    var suggestionCard: CoachCard? { didSet { updatePanelCards() } }
    var lastBoardRect: CGRect?

    let activity = SystemActivity()
    var pausedShown = false
    var tracked: (displayID: CGDirectDisplayID, frame: CGRect, rect: CGRect)?
    var lastChecksum: UInt64?
    var boardStabilizer = RectStabilizer()
    var regionMisses = 0
    var confirmSoon = false
    var reviewSeq = 0
    var pendingReview: (moves: [PlayedMove], marked: Set<String>)?
    var cachedLocated: Located?
    var lastScreen: [[Character?]]?
    var lastChangeAt = Date()
    var lostSince: Date?
    var settled = false
    var lastAnalysisAt = Date()

    var hintCtx: HintContext?
    var hintFen: String?
    var hintLevel = 0 { didSet { panel.model.hintLevel = hintLevel } }
    var answerCard: CoachCard? { didSet { updatePanelCards() } }
    var lastMe: PieceColor?
    let store = LearningStore()

    @Published var windowRequest: WindowRequest?
    @Published var lastReport: GameReport?
    @Published var summaryDraft: [String: String] = [:]
    @Published var movesInGame = 0
    @Published private(set) var focusLine = ""
    var cancellables = Set<AnyCancellable>()
    var record = GameRecord()
    var entries: [MoveEntry] = []
    var gameId = UUID().uuidString.lowercased()
    var gameFinished = false
    var countedProfiles = Set<String>()
    var usedHint: [String: Int] = [:]
    var threatCard: CoachCard? { didSet { updatePanelCards() } }
    var reportCard: CoachCard? { didSet { updatePanelCards() } }
    var jobs: [Slot: ExplainJob] = [:]
    var tasks: [Slot: Task<Void, Never>] = [:]
    var summaryTasks: [String: Task<Void, Never>] = [:]
    var explanationCache: [String: String] = [:]
    var ideaCache: [String: String] = [:]
    var ideaTask: Task<Void, Never>?
    var ideaFen: String?
    var worker: Task<Void, Never>?
    var idleStopTask: Task<Void, Never>?
    var engineBusy = false
    var engineWaiters: [(priority: Bool, continuation: CheckedContinuation<Void, Never>)] = []
    var lastExplanation: (style: ExplanationStyle, profileId: String, at: Date)?
    var openingLine: String?
    var questionTask: Task<Void, Never>?

    struct HintContext {
        var position: ChessPosition
        var toMove: PieceColor
        var me: PieceColor
        var whiteAtBottom: Bool
        var result: EngineResult
        var hints: HintSet
        var threat: Threats.Threat?
        var fen: String
        var boardRect: CGRect
        var who: String
        var partner: Bool
        var profileId: String?
        var text: String
        var score: String
    }

    struct Located {
        var display: CapturedDisplay
        var scan: ScanResult
        var scale: Double
        var unchanged: Bool
    }

    func invalidate() { settled = false; lastChangeAt = Date() }


    init() {
        signal(SIGPIPE, SIG_IGN)
        Log.write("Chess Coach started, version \(Bundle.main.infoDictionary?["CFBundleShortVersionString"] ?? "?"), language \(Language.current), \(String(localized: "Off"))")
        side = Side(rawValue: defaults.string(forKey: "side") ?? "") ?? .auto
        thinkMs = defaults.integer(forKey: "thinkMs") > 0 ? defaults.integer(forKey: "thinkMs") : 1000
        alwaysMyTurn = defaults.flag("alwaysMyTurn", false)
        duoTraining = defaults.flag("duoTraining", false)
        explainSuggestions = defaults.flag("explainSuggestions", true)
        reviewMoves = defaults.flag("reviewMoves", true)
        showThreats = defaults.flag("showThreats", true)
        expertUnlocked = defaults.flag("expertUnlocked", false)
        showBestImmediately = defaults.flag("showBestImmediately", false)
        llmURL = defaults.string(forKey: "llmURL") ?? ""
        llmBase = defaults.string(forKey: "llmBase") ?? ""
        llmModel = defaults.string(forKey: "llmModel") ?? ""
        llmToken = TokenStore.load()
        templates = Self.loadTemplates() ?? PieceTemplates()
        hasTemplates = !templates.isEmpty
        if !llmModel.isEmpty { llmModels = [LLMModel(id: llmModel, name: llmModel)] }
        if defaults.bool(forKey: "resumeActive") {
            defaults.removeObject(forKey: "resumeActive")
            Task { @MainActor in self.isActive = true }
        }
        panel.model.onNextHint = { [weak self] in self?.nextHint() }
        panel.model.onExplainAgain = { [weak self] in self?.explainAgain() }
        panel.model.onAsk = { [weak self] q in self?.ask(q) }
        activity.onChange = { [weak self] in self?.invalidate() }
        HotKeys.shared.register(keyCode: HotKeys.keyH, modifiers: HotKeys.optionCommand) { [weak self] in self?.nextHint() }
        HotKeys.shared.register(keyCode: HotKeys.keyT, modifiers: HotKeys.optionCommand) { [weak self] in self?.toggleSideToMove() }
        bindStore()
        bindPreferences()
        if let name = ProcessInfo.processInfo.environment["CHESSCOACH_OPEN"] {
            Task { @MainActor in self.open(name) }
        }
    }

    func start() {
        if !ScreenCapture.hasPermission && !askedForPermission {
            askedForPermission = true
            ScreenCapture.requestPermission()
        }
        status = String(localized: "Looking for a chessboard …")
        failedFens = []
        activity.start()
        invalidate()
        loopTask?.cancel()
        loopTask = Task { [weak self] in
            while !Task.isCancelled {
                guard let self else { return }
                await self.tick()
                self.stopIdleEngine()
                try? await Task.sleep(for: self.nextDelay())
            }
        }
    }

    func stop() {
        finishGame()
        loopTask?.cancel()
        loopTask = nil
        activity.stop()
        pausedShown = false
        tracked = nil
        dropCapture()
        hideAll()
        engine?.stop()
        engine = nil
        resetTracking()
        status = String(localized: "Off")
        suggestion = nil
    }

    func resetTracking() {
        pendingScreen = nil
        stablePlacement = nil
        pendingReview = nil
        boardStabilizer.reset()
        autoWhiteAtBottom = nil
        cancelAllTasks()
        reviewCard = nil
        suggestionCard = nil
        answerCard = nil
        threatCard = nil
        record.clear()
        entries = []
        sideOverride = nil
        gameFinished = false
        gameId = UUID().uuidString.lowercased()
        forgetHints()
        panel.model.canHint = false
        panel.model.canExplain = false
        panel.model.isAsking = false
        dropCapture()
        invalidate()
    }

    func cancelAllTasks() {
        for t in tasks.values { t.cancel() }
        tasks = [:]
        questionTask?.cancel()
        ideaTask?.cancel()
        ideaFen = nil
        worker?.cancel()
        worker = nil
    }

    func updatePanelCards() {
        guard let rect = lastBoardRect, isActive else { return }
        let review = displayedReviewCard()
        let reviewIsOld = review.map { $0.reviewPly != nil && $0.reviewPly! < record.moves.count } ?? false
        let cards = (reviewIsOld ? [reportCard, suggestionCard, threatCard, answerCard, review] : [reportCard, review, threatCard, suggestionCard, answerCard]).compactMap { $0 }
        if cards.isEmpty { panel.hide() } else { panel.show(nextTo: rect, cards: cards) }
    }

    func hideAll() {
        overlay.hide()
        panel.hide()
        hintCtx = nil
        panel.model.canHint = false
    }

    func open(_ window: String) {
        windowRequest = WindowRequest(id: window, serial: (windowRequest?.serial ?? 0) + 1)
    }

    func refreshActions() {
        var actions = [
            PanelAction(id: "training", title: String(localized: "Training"), symbol: "graduationcap") { [weak self] in self?.open("training") },
            PanelAction(id: "profile", title: String(localized: "Profile"), symbol: "chart.line.uptrend.xyaxis") { [weak self] in self?.open("profile") },
        ]
        if lastReport != nil {
            actions.append(PanelAction(id: "report", title: String(localized: "Evaluation"), symbol: "doc.text.magnifyingglass") { [weak self] in self?.open("report") })
        }
        panel.model.extraActions = actions
    }

    func refreshProfileInfo(_ profiles: [PlayerProfile], _ activeId: String) {
        guard let p = profiles.first(where: { $0.id == activeId }) ?? profiles.first else { return }
        focusLine = Curriculum.focusLabel(p.focusSkill)
        panel.model.focusText = "\(p.name) · \(focusLine)"
        updateInfoLines()
    }

    func bindStore() {
        store.$profiles.combineLatest(store.$activeId)
            .sink { [weak self] profiles, id in self?.refreshProfileInfo(profiles, id) }
            .store(in: &cancellables)
        panel.model.$collapsed.dropFirst()
            .sink { [weak self] collapsed in self?.panelCollapsed(collapsed) }
            .store(in: &cancellables)
        panel.model.onFeedback = { [weak self] id, value in self?.feedback(cardId: id, value) }
        refreshActions()
    }

}
