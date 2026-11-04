import AppKit
import Charts
import SwiftUI
import UniformTypeIdentifiers

struct ReportView: View {
    @ObservedObject var app: AppModel
    @ObservedObject var store: LearningStore
    @State private var selection: String?

    init(app: AppModel) {
        self.app = app
        self.store = app.store
    }

    private var selected: GameReport? {
        store.games.first { $0.id == (selection ?? app.recapSelection ?? app.lastReport?.id ?? store.games.first?.id) }
    }

    var body: some View {
        NavigationSplitView {
            List(store.games, id: \.id, selection: $selection) { g in
                VStack(alignment: .leading, spacing: 2) {
                    Text(g.date.formatted(date: .abbreviated, time: .shortened)).font(.headline)
                    Text(g.opening ?? String(localized: "No opening recognised")).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                }
                .tag(g.id)
            }
            .navigationSplitViewColumnWidth(min: 190, ideal: 220)
            .overlay {
                if store.games.isEmpty {
                    ContentUnavailableView("No games yet", systemImage: "flag.checkered",
                                           description: Text("The evaluation appears here after a game ends."))
                }
            }
        } detail: {
            if let g = selected {
                ReportDetail(app: app, store: store, report: g)
            } else if let p = app.recapProgress {
                VStack(spacing: 12) {
                    ProgressView(value: Double(p.done), total: Double(max(p.total, 1)))
                        .frame(width: 240)
                    Text("Analysing moves \(p.done) of \(p.total) …").foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                ContentUnavailableView("No game selected", systemImage: "doc.text.magnifyingglass")
            }
        }
        .frame(minWidth: 820, minHeight: 560)
        .navigationTitle("Evaluation")
        .onChange(of: app.recapSelection) { _, id in if let id { selection = id } }
        .onAppear { if let id = app.recapSelection { selection = id } }
    }
}

private struct ReportDetail: View {
    @ObservedObject var app: AppModel
    @ObservedObject var store: LearningStore
    let report: GameReport

    private var perspective: (color: String, profile: PlayerProfile?) { app.perspective(of: report) }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                header
                HStack(alignment: .top, spacing: 16) {
                    sideCard(color: "w")
                    sideCard(color: "b")
                }
                chart
                keyMoments
                summary
                moves
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .toolbar {
            ToolbarItemGroup {
                Button("Copy PGN", systemImage: "doc.on.doc") { copyPGN() }
                Button("Save …", systemImage: "square.and.arrow.down") { savePGN() }
            }
        }
        .onAppear {
            if report.summary == nil, app.summaryDraft[report.id] == nil, app.llmConfigured, !report.moves.isEmpty {
                app.writeSummary(for: report)
            }
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(report.date.formatted(date: .long, time: .shortened)).font(.title2.bold())
            Text(report.opening.map { String(localized: "Opening: \($0)") } ?? String(localized: "No opening recognised"))
                .foregroundStyle(.secondary)
            Text("\(report.moves.count) half-moves").font(.callout).foregroundStyle(.secondary)
            if let r = report.result, r != "*" { Text("Result: \(r)").font(.callout).foregroundStyle(.secondary) }
            if let p = app.recapProgress, p.id == report.id {
                HStack(spacing: 8) {
                    ProgressView(value: Double(p.done), total: Double(max(p.total, 1))).frame(width: 160)
                    Text("Analysing moves \(p.done) of \(p.total) …").font(.callout).foregroundStyle(.secondary)
                }
            }
        }
    }

    private func name(_ color: String) -> String {
        let id = color == "w" ? report.whiteProfileId : report.blackProfileId
        let base = color == "w" ? String(localized: "White") : String(localized: "Black")
        guard let p = store.profile(id: id) else { return base }
        return "\(base) · \(p.name)"
    }

    private func sideCard(color: String) -> some View {
        let s = report.side(color)
        return VStack(alignment: .leading, spacing: 8) {
            Text(name(color)).font(.headline)
            Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 4) {
                GridRow { Text("Accuracy"); Text("\(s.accuracy, specifier: "%.0f") %").monospacedDigit() }
                GridRow { Text("Average loss"); Text("\(s.acpl, specifier: "%.0f") cp").monospacedDigit() }
                GridRow { Text("Performance rating"); Text("\(s.performance, specifier: "%.0f")").monospacedDigit() }
            }
            Divider()
            HStack(spacing: 10) {
                count("star.circle.fill", s.best, .green)
                count("checkmark.circle.fill", s.good, .mint)
                count("exclamationmark.circle.fill", s.inaccuracies, .yellow)
                count("questionmark.circle.fill", s.mistakes, .orange)
                count("xmark.octagon.fill", s.blunders, .red)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
    }

    private func count(_ symbol: String, _ n: Int, _ color: Color) -> some View {
        HStack(spacing: 3) {
            Image(systemName: symbol).foregroundStyle(color)
            Text("\(n)").monospacedDigit()
        }
    }

    private var chart: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text("Winning chances for White").font(.headline)
            Chart(report.moves, id: \.ply) { m in
                LineMark(x: .value("Move", m.ply), y: .value("White", m.color == "w" ? m.winBefore : 100 - m.winBefore))
                    .interpolationMethod(.monotone)
            }
            .chartYScale(domain: 0...100)
            .frame(height: 140)
        }
    }

    private var keyMoments: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Key moments").font(.headline)
            if report.keyMoments.isEmpty { Text("No big swings. A clean game.").foregroundStyle(.secondary) }
            ForEach(report.keyMoments, id: \.ply) { k in
                VStack(alignment: .leading, spacing: 2) {
                    Text("\(GameReport.moveLabel(ply: k.ply, color: k.color)): \(k.moveText)").bold()
                    Text(k.facts).font(.callout).foregroundStyle(.secondary)
                }
            }
        }
    }

    private var summary: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text("Summary").font(.headline)
                if app.summaryDraft[report.id] != nil { ProgressView().controlSize(.small) }
            }
            if let draft = app.summaryDraft[report.id], !draft.isEmpty {
                Text(draft)
            } else if let s = report.summary {
                Text(s)
            } else {
                Text(report.fallbackSummary(for: perspective.color)).foregroundStyle(.secondary)
            }
            if app.llmConfigured {
                Button(report.summary == nil ? "Write summary" : "Rewrite summary") { app.writeSummary(for: report) }
                    .disabled(app.summaryDraft[report.id] != nil)
            } else {
                Text("Set up an AI server in the settings (⌘,) for a written summary with three lessons.")
                    .font(.callout).foregroundStyle(.secondary)
            }
        }
    }

    private var moves: some View {
        DisclosureGroup("Moves") {
            LazyVStack(alignment: .leading, spacing: 2) {
                ForEach(report.moves, id: \.ply) { m in
                    HStack {
                        Text("\((m.ply + 1) / 2)\(m.color == "w" ? "." : "…")").monospacedDigit().frame(width: 40, alignment: .trailing)
                        Text(m.san ?? m.uci).frame(width: 70, alignment: .leading)
                        Image(systemName: MoveRating(rawValue: m.rating)?.symbol ?? "circle")
                            .foregroundStyle(ratingColor(m.rating))
                            .accessibilityLabel(m.rating)
                        Text("−\(m.loss) cp").monospacedDigit().foregroundStyle(.secondary)
                        if let b = m.bestText { Text("Better: \(b)").font(.caption).foregroundStyle(.secondary) }
                    }
                }
            }
            .padding(.top, 6)
        }
    }

    private func ratingColor(_ raw: String) -> Color {
        switch MoveRating(rawValue: raw) {
        case .best?: .green
        case .good?: .mint
        case .inaccuracy?: .yellow
        case .mistake?: .orange
        case .blunder?: .red
        case nil: .secondary
        }
    }

    private func pgnText() -> String? { store.pgn(for: report) }

    private func copyPGN() {
        guard let text = pgnText() else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }

    private func savePGN() {
        guard let text = pgnText() else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "pgn") ?? .plainText]
        panel.nameFieldStringValue = "game-\(report.date.formatted(.iso8601.year().month().day())).pgn"
        if panel.runModal() == .OK, let url = panel.url { try? text.write(to: url, atomically: true, encoding: .utf8) }
    }
}
