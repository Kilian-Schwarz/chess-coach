import Foundation

extension Coach {
    struct ExplanationRequest {
        let system: String
        let user: String
        let maxTokens: Int
        var approxTokens: Int { (system.count + user.count) / 4 }
    }

    static let systemPrompt = """
    You are a patient chess coach talking directly to a hobby player who sits at the board. \
    Everything in the message about the position, the moves and the evaluations comes from a chess engine and is correct: \
    rely only on it, never invent moves, variations or threats, and do not calculate yourself. \
    Speak as the coach who sees the board yourself; never refer to notes, lists, engine output or material you were given. \
    Leave out what you cannot support. State the concrete purpose of a move from the lines; if it has none there, say in one short sentence that it keeps the advantage or the position sound, \
    and never list generic benefits such as covering squares, protecting pawns or king safety. \
    Say that a move gives check only if the message says so. Explain the idea behind the move, not every move one by one. \
    Name pieces and squares exactly as the message and the answer-language line write them. Address the player as "you", with no heading and no bullet list. \
    The message ends with a style card: follow its length, depth, tone and language strictly and respect the word limit. \
    Write a line "Takeaway: ..." only if the style card lists "takeaway", using the label given there.
    """

    static let hintSystemPrompt = """
    You are a patient chess coach. The player asked for a hint, not for the solution. \
    Everything in the message comes from a chess engine and is correct. \
    Write one or two sentences of at most 40 words in total that tell the player exactly what to look for on the board: \
    name the piece that should move, the file, rank or area it should head for, and the purpose with the concrete pieces and squares \
    named in the message (what it attacks, protects or frees). \
    Do not name the exact destination square or the move, give no move notation and do not reveal which piece is captured. \
    Invent nothing beyond what the message states. Address the player as "you".
    """

    static let questionSystemPrompt = """
    You are a patient chess coach talking directly to a hobby player about the position on the board. \
    The message describes the position as a chess engine sees it: evaluation, best line, the opponent's threat and what it would win, \
    pins, loose pieces, checks, and what the player's current hint shows. All of it is correct. \
    Use it, never invent moves, variations or threats, and speak as the coach who sees the board yourself: \
    never refer to notes, lists, engine output or material you were given. \
    If the player asks why, answer with the concrete reason in simple words: which piece is pinned, what is threatened and what it would cost, \
    what a move frees or stops. Say that a move gives check only if the message says so. Follow the hint rule in the message. \
    If the position truly gives no reason, say so briefly in your own words. Address the player as "you", with no heading and no takeaway. \
    Respect the word limit and the language in the last lines.
    """

    static let summarySystemPrompt = """
    You are a patient chess coach. You receive the evaluation of a finished game, computed by a chess engine. \
    The numbers and moves are correct: rely only on them and invent nothing. \
    Write a short summary (2 to 3 sentences). If takeaways are listed, restate exactly those as numbered lines in friendly words \
    and add no others; if none are listed, write no lessons. Address the player as "you".
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
        facts += moveEffects(before: r.before, move: r.played, me: r.player, label: "The played move")
        let hanging = r.before.applying(r.played).hangingPieces(of: r.player, .facts)
        if !hanging.isEmpty { facts.append("After the move, \(name) has: \(hanging.joined(separator: "; ")).") }
        if let best = r.best, r.rating != .best { facts += tacticFacts(before: r.before, move: best, me: r.player, label: "The better move") }
        let good = r.rating == .best || r.rating == .good
        let ask = good ? "Explain to the \(name) player why their move was good."
                       : "Explain to the \(name) player why their move was weaker and what idea the better move had."
        return request(facts, ask: ask, context: context, style: style, language: language)
    }

    static func hintRequest(_ hint: HintSet, context: PlayerContext, language: String = "English") -> ExplanationRequest {
        ExplanationRequest(system: hintSystemPrompt, user: "\(hint.stage3Prompt)\n\(tone(for: context.level)) \(answerIn(language))", maxTokens: 110)
    }

    static func questionRequest(grounding: PositionFacts.Grounding, question: String, context: PlayerContext, style: ExplanationStyle,
                                language: String = "English") -> ExplanationRequest {
        var capped = style
        if capped.length == .long { capped.length = .medium }
        return ExplanationRequest(system: questionSystemPrompt,
                                  user: grounding.lines.joined(separator: "\n") + "\n\nPlayer's question: \(question)\n"
                                      + styleCard(context: context, style: capped, language: language),
                                  maxTokens: max(capped.maxTokens, 160))
    }

    private static func request(_ facts: [String], ask: String, context: PlayerContext, style: ExplanationStyle, language: String) -> ExplanationRequest {
        ExplanationRequest(system: systemPrompt,
                           user: facts.joined(separator: "\n") + "\n\n" + ask + "\n" + styleCard(context: context, style: style, language: language),
                           maxTokens: style.maxTokens)
    }

    static func moveEffects(before: Placement, move: UCIMove, me: PieceColor, label: String) -> [String] {
        let after = before.applying(move)
        var facts: [String] = []
        if let victim = before.piece(move.toFile, move.toRank) {
            facts.append("\(label) captures \(PieceInfo.name(victim, .facts)) \(UCIMove.squareName(move.toFile, move.toRank)).")
        }
        let targets = after.attacks(file: move.toFile, rank: move.toRank).compactMap { f, r -> String? in
            guard let p = after.piece(f, r), Placement.color(of: p) != me else { return nil }
            return "\(PieceInfo.name(p, .facts)) \(UCIMove.squareName(f, r))"
        }
        let mover = after.piece(move.toFile, move.toRank) ?? "p"
        let name = "\(PieceInfo.name(mover, .facts)) \(UCIMove.squareName(move.toFile, move.toRank))"
        facts.append(targets.isEmpty ? "After \(label.lowercased()), the \(name) attacks no enemy piece directly."
                                     : "\(label) then attacks directly: \(targets.joined(separator: ", ")).")
        for x in after.xrays(file: move.toFile, rank: move.toRank) {
            facts.append("The \(x.slider.text(.facts)) only x-rays \(x.target.text(.facts)) through its own \(x.blocker.text(.facts)); it does not attack it while that piece stays.")
        }
        for d in before.discoveredTargets(by: move, color: me) {
            facts.append("Moving the \(d.blocker.text(.facts)) uncovers an attack by \(d.slider.text(.facts)) on \(d.target.text(.facts)).")
        }
        for pin in before.unpinned(by: move, color: me) {
            facts.append("It ends the pin on \(PieceInfo.name(pin.piece, .facts)) \(pin.square).")
        }
        for pin in before.createdPins(by: move, against: me.opposite) { facts.append("It creates a pin: \(pin.text(.facts)).") }
        if after.isInCheck(me.opposite) { facts.append("\(label) gives check.") }
        return facts
    }

    static func tacticFacts(before: Placement, move: UCIMove, me: PieceColor, label: String) -> [String] {
        var facts = moveEffects(before: before, move: move, me: me, label: label)
        for color in [me, me.opposite] {
            let hanging = before.hangingPieces(of: color, .facts)
            if !hanging.isEmpty { facts.append("Before the move, \(color.title(.facts)) has: \(hanging.joined(separator: "; ")).") }
        }
        return facts
    }
}
