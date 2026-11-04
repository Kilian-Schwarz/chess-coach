import Foundation

struct ReviewedMove: Codable, Equatable {
    var color: String
    var uci: String
    var san: String?
    var fenBefore: String
    var valueBefore: Int
    var valueAfter: Int
    var bestUci: String?
}

struct MoveRecord: Codable, Equatable {
    var ply: Int
    var color: String
    var uci: String
    var san: String?
    var text: String
    var winBefore: Double
    var winAfter: Double
    var loss: Int
    var accuracy: Double
    var rating: String
    var bestText: String?
}

struct SideSummary: Codable, Equatable {
    var moves: Int = 0
    var accuracy: Double = 0
    var acpl: Double = 0
    var performance: Double = 0
    var best = 0, good = 0, inaccuracies = 0, mistakes = 0, blunders = 0
}

struct KeyMoment: Codable, Equatable {
    var ply: Int
    var color: String
    var moveText: String
    var bestText: String?
    var winBefore: Double
    var winAfter: Double
    var facts: String
}

struct GameReport: Codable, Equatable {
    var schemaVersion = 1
    var id: String
    var date: Date
    var whiteProfileId: String?
    var blackProfileId: String?
    var opening: String?
    var moves: [MoveRecord]
    var white: SideSummary
    var black: SideSummary
    var keyMoments: [KeyMoment]
    var summary: String?

    func side(_ color: String) -> SideSummary { color == "w" ? white : black }

    static func moveRating(loss: Int, bestUci: String?, uci: String) -> MoveRating {
        MoveRating.from(loss: loss, wasBest: bestUci != nil && bestUci == uci)
    }

    static func build(moves reviewed: [ReviewedMove], id: String = UUID().uuidString.lowercased(), date: Date = Date(),
                      whiteProfileId: String? = nil, blackProfileId: String? = nil, opening: String? = nil) -> GameReport {
        var records: [MoveRecord] = []
        var moments: [(drop: Double, moment: KeyMoment)] = []
        for (i, m) in reviewed.enumerated() {
            let place = ChessPosition(fen: m.fenBefore)?.board
            let mv = UCIMove(m.uci)
            let text = place.flatMap { p in mv.map { p.describeMove($0) } } ?? m.uci
            let bestText: String? = m.bestUci.flatMap { b in b == m.uci ? nil : place.flatMap { p in UCIMove(b).map { p.describeMove($0) } } }
            let wb = Accuracy.winChance(cp: m.valueBefore), wa = Accuracy.winChance(cp: m.valueAfter)
            let loss = Accuracy.cpLoss(valueBefore: m.valueBefore, valueAfter: m.valueAfter)
            let rating = moveRating(loss: loss, bestUci: m.bestUci, uci: m.uci)
            let san = m.san ?? mv.flatMap { mv in ChessPosition(fen: m.fenBefore).map { SAN.san(mv, in: $0) } }
            records.append(MoveRecord(ply: i + 1, color: m.color, uci: m.uci, san: san, text: text, winBefore: wb, winAfter: wa, loss: loss,
                                      accuracy: Accuracy.moveAccuracy(winBefore: wb, winAfter: wa), rating: rating.rawValue, bestText: bestText))
            guard wb - wa >= 10, let place, let mv else { continue }
            var facts: [String] = []
            let hanging = place.applying(mv).hangingPieces(of: m.color == "w" ? .white : .black)
            if !hanging.isEmpty { facts.append(Loc.display("report.after", "Afterwards: \(hanging.joined(separator: "; "))")) }
            if let b = bestText { facts.append(Loc.display("report.better", "Better was: \(b)")) }
            if (i + 1 < reviewed.count ? reviewed[i + 1].valueBefore : 0) >= 9000 {
                facts.append(Loc.display("report.opponentMates", "The opponent can checkmate"))
            }
            facts.append(Loc.display("report.winChance", "Win chance fell from \(Int(wb.rounded())) % to \(Int(wa.rounded())) %"))
            moments.append((wb - wa, KeyMoment(ply: i + 1, color: m.color, moveText: text, bestText: bestText,
                                              winBefore: wb, winAfter: wa, facts: facts.joined(separator: ". "))))
        }
        func summary(_ c: String) -> SideSummary {
            let mine = records.filter { $0.color == c }
            var s = SideSummary()
            s.moves = mine.count
            s.accuracy = Accuracy.gameAccuracy(mine.map(\.accuracy))
            s.acpl = Accuracy.acpl(mine.map(\.loss))
            s.performance = mine.isEmpty ? 0 : Accuracy.performanceRating(acpl: s.acpl)
            for r in mine {
                switch MoveRating(rawValue: r.rating) {
                case .best: s.best += 1
                case .good: s.good += 1
                case .inaccuracy: s.inaccuracies += 1
                case .mistake: s.mistakes += 1
                case .blunder: s.blunders += 1
                case .none: break
                }
            }
            return s
        }
        let top = moments.sorted { $0.drop > $1.drop }.prefix(3).map(\.moment).sorted { $0.ply < $1.ply }
        return GameReport(id: id, date: date, whiteProfileId: whiteProfileId, blackProfileId: blackProfileId, opening: opening,
                          moves: records, white: summary("w"), black: summary("b"), keyMoments: Array(top), summary: nil)
    }

    static func moveLabel(ply: Int, color: String, _ loc: Loc = .display) -> String {
        loc("report.moveLabel", "Move \((ply + 1) / 2), \(color == "w" ? PieceColor.white.title(loc) : PieceColor.black.title(loc))")
    }

    func summaryRequest(for color: String, context: PlayerContext, language: String = "English") -> Coach.ExplanationRequest {
        let me = side(color), you = side(color == "w" ? "b" : "w")
        func f(_ d: Double) -> String { String(format: "%.0f", d) }
        var lines: [String] = []
        if let o = opening { lines.append("Opening: \(o)") }
        lines.append("The player is \(color == "w" ? "White" : "Black"), \(moves.count) half-moves in total.")
        lines.append("Accuracy \(f(me.accuracy)) %, average loss \(f(me.acpl)) centipawns, performance rating \(f(me.performance)).")
        lines.append("Opponent: accuracy \(f(you.accuracy)) %.")
        lines.append("Player's moves: \(me.best) best, \(me.good) good, \(me.inaccuracies) inaccuracies, \(me.mistakes) mistakes, \(me.blunders) blunders.")
        for k in keyMoments where k.color == color {
            lines.append("Key moment (\(GameReport.moveLabel(ply: k.ply, color: k.color, .facts))): \(k.moveText). \(k.facts).")
        }
        let card = Coach.styleCard(context: context, style: ExplanationStyle(length: .long, depth: .concrete), language: language)
        return Coach.ExplanationRequest(system: Coach.summarySystemPrompt, user: lines.joined(separator: "\n") + "\n\n" + card, maxTokens: 400)
    }

    func fallbackSummary(for color: String, _ loc: Loc = .display) -> String {
        let me = side(color), issues = me.mistakes + me.blunders
        var text = loc("report.fallback", "Accuracy \(Int(me.accuracy.rounded())) %, \(issues) mistakes or blunders")
        if let k = keyMoments.first(where: { $0.color == color }) {
            text += loc("report.fallbackMoment", ". Key moment: \(GameReport.moveLabel(ply: k.ply, color: k.color, loc)), \(k.moveText)")
        }
        return text + "."
    }
}
