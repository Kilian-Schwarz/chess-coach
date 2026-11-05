import Foundation

struct SkillObservation: Equatable {
    let skill: Skill
    let value: Double
}

struct MoveAnalysisInput {
    let review: MoveReview
    let beforeResult: EngineResult
    let afterResult: EngineResult?
    let threatResult: EngineResult?
    let moveNumber: Int
}

struct MoveAnalysis {
    var observations: [SkillObservation]
    var mistakeType: MistakeType?
    var example: String?
    var theme: HintTheme?
    var threat: Threats.Threat?
    var accuracy: Double
    var loss: Int
    var phase: GamePhase
}

enum GamePhase: String, Codable { case opening, middlegame, endgame }

enum MoveAnalyzer {
    static func phase(of p: Placement, moveNumber: Int) -> GamePhase {
        if p.pieceCount - 2 <= 12 { return .endgame }
        return moveNumber <= 10 ? .opening : .middlegame
    }

    static func pawnShield(_ p: Placement, color: PieceColor) -> Int {
        guard let (kf, kr) = p.kingSquare(color) else { return 0 }
        let pawn: Character = color == .white ? "P" : "p"
        let dir = color == .white ? 1 : -1
        var n = 0
        for f in max(0, kf - 1)...min(7, kf + 1) { for step in 1...2 where p.piece(f, kr + dir * step) == pawn { n += 1 } }
        return n
    }

    static func analyze(_ input: MoveAnalysisInput) -> MoveAnalysis {
        let r = input.review, me = r.player
        let beforeValue = input.beforeResult.value
        let loss = r.loss
        let acc = Accuracy.moveAccuracy(valueBefore: beforeValue, valueAfter: beforeValue - loss)
        let after = r.before.applying(r.played)
        let ph = phase(of: r.before, moveNumber: input.moveNumber)
        var obs: [SkillObservation] = []

        let hangingBefore = Set(r.before.hanging(of: me).map(\.square))
        let hangingAfter = after.hanging(of: me)
        let newHanging = hangingAfter.contains { !hangingBefore.contains($0.square) }
        if !hangingBefore.isEmpty || !hangingAfter.isEmpty {
            obs.append(SkillObservation(skill: .pieceSafety, value: loss >= 50 && !hangingAfter.isEmpty ? 0 : 1))
        }

        let threat = input.threatResult.flatMap { Threats.detect(board: r.before, currentValue: beforeValue, opponent: $0) }
        if threat != nil { obs.append(SkillObservation(skill: .threats, value: loss < 50 ? 1 : 0)) }

        let lines = input.beforeResult.lines
        let tacticAvailable = lines.count >= 2 && lines[0].value - lines[1].value >= 150 && !r.before.isInCheck(me)
        let foundBest = r.best?.uci == r.played.uci
        if tacticAvailable { obs.append(SkillObservation(skill: .tactics, value: foundBest ? 1 : 0)) }

        let accValue = acc / 100
        if abs(Accuracy.winChance(cp: beforeValue) - 50) <= 45 {
            switch ph {
            case .opening: obs.append(SkillObservation(skill: .opening, value: accValue))
            case .middlegame: obs.append(SkillObservation(skill: .middlegame, value: accValue))
            case .endgame: obs.append(SkillObservation(skill: .endgame, value: accValue))
            }
        }

        let movedPiece = r.before.piece(r.played.fromFile, r.played.fromRank)
        let kind = movedPiece.map { Character($0.lowercased()) }
        let castled = kind == "k" && abs(r.played.toFile - r.played.fromFile) == 2
        var weakened = false
        if kind == "p", let (kf, kr) = r.before.kingSquare(me), abs(r.played.fromFile - kf) <= 1,
           abs(r.played.fromRank - kr) <= 2, pawnShield(after, color: me) < pawnShield(r.before, color: me) { weakened = true }
        let allowsMate = (input.afterResult?.mateIn ?? 0) > 0
        if castled { obs.append(SkillObservation(skill: .kingSafety, value: 1)) }
        else if weakened { obs.append(SkillObservation(skill: .kingSafety, value: loss >= 100 ? 0 : 1)) }
        else if allowsMate && loss >= 100 { obs.append(SkillObservation(skill: .kingSafety, value: 0)) }

        var mistake: MistakeType?
        if loss > 60 && Accuracy.winChance(cp: beforeValue) - Accuracy.winChance(cp: beforeValue - loss) >= MoveRating.inaccuracyDrop {
            if newHanging && loss >= 100 { mistake = .hangingPiece }
            else if threat != nil && loss >= 50 { mistake = .missedThreat }
            else if tacticAvailable && !foundBest { mistake = .missedTactic }
            else if (weakened || allowsMate) && loss >= 100 { mistake = .kingSafety }
            else if ph == .opening { mistake = .openingPrinciple }
            else if ph == .endgame { mistake = .endgameTechnique }
            else { mistake = .other }
        }
        var theme: HintTheme?
        if let best = r.best {
            theme = Hints.theme(placement: r.before, best: best, player: me, threat: threat, bestMate: (lines.first?.mateIn ?? 0) > 0, pv: lines.first?.pv ?? [], value: lines.first?.value ?? 0)
        }
        let lossText = String(format: "%.1f", locale: Loc.display.locale, Double(loss) / 100)
        return MoveAnalysis(observations: obs, mistakeType: mistake,
                            example: mistake == nil ? nil : Loc.display("mistake.example", "\(r.playedText()) (loss \(lossText))"),
                            theme: theme, threat: threat, accuracy: acc, loss: loss, phase: ph)
    }
}
