import Foundation

enum HintVariant { case standard, forcedMate, line }

enum HintTheme: String, Codable, CaseIterable {
    case mate, promotion, discovered, captureGain, fork, check, pin, unpin, parryThreat, castle, develop, kingActivate, pawnMove, improvePiece, convert

    func title(_ loc: Loc = .display) -> String {
        switch self {
        case .mate: loc("theme.mate", "Mate")
        case .captureGain: loc("theme.captureGain", "Material gain")
        case .fork: loc("theme.fork", "Fork")
        case .check: loc("theme.check", "Check")
        case .promotion: loc("theme.promotion", "Promotion")
        case .discovered: loc("theme.discovered", "Discovered attack")
        case .pin: loc("theme.pin", "Pin")
        case .unpin: loc("theme.unpin", "Break the pin")
        case .parryThreat: loc("theme.parryThreat", "Parry the threat")
        case .castle: loc("theme.castle", "Castling")
        case .develop: loc("theme.develop", "Develop a piece")
        case .kingActivate: loc("theme.kingActivate", "Activate the king")
        case .pawnMove: loc("theme.pawnMove", "Pawn move")
        case .improvePiece: loc("theme.improvePiece", "Improve a piece")
        case .convert: loc("theme.convert", "Convert your advantage")
        }
    }

    func stage1Text(_ loc: Loc = .display, variant: HintVariant = .standard) -> String {
        switch self {
        case .mate where variant == .forcedMate: loc("hint.stage1.mateForced", "You have a forced mate.")
        case .mate: loc("hint.stage1.mate", "You can checkmate your opponent here.")
        case .captureGain: loc("hint.stage1.captureGain", "You can win material here.")
        case .fork: loc("hint.stage1.fork", "Look for a fork: a move that attacks two pieces at once.")
        case .check: loc("hint.stage1.check", "A check is strong here.")
        case .promotion: loc("hint.stage1.promotion", "A pawn can promote here.")
        case .discovered: loc("hint.stage1.discovered", "Look for a discovered attack: moving one piece unmasks another.")
        case .pin: loc("hint.stage1.pin", "Look for a pin: a piece that cannot move without exposing a more valuable one.")
        case .unpin: loc("hint.stage1.unpin", "One of your pieces is pinned. Look for a way to free it.")
        case .parryThreat: loc("hint.stage1.parryThreat", "Watch out for your opponent's threat.")
        case .castle: loc("hint.stage1.castle", "Bring your king to safety.")
        case .develop: loc("hint.stage1.develop", "Develop a piece.")
        case .kingActivate: loc("hint.stage1.kingActivate", "Activate your king.")
        case .pawnMove: loc("hint.stage1.pawnMove", "A pawn move fits well here.")
        case .improvePiece: loc("hint.stage1.improvePiece", "Improve your worst piece.")
        case .convert: loc("hint.stage1.convert", "You are clearly ahead: trade pieces, attack the king or win more material.")
        }
    }
}

struct HintSet {
    let move: UCIMove
    let theme: HintTheme
    let threat: Threats.Threat?
    let stage1: String
    let stage2Square: String
    let stage2: String
    let stage3Fallback: String
    let stage3Prompt: String
    let stage4: String
    var variant = HintVariant.standard
    var opportunity = false

    func text(level: Int) -> String {
        switch level {
        case 1: stage1
        case 2: stage2
        case 3: stage3Fallback
        default: stage4
        }
    }
}

enum Hints {
    static func theme(placement: Placement, best: UCIMove, player: PieceColor, threat: Threats.Threat? = nil, bestMate: Bool = false,
                      pv: [String] = [], value: Int = 0) -> HintTheme {
        guard let mover = placement.piece(best.fromFile, best.fromRank) else { return .improvePiece }
        if bestMate { return .mate }
        if best.promotion != nil { return .promotion }
        if !placement.discoveredTargets(by: best, color: player).isEmpty { return .discovered }
        let kind = Character(mover.lowercased())
        let after = placement.applying(best)
        let opp = player.opposite
        let defenders = after.attackers(of: opp)
        if let victim = placement.piece(best.toFile, best.toRank), Placement.color(of: victim) == opp, Character(victim.lowercased()) != "k",
           PieceInfo.value(victim) > PieceInfo.value(mover) || defenders[best.toRank][best.toFile].isEmpty { return .captureGain }
        let forkTargets = after.attacks(file: best.toFile, rank: best.toRank).filter { f, r in
            guard let t = after.piece(f, r), Placement.color(of: t) == opp else { return false }
            return Character(t.lowercased()) == "k" || PieceInfo.value(t) > PieceInfo.value(mover) || defenders[r][f].isEmpty
        }.count
        if forkTargets >= 2, kind != "k" { return .fork }
        if after.isInCheck(opp) { return .check }
        if !placement.createdPins(by: best, against: opp).isEmpty { return .pin }
        if !placement.unpinned(by: best, color: player).isEmpty { return .unpin }
        if let threat, threat.isParried(by: best) { return .parryThreat }
        if kind == "k" && abs(best.toFile - best.fromFile) == 2 { return .castle }
        if !pv.isEmpty, let o = PositionFacts.outcome(pv: pv, from: ChessPosition(board: placement, turn: player, castling: .none), me: player), o.delta >= 2 {
            return .captureGain
        }
        if value >= 300 { return .convert }
        let nonKings = placement.pieceCount - 2
        if (kind == "n" || kind == "b") && best.fromRank == (player == .white ? 0 : 7) && nonKings > 12 { return .develop }
        if kind == "k" && nonKings <= 12 { return .kingActivate }
        return kind == "p" ? .pawnMove : .improvePiece
    }

    static func opportunityText(_ loc: Loc = .display) -> String {
        loc("threat.opportunity", "Your opponent has a threat, but you can win material yourself first.")
    }

    static func isCastling(_ move: UCIMove, piece: Character) -> Bool {
        Character(piece.lowercased()) == "k" && abs(move.toFile - move.fromFile) == 2
    }

    static func stage2Text(piece: Character, square: String, castling: Bool, _ loc: Loc = .display) -> String {
        castling ? loc("hint.stage2.castle", "Bring your king to safety: castle.") : stage2Text(piece: piece, square: square, loc)
    }

    static func stage2Text(piece: Character, square: String, _ loc: Loc = .display) -> String {
        switch Character(piece.lowercased()) {
        case "k": loc("hint.stage2.king", "Move the king on \(square).")
        case "q": loc("hint.stage2.queen", "Move the queen on \(square).")
        case "r": loc("hint.stage2.rook", "Move the rook on \(square).")
        case "b": loc("hint.stage2.bishop", "Move the bishop on \(square).")
        case "n": loc("hint.stage2.knight", "Move the knight on \(square).")
        default: loc("hint.stage2.pawn", "Move the pawn on \(square).")
        }
    }

    static func yourPiece(_ piece: Character, _ loc: Loc) -> String {
        switch Character(piece.lowercased()) {
        case "k": loc("piece.king.your", "Your king")
        case "q": loc("piece.queen.your", "Your queen")
        case "r": loc("piece.rook.your", "Your rook")
        case "b": loc("piece.bishop.your", "Your bishop")
        case "n": loc("piece.knight.your", "Your knight")
        default: loc("piece.pawn.your", "Your pawn")
        }
    }

    static func variant(theme: HintTheme, board: Placement, player: PieceColor, best: UCIMove) -> HintVariant {
        switch theme {
        case .mate:
            let next = ChessPosition(board: board, turn: player, castling: .none).applying(best)
            return next.isCheck && next.legalMoves().isEmpty ? .standard : .forcedMate
        case .captureGain: return board.piece(best.toFile, best.toRank) == nil ? .line : .standard
        default: return .standard
        }
    }

    static func stage3Fallback(theme: HintTheme, piece: Character, variant: HintVariant = .standard, _ loc: Loc = .display) -> String {
        let me = yourPiece(piece, loc)
        switch theme {
        case .mate where variant == .forcedMate: return loc("hint.stage3.mateForced", "\(me) starts a forced mate.")
        case .mate: return loc("hint.stage3.mate", "\(me) can checkmate your opponent.")
        case .captureGain where variant == .line: return loc("hint.stage3.captureLine", "\(me) can make a move that wins material a few moves later.")
        case .captureGain: return loc("hint.stage3.captureGain", "\(me) can capture an enemy piece and win material.")
        case .fork: return loc("hint.stage3.fork", "\(me) can attack two enemy pieces at once.")
        case .check: return loc("hint.stage3.check", "\(me) can give check, and that helps you.")
        case .promotion: return loc("hint.stage3.promotion", "\(me) can reach the last rank and become a queen.")
        case .discovered: return loc("hint.stage3.discovered", "When \(me.lowercasedFirst) moves, a piece behind it suddenly attacks something valuable.")
        case .pin: return loc("hint.stage3.pin", "\(me) can pin an enemy piece so that it is stuck in front of a more valuable one.")
        case .unpin: return loc("hint.stage3.unpin", "A piece of yours is pinned and cannot move freely. Find the move that frees it.")
        case .parryThreat: return loc("hint.stage3.parryThreat", "Your opponent is threatening something. \(me) can ward it off.")
        case .castle: return loc("hint.stage3.castle", "Your king is still in the centre. Castling brings it to safety and activates the rook.")
        case .develop: return loc("hint.stage3.develop", "\(me) is still on the back rank. Bring it to an active square where it controls more squares.")
        case .kingActivate: return loc("hint.stage3.kingActivate", "In the endgame the king is a strong piece. Bring it closer to the pawns and the centre.")
        case .pawnMove: return loc("hint.stage3.pawnMove", "A pawn move improves your position here: it gains space or opens lines for your pieces.")
        case .convert: return loc("hint.stage3.convert", "\(me) helps you cash in: trade pieces, go after the king or win more material.")
        case .improvePiece: return loc("hint.stage3.improvePiece", "\(me) is not placed well. Find a square where it does more.")
        }
    }

    static func revealsTarget(_ answer: String, move: UCIMove) -> Bool {
        answer.lowercased().contains(UCIMove.squareName(move.toFile, move.toRank))
    }

    static func outranksThreat(_ threat: Threats.Threat, position pos: ChessPosition, pv: [String]) -> Bool {
        guard (threat.mateIn ?? 0) <= 0, !threat.lost.isEmpty, let o = PositionFacts.outcome(pv: pv, from: pos, me: pos.turn) else { return false }
        return o.delta >= max(threat.lost.map { PieceInfo.value($0.piece) }.reduce(0, +), 2)
    }

    static func build(placement: Placement, player: PieceColor, result: EngineResult, threatResult: EngineResult? = nil,
                      themeOverride: HintTheme? = nil) -> HintSet? {
        guard let first = result.lines.first, let best = UCIMove(result.bestMove.isEmpty ? first.move : result.bestMove),
              let piece = placement.piece(best.fromFile, best.fromRank) else { return nil }
        let rawThreat = threatResult.flatMap { Threats.detect(board: placement, currentValue: result.value, opponent: $0) }
        let theme = themeOverride ?? theme(placement: placement, best: best, player: player, threat: rawThreat, bestMate: (first.mateIn ?? 0) > 0, pv: first.pv, value: first.value)
        let pos = ChessPosition(board: placement, turn: player, castling: .none)
        let opportunity = rawThreat.map { outranksThreat($0, position: pos, pv: first.pv) } ?? false
        let threat = opportunity ? nil : rawThreat
        let variant = variant(theme: theme, board: placement, player: player, best: best)
        let from = UCIMove.squareName(best.fromFile, best.fromRank)
        let me = player.title(.facts), score = first.score(.facts)
        var facts = [
            "Position (FEN): \(placement.fen(sideToMove: player))",
            "\(me) to move. Evaluation from \(me)'s point of view: \(score).",
            "Theme of the best move: \(theme.title(.facts)).",
            "The piece that should move: \(PieceInfo.name(piece, .facts)) on \(from).",
        ]
        if let threat {
            let line = threat.text(.facts)
            facts.append(line.hasSuffix(".") ? line : line + ".")
        }
        facts += PositionFacts.reasons(position: pos, best: best, threat: threat).filter { !$0.hasPrefix("The best move captures") }
        facts += PositionFacts.purposeLines(position: pos, best: best)
        let pins = placement.pins(of: player) + placement.pins(of: player.opposite)
        if !pins.isEmpty { facts.append("Pins: " + pins.map { $0.text(.facts) }.joined(separator: "; ") + ".") }
        let theirs = placement.hangingPieces(of: player.opposite, .facts), mine = placement.hangingPieces(of: player, .facts)
        if !theirs.isEmpty { facts.append("Hanging for the opponent: \(theirs.joined(separator: "; ")).") }
        if !mine.isEmpty { facts.append("Hanging for \(me): \(mine.joined(separator: "; ")).") }
        if placement.applying(best).isInCheck(player.opposite) { facts.append("The best move gives check.") }
        let ask = "\n\nGive the \(me) player a short, concrete hint (piece, file or rank, purpose) without naming the target square."
        return HintSet(move: best, theme: theme, threat: threat,
                       stage1: theme.stage1Text(variant: variant),
                       stage2Square: from, stage2: stage2Text(piece: piece, square: from, castling: isCastling(best, piece: piece)),
                       stage3Fallback: stage3Fallback(theme: theme, piece: piece, variant: variant),
                       stage3Prompt: facts.joined(separator: "\n") + ask,
                       stage4: Loc.display("hint.stage4", "Best move: \(placement.describeMove(best)) (evaluation \(first.score()))."),
                       variant: variant, opportunity: opportunity)
    }
}
