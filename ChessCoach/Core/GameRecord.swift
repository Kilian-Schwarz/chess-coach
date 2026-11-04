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
    var lastMove: PlayedMove? { newMoves.last }
    var isGameOver: Bool {
        switch result { case .checkmate?, .stalemate?: return true; default: return false }
    }
}

struct GameRecord {
    private(set) var startPosition: ChessPosition?
    private(set) var isEstimatedStart = false
    private(set) var moves: [PlayedMove] = []
    private(set) var earlierSegments: [[PlayedMove]] = []
    private(set) var current: ChessPosition?
    private(set) var startedAt = Date()

    init() {}

    var isEmpty: Bool { moves.isEmpty }

    var positions: [ChessPosition] {
        guard let s = startPosition else { return [] }
        return [s] + moves.map(\.after)
    }

    @discardableResult
    mutating func update(placement: Placement, whiteAtBottom: Bool, hintedMover: PieceColor? = nil) -> GameUpdate {
        if placement.isStartPosition {
            if let c = current, c.board == placement, moves.isEmpty, !isEstimatedStart {
                return makeUpdate(.unchanged, [])
            }
            if !moves.isEmpty { earlierSegments.append(moves) }
            reset(to: .start, estimated: false)
            return makeUpdate(.newGame, [])
        }

        guard let cur = current else {
            let pos = estimate(placement, previous: nil, whiteAtBottom: whiteAtBottom, hint: hintedMover)
            reset(to: pos, estimated: true)
            return makeUpdate(.firstSight, [])
        }

        if cur.board == placement {
            if isEstimatedStart, moves.isEmpty, let h = hintedMover, cur.turn != h.opposite {
                var p = cur
                p.turn = h.opposite
                startPosition = p; current = p
            }
            return makeUpdate(.unchanged, [])
        }

        if let found = explain(from: cur, to: placement, hint: hintedMover) {
            append(found)
            return makeUpdate(.moves, found)
        }
        if isEstimatedStart, moves.isEmpty {
            var flipped = cur
            flipped.turn = cur.turn.opposite
            flipped.enPassantFile = nil
            if let found = explain(from: flipped, to: placement, hint: hintedMover) {
                startPosition = flipped; current = flipped
                append(found)
                return makeUpdate(.moves, found)
            }
        }
        let all = positions
        for i in stride(from: all.count - 2, through: max(0, all.count - 8), by: -1) where all[i].board == placement {
            moves.removeLast(moves.count - i)
            current = all[i]
            return makeUpdate(.takeback, [])
        }
        if !moves.isEmpty { earlierSegments.append(moves) }
        let pos = estimate(placement, previous: cur, whiteAtBottom: whiteAtBottom, hint: hintedMover)
        reset(to: pos, estimated: true)
        return makeUpdate(.resync, [])
    }

    mutating func clear() {
        startPosition = nil; current = nil; moves = []; earlierSegments = []; isEstimatedStart = false
    }

    private mutating func reset(to pos: ChessPosition, estimated: Bool) {
        startPosition = pos; current = pos; moves = []; isEstimatedStart = estimated
        startedAt = Date()
    }

    private mutating func append(_ found: [PlayedMove]) {
        moves += found
        current = found.last!.after
    }

    private func makeUpdate(_ change: GameUpdate.Change, _ new: [PlayedMove]) -> GameUpdate {
        let pos = current!
        return GameUpdate(change: change, newMoves: new, position: pos, result: pos.result,
                          estimated: isEstimatedStart && moves.isEmpty)
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

    private func explain(from pos: ChessPosition, to placement: Placement, hint: PieceColor?) -> [PlayedMove]? {
        let first = pos.legalMoves()
        for m in first {
            let next = pos.applying(m)
            if next.board == placement { return [played(m, in: pos)] }
        }
        var candidates: [[PlayedMove]] = []
        for m in first {
            let next = pos.applying(m)
            for m2 in next.legalMoves() where next.applying(m2).board == placement {
                candidates.append([played(m, in: pos), played(m2, in: next)])
            }
        }
        guard !candidates.isEmpty else { return nil }
        if candidates.count > 1, let h = hint, let match = candidates.first(where: { $0.last!.mover == h }) { return match }
        return candidates[0]
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
            if m.mover == .white { tokens.append("\(m.moveNumber).") }
            else if i == 0 { tokens.append("\(m.moveNumber)...") }
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
