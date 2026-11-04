import Charts
import SwiftUI

struct ProfileView: View {
    @ObservedObject var app: AppModel
    @ObservedObject var store: LearningStore
    @State private var viewing: String?
    @State private var nameDialog: NameDialog?
    @State private var nameText = ""

    enum NameDialog: Identifiable {
        case create, rename
        var id: Int { self == .create ? 0 : 1 }
    }

    init(app: AppModel) {
        self.app = app
        self.store = app.store
    }

    private var profile: PlayerProfile { store.profile(id: viewing) ?? store.active }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 22) {
                picker
                overview
                ratingChart
                skills
                mistakes
                puzzles
                style
                recentGames
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .frame(minWidth: 640, minHeight: 600)
        .navigationTitle("Player profile")
        .alert(nameDialog == .create ? "New profile" : "Rename profile", isPresented: Binding(get: { nameDialog != nil }, set: { if !$0 { nameDialog = nil } })) {
            TextField("Name", text: $nameText)
            Button("Cancel", role: .cancel) {}
            Button(nameDialog == .create ? "Create" : "Rename") {
                if nameDialog == .create { viewing = store.addProfile(name: nameText).id }
                else { store.rename(profile.id, to: nameText) }
            }
        }
    }

    private var picker: some View {
        HStack {
            Picker("Profile", selection: Binding(get: { profile.id }, set: { viewing = $0 })) {
                ForEach(store.profiles) { Text($0.name).tag($0.id) }
            }
            .frame(maxWidth: 260)
            Button("New …") { nameText = ""; nameDialog = .create }
            Button("Rename …") { nameText = profile.name; nameDialog = .rename }
            Spacer()
            if profile.id == store.activeId {
                Label("Active (bottom side)", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
            } else {
                Button("Use for the bottom side") { store.activeId = profile.id }
            }
            if profile.id == store.partnerId {
                Button("Remove as partner") { store.partnerId = nil }
            } else if profile.id != store.activeId {
                Button("Use as partner (top side)") { store.partnerId = profile.id }
            }
        }
    }

    private var overview: some View {
        let p = profile
        return VStack(alignment: .leading, spacing: 6) {
            Text(p.name).font(.largeTitle.bold())
            if let t = p.placementText() {
                Text(t).foregroundStyle(.orange)
            } else {
                Text("\(p.level.title()) · rating \(Int(p.rating.rounded()))").font(.title3)
            }
            Text(Curriculum.focusLabel(p.focusSkill)).foregroundStyle(.secondary)
            Text("\(p.gamesPlayed) games evaluated").font(.callout).foregroundStyle(.secondary)
        }
    }

    @ViewBuilder
    private var ratingChart: some View {
        let history = profile.ratingHistory
        VStack(alignment: .leading, spacing: 6) {
            Text("Rating history").font(.headline)
            if history.count < 2 {
                Text("The chart appears after two evaluated games.").foregroundStyle(.secondary)
            } else {
                Chart {
                    ForEach(Array(history.enumerated()), id: \.offset) { i, e in
                        LineMark(x: .value("Game", i + 1), y: .value("Rating", e.rating), series: .value("Series", "Rating"))
                        PointMark(x: .value("Game", i + 1), y: .value("Rating", e.rating))
                        PointMark(x: .value("Game", i + 1), y: .value("Performance", e.performance))
                            .symbol(.diamond)
                            .foregroundStyle(.secondary)
                    }
                }
                .chartXAxis { AxisMarks(values: .automatic(desiredCount: 6)) }
                .frame(height: 170)
                Text("Line: rating. Diamonds: performance in each game.").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private var skills: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Skills").font(.headline)
            ForEach(Curriculum.path, id: \.number) { stage in
                let v = profile.skills[stage.skill.rawValue] ?? 0.5
                HStack {
                    Text(stage.skill.title())
                        .fontWeight(stage.skill == profile.focusSkill ? .bold : .regular)
                        .frame(width: 160, alignment: .leading)
                    ProgressView(value: v)
                        .tint(stage.skill == profile.focusSkill ? .orange : .accentColor)
                    Text("\(Int((v * 100).rounded())) %").monospacedDigit().frame(width: 50, alignment: .trailing)
                }
                .accessibilityElement(children: .combine)
            }
        }
    }

    private var mistakes: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Most common mistakes").font(.headline)
            let top = profile.topMistakes(5)
            if top.isEmpty { Text("None recorded yet.").foregroundStyle(.secondary) }
            ForEach(top, id: \.type) { m in
                VStack(alignment: .leading, spacing: 1) {
                    Text("\(m.type.title()) · \(m.count)×").bold()
                    if let ex = profile.mistakeExamples?[m.type.rawValue]?.last {
                        Text("Latest: \(ex)").font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
        }
    }

    private var puzzles: some View {
        let s = PuzzleScheduler.stats(store.puzzles(for: profile.id).puzzles)
        let uses = profile.hintStats.levelUses
        return VStack(alignment: .leading, spacing: 6) {
            Text("Tasks").font(.headline)
            Text("\(s.total) tasks · \(s.mastered) mastered · \(s.due + s.new) due · \(s.attempts) attempts")
            if let q = s.recentSuccessRate { Text("Success rate (last 30): \(Int((q * 100).rounded())) %") }
            Text("Learning pace: ×\(profile.learningSpeed, specifier: "%.1f")")
            Text("Moves without a hint: \(uses[0])").font(.callout).foregroundStyle(.secondary)
            Text("Hint steps 1 to 4 used: \(uses[1]) · \(uses[2]) · \(uses[3]) · \(uses[4])").font(.callout).foregroundStyle(.secondary)
        }
    }

    private var style: some View {
        let st = profile.style
        return VStack(alignment: .leading, spacing: 6) {
            Text("Explanation style").font(.headline)
            LabeledContent("Length") { Text(st.length.title) }
            LabeledContent("Depth") { Text(st.depth.title) }
            Text("The app learns from your buttons, from tasks solved after an explanation, and from asking again.")
                .font(.callout).foregroundStyle(.secondary)
            Button("Reset style") { store.resetStyle(profile.id) }
        }
    }

    private var recentGames: some View {
        let games = store.games.filter { $0.whiteProfileId == profile.id || $0.blackProfileId == profile.id }.prefix(30)
        return VStack(alignment: .leading, spacing: 6) {
            Text("Recent games").font(.headline)
            if games.isEmpty { Text("No games yet.").foregroundStyle(.secondary) }
            ForEach(Array(games), id: \.id) { g in
                let color = g.whiteProfileId == profile.id ? "w" : "b"
                Button { app.showRecap(g.id) } label: {
                    HStack {
                        Text(g.date.formatted(date: .abbreviated, time: .shortened)).frame(width: 150, alignment: .leading)
                        Text("Accuracy \(g.side(color).accuracy, specifier: "%.0f") %").monospacedDigit().frame(width: 130, alignment: .leading)
                        Text(g.opening ?? "–").foregroundStyle(.secondary).lineLimit(1)
                        Spacer()
                        Image(systemName: "chevron.right").foregroundStyle(.secondary).accessibilityHidden(true)
                    }
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
                .help("Open recap")
            }
        }
    }
}

extension StyleLength {
    var title: LocalizedStringResource {
        switch self { case .short: "Short"; case .medium: "Medium"; case .long: "Long" }
    }
}

extension StyleDepth {
    var title: LocalizedStringResource {
        switch self { case .principle: "Principle"; case .concrete: "Concrete with variations" }
    }
}
