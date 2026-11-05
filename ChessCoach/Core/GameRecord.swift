import Foundation

struct PlayedMove: Equatable {
    let move: UCIMove
    let san: String
    let mover: PieceColor
    let before: ChessPosition
    let after: ChessPosition
    let moveNumber: Int

    var sanDisplay: String { SAN.localize(san) }
    var uci: String { move.uci }
    var fenBefore: String { before.fen }
    var fenAfter: String { after.fen }
}

struct GameUpdate {
    enum Change: Equatable {
        case unchanged
        case newGame
        case moves
        case takeback
        case resync
        case firstSight
    }
    var change: Change
    var newMoves: [PlayedMove]
    var position: ChessPosition
    var fen: String { position.fen }
    var sideToMove: PieceColor { position.turn }
    var result: GameResult?
    var estimated: Bool
    var reviewable: [PlayedMove] = []
    var gap = false
    var log: [String] = []
    var lastMove: PlayedMove? { newMoves.last }
    var isGameOver: Bool {
        switch result { case .checkmate?, .stalemate?: return true; default: return false }
    }
}

private struct Stash {
    let moves: [PlayedMove]
    let breaks: [Int]
    let start: ChessPosition?
    let segmentStart: ChessPosition?
    let estimated: Bool
    let current: ChessPosition
}

enum ResyncContinuity {
    static let maxLostPieces = 6
    static let maxChangedSquares = 16

    static func isPlausible(from old: Placement, to new: Placement) -> Bool {
        let lost = old.pieceCount - new.pieceCount
        guard lost >= 0, lost <= maxLostPieces else { return false }
        var changed = 0
        for r in 0..<8 { for f in 0..<8 where old.squares[r][f] != new.squares[r][f] { changed += 1 } }
        return changed <= maxChangedSquares
    }
}

struct GameRecord {
    private(set) var startPosition: ChessPosition?
    private(set) var isEstimatedStart = false
    private(set) var moves: [PlayedMove] = []
    private(set) var breaks: [Int] = []
    private var segmentStart: ChessPosition?
    private var stash: Stash?
    private(set) var current: ChessPosition?
    private(set) var startedAt = Date()

    init() {}

    var isEmpty: Bool { moves.isEmpty }
    var segmentMoves: Int { moves.count - (breaks.last ?? 0) }

    var positions: [ChessPosition] {
        guard let s = startPosition else { return [] }
        return [s] + moves.map(\.after)
    }

    @discardableResult
    mutating func update(placement: Placement, whiteAtBottom: Bool, hintedMover: PieceColor? = nil, forced: Bool = false) -> GameUpdate {
        if placement.isStartPosition {
            if let c = current, c.board == placement, moves.isEmpty, !isEstimatedStart {
                return makeUpdate(.unchanged, [])
            }
            reset(to: .start, estimated: false)
            return makeUpdate(.newGame, [], log: ["New game: start position"])
        }

        guard let cur = current else {
            let pos = estimate(placement, previous: nil, whiteAtBottom: whiteAtBottom, hint: hintedMover)
            reset(to: pos, estimated: true)
            return makeUpdate(.firstSight, [], log: ["First sight: \(pos.fen), side to move guessed"])
        }

        if cur.board == placement {
            if isEstimatedStart, segmentMoves == 0, let h = hintedMover, cur.turn != h.opposite {
                var p = cur
                p.turn = h.opposite
                if moves.isEmpty { startPosition = p }
                segmentStart = p; current = p
                return makeUpdate(.unchanged, [], log: ["Side override: last-move marker says \(h.opposite) to move"])
            }
            return makeUpdate(.unchanged, [])
        }

        if let found = explain(from: cur, to: placement, hint: hintedMover) {
            append(found.moves)
            return makeUpdate(.moves, found.moves, reviewable: found.unique ? found.moves : [], log: [moveLog(found)])
        }
        if isEstimatedStart, segmentMoves == 0 {
            var flipped = cur
            flipped.turn = cur.turn.opposite
            flipped.enPassantFile = nil
            if let found = explain(from: flipped, to: placement, hint: hintedMover) {
                if moves.isEmpty { startPosition = flipped }
                segmentStart = flipped; current = flipped
                append(found.moves)
                return makeUpdate(.moves, found.moves, log: ["Side override: \(flipped.turn) to move fits the move", moveLog(found)])
            }
        }
        let base = breaks.last ?? 0
        if let first = segmentStart {
            let all = [first] + moves[base...].map(\.after)
            for i in stride(from: all.count - 2, through: max(0, all.count - 8), by: -1) where all[i].board == placement {
                let dropped = all.count - 1 - i
                moves.removeLast(dropped)
                current = all[i]
                return makeUpdate(.takeback, [], log: ["Take-back of \(dropped) ply: \(all[i].fen)"])
            }
        }
        let missing = cur.board.missingPieces(in: placement)
        if !forced, missing > 0, missing <= 2 {
            return makeUpdate(.unchanged, [], log: ["Partial frame ignored: \(missing) piece(s) missing"])
        }
        if let s = stash, moves.count == s.moves.count, ResyncContinuity.isPlausible(from: s.current.board, to: placement) {
            restore(s)
            return update(placement: placement, whiteAtBottom: whiteAtBottom, hintedMover: hintedMover, forced: forced)
        }
        let pos = estimate(placement, previous: cur, whiteAtBottom: whiteAtBottom, hint: hintedMover)
        guard !moves.isEmpty else {
            reset(to: pos, estimated: true, keepStash: true)
            return makeUpdate(.resync, [], gap: true, log: ["Resync: board not reachable within 2 plies from \(cur.fen), new game segment at \(pos.fen)"])
        }
        let plausible = ResyncContinuity.isPlausible(from: cur.board, to: placement)
        if !plausible {
            stash = Stash(moves: moves, breaks: breaks, start: startPosition, segmentStart: segmentStart, estimated: isEstimatedStart, current: cur)
        }
        breaks.append(moves.count)
        segmentStart = pos; current = pos; isEstimatedStart = true
        return makeUpdate(.resync, [], gap: true, log: ["Resync: board not reachable within 2 plies from \(cur.fen), segment break at ply \(moves.count), now \(pos.fen)"])
    }

    private func moveLog(_ found: (moves: [PlayedMove], unique: Bool)) -> String {
        let sans = found.moves.map(\.san).joined(separator: " ")
        let kind = found.moves.count == 1 ? "Move" : (found.unique ? "Moves (2 plies, missed scan)" : "Moves (2 plies, ambiguous, not rated)")
        return "\(kind) \(sans) -> \(found.moves.last!.after.fen)"
    }

    mutating func seed(_ pos: ChessPosition) {
        clear()
        reset(to: pos, estimated: true)
    }

    mutating func clear() {
        startPosition = nil; segmentStart = nil; current = nil; moves = []; breaks = []; stash = nil; isEstimatedStart = false
    }

    private mutating func reset(to pos: ChessPosition, estimated: Bool, keepStash: Bool = false) {
        startPosition = pos; segmentStart = pos; current = pos; moves = []; breaks = []; isEstimatedStart = estimated
        if !keepStash { stash = nil }
        startedAt = Date()
    }

    private mutating func restore(_ s: Stash) {
        moves = s.moves; breaks = s.breaks; startPosition = s.start; segmentStart = s.segmentStart
        isEstimatedStart = s.estimated; current = s.current; stash = nil
    }

    private mutating func append(_ found: [PlayedMove]) {
        moves += found
        current = found.last!.after
    }

    private func makeUpdate(_ change: GameUpdate.Change, _ new: [PlayedMove], reviewable: [PlayedMove] = [],
                            gap: Bool = false, log: [String] = []) -> GameUpdate {
        let pos = current!
        return GameUpdate(change: change, newMoves: new, position: pos, result: pos.result,
                          estimated: isEstimatedStart && segmentMoves == 0, reviewable: reviewable, gap: gap, log: log)
    }

    private func estimate(_ placement: Placement, previous: ChessPosition?, whiteAtBottom: Bool, hint: PieceColor?) -> ChessPosition {
        var turn: PieceColor
        if let h = hint { turn = h.opposite }
        else if let prev = previous, let m = placement.mover(from: prev.board) { turn = m.opposite }
        else { turn = whiteAtBottom ? .white : .black }
        if placement.isInCheck(turn.opposite) { turn = turn.opposite }
        let rights = (previous?.castling ?? .all).limited(to: placement)
        return ChessPosition(board: placement, turn: turn, castling: rights, enPassantFile: nil,
                             halfmoveClock: 0, fullmoveNumber: previous?.fullmoveNumber ?? 1)
    }

    private func played(_ m: UCIMove, in pos: ChessPosition) -> PlayedMove {
        PlayedMove(move: m, san: SAN.san(m, in: pos), mover: pos.turn,
                   before: pos, after: pos.applying(m), moveNumber: pos.fullmoveNumber)
    }

    private func explain(from pos: ChessPosition, to placement: Placement, hint: PieceColor?) -> (moves: [PlayedMove], unique: Bool)? {
        let first = pos.legalMoves()
        for m in first {
            let next = pos.applying(m)
            if next.board == placement { return ([played(m, in: pos)], true) }
        }
        var candidates: [[PlayedMove]] = []
        for m in first {
            let next = pos.applying(m)
            for m2 in next.legalMoves() where next.applying(m2).board == placement {
                candidates.append([played(m, in: pos), played(m2, in: next)])
            }
        }
        guard !candidates.isEmpty else { return nil }
        let unique = Set(candidates.map { $0.map(\.uci).joined(separator: " ") }).count == 1
        if candidates.count > 1, let h = hint, let match = candidates.first(where: { $0.last!.mover == h }) { return (match, unique) }
        return (candidates[0], unique)
    }

    func pgn(white: String = "?", black: String = "?", event: String = "Chess Coach",
             date: Date? = nil, opening: String? = nil, result: String? = nil) -> String {
        let fmt = DateFormatter()
        fmt.dateFormat = "yyyy.MM.dd"
        fmt.locale = Locale(identifier: "en_US_POSIX")
        let res = result ?? current?.result?.pgn ?? "*"
        var head: [(String, String)] = [("Event", event), ("Site", "?"), ("Date", fmt.string(from: date ?? startedAt)),
                                        ("Round", "?"), ("White", white), ("Black", black), ("Result", res)]
        if let o = opening, !o.isEmpty { head.append(("Opening", o)) }
        if let s = startPosition, s != ChessPosition.start {
            head.append(("SetUp", "1")); head.append(("FEN", s.fen))
        }
        var text = head.map { "[\($0.0) \"\($0.1.replacingOccurrences(of: "\"", with: "'"))\"]" }.joined(separator: "\n")
        var tokens: [String] = []
        for (i, m) in moves.enumerated() {
            if breaks.contains(i) { tokens.append("{ moves missed }") }
            if m.mover == .white { tokens.append("\(m.moveNumber).") }
            else if i == 0 || breaks.contains(i) { tokens.append("\(m.moveNumber)...") }
            tokens.append(m.san)
        }
        tokens.append(res)
        var lines: [String] = [], line = ""
        for t in tokens {
            if !line.isEmpty && line.count + 1 + t.count > 80 { lines.append(line); line = t }
            else { line += (line.isEmpty ? "" : " ") + t }
        }
        if !line.isEmpty { lines.append(line) }
        text += "\n\n" + lines.joined(separator: "\n") + "\n"
        return text
    }
}
