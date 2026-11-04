import SwiftUI

struct WelcomeView: View {
    @ObservedObject var app: AppModel
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Welcome to Chess Coach").font(.title2.bold())
            Text("Chess Coach watches a chessboard on your screen and helps you improve with hints, move ratings and a recap after each game. Everything is analysed on this Mac.")
                .fixedSize(horizontal: false, vertical: true)
            VStack(alignment: .leading, spacing: 10) {
                step("1.circle", Text("Open a chess website or app and start a game."))
                step("2.circle", Text("Switch Chess Coach on and allow screen recording when macOS asks."))
                step("3.circle", Text("Show a start position once so the app can learn how your pieces look. If the piece set changes later, choose “Relearn pieces”."))
                step("4.circle", Text("Chess Coach never plays a move for you. Language and the optional AI for explanations are in Settings (⌘,)."))
            }
            HStack {
                Spacer()
                Button("Got It") { dismiss() }
                Button("Switch On") { app.isActive = true; dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
        }
        .padding(24)
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
    }

    private func step(_ symbol: String, _ text: Text) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 10) {
            Image(systemName: symbol).foregroundStyle(.tint)
            text.fixedSize(horizontal: false, vertical: true)
        }
    }
}
