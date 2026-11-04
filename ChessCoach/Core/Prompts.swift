import Foundation

extension Coach {
    struct ExplanationRequest {
        let system: String
        let user: String
        let maxTokens: Int
        var approxTokens: Int { (system.count + user.count) / 4 }
    }

    static let systemPrompt = """
    You are a patient chess coach for a hobby player. You receive facts computed by a chess engine. \
    They are correct: rely only on them, never invent moves, variations or threats, and do not calculate yourself. \
    Leave out what the facts do not support. Explain the idea behind the move, not every move one by one. \
    Name pieces and squares as in the facts, e.g. "Knight f3". Address the player as "you", with no heading and no bullet list. \
    The message ends with a style card: follow its length, depth, tone and language strictly and respect the word limit. \
    Write a line "Takeaway: ..." only if the style card lists "takeaway", using the label given there.
    """

    static let hintSystemPrompt = """
    You are a patient chess coach. The player asked for a hint, not for the solution. \
    You receive facts computed by a chess engine. They are correct. \
    Write exactly one sentence of at most 30 words that hints at the idea of the best move. \
    Do not name the target square or the move, give no move notation and do not reveal which piece is captured. \
    Invent nothing that is not in the facts. Address the player as "you".
    """

    static let questionSystemPrompt = """
    You are a patient chess coach for a hobby player. You receive facts about the current position, \
    computed by a chess engine, and a question from the player. The facts are correct. \
    Answer the question in simple words and rely only on these facts. Do not invent moves, variations or threats. \
    If the answer is not in the facts, say so honestly. Do not name a best move unless the facts contain one. \
    At most 120 words, no heading, no takeaway.
    """

    static let summarySystemPrompt = """
    You are a patient chess coach. You receive the evaluation of a finished game, computed by a chess engine. \
    The numbers and moves are correct: rely only on them and invent nothing. \
    Write a short summary (2 to 3 sentences), then exactly three lessons as numbered lines, \
    each naming a general principle the player can apply in the next game. Address the player as "you".
    """

    static func tone(for level: PlayerLevel) -> String {
        switch level {
        case .beginner: "Tone: simple words, explain basics."
        case .intermediate: "Tone: clear, friendly."
        case .strong: "Tone: technical terms fine."
        }
    }

    static func styleCard(context: PlayerContext, style: ExplanationStyle, language: String = "English") -> String {
        var card = "Player: \(context.level.title(.facts))"
        if let f = context.focus { card += ", focus \(f.title(.facts))" }
        if let m = context.typicalMistakes.first { card += ", mistake: \(m)" }
        card += ". Style: \(style.length.rawValue), \(style.depth == .principle ? "principle" : "concrete+variation")"
        card += ", max \(style.length.maxWords) words"
        if style.length == .long { card += ", takeaway" }
        if style.examples { card += ", example" }
        card += ". " + (style.tone == "auto" ? tone(for: context.level) : "Tone: \(style.tone).")
        if let f = context.focus, style.length != .short { card += " " + f.promptHint }
        return card + " " + answerIn(language, takeaway: style.length == .long)
    }

    static func answerIn(_ language: String, takeaway: Bool = false, _ loc: Loc = .display) -> String {
        var text = "Answer in \(language)."
        guard language != "English" else { return text }
        let terms = zip(["king", "queen", "rook", "bishop", "knight", "pawn"], "kqrbnp").map { "\($0)=\(PieceInfo.name($1, loc))" }
        text += " Use exactly: " + terms.joined(separator: ", ")
        if takeaway { text += "; label=\(loc("hint.takeawayLabel", "Takeaway"))" }
        return text + "."
    }

    static func suggestionRequest(placement: Placement, player me: PieceColor, fen: String, result: EngineResult,
                                  context: PlayerContext, style: ExplanationStyle, language: String = "English") -> ExplanationRequest? {
        guard let first = result.lines.first, let best = UCIMove(first.move) else { return nil }
        let name = me.title(.facts)
        var facts = [
            "Position (FEN): \(fen)",
            "\(name) to move. All evaluations are from \(name)'s point of view.",
            "Best move per engine: \(placement.describeMove(best, .facts)) (evaluation: \(first.score(.facts)))",
            "Expected continuation: \(placement.describeLine(first.pv, maxMoves: 4, .facts))",
        ]
        for (i, line) in result.lines.dropFirst().enumerated() {
            guard let m = UCIMove(line.move) else { continue }
            facts.append("Alternative \(i + 1): \(placement.describeMove(m, .facts)) (evaluation: \(line.score(.facts)))")
        }
        facts += tacticFacts(before: placement, move: best, me: me, label: "The best move")
        return request(facts, ask: "Explain to the \(name) player why the best move is good.", context: context, style: style, language: language)
    }

    static func reviewRequest(_ r: MoveReview, context: PlayerContext, style: ExplanationStyle, language: String = "English") -> ExplanationRequest {
        let name = r.player.title(.facts), opponent = r.player.opposite.title(.facts)
        var facts = [
            "Position before the move (FEN): \(r.before.fen(sideToMove: r.player))",
            "\(name) moved. All evaluations are from \(name)'s point of view.",
            "Played move: \(r.playedText(.facts)). Evaluation after it: \(r.playedScore.text(.facts)).",
            "Rating: \(r.rating.title(.facts)) (loss against the best move: \(String(format: "%.1f", Double(r.loss) / 100)) pawns).",
        ]
        if let best = r.bestText(.facts), r.rating != .best { facts.append("Better was: \(best) (evaluation: \(r.bestScore.text(.facts))).") }
        let reply = r.replyLine(.facts)
        if !reply.isEmpty { facts.append("Best reply for \(opponent): \(reply)") }
        let hanging = r.before.applying(r.played).hangingPieces(of: r.player, .facts)
        if !hanging.isEmpty { facts.append("After the move, \(name) has: \(hanging.joined(separator: "; ")).") }
        if let best = r.best, r.rating != .best { facts += tacticFacts(before: r.before, move: best, me: r.player, label: "The better move") }
        let good = r.rating == .best || r.rating == .good
        let ask = good ? "Explain to the \(name) player why their move was good."
                       : "Explain to the \(name) player why their move was weaker and what idea the better move had."
        return request(facts, ask: ask, context: context, style: style, language: language)
    }

    static func hintRequest(_ hint: HintSet, context: PlayerContext, language: String = "English") -> ExplanationRequest {
        ExplanationRequest(system: hintSystemPrompt, user: "\(hint.stage3Prompt)\n\(tone(for: context.level)) \(answerIn(language))", maxTokens: 80)
    }

    static func questionRequest(placement: Placement, toMove: PieceColor, question: String, score: String? = nil, bestMove: String? = nil,
                                threat: String? = nil, language: String = "English") -> ExplanationRequest {
        let name = toMove.title(.facts)
        var facts = [
            "Position (FEN): \(placement.fen(sideToMove: toMove))",
            "\(name) to move. All evaluations are from \(name)'s point of view.",
            "\(name) pieces: \(placement.pieceList(toMove))",
            "\(toMove.opposite.title(.facts)) pieces: \(placement.pieceList(toMove.opposite))",
        ]
        if let score { facts.append("Evaluation of the position: \(score)") }
        if let bestMove { facts.append("Best move per engine: \(bestMove)") }
        if let threat { facts.append(threat) }
        for c in [toMove, toMove.opposite] {
            let hanging = placement.hangingPieces(of: c, .facts)
            if !hanging.isEmpty { facts.append("Unprotected or threatened for \(c.title(.facts)): \(hanging.joined(separator: "; "))") }
        }
        return ExplanationRequest(system: questionSystemPrompt, user: facts.joined(separator: "\n") + "\n\nPlayer's question: \(question)\n\(answerIn(language))", maxTokens: 260)
    }

    private static func request(_ facts: [String], ask: String, context: PlayerContext, style: ExplanationStyle, language: String) -> ExplanationRequest {
        ExplanationRequest(system: systemPrompt,
                           user: facts.joined(separator: "\n") + "\n\n" + ask + "\n" + styleCard(context: context, style: style, language: language),
                           maxTokens: style.maxTokens)
    }

    private static func tacticFacts(before: Placement, move: UCIMove, me: PieceColor, label: String) -> [String] {
        let after = before.applying(move)
        var facts: [String] = []
        if let victim = before.piece(move.toFile, move.toRank) {
            facts.append("\(label) captures \(PieceInfo.name(victim, .facts)) \(UCIMove.squareName(move.toFile, move.toRank)).")
        }
        let targets = after.attacks(file: move.toFile, rank: move.toRank).compactMap { f, r -> String? in
            guard let p = after.piece(f, r), Placement.color(of: p) != me else { return nil }
            return "\(PieceInfo.name(p, .facts)) \(UCIMove.squareName(f, r))"
        }
        if !targets.isEmpty { facts.append("\(label) then attacks: \(targets.joined(separator: ", ")).") }
        for color in [me, me.opposite] {
            let hanging = before.hangingPieces(of: color, .facts)
            if !hanging.isEmpty { facts.append("Before the move, \(color.title(.facts)) has: \(hanging.joined(separator: "; ")).") }
        }
        if after.isInCheck(me.opposite) { facts.append("\(label) gives check.") }
        return facts
    }
}
