import Foundation

struct MoveEntry: Identifiable {
    let played: PlayedMove
    var before: EngineResult?
    var after: EngineResult?
    var triedBefore = false
    var triedAfter = false
    var processed = false
    var id: String { played.fenBefore + played.uci }
}

struct CachedAnalysis {
    let result: EngineResult
    let ms: Int
}

struct WindowRequest: Equatable {
    let id: String
    let serial: Int
}

enum Slot { case review, suggestion }

struct ExplainJob {
    var cardId: String
    var profileId: String?
    var themeKey: String?
    var allowed: (() -> Set<String>)?
    var build: (PlayerContext, ExplanationStyle) -> Coach.ExplanationRequest?
}

struct RecapProgress: Equatable {
    var id: String
    var done: Int
    var total: Int
}

struct GameSnapshot {
    var id: String
    var end: GameEnd
    var date: Date
    var entries: [MoveEntry]
    var opening: String?
    var whiteProfile: String?
    var blackProfile: String?
    var pgn: String
}
