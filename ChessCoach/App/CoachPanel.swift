import AppKit
import SwiftUI
import Combine

struct CoachCard: Equatable, Identifiable {
    enum Kind { case suggestion, review, hint, answer, threat, report }
    var id: String
    var kind: Kind
    var title: String
    var rating: MoveRating?
    var lines: [String] = []
    var text = ""
    var isLoading = false
    var note: String?
    var style: ExplanationStyle?
    var profileId: String?
    var feedback: ExplanationFeedback?
    var reviewPly: Int?
    var reviewSan: String?
    var staleTitle: String?
}

struct PanelAction: Identifiable {
    var id: String
    var title: String
    var symbol: String
    var handler: @MainActor () -> Void
}

@MainActor
final class CoachPanelModel: ObservableObject {
    @Published var cards: [CoachCard] = []
    @Published var collapsed = UserDefaults.standard.bool(forKey: "panelCollapsed")
    @Published var hintLevel = 0
    let hintMax = 4
    @Published var canHint = false
    @Published var canExplain = false
    @Published var question = ""
    @Published var isAsking = false
    @Published var focusText: String?
    @Published var infoLines: [String] = []
    @Published var extraActions: [PanelAction] = []

    var onNextHint: () -> Void = {}
    var onExplainAgain: () -> Void = {}
    var onAsk: (String) -> Void = { _ in }
    var onFeedback: (String, ExplanationFeedback) -> Void = { _, _ in }
}

@MainActor
final class CoachPanelController {
    static let width: CGFloat = 340
    private static let collapsedHeight: CGFloat = 44
    private var panel: NSPanel?
    private var moveObserver: NSObjectProtocol?
    private var collapseObserver: Any?
    private var applyingFrame = false
    private var lastBoard: CGRect?
    private var placed: (board: CGRect, collapsed: Bool)?
    let model = CoachPanelModel()

    func show(nextTo board: CGRect, cards: [CoachCard]) {
        if model.cards != cards { model.cards = cards }
        lastBoard = board
        let panel = self.panel ?? makePanel()
        self.panel = panel
        if !panel.isVisible || placed?.board != board || placed?.collapsed != model.collapsed {
            place(panel, board: board)
            placed = (board, model.collapsed)
        }
        if !panel.isVisible { panel.orderFrontRegardless() }
    }

    func hide() { panel?.orderOut(nil) }

    private func screen(containing p: CGPoint) -> NSScreen? {
        NSScreen.screens.first { $0.frame.contains(p) } ?? NSScreen.main
    }

    private func key(_ s: NSScreen) -> String {
        ((s.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.stringValue) ?? "main"
    }

    private func place(_ panel: NSPanel, board: CGRect) {
        let screen = screen(containing: CGPoint(x: board.midX, y: board.midY))
        let visible = screen?.visibleFrame ?? board
        let w = CoachPanelController.width
        let h = model.collapsed ? CoachPanelController.collapsedHeight : min(max(board.height, 320), 640)
        var origin: CGPoint?
        if let saved = screen.flatMap({ savedOffset(for: $0) }) {
            origin = CGPoint(x: visible.minX + saved.x, y: visible.maxY - saved.y - h)
        } else {
            let gap: CGFloat = 14
            var x = board.maxX + gap
            if x + w > visible.maxX {
                x = board.minX - gap - w >= visible.minX ? board.minX - gap - w : visible.maxX - w - 8
            }
            origin = CGPoint(x: x, y: board.maxY - h)
        }
        var frame = CGRect(origin: origin ?? .zero, size: CGSize(width: w, height: h))
        frame.origin.x = min(max(frame.origin.x, visible.minX), max(visible.minX, visible.maxX - w))
        frame.origin.y = min(max(frame.origin.y, visible.minY), max(visible.minY, visible.maxY - h))
        if panel.frame != frame {
            applyingFrame = true
            panel.setFrame(frame, display: true)
            applyingFrame = false
        }
    }

    private func savedOffset(for screen: NSScreen) -> CGPoint? {
        guard let d = UserDefaults.standard.dictionary(forKey: "panelPositions") as? [String: [Double]],
              let v = d[key(screen)], v.count == 2 else { return nil }
        return CGPoint(x: v[0], y: v[1])
    }

    private func remember(_ panel: NSPanel) {
        guard !applyingFrame, panel.isVisible,
              let screen = screen(containing: CGPoint(x: panel.frame.midX, y: panel.frame.midY)) else { return }
        let visible = screen.visibleFrame
        var d = (UserDefaults.standard.dictionary(forKey: "panelPositions") as? [String: [Double]]) ?? [:]
        d[key(screen)] = [Double(panel.frame.minX - visible.minX), Double(visible.maxY - panel.frame.maxY)]
        UserDefaults.standard.set(d, forKey: "panelPositions")
    }

    func resetPosition() {
        UserDefaults.standard.removeObject(forKey: "panelPositions")
        if let panel, let b = lastBoard { place(panel, board: b); placed = (b, model.collapsed) }
    }

    private func makePanel() -> NSPanel {
        let p = CoachNSPanel(contentRect: CGRect(x: 0, y: 0, width: CoachPanelController.width, height: 400),
                             styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = true
        p.isFloatingPanel = true
        p.becomesKeyOnlyIfNeeded = true
        p.hidesOnDeactivate = false
        p.isMovableByWindowBackground = true
        p.level = .statusBar
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        p.contentView = NSHostingView(rootView: CoachPanelView(model: model))
        moveObserver = NotificationCenter.default.addObserver(forName: NSWindow.didMoveNotification, object: p, queue: .main) { [weak self, weak p] _ in
            MainActor.assumeIsolated { if let self, let p { self.remember(p) } }
        }
        collapseObserver = model.$collapsed.dropFirst().sink { [weak self] value in
            UserDefaults.standard.set(value, forKey: "panelCollapsed")
            DispatchQueue.main.async {
                MainActor.assumeIsolated {
                    if let self, let panel = self.panel, let b = self.lastBoard { self.place(panel, board: b) }
                }
            }
        }
        return p
    }
}

private final class CoachNSPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
}

struct CoachPanelView: View {
    @ObservedObject var model: CoachPanelModel
    @FocusState private var questionFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
            if !model.collapsed {
                Divider().opacity(0.4)
                ScrollView {
                    VStack(alignment: .leading, spacing: 10) {
                        ForEach(model.infoLines, id: \.self) { Text($0).font(.caption).foregroundStyle(.secondary) }
                        ForEach(model.cards) { c in CardView(card: c) { model.onFeedback(c.id, $0) } }
                    }
                    .padding(12)
                    .frame(maxWidth: .infinity, alignment: .topLeading)
                }
                Divider().opacity(0.4)
                controls
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 14))
        .environment(\.colorScheme, .dark)
    }

    private var header: some View {
        HStack(spacing: 8) {
            Image(systemName: "checkerboard.rectangle").foregroundStyle(.secondary).accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 0) {
                Text("Chess Coach").font(.headline)
                if let f = model.focusText { Text(f).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
            }
            Spacer()
            if model.hintLevel > 0 {
                Text("Hint \(model.hintLevel) of \(model.hintMax)")
                    .font(.caption).monospacedDigit().foregroundStyle(.secondary)
            }
            Button {
                withAnimation(.smooth(duration: 0.15)) { model.collapsed.toggle() }
            } label: {
                Image(systemName: model.collapsed ? "chevron.down" : "chevron.up")
                    .frame(width: 24, height: 24)
            }
            .buttonStyle(.borderless)
            .accessibilityLabel(model.collapsed ? "Expand panel" : "Collapse panel")
        }
        .padding(.horizontal, 12)
        .frame(height: 44)
    }

    private var controls: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Button("Next hint") { model.onNextHint() }
                    .buttonStyle(.borderedProminent)
                    .disabled(!model.canHint || model.hintLevel >= model.hintMax)
                Button("Explain again") { model.onExplainAgain() }
                    .disabled(!model.canExplain)
            }
            if !model.extraActions.isEmpty {
                HStack {
                    ForEach(model.extraActions) { a in
                        Button { a.handler() } label: { Label(a.title, systemImage: a.symbol) }
                    }
                }
                .controlSize(.small)
            }
            HStack(spacing: 6) {
                TextField("Ask a question …", text: $model.question)
                    .textFieldStyle(.roundedBorder)
                    .focused($questionFocused)
                    .onSubmit(send)
                Button(action: send) {
                    if model.isAsking { ProgressView().controlSize(.small) }
                    else { Image(systemName: "arrow.up.circle.fill").font(.title3) }
                }
                .buttonStyle(.borderless)
                .disabled(model.question.trimmingCharacters(in: .whitespaces).isEmpty || model.isAsking)
                .accessibilityLabel("Send question")
            }
        }
        .padding(12)
    }

    private func send() {
        let q = model.question.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !q.isEmpty, !model.isAsking else { return }
        model.onAsk(q)
    }
}

private struct CardView: View {
    let card: CoachCard
    let onFeedback: (ExplanationFeedback) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                if let r = card.rating {
                    Image(systemName: r.symbol)
                        .foregroundStyle(color(r))
                        .accessibilityLabel(r.title())
                }
                if card.kind == .threat {
                    Image(systemName: "exclamationmark.triangle.fill").foregroundStyle(.red).accessibilityHidden(true)
                }
                Text(card.title).font(.headline)
            }
            ForEach(card.lines, id: \.self) { Text($0).font(.subheadline).foregroundStyle(.secondary) }
            if !card.text.isEmpty {
                Text(card.text).font(.body).fixedSize(horizontal: false, vertical: true)
            } else if card.isLoading {
                HStack(spacing: 6) {
                    ProgressView().controlSize(.small)
                    Text("Writing explanation …").font(.subheadline).foregroundStyle(.secondary)
                }
            }
            if let note = card.note { Text(note).font(.caption).foregroundStyle(.secondary) }
            if card.style != nil, !card.isLoading, !card.text.isEmpty {
                feedbackRow
            }
        }
        .padding(10)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.white.opacity(0.07), in: RoundedRectangle(cornerRadius: 10))
    }

    @ViewBuilder
    private var feedbackRow: some View {
        if card.feedback == nil {
            HStack(spacing: 6) {
                Button("Too long") { onFeedback(.tooLong) }
                Button("Too short") { onFeedback(.tooShort) }
                Button("Helpful") { onFeedback(.helpful) }
            }
            .controlSize(.small)
        } else {
            Label("Thanks, noted", systemImage: "checkmark").font(.caption).foregroundStyle(.secondary)
        }
    }

    private func color(_ r: MoveRating) -> Color {
        switch r {
        case .best: .green
        case .good: .mint
        case .inaccuracy: .yellow
        case .mistake: .orange
        case .blunder: .red
        }
    }
}
