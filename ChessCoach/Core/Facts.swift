import Foundation

enum PositionFacts {
    struct Grounding {
        var lines: [String]
        var positions: [ChessPosition]
        var pvs: [[String]]
    }

    struct Played {
        let move: UCIMove
        let before: ChessPosition
    }

    static func sanLine(_ pv: [String], from start: ChessPosition, plies: Int) -> (text: String, played: [Played]) {
        var cur = start
        var parts: [String] = []
        var played: [Played] = []
        for (i, u) in pv.prefix(plies).enumerated() {
            guard let m = UCIMove(u), cur.isLegal(m) else { break }
            var token = SAN.san(m, in: cur)
            if cur.turn == .white { token = "\(cur.fullmoveNumber).\(token)" }
            else if i == 0 { token = "\(cur.fullmoveNumber)...\(token)" }
            parts.append(token)
            played.append(Played(move: m, before: cur))
            cur = cur.applying(m)
        }
        return (parts.joined(separator: " "), played)
    }

    static func capturedPiece(_ p: Played) -> (piece: Character, square: String)? {
        let m = p.move
        if let victim = p.before.board.piece(m.toFile, m.toRank) { return (victim, UCIMove.squareName(m.toFile, m.toRank)) }
        if p.before.isEnPassant(m), let pawn = p.before.board.piece(m.toFile, m.fromRank) { return (pawn, UCIMove.squareName(m.toFile, m.fromRank)) }
        return nil
    }

    struct Outcome {
        var played: [Played]
        var captures: [(by: PieceColor, victim: Character, square: String)]
        var delta: Int
        var line: String
    }

    static func outcome(pv: [String], from start: ChessPosition, me: PieceColor) -> Outcome? {
        var plies = min(pv.count, 4)
        var line = sanLine(pv, from: start, plies: plies)
        guard !line.played.isEmpty else { return nil }
        if let last = line.played.last, capturedPiece(last) != nil, pv.count > plies {
            plies = min(pv.count, 6)
            line = sanLine(pv, from: start, plies: plies)
        }
        var end = start
        for p in line.played { end = end.applying(p.move) }
        let captures = line.played.compactMap { p -> (by: PieceColor, victim: Character, square: String)? in
            capturedPiece(p).map { (p.before.turn, $0.piece, $0.square) }
        }
        let delta = (end.board.material(me) - end.board.material(me.opposite)) - (start.board.material(me) - start.board.material(me.opposite))
        return Outcome(played: line.played, captures: captures, delta: delta, line: line.text)
    }

    static func consequence(pv: [String], from start: ChessPosition, me: PieceColor) -> (text: String, delta: Int)? {
        guard let o = outcome(pv: pv, from: start, me: me) else { return nil }
        let captures = o.captures.map { "\($0.by.title(.facts)) captures \(Placement.color(of: $0.victim).title(.facts)) \(PieceInfo.name($0.victim, .facts)) \($0.square)" }
        let net = "Net material change for \(me.title(.facts)): \(String(format: "%+d", o.delta)) (pawn 1, knight or bishop 3, rook 5, queen 9)"
        let text = captures.isEmpty ? "No material changes hands in that line." : "In that line: \(captures.joined(separator: "; ")). \(net)."
        return (o.line + ". " + text, o.delta)
    }

    static func threatLines(position pos: ChessPosition, nullResult: EngineResult?) -> [String] {
        guard !pos.isCheck, let line = nullResult?.lines.first else { return [] }
        let me = pos.turn, opponent = me.opposite
        let nullPos = pos.nullMove
        guard let first = line.pv.first.flatMap({ UCIMove($0) }), nullPos.isLegal(first),
              let result = consequence(pv: line.pv, from: nullPos, me: me) else { return [] }
        var out: [String] = []
        let afterFirst = nullPos.board.applying(first)
        let known = Set(pos.board.hanging(of: me).map(\.square))
        let pinsAfter = afterFirst.pins(of: me)
        for h in afterFirst.hanging(of: me) where !known.contains(h.square) {
            var text = "After \(SAN.san(first, in: nullPos)), \(h.text(.facts))"
            if let pin = pinsAfter.first(where: { $0.file == h.file && $0.rank == h.rank }) {
                text += " and it is pinned against \(PieceInfo.name(pin.target, .facts)) \(pin.targetSquare), so it cannot move away"
            }
            out.append(text + ".")
        }
        let serious = result.delta <= -2 || !out.isEmpty || (line.mateIn ?? 0) > 0
        guard serious else { return [] }
        var head = ["Opponent's threat if \(me.title(.facts)) makes no useful move: \(result.text)"]
        if (line.mateIn ?? 0) > 0 { head.append("This line leads to mate in \(line.mateIn ?? 0).") }
        return head + out
    }

    static func reasons(position pos: ChessPosition, best: UCIMove, threat: Threats.Threat?) -> [String] {
        let me = pos.turn, after = pos.applying(best)
        var out: [String] = []
        if let p = pos.board.piece(best.fromFile, best.fromRank), Character(p.lowercased()) == "k", abs(best.toFile - best.fromFile) == 2 {
            let king = UCIMove.squareName(best.toFile, best.toRank)
            out.append("Castling puts the king on \(king) behind its pawns and brings the rook into play.")
        }
        for pin in pos.board.unpinned(by: best, color: me) {
            out.append("The best move ends the pin: \(Placement.color(of: pin.piece).title(.facts)) \(PieceInfo.name(pin.piece, .facts)) \(pin.square) is no longer pinned against \(PieceInfo.name(pin.target, .facts)) \(pin.targetSquare).")
        }
        let before = pos.board.pins(of: me.opposite)
        for pin in after.board.pins(of: me.opposite) where !before.contains(pin) {
            out.append("The best move creates a pin: \(pin.text(.facts)).")
        }
        if let threat, threat.isParried(by: best) { out.append("The best move deals with the opponent's threat (\(threat.text(.facts))).") }
        if let victim = pos.board.piece(best.toFile, best.toRank) {
            out.append("The best move captures \(PieceInfo.name(victim, .facts)) \(UCIMove.squareName(best.toFile, best.toRank)).")
        }
        if after.isCheck { out.append("The best move gives check.") }
        return out
    }

    static func purposeLines(position pos: ChessPosition, best: UCIMove) -> [String] {
        let me = pos.turn
        guard let piece = pos.board.piece(best.fromFile, best.fromRank) else { return [] }
        let kind = Character(piece.lowercased())
        let after = pos.board.applying(best)
        var out: [String] = []
        if (kind == "r" || kind == "q") && best.fromFile == best.toFile {
            out.append("Destination area for the hint: rank \(best.toRank + 1) (do not name the exact square).")
        } else if kind != "k" || abs(best.toFile - best.fromFile) != 2 {
            out.append("Destination area for the hint: the \(Array("abcdefgh")[best.toFile])-file (do not name the exact square).")
        }
        let name = PieceInfo.name(piece, .facts)
        let reach = after.attacks(file: best.toFile, rank: best.toRank)
        let enemy = reach.compactMap { f, r -> String? in
            guard let p = after.piece(f, r), Placement.color(of: p) != me else { return nil }
            return "\(PieceInfo.name(p, .facts)) \(UCIMove.squareName(f, r))"
        }
        if !enemy.isEmpty { out.append("From its new square the \(name) attacks: \(enemy.joined(separator: ", ")).") }
        let enemyAttackers = after.attackers(of: me.opposite)
        let guarded = reach.compactMap { f, r -> String? in
            guard let p = after.piece(f, r), Placement.color(of: p) == me, Character(p.lowercased()) != "k", !enemyAttackers[r][f].isEmpty else { return nil }
            return "\(PieceInfo.name(p, .facts)) \(UCIMove.squareName(f, r))"
        }
        if !guarded.isEmpty { out.append("From its new square the \(name) protects the attacked \(guarded.joined(separator: ", ")).") }
        return out
    }

    static func evalDrop(best: EngineLine, other: Int) -> String {
        let drop = best.value - other
        if abs(best.value) >= 9_000 || abs(other) >= 9_000 { return drop > 0 ? "clearly worse than the best move" : "as good as the best move" }
        return String(format: "%.1f pawns worse than the best move", Double(max(0, drop)) / 100)
    }

    static func build(position pos: ChessPosition, result: EngineResult?, nullResult: EngineResult?, threat: Threats.Threat?,
                      revealBest: Bool) -> Grounding {
        let me = pos.turn, name = me.title(.facts), opponent = me.opposite.title(.facts)
        var lines = [
            "Position (FEN): \(pos.fen)",
            "\(name) to move. All evaluations are from \(name)'s point of view.",
            "\(name) pieces: \(pos.board.pieceList(me))",
            "\(opponent) pieces: \(pos.board.pieceList(me.opposite))",
        ]
        var positions = [pos]
        var pvs: [[String]] = []
        if let result { lines.append("Evaluation of the position: \(result.score(.facts))") }
        if pos.isCheck { lines.append("\(name)'s king is in check.") }
        let pins = pos.board.pins(of: me) + pos.board.pins(of: me.opposite)
        if !pins.isEmpty { lines.append("Pins: " + pins.map { $0.text(.facts) }.joined(separator: "; ") + ".") }
        for c in [me, me.opposite] {
            let hanging = pos.board.hangingPieces(of: c, .facts)
            if !hanging.isEmpty { lines.append("Unprotected or threatened for \(c.title(.facts)): \(hanging.joined(separator: "; ")).") }
        }
        let checks = pos.legalMoves().filter { pos.applying($0).isCheck }.prefix(5).map { SAN.san($0, in: pos) }
        if !checks.isEmpty { lines.append("Checks available to \(name): \(checks.joined(separator: ", ")).") }
        let threatFacts = threatLines(position: pos, nullResult: nullResult)
        if !threatFacts.isEmpty {
            lines += threatFacts
            positions.append(pos.nullMove)
            if let pv = nullResult?.lines.first?.pv { pvs.append(pv) }
        }
        guard revealBest, let result, let first = result.lines.first, let best = UCIMove(result.bestMove.isEmpty ? first.move : result.bestMove),
              pos.isLegal(best) else { return Grounding(lines: lines, positions: positions, pvs: pvs) }
        let described = pos.board.describeMove(best, .facts, check: false)
        lines.append("Best move per engine: \(SAN.san(best, in: pos)) (\(described)), evaluation \(first.score(.facts)).")
        let main = sanLine(first.pv, from: pos, plies: 6)
        if !main.text.isEmpty { lines.append("Expected continuation: \(main.text).") }
        pvs.append(first.pv)
        lines += reasons(position: pos, best: best, threat: threat)
        for (i, line) in result.lines.dropFirst().enumerated() {
            guard let m = UCIMove(line.move), pos.isLegal(m) else { continue }
            let alt = sanLine(line.pv, from: pos, plies: 4)
            lines.append("Alternative \(i + 1): \(SAN.san(m, in: pos)), evaluation \(line.score(.facts)), \(evalDrop(best: first, other: line.value)). Line: \(alt.text).")
            pvs.append(line.pv)
        }
        return Grounding(lines: lines, positions: positions, pvs: pvs)
    }

    static func mentionedMove(in question: String, position pos: ChessPosition) -> UCIMove? {
        let separators = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-=")).inverted
        for raw in question.components(separatedBy: separators) where raw.count >= 3 {
            let castle = raw.lowercased().replacingOccurrences(of: "0", with: "o").hasPrefix("o-o")
            let pieceFirst = raw.first.map { SAN.displayPieces.contains($0) || "KQRBN".contains($0) } ?? false
            let uci = UCIMove(raw).map { pos.isLegal($0) } ?? false
            guard castle || pieceFirst && raw.first?.isUppercase == true || uci else { continue }
            if uci, let m = UCIMove(raw) { return m }
            if let m = SAN.parse(raw, in: pos) ?? SAN.parse(raw, in: pos, pieces: SAN.displayPieces) { return m }
        }
        return nil
    }

    static func mentionedLines(position pos: ChessPosition, move: UCIMove, after: EngineResult, best: EngineLine?) -> (lines: [String], position: ChessPosition, pv: [String]) {
        let next = pos.applying(move)
        let mine = -after.value
        var text = "The player asks about \(SAN.san(move, in: pos)): after it the engine evaluates the position as \(Score.cp(mine).text(.facts)) for \(pos.turn.title(.facts))"
        if let best { text += " (\(evalDrop(best: best, other: mine)))" }
        var lines = [text + "."]
        if let line = after.lines.first {
            let reply = sanLine(line.pv, from: next, plies: 4)
            if !reply.text.isEmpty { lines.append("Likely continuation after \(SAN.san(move, in: pos)): \(reply.text).") }
            let captured = consequence(pv: line.pv, from: next, me: pos.turn)
            if let captured, captured.delta <= -2 { lines.append("That costs material: \(captured.text)") }
            return (lines, next, line.pv)
        }
        return (lines, next, [])
    }

    static func hintLines(level: Int, hints: HintSet?, position pos: ChessPosition) -> [String] {
        guard let hints else { return [] }
        switch level {
        case ...0:
            return ["Hint rule: the player has not asked for a hint. Do not name or hint at the best move; you may explain threats, pins and loose pieces."]
        case 1...3:
            var out = ["Hint stage \(level) of 4 is on screen for the player: \"\(hints.text(level: level))\""]
            if level >= 2, let p = pos.board.piece(hints.move.fromFile, hints.move.fromRank) {
                out.append("The highlighted piece is the \(PieceInfo.name(p, .facts)) on \(hints.stage2Square).")
            }
            out.append("Hint rule: do not name the best move or its target square and do not reveal more than the hint stage shows, unless the player asks why the hint makes sense or what it means. Then explain the concrete reason from the lines above, for example which piece it frees or which threat it stops.")
            return out
        default:
            return ["The best move is shown on screen as an arrow. Explain it freely."]
        }
    }
}
