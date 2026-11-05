import Foundation

extension Score {
    var centipawns: Int {
        switch self {
        case .cp(let v): v
        case .mate(let m): m > 0 ? 10_000 - m * 10 : -10_000 - m * 10
        case .checkmate: 10_000
        case .stalemate: 0
        }
    }
}

struct PuzzleLesson: Equatable {
    var why: String
    var habit: String
}

struct ThemeProgress: Equatable {
    let theme: HintTheme
    let solved: Int
    let total: Int

    func text(_ loc: Loc = .display) -> String { loc("puzzle.progress", "\(theme.title(loc)): \(solved)/\(total) solved") }
}

enum PuzzleCoach {
    static func theme(_ p: Puzzle) -> HintTheme { HintTheme(rawValue: p.theme) ?? .improvePiece }

    static func needsThemeCheck(_ p: Puzzle) -> Bool { p.themeChecked != true }

    static func bestMove(_ p: Puzzle) -> UCIMove? { (p.bestLine.first ?? p.acceptedMoves.first).flatMap { UCIMove($0) } }

    static func classify(_ p: Puzzle, result: EngineResult) -> HintTheme? {
        guard let pos = ChessPosition(fen: p.fen), let best = bestMove(p) else { return nil }
        let top = result.lines.first
        let pv = p.bestLine.isEmpty ? (top?.pv ?? []) : p.bestLine
        return Hints.theme(placement: pos.board, best: best, player: pos.turn, bestMate: (top?.mateIn ?? 0) > 0, pv: pv, value: top?.value ?? 0)
    }

    static func source(_ p: Puzzle, _ loc: Loc = .display) -> String {
        let date = (p.sourceDate ?? p.createdAt).formatted(date: .abbreviated, time: .omitted)
        guard let number = p.sourceMove, let value = p.playedValue, let played = p.playedMove.flatMap({ UCIMove($0) }),
              let pos = ChessPosition(fen: p.fen), pos.isLegal(played) else {
            return loc("puzzle.source.date", "From your game on \(date).")
        }
        let san = SAN.localize(SAN.san(played, in: pos))
        return loc("puzzle.source.move", "From your game on \(date), move \(number): you played \(san) there (\(evaluation(value))).")
    }

    static func evaluation(_ cp: Int) -> String {
        if abs(cp) >= 9_000 { return cp > 0 ? "+M" : "−M" }
        return String(format: "%+.1f", Double(cp) / 100).replacingOccurrences(of: "-", with: "−")
    }

    static func question(_ t: HintTheme, _ loc: Loc = .display) -> String {
        switch t {
        case .mate: loc("puzzle.ask.mate", "The enemy king looks short of safe squares. Which checks could end the game?")
        case .captureGain: loc("puzzle.ask.captureGain", "Your opponent's last move left something undefended. What can you attack?")
        case .fork: loc("puzzle.ask.fork", "Two enemy pieces stand on squares one piece can reach. Can one move attack both?")
        case .check: loc("puzzle.ask.check", "Which checks are available, and what does each one force?")
        case .promotion: loc("puzzle.ask.promotion", "A pawn is close to the last rank. What stops it, and can you clear the way?")
        case .discovered: loc("puzzle.ask.discovered", "One of your pieces stands in front of a rook, bishop or queen. What if it moves with a threat?")
        case .pin: loc("puzzle.ask.pin", "Is an enemy piece lined up with something more valuable behind it?")
        case .unpin: loc("puzzle.ask.unpin", "One of your pieces is tied down. How do you set it free?")
        case .parryThreat: loc("puzzle.ask.parryThreat", "What is your opponent threatening right now, and how do you stop it?")
        case .castle: loc("puzzle.ask.castle", "Your king is still in the centre. What is the safest way to fix that?")
        case .develop: loc("puzzle.ask.develop", "Which of your pieces is still at home and could join the game?")
        case .kingActivate: loc("puzzle.ask.kingActivate", "Few pieces are left. Where can your king help most?")
        case .pawnMove: loc("puzzle.ask.pawnMove", "Which pawn move gains space or opens a line for your pieces?")
        case .improvePiece: loc("puzzle.ask.improvePiece", "Which of your pieces does the least? Where would it do more?")
        case .convert: loc("puzzle.ask.convert", "You are ahead. How do you cash in without allowing counterplay?")
        }
    }

    static func habit(_ t: HintTheme, _ loc: Loc = .display) -> String {
        switch t {
        case .mate: loc("puzzle.habit.mate", "Before every move, list all checks, then all captures, then all threats.")
        case .captureGain: loc("puzzle.habit.captureGain", "Before moving, ask which enemy pieces are undefended or attacked more often than defended.")
        case .fork: loc("puzzle.habit.fork", "Look for squares from which one piece hits two targets, especially with a knight.")
        case .check: loc("puzzle.habit.check", "Check every check. A forcing move often decides before the opponent can react.")
        case .promotion: loc("puzzle.habit.promotion", "Count the squares to promotion and who controls them.")
        case .discovered: loc("puzzle.habit.discovered", "Before moving, check pieces standing in front of your long-range pieces.")
        case .pin: loc("puzzle.habit.pin", "Look along lines for an enemy piece with a more valuable piece behind it.")
        case .unpin: loc("puzzle.habit.unpin", "Check whether one of your pieces is pinned before you plan anything else.")
        case .parryThreat: loc("puzzle.habit.parryThreat", "Ask what your opponent wants to do next before you play your own plan.")
        case .castle: loc("puzzle.habit.castle", "Bring the king to safety early, then use the rook.")
        case .develop: loc("puzzle.habit.develop", "Bring every piece into play before you start a second plan.")
        case .kingActivate: loc("puzzle.habit.kingActivate", "In the endgame, bring the king toward the action.")
        case .pawnMove: loc("puzzle.habit.pawnMove", "Before a pawn move, check what it opens for you and for your opponent.")
        case .improvePiece: loc("puzzle.habit.improvePiece", "Find your worst piece and improve it.")
        case .convert: loc("puzzle.habit.convert", "When ahead, trade pieces and keep the king safe instead of looking for fireworks.")
        }
    }

    static func displayLine(_ text: String) -> String {
        text.split(separator: " ").map { token -> String in
            let s = String(token)
            guard let dot = s.lastIndex(of: ".") else { return SAN.localize(s) }
            let head = s[...dot]
            return head + SAN.localize(String(s[s.index(after: dot)...]))
        }.joined(separator: " ")
    }

    static func mechanism(_ p: Puzzle, position pos: ChessPosition, best: UCIMove, _ loc: Loc = .display) -> String? {
        let me = pos.turn
        switch theme(p) {
        case .discovered:
            guard let x = pos.board.discoveredTargets(by: best, color: me).first else { return nil }
            return loc("puzzle.why.discovered", "\(x.blocker.text(loc)) moves away, so \(x.slider.text(loc)) attacks \(x.target.text(loc)).")
        case .pin:
            guard let pin = pos.board.createdPins(by: best, against: me.opposite).first else { return nil }
            let pinned = PieceAt(piece: pin.piece, square: pin.square), behind = PieceAt(piece: pin.target, square: pin.targetSquare)
            return loc("puzzle.why.pin", "\(pinned.text(loc)) is now pinned in front of \(behind.text(loc)).")
        case .fork:
            let targets = forkTargets(pos, best)
            guard targets.count >= 2, let mover = pos.board.piece(best.fromFile, best.fromRank) else { return nil }
            let list = targets.prefix(3).map { $0.text(loc) }.joined(separator: ", ")
            return loc("puzzle.why.fork", "\(PieceAt(piece: mover, square: UCIMove.squareName(best.toFile, best.toRank)).text(loc)) attacks \(list) at once.")
        default:
            return nil
        }
    }

    static func forkTargets(_ pos: ChessPosition, _ best: UCIMove) -> [PieceAt] {
        guard let mover = pos.board.piece(best.fromFile, best.fromRank) else { return [] }
        let after = pos.board.applying(best), opp = pos.turn.opposite
        let defenders = after.attackers(of: opp)
        return after.attacks(file: best.toFile, rank: best.toRank).compactMap { f, r in
            guard let t = after.piece(f, r), Placement.color(of: t) == opp else { return nil }
            let big = Character(t.lowercased()) == "k" || PieceInfo.value(t) > PieceInfo.value(mover) || defenders[r][f].isEmpty
            return big ? PieceAt(piece: t, square: UCIMove.squareName(f, r)) : nil
        }
    }

    static func lesson(_ p: Puzzle, _ loc: Loc = .display) -> PuzzleLesson {
        let t = theme(p)
        var parts: [String] = []
        if let pos = ChessPosition(fen: p.fen), let best = bestMove(p) {
            if let m = mechanism(p, position: pos, best: best, loc) { parts.append(m) }
            let line = p.bestLine.isEmpty ? [best.uci] : p.bestLine
            if let o = PositionFacts.outcome(pv: line, from: pos, me: pos.turn) {
                let text = displayLine(o.line)
                if o.line.hasSuffix("#") { parts.append(loc("puzzle.why.mate", "Main line: \(text). That is checkmate.")) }
                else if o.delta > 0 { parts.append(loc("puzzle.why.gain", "Main line: \(text). You win \(o.delta) points of material.")) }
                else { parts.append(loc("puzzle.why.line", "Main line: \(text).")) }
            }
        }
        return PuzzleLesson(why: parts.joined(separator: " "), habit: habit(t, loc))
    }

    static func hint3(_ h: HintSet, position pos: ChessPosition, _ loc: Loc = .display) -> String {
        let area = areaClause(h, position: pos, loc)
        guard let area, !Hints.revealsTarget(area, move: h.move) else { return h.stage3Fallback }
        return h.stage3Fallback + " " + area
    }

    static func areaClause(_ h: HintSet, position pos: ChessPosition, _ loc: Loc) -> String? {
        let best = h.move, me = pos.turn
        let dest = UCIMove.squareName(best.toFile, best.toRank)
        if h.theme == .discovered, let x = pos.board.discoveredTargets(by: best, color: me).first(where: { $0.target.square != dest }) {
            return loc("puzzle.area.discovered", "Behind it, \(x.slider.text(loc)) then attacks \(x.target.text(loc)).")
        }
        let after = pos.board.applying(best)
        let enemy = after.attacks(file: best.toFile, rank: best.toRank).compactMap { f, r -> String? in
            guard let t = after.piece(f, r), Placement.color(of: t) != me else { return nil }
            return PieceAt(piece: t, square: UCIMove.squareName(f, r)).text(loc)
        }
        if !enemy.isEmpty { return loc("puzzle.area.attacks", "From its new square it attacks: \(enemy.prefix(3).joined(separator: ", ")).") }
        guard let piece = pos.board.piece(best.fromFile, best.fromRank), !Hints.isCastling(best, piece: piece) else { return nil }
        if best.fromFile == best.toFile { return loc("puzzle.area.rank", "It heads for rank \(best.toRank + 1).") }
        return loc("puzzle.area.file", "It heads for the \(Array("abcdefgh")[best.toFile])-file.")
    }

    static func failReason(_ p: Puzzle, after: ChessPosition, refutation pv: [String], _ loc: Loc = .display) -> String {
        let me = after.turn.opposite
        if let o = PositionFacts.outcome(pv: pv, from: after, me: me) {
            if o.line.hasSuffix("#") { return loc("puzzle.fail.mate", "That allows checkmate.") }
            if o.delta < 0, let c = o.captures.first(where: { $0.by != me }) {
                return loc("puzzle.fail.loss", "That loses \(PieceInfo.name(c.victim, loc)) on \(c.square).")
            }
        }
        return loc("puzzle.fail.miss", "It misses the idea here: \(theme(p).title(loc)).")
    }

    static func progress(_ puzzles: [Puzzle]) -> [ThemeProgress] {
        let order = HintTheme.allCases
        return Dictionary(grouping: puzzles) { theme($0) }.map { t, list in
            ThemeProgress(theme: t, solved: list.filter { $0.mastered || (!$0.isNew && $0.box >= 2) }.count, total: list.count)
        }.sorted { a, b in
            a.total != b.total ? a.total > b.total : order.firstIndex(of: a.theme)! < order.firstIndex(of: b.theme)!
        }
    }

    static func whyRequest(_ p: Puzzle, context: PlayerContext, style: ExplanationStyle, language: String = "English") -> Coach.ExplanationRequest? {
        guard let pos = ChessPosition(fen: p.fen), let best = bestMove(p) else { return nil }
        let line = p.bestLine.isEmpty ? [best.uci] : p.bestLine
        let me = pos.turn.title(.facts)
        var facts = [
            "Position (FEN): \(p.fen)",
            "\(me) to move. This is a training puzzle; the player just solved it.",
            "Theme: \(theme(p).title(.facts)).",
            "Best move: \(pos.board.describeMove(best, .facts)).",
        ]
        if let m = mechanism(p, position: pos, best: best, .facts) { facts.append(m) }
        if let c = PositionFacts.consequence(pv: line, from: pos, me: pos.turn) { facts.append("Best line: " + c.text) }
        var capped = style
        if capped.length == .long { capped.length = .medium }
        let ask = "Explain to the \(me) player in plain words why the best move works and what it wins. Do not list the moves again."
        return Coach.ExplanationRequest(system: Coach.systemPrompt,
                                        user: facts.joined(separator: "\n") + "\n\n" + ask + "\n" + Coach.styleCard(context: context, style: capped, language: language),
                                        maxTokens: capped.maxTokens)
    }
}
