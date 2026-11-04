import Foundation

enum Skill: String, Codable, CaseIterable {
    case pieceSafety = "piece_safety", threats, tactics, opening, middlegame, endgame, kingSafety = "king_safety"

    func title(_ loc: Loc = .display) -> String {
        switch self {
        case .pieceSafety: loc("skill.pieceSafety", "Piece safety")
        case .threats: loc("skill.threats", "Threats")
        case .tactics: loc("skill.tactics", "Tactics")
        case .opening: loc("skill.opening", "Opening principles")
        case .middlegame: loc("skill.middlegame", "Plans")
        case .endgame: loc("skill.endgame", "Endgames")
        case .kingSafety: loc("skill.kingSafety", "King safety")
        }
    }

    func summary(_ loc: Loc = .display) -> String {
        switch self {
        case .pieceSafety: loc("skill.pieceSafety.summary", "Never leave a piece undefended or unprotected.")
        case .threats: loc("skill.threats.summary", "Before every move, check what the opponent is up to.")
        case .tactics: loc("skill.tactics.summary", "Spot and use forks, pins, skewers and discovered attacks.")
        case .opening: loc("skill.opening.summary", "Develop pieces, take the centre, castle early.")
        case .middlegame: loc("skill.middlegame.summary", "Have a plan in the middlegame: improve pieces, attack weaknesses.")
        case .endgame: loc("skill.endgame.summary", "Activate the king, promote pawns, win simple endgames safely.")
        case .kingSafety: loc("skill.kingSafety.summary", "Keep the king protected and do not weaken its cover without need.")
        }
    }

    var promptHint: String {
        switch self {
        case .pieceSafety: "Stress loose or cheaply attacked pieces."
        case .threats: "Stress: what does the opponent want?"
        case .tactics: "Stress tactical patterns shown."
        case .opening: "Stress development, centre, castling."
        case .kingSafety: "Stress king safety, pawn cover."
        case .endgame: "Stress active king, passed pawns."
        case .middlegame: "Stress a plan, improving pieces."
        }
    }
}

enum MistakeType: String, Codable, CaseIterable {
    case hangingPiece = "hanging_piece"
    case missedThreat = "missed_threat"
    case missedTactic = "missed_tactic"
    case kingSafety = "king_safety"
    case openingPrinciple = "opening_principle"
    case endgameTechnique = "endgame_technique"
    case other

    func title(_ loc: Loc = .display) -> String {
        switch self {
        case .hangingPiece: loc("mistake.hangingPiece", "Hanging piece")
        case .missedThreat: loc("mistake.missedThreat", "Missed threat")
        case .missedTactic: loc("mistake.missedTactic", "Missed tactic")
        case .kingSafety: loc("mistake.kingSafety", "King safety")
        case .openingPrinciple: loc("mistake.openingPrinciple", "Opening principle")
        case .endgameTechnique: loc("mistake.endgameTechnique", "Endgame technique")
        case .other: loc("mistake.other", "Other")
        }
    }

    var skill: Skill {
        switch self {
        case .hangingPiece: .pieceSafety
        case .missedThreat: .threats
        case .missedTactic: .tactics
        case .kingSafety: .kingSafety
        case .openingPrinciple: .opening
        case .endgameTechnique: .endgame
        case .other: .middlegame
        }
    }
}

struct PathStage {
    let number: Int
    let skill: Skill
}

struct Adaptation: Equatable {
    var defaultHintLevel: Int
    var newPuzzlesPerDay: Int
    var difficultyBias: Int
    var intervalFactor: Double
}

enum Curriculum {
    static let focusThreshold = 0.7
    static let targetRange = 0.70...0.85

    static let path: [PathStage] = [.pieceSafety, .threats, .tactics, .opening, .kingSafety, .endgame, .middlegame]
        .enumerated().map { PathStage(number: $0 + 1, skill: $1) }

    static func value(_ skills: [String: Double], _ s: Skill) -> Double { skills[s.rawValue] ?? 0.5 }

    static func focus(skills: [String: Double]) -> Skill {
        if let s = path.first(where: { value(skills, $0.skill) < focusThreshold }) { return s.skill }
        return path.min { value(skills, $0.skill) < value(skills, $1.skill) }!.skill
    }

    static func focusLabel(_ skill: Skill, _ loc: Loc = .display) -> String {
        loc("focus.label", "Learning focus: \(skill.title(loc))")
    }

    static func adaptation(level: PlayerLevel, focus: Skill, skills: [String: Double], successRate: Double?) -> Adaptation {
        var hint = level == .beginner ? 2 : 1
        if value(skills, focus) < 0.4 { hint += 1 }
        var a = Adaptation(defaultHintLevel: hint, newPuzzlesPerDay: 10, difficultyBias: 0, intervalFactor: 1)
        if let q = successRate {
            if q > targetRange.upperBound {
                a.defaultHintLevel -= 1; a.difficultyBias = 1; a.intervalFactor = 1.2
            } else if q < targetRange.lowerBound {
                a.defaultHintLevel += 1; a.difficultyBias = -1; a.intervalFactor = 0.8; a.newPuzzlesPerDay = 5
            }
        }
        a.defaultHintLevel = min(3, max(1, a.defaultHintLevel))
        return a
    }
}
