import Foundation

var failures = 0
var checks = 0
func check(_ ok: Bool, _ msg: @autoclosure () -> String) {
    checks += 1
    if !ok { failures += 1; print("FAIL: \(msg())") }
}
func near(_ a: Double, _ b: Double, _ eps: Double = 0.01) -> Bool { abs(a - b) <= eps }
func section(_ s: String) { print("--- \(s)") }
func parse(_ fen: String) -> (placement: Placement, side: PieceColor) {
    let p = ChessPosition(fen: fen)!
    return (p.board, p.turn)
}

let t0 = Date(timeIntervalSince1970: 1_800_000_000)
let day = 86_400.0
let env = ProcessInfo.processInfo.environment
let llmURL = env["SKIP_LLM"] == nil ? env["CHESSCOACH_TEST_LLM_URL"] : nil
let llmModel = env["CHESSCOACH_TEST_LLM_MODEL"] ?? ""

section("accuracy")
check(near(Accuracy.winChance(cp: 0), 50), "win(0)")
check(near(Accuracy.winChance(cp: 100), 50 + 50 * (2 / (1 + exp(-0.368208)) - 1), 1e-9), "win(100)")
check(Accuracy.winChance(cp: 10_000) > 99.9 && Accuracy.winChance(cp: -10_000) < 0.1, "win(mate)")
check(near(Accuracy.moveAccuracy(winBefore: 50, winAfter: 50), 100), "equal win chance is 100")
check(Accuracy.moveAccuracy(winBefore: 90, winAfter: 10) < 5, "collapse is low")
check(Accuracy.moveAccuracy(winBefore: 50, winAfter: 60) == 100, "capped at 100")
check(near(Accuracy.moveAccuracy(winBefore: 60, winAfter: 50), 103.1668 * exp(-0.4354) - 3.1669), "10 points")
check(Accuracy.cpLoss(valueBefore: 500, valueAfter: -9000) == 1000, "loss capped at 1000")
check(Accuracy.cpLoss(valueBefore: 10, valueAfter: 50) == 0, "loss never negative")
check(near(Accuracy.acpl([10, 20, 3000]), (10 + 20 + 1000) / 3), "ACPL capped")
check(near(Accuracy.gameAccuracy([100, 50]), 75), "game accuracy")
for (a, r) in [(10.0, 2400.0), (20, 2100), (35, 1800), (50, 1600), (70, 1400), (100, 1200), (150, 1000), (250, 800), (300, 600), (0, 2400), (15, 2250), (125, 1100)] {
    check(near(Accuracy.performanceRating(acpl: a), r), "performance acpl \(a)")
}
check(near(Accuracy.ratingDelta(rating: 1000, performance: 1400, gameNumber: 4), 50), "delta K=0.5")
check(near(Accuracy.ratingDelta(rating: 1000, performance: 1400, gameNumber: 11), 25), "delta K=0.25")
check(near(Accuracy.ratingDelta(rating: 600, performance: 2400, gameNumber: 4), 150), "delta capped +150")
check(near(Accuracy.ratingDelta(rating: 2400, performance: 600, gameNumber: 4), -150), "delta capped -150")
check(PlayerLevel.from(rating: 1199) == .beginner && PlayerLevel.from(rating: 1200) == .intermediate
      && PlayerLevel.from(rating: 1800) == .intermediate && PlayerLevel.from(rating: 1801) == .strong, "levels")

section("profile")
var prof = PlayerProfile(id: "p1", name: "Tester", createdAt: t0)
check(prof.placementText == "Placement in progress: game 1 of 3", "placement text")
prof.completeGame(performance: 1000, date: t0)
check(prof.placementText == "Placement in progress: game 2 of 3" && near(prof.rating, 1000), "placement after 1")
prof.completeGame(performance: 1400, date: t0)
check(near(prof.rating, 1200), "placement mean of 2")
prof.completeGame(performance: 1600, date: t0)
check(!prof.isPlacing && near(prof.rating, 1333.33, 0.01) && prof.level == .intermediate, "placement done: \(prof.rating)")
check(prof.placementText == nil, "no placement text afterwards")
let r3 = prof.rating
prof.completeGame(performance: 1800, date: t0)
check(near(prof.rating, r3 + 0.5 * (1800 - r3) / 4), "rating update game 4")
check(prof.ratingHistory.count == 4 && prof.gamesPlayed == 4, "history")
var p2 = PlayerProfile(name: "X")
p2.observe(.tactics, value: 1)
check(near(p2.skills["tactics"]!, 0.75, 1e-9) && p2.observationCount(.tactics) == 1, "first observation weighs 1/2")
for _ in 0..<30 { p2.observe(.threats, value: 1) }
check(near(p2.skills["threats"]!, 1, 0.01) && p2.hasEnoughData(.threats) && !p2.hasEnoughData(.tactics), "counted estimate and minimum data")
var gain = PlayerProfile(name: "G")
gain.observe(.tactics, value: 1, gainWeight: 0)
check(near(gain.skills["tactics"]!, 0.5, 1e-9), "gain weight 0 blocks gains")
gain.observe(.tactics, value: 0, gainWeight: 0)
check(gain.skills["tactics"]! < 0.5, "losses always count")
var fh = PlayerProfile(name: "F")
fh.skills = Dictionary(uniqueKeysWithValues: Skill.allCases.map { ($0.rawValue, 0.65) })
fh.focus = Skill.tactics.rawValue
fh.skills["piece_safety"] = 0.62; fh.updateFocus()
check(fh.focus == "tactics", "no switch while the earlier skill stays at 0.6 or above")
fh.skills["piece_safety"] = 0.55; fh.updateFocus()
check(fh.focus == "piece_safety", "switch to an earlier skill below 0.6")
fh.skills["piece_safety"] = 0.85; fh.updateFocus()
check(fh.focus != "piece_safety", "leave the focus at 0.8")
fh.pinFocus(.endgame); fh.skills["endgame"] = 0.95; fh.updateFocus()
check(fh.focus == "endgame", "pinned focus holds")
fh.completeGame(performance: 1200)
check(fh.pinnedFocus == nil, "pin ends with the next game")
var rated = PlayerProfile(name: "R")
rated.completeGame(performance: 1000, weight: 10)
rated.completeGame(performance: 1600, weight: 30)
check(near(rated.rating, 1450), "placement is weighted by own moves: \(rated.rating)")
var gp = PlayerProfile(name: "A"); gp.placementGames = 3; gp.rating = 1200
var gp2 = gp
gp.completeGame(performance: 1600, assistedShare: 0)
gp2.completeGame(performance: 1600, assistedShare: 0.5)
check(near(gp2.rating - 1200, (gp.rating - 1200) / 2), "assisted share halves rating gains")
var short = SideSummary(); short.moves = 9
var longG = SideSummary(); longG.moves = 10; longG.performance = 1500
var np = PlayerProfile(name: "N")
check(np.completeGame(summary: short) == nil && np.gamesPlayed == 0, "games under 10 own moves are ignored")
check(np.completeGame(summary: longG) != nil && np.gamesPlayed == 1, "10 own moves count")
for level in [0, 2, 2, 9] { p2.recordHintUse(level: level) }
check(p2.hintStats.levelUses == [1, 0, 2, 0, 1], "hint stats \(p2.hintStats.levelUses)")
check(near(p2.averageHintLevel ?? 0, (2 + 2 + 4) / 3.0), "average hint level")
p2.recordMistake(.hangingPiece, example: "a"); p2.recordMistake(.hangingPiece, example: "b"); p2.recordMistake(.other)
check(p2.topMistakes(1).first?.type == .hangingPiece && p2.topMistakes(1).first?.count == 2, "top mistake")
for i in 0..<7 { p2.recordMistake(.missedTactic, example: "e\(i)") }
check(p2.mistakeExamples?["missed_tactic"]?.count == 5 && p2.mistakeExamples?["missed_tactic"]?.last == "e6", "at most 5 examples")
check(!PlayerProfile.defaultName.isEmpty, "default name")
check(PlayerProfile(name: "N", startStyle: .shortPrinciple).style.current.length == .short, "short principle preset")

section("curriculum")
var sk: [String: Double] = [:]
for s in Skill.allCases { sk[s.rawValue] = 0.9 }
sk["tactics"] = 0.6; sk["endgame"] = 0.2
check(Curriculum.focus(skills: sk) == .tactics, "focus is the earliest stage below 0.7")
sk["tactics"] = 0.9; sk["opening"] = 0.69
check(Curriculum.focus(skills: sk) == .opening, "focus stage 4")
sk["opening"] = 0.9; sk["endgame"] = 0.95; sk["middlegame"] = 0.75; sk["king_safety"] = 0.8
check(Curriculum.focus(skills: sk) == .middlegame, "focus is the weakest when all are at least 0.7")
check(Curriculum.path.map(\.skill) == [.pieceSafety, .threats, .tactics, .opening, .kingSafety, .endgame, .middlegame], "path order")
check(Curriculum.focusLabel(.threats) == "Learning focus: Threats", "focus label")
let base = Curriculum.adaptation(level: .beginner, focus: .pieceSafety, skills: ["piece_safety": 0.5], successRate: 0.78)
check(base.defaultHintLevel == 2 && base.newPuzzlesPerDay == 10 && base.thinkSeconds == 15, "adaptation in target range")
let hi = Curriculum.adaptation(level: .intermediate, focus: .tactics, skills: ["tactics": 0.5], successRate: 0.95)
check(hi.defaultHintLevel == 1 && hi.thinkSeconds > 15, "adaptation too easy")
let lo = Curriculum.adaptation(level: .intermediate, focus: .tactics, skills: ["tactics": 0.5], successRate: 0.5)
check(lo.defaultHintLevel == 2 && lo.thinkSeconds < 15 && lo.newPuzzlesPerDay < 10, "adaptation too hard")

section("texts")
let start = parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1").placement
check(start.describeMove(UCIMove("g1f3")!) == "Knight g1–f3", "plain move: \(start.describeMove(UCIMove("g1f3")!))")
let capture = parse("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1").placement
check(capture.describeMove(UCIMove("d1d5")!) == "Rook d1 captures Queen d5", "capture: \(capture.describeMove(UCIMove("d1d5")!))")
check(capture.describeMove(UCIMove("d1a1")!) == "Rook d1–a1" && capture.describeMove(UCIMove("d1d8")!, check: false) == "Rook d1–d8", "plain rook moves")
let castle = parse("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 1").placement
check(castle.describeMove(UCIMove("e1g1")!) == "short castling", "castling")
check(Score.mate(3).text() == "Mate in 3" && Score.mate(-2).text() == "mated in 2" && Score.cp(40).text() == "+0.4" && Score.cp(-120).text() == "-1.2", "score texts")
check(Score.checkmate.text() == "Checkmate" && Score.stalemate.text() == "Stalemate", "terminal scores")
check(MoveRating.blunder.title() == "Blunder" && PlayerLevel.strong.title() == "Strong" && PieceColor.black.title() == "Black", "titles")
check(MistakeType.hangingPiece.title() == "Hanging piece" && Skill.kingSafety.title() == "King safety", "mistake and skill titles")
check(GameReport.moveLabel(ply: 23, color: "w") == "Move 12, White", "move label")
check(ChessPosition.start.result == nil && GameResult.checkmate(winner: .white).title() == "Checkmate, White wins", "result title")
check(OpeningInfo(eco: "C50", name: "Italian Game").display == "Italian Game (C50)", "opening display")
check(SAN.localize("Nbd7+", pieces: "KDTLS", castleShort: "0-0", castleLong: "0-0-0") == "Sbd7+"
      && SAN.localize("O-O-O+", pieces: "KDTLS", castleShort: "0-0", castleLong: "0-0-0") == "0-0-0+"
      && SAN.localize("e8=Q", pieces: "KDTLS") == "e8=D" && SAN.localize("Qxd4") == "Qxd4", "SAN display letters")
check(SAN.delocalize("Sbd7+", pieces: "KDTLS") == "Nbd7+" && SAN.delocalize("e8=D", pieces: "KDTLS") == "e8=Q", "SAN input letters")
let hanging = parse("4k3/8/4p3/3n4/4P3/8/8/4K3 b - - 0 1").placement
check(hanging.hangingPieces(of: .black) == ["Knight d5 is attacked by a cheaper piece"], "cheaper attacker text")
check(capture.hangingPieces(of: .black) == ["Queen d5 is attacked and unprotected"], "unprotected text")

section("hint themes")
func theme(_ fen: String, _ mv: String, threat: Threats.Threat? = nil) -> HintTheme {
    let (p, side) = parse(fen)
    return Hints.theme(placement: p, best: UCIMove(mv)!, player: side, threat: threat)
}
check(theme("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1", "d1d5") == .captureGain, "capture")
check(theme("r3k3/8/8/1N6/8/8/8/4K3 w - - 0 1", "b5c7") == .fork, "fork")
check(theme("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "a1a8") == .check, "check")
check(theme("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "g1f3") == .develop, "develop")
check(theme("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 1", "e1g1") == .castle, "castle")
check(theme("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4") == .pawnMove, "pawn move")
check(theme("8/8/4k3/8/8/4K3/8/8 w - - 0 1", "e3e4") == .kingActivate, "activate king")
check(theme("r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 1", "d1e2") == .improvePiece, "improve piece")
do {
    let fen = "4k3/8/8/7b/8/8/8/3RK3 w - - 0 1"
    let (p, _) = parse(fen)
    let th = Threats.Threat(move: UCIMove("h5d1")!, line: [], gain: 200, mateIn: nil, board: p)
    check(th.isParried(by: UCIMove("d1c1")!), "parried by moving away")
    check(th.isParried(by: UCIMove("d1d5")!), "parried, target gone")
    check(!th.isParried(by: UCIMove("e8e7")!), "not parried")
    check(theme(fen, "d1c1", threat: th) == .parryThreat, "parry theme")
    check(th.text() == "Threat: Bishop h5 captures Rook d1", "threat text: \(th.text())")
}
check(Hints.theme(placement: parse("6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1").placement, best: UCIMove("a1a8")!, player: .white, bestMate: true) == .mate, "mate")
check(ChessPosition(fen: "x") == nil, "invalid FEN")
check(Hints.stage2Text(piece: "N", square: "g1") == "Move the knight on g1." && Hints.stage2Text(piece: "q", square: "d8") == "Move the queen on d8.", "stage 2 text")
do {
    let (p, side) = parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")
    let res = EngineResult(fen: p.fen(sideToMove: side), bestMove: "d2d4", lines: [EngineLine(move: "d2d4", scoreCP: 30, mateIn: nil, pv: ["d2d4"])])
    if let h = Hints.build(placement: p, player: side, result: res) {
        check(h.stage2Square == "d2" && h.stage2.contains("d2") && h.stage2 == "Move the pawn on d2.", "stage 2 names the from square: \(h.stage2)")
        check(![h.stage1, h.stage2, h.stage3Fallback].contains { $0.contains("d4") }, "stages 1-3 hide the target square")
    } else { check(false, "start position hint") }
    let catalog = (try? JSONSerialization.jsonObject(with: Data(contentsOf: URL(fileURLWithPath: "ChessCoach/Resources/Localizable.xcstrings")))) as? [String: Any]
    let units = ((catalog?["strings"] as? [String: Any])?["hint.stage2.pawn"] as? [String: Any])?["localizations"] as? [String: [String: Any]]
    let de = (units?["de"]?["stringUnit"] as? [String: Any])?["value"] as? String
    check(de?.replacingOccurrences(of: "%1$@", with: "d2") == "Zieh den Bauern, der auf d2 steht.", "German stage 2 text: \(de ?? "-")")
}
check(Hints.revealsTarget("Go to f3 please", move: UCIMove("g1f3")!) && !Hints.revealsTarget("Develop the knight", move: UCIMove("g1f3")!), "target check")
for th in HintTheme.allCases {
    check(!th.stage1Text().isEmpty && !th.title().isEmpty && !Hints.stage3Fallback(theme: th, piece: "N").isEmpty, "texts \(th)")
}
check(Hints.stage3Fallback(theme: .develop, piece: "N").hasPrefix("Your knight is still on the back rank"), "stage 3 text")

section("puzzles")
var utc = Calendar(identifier: .gregorian)
utc.timeZone = TimeZone(identifier: "UTC")!
let dayStart = utc.startOfDay(for: t0)
func puzzle(_ fen: String = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", accepted: [String] = ["a1a8"], type: MistakeType = .missedTactic) -> Puzzle {
    Puzzle(id: "z", fen: fen, acceptedMoves: accepted, bestLine: accepted, theme: "check", mistakeType: type.rawValue, createdAt: t0, dueAt: t0)
}
var z = puzzle()
check(z.isNew && z.box == 1, "new puzzle")
z.attempt(move: "a1a8", now: t0, calendar: utc)
check(z.box == 2 && z.dueAt == dayStart.addingTimeInterval(3 * day), "correct moves to box 2, due at the start of day +3")
z.attempt(move: "a1a8", hintLevel: 2, now: t0, calendar: utc)
check(z.box == 2 && z.attempts.last?.assisted == true, "correct with hint keeps the box and is assisted")
z.attempt(move: "a1a8", now: t0, calendar: utc); z.attempt(move: "a1a8", now: t0, calendar: utc)
check(z.box == 4 && z.dueAt == dayStart.addingTimeInterval(16 * day), "box 4, 16 days")
z.attempt(move: "a1a8", hintLevel: 4, now: t0, calendar: utc)
check(z.box == 3, "hint 4 moves the box back by one")
z.attempt(move: "a1a2", now: t0, calendar: utc)
check(z.box == 1 && z.dueAt == dayStart.addingTimeInterval(day) && !z.mastered, "wrong goes back to box 1, due tomorrow at day start")
for _ in 0..<5 { z.attempt(move: "a1a8", now: t0, learningSpeed: 1.5, calendar: utc) }
check(z.mastered, "mastered after box 5")
var z2 = puzzle(); z2.attempt(move: "a1a8", now: t0, learningSpeed: 1.5, calendar: utc)
check(z2.dueAt == dayStart.addingTimeInterval(5 * day), "learning speed stretches the interval (4.5 rounds to 5)")
var z3 = puzzle(); z3.attempt(move: "a1a8", now: t0, learningSpeed: 0.6, calendar: utc)
check(z3.dueAt == dayStart.addingTimeInterval(2 * day), "learning speed shortens the interval (1.8 rounds to 2)")
var z4 = puzzle(); z4.attempt(move: "a1a2", now: t0, learningSpeed: 0.6, calendar: utc)
check(z4.dueAt == dayStart.addingTimeInterval(day), "interval is at least one day")
check(puzzle(accepted: ["e2e4", "d2d4"]).accepts("D2D4") && !puzzle().accepts("a1a7"), "accepts")
var alt = puzzle(); alt.acceptAlternative("A1A7"); alt.acceptAlternative("a1a7")
check(alt.accepts("a1a7") && alt.acceptedMoves.count == 2, "alternative accepted once")
check(Puzzle.isAcceptableAlternative(bestValue: 100, playedValue: 60) && !Puzzle.isAcceptableAlternative(bestValue: 100, playedValue: 59), "alternative tolerance 40 cp")
func withReviews(_ n: Int, correct: Int, assistedFirst: Int = 0) -> [Puzzle] {
    (0..<n).map { i in
        var p = puzzle()
        p.id = "r\(i)"
        p.attempts = [PuzzleAttempt(date: t0.addingTimeInterval(Double(i) - 1000), correct: true, hintLevel: 0),
                      PuzzleAttempt(date: t0.addingTimeInterval(Double(i)), correct: i < correct, hintLevel: i < assistedFirst ? 1 : 0, assisted: i < assistedFirst)]
        return p
    }
}
check(near(PuzzleScheduler.adjustedLearningSpeed(current: 1.0, puzzles: withReviews(30, correct: 28)), 1.1), "speed up")
check(near(PuzzleScheduler.adjustedLearningSpeed(current: 1.0, puzzles: withReviews(30, correct: 15)), 0.9), "speed down")
check(near(PuzzleScheduler.adjustedLearningSpeed(current: 1.0, puzzles: withReviews(30, correct: 23)), 1.0), "speed unchanged at 77 %")
check(near(PuzzleScheduler.adjustedLearningSpeed(current: 1.5, puzzles: withReviews(30, correct: 30)), 1.5), "speed max 1.5")
check(near(PuzzleScheduler.adjustedLearningSpeed(current: 0.6, puzzles: withReviews(30, correct: 0)), 0.6), "speed min 0.6")
check(near(PuzzleScheduler.adjustedLearningSpeed(current: 1.0, puzzles: withReviews(5, correct: 5)), 1.0), "speed needs 10 reviews")
check(PuzzleScheduler.recentSuccessRate(withReviews(20, correct: 10, assistedFirst: 20)) == nil, "assisted attempts do not count")
check(near(PuzzleScheduler.recentSuccessRate(withReviews(20, correct: 20, assistedFirst: 10)) ?? 0, 1), "only unassisted reviews count")
var newOnly = puzzle(); newOnly.attempts = [PuzzleAttempt(date: t0, correct: false, hintLevel: 0)]
check(PuzzleScheduler.recentSuccessRate([newOnly]) == nil, "first attempts are not reviews")
var spd = PlayerProfile(name: "S", createdAt: t0)
spd.adjustLearningSpeed(puzzles: withReviews(30, correct: 30), now: t0, calendar: utc)
spd.adjustLearningSpeed(puzzles: withReviews(30, correct: 30), now: t0.addingTimeInterval(3600), calendar: utc)
check(near(spd.learningSpeed, 1.1), "learning speed adjusts once per day")
spd.adjustLearningSpeed(puzzles: withReviews(30, correct: 30), now: t0.addingTimeInterval(day), calendar: utc)
check(near(spd.learningSpeed, 1.2), "and again the next day")
var set = PuzzleSet()
for i in 0..<14 {
    var p = puzzle(type: i < 3 ? .hangingPiece : .missedTactic)
    p.id = "n\(i)"; p.fen = "f\(i)"; p.dueAt = t0.addingTimeInterval(Double(-i)); p.createdAt = t0.addingTimeInterval(Double(i)); set.add(p)
}
var dueNotFocus = puzzle(type: .missedTactic)
dueNotFocus.id = "d1"; dueNotFocus.fen = "fd1"; dueNotFocus.dueAt = t0.addingTimeInterval(-1)
dueNotFocus.attempts = [PuzzleAttempt(date: t0.addingTimeInterval(-9 * day), correct: true, hintLevel: 0)]
var dueFocus = puzzle(type: .hangingPiece)
dueFocus.id = "d2"; dueFocus.fen = "fd2"; dueFocus.attempts = dueNotFocus.attempts; dueFocus.dueAt = t0.addingTimeInterval(-1)
var future = puzzle()
future.id = "fut"; future.fen = "ff"; future.attempts = dueNotFocus.attempts; future.dueAt = t0.addingTimeInterval(day)
let dup = set.puzzles[0]
set.puzzles[0].box = 3
var again = dup; again.id = "again"; again.dueAt = t0
check(!set.add(again) && set.puzzles.count == 14 && set.puzzles[0].box == 1, "duplicate position is not added but resets to box 1")
set.puzzles += [dueNotFocus, dueFocus, future]
var rng1 = SeededRNG(seed: 5)
let sel = PuzzleScheduler.todaysSelection(set.puzzles, focus: .pieceSafety, now: t0, maxNew: 10, maxTotal: 15, priority: [], calendar: utc, using: &rng1)
check(sel.count == 12, "selection: 2 due + 10 new, was \(sel.count)")
check(!sel.contains { $0.id == "fut" }, "future puzzles excluded")
var runLen = 1, maxRun = 1
for i in sel.indices.dropFirst() { runLen = sel[i].theme == sel[i - 1].theme ? runLen + 1 : 1; maxRun = max(maxRun, runLen) }
check(Set(sel.map(\.id)).count == sel.count, "no duplicates in the queue")
var many = PuzzleSet()
for i in 0..<40 { var p = puzzle(); p.id = "m\(i)"; p.fen = "m\(i)"; p.dueAt = t0.addingTimeInterval(Double(-i)); p.attempts = dueNotFocus.attempts; many.add(p) }
let capped = PuzzleScheduler.todaysSelection(many.puzzles, focus: nil, now: t0, calendar: utc)
check(capped.count == 15 && capped.allSatisfy { Int($0.id.dropFirst())! >= 25 }, "daily cap keeps the oldest due reviews, was \(capped.count)")
var themed = PuzzleSet()
for i in 0..<12 { var p = puzzle(type: i < 8 ? .hangingPiece : .missedTactic); p.id = "t\(i)"; p.fen = "t\(i)"; p.theme = i < 8 ? "a" : "b"; themed.add(p) }
for seed in 1...20 {
    var g = SeededRNG(seed: UInt64(seed))
    let q = PuzzleScheduler.interleave(themed.puzzles, focus: .pieceSafety, using: &g)
    var run = 1, worst = 1
    for i in q.indices.dropFirst() { run = q[i].theme == q[i - 1].theme ? run + 1 : 1; worst = max(worst, run) }
    check(q.count == 12 && (worst <= 3 || q.suffix(worst).allSatisfy { $0.theme == "a" }), "interleave run limit (seed \(seed), run \(worst))")
}
var g0 = SeededRNG(seed: 9)
let prio = PuzzleScheduler.todaysSelection(set.puzzles, focus: nil, now: t0, maxNew: 10, maxTotal: 15, priority: ["fut", "n5"], calendar: utc, using: &g0)
check(prio.prefix(2).map(\.id) == ["fut", "n5"] && prio.count >= 13 && Set(prio.map(\.id)).count == prio.count, "priority puzzles come first, even when not due")
var startedToday = puzzle()
startedToday.id = "s"; startedToday.fen = "s"; startedToday.dueAt = t0.addingTimeInterval(3 * day)
startedToday.attempts = [PuzzleAttempt(date: t0, correct: true, hintLevel: 0)]
let selDone = PuzzleScheduler.todaysSelection(set.puzzles + [startedToday], focus: nil, now: t0, maxTotal: 40, calendar: utc)
check(selDone.filter(\.isNew).count == 9, "puzzles started today count against the 10 new ones")
let st = PuzzleScheduler.stats(set.puzzles, now: t0)
check(st.total == 17 && st.new == 14 && st.due == 2, "stats \(st)")

struct LLMAnswer { var firstToken: Double; var total: Double; var text: String }

func askLLM(_ req: Coach.ExplanationRequest) async -> LLMAnswer? {
    guard let base = llmURL, let url = URL(string: base + "/chat/completions") else { return nil }
    var r = URLRequest(url: url, timeoutInterval: 30)
    r.httpMethod = "POST"
    r.setValue("application/json", forHTTPHeaderField: "Content-Type")
    let body: [String: Any] = ["model": llmModel, "stream": true, "max_tokens": req.maxTokens, "temperature": 0.4,
                               "chat_template_kwargs": ["enable_thinking": false],
                               "messages": [["role": "system", "content": req.system], ["role": "user", "content": req.user]]]
    r.httpBody = try? JSONSerialization.data(withJSONObject: body)
    let start = Date()
    var first: Double?
    var text = ""
    do {
        let (bytes, resp) = try await URLSession.shared.bytes(for: r)
        guard (resp as? HTTPURLResponse)?.statusCode == 200 else { print("  LLM: HTTP \((resp as? HTTPURLResponse)?.statusCode ?? 0)"); return nil }
        for try await line in bytes.lines where line.hasPrefix("data: ") && !line.contains("[DONE]") {
            guard let d = line.dropFirst(6).data(using: .utf8), let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any],
                  let delta = ((j["choices"] as? [[String: Any]])?.first)?["delta"] as? [String: Any],
                  let c = delta["content"] as? String, !c.isEmpty else { continue }
            if first == nil { first = Date().timeIntervalSince(start) }
            text += c
        }
    } catch { print("  LLM: unreachable (\(error.localizedDescription))"); return nil }
    return LLMAnswer(firstToken: first ?? -1, total: Date().timeIntervalSince(start), text: Coach.cleanAnswer(text))
}

func timeLLM(_ req: Coach.ExplanationRequest, label: String) async {
    guard let a = await askLLM(req) else { return }
    print(String(format: "  LLM %@: first text %.2f s, done %.2f s, %d words, prompt about %d tokens", label, a.firstToken, a.total,
                 a.text.split(separator: " ").count, req.approxTokens))
    print("    -> " + a.text.replacingOccurrences(of: "\n", with: " / "))
}

section("engine")
func runEngine() async {
    guard let path = StockfishEngine.findBinary(), let eng = try? StockfishEngine(path: path) else {
        check(false, "Stockfish cannot be started"); return
    }
    func analyze(_ fen: String, ms: Int = 500) async -> EngineResult? { await eng.analyze(fen: fen, movetimeMs: ms) }

    for (fen, expected, mv) in [("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1", HintTheme.captureGain, "d1d5"), ("6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1", .mate, "a1a8")] {
        let (p, side) = parse(fen)
        guard let res = await analyze(fen) else { check(false, "no analysis \(fen)"); continue }
        let h = Hints.build(placement: p, player: side, result: res)
        check(h?.move.uci == mv && h?.theme == expected, "hint \(fen): \(h?.move.uci ?? "-") \(h?.theme.rawValue ?? "-")")
        if let h {
            check(!h.stage3Fallback.contains(UCIMove.squareName(h.move.toFile, h.move.toRank)), "stage 3 without target square")
            check(h.stage4.hasPrefix("Best move: "), "stage 4")
        }
    }
    do {
        let fen = "4k3/pppp4/8/7b/8/8/PPPP4/3QK3 w - - 0 1"
        let (p, side) = parse(fen)
        let res = await analyze(fen)!
        let nullRes = await analyze(p.fen(sideToMove: .black))
        let h = Hints.build(placement: p, player: side, result: res, threatResult: nullRes)
        print("  threat position: best \(h?.move.uci ?? "-"), theme \(h?.theme.rawValue ?? "-"), threat \(h?.threat?.text ?? "none")")
        check(h?.threat?.text.hasPrefix("Threat: Bishop h5 captures Queen d1") == true, "threat detected")
        check(h?.theme == .parryThreat || h?.theme == .check || h?.theme == .captureGain, "theme in threat position: \(h?.theme.rawValue ?? "-")")
    }
    do {
        let fen = "r3k3/8/8/8/8/8/3Q4/4K3 w - - 0 1"
        let (p, side) = parse(fen)
        let before = await analyze(fen)!
        let played = UCIMove("d2a2")!
        let afterP = p.applying(played)
        let after = await analyze(afterP.fen(sideToMove: .black))
        let rev = Coach.review(before: p, played: played, after: afterP, by: side, beforeResult: before, afterResult: after)!
        check(rev.rating == .blunder || rev.rating == .mistake, "queen blunder is a mistake: \(rev.rating.rawValue) \(rev.loss)")
        let nullRes = await analyze(p.fen(sideToMove: .black))
        let an = MoveAnalyzer.analyze(MoveAnalysisInput(review: rev, beforeResult: before, afterResult: after, threatResult: nullRes, moveNumber: 25))
        print("  analysis: \(an.mistakeType?.rawValue ?? "-"), observations \(an.observations.map { "\($0.skill.rawValue)=\($0.value)" })")
        check(an.mistakeType == .hangingPiece, "mistake type hanging piece")
        check(an.example?.hasPrefix("Queen d2–a2") == true, "example text: \(an.example ?? "-")")
        check(an.observations.contains { $0.skill == .pieceSafety && $0.value == 0 }, "piece safety 0")
        check(an.phase == .endgame && an.observations.contains { $0.skill == .endgame }, "endgame phase")
        let puz = Puzzle.make(review: rev, beforeResult: before, mistakeType: an.mistakeType, theme: an.theme, level: .beginner, now: t0)
        check(puz != nil && puz!.acceptedMoves.contains(before.bestMove), "puzzle from mistake")
        check(puz.map { !$0.accepts("d2a2") } ?? false, "the mistake is not accepted")
        if llmURL != nil {
            let ctx = PlayerContext(level: .intermediate, focus: .pieceSafety, typicalMistakes: ["Hanging piece"])
            for l in StyleLength.allCases {
                await timeLLM(Coach.reviewRequest(rev, context: ctx, style: ExplanationStyle(length: l)), label: "style \(l.rawValue)")
            }
            for language in ["German", "English"] {
                let req = Coach.reviewRequest(rev, context: ctx, style: ExplanationStyle(length: .short), language: language)
                guard let a = await askLLM(req) else { check(false, "no \(language) answer"); continue }
                print(String(format: "  LLM answer in %@: first text %.2f s, done %.2f s", language, a.firstToken, a.total))
                print("    -> " + a.text.replacingOccurrences(of: "\n", with: " / "))
                check(!a.text.isEmpty && a.total < 5, "\(language) answer within 5 s")
            }
            if let h = Hints.build(placement: p, player: side, result: before) {
                await timeLLM(Coach.hintRequest(h, context: ctx, language: "German"), label: "hint stage 3 (German)")
            }
        }
    }
    do {
        let fen = "4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1"
        let (p, side) = parse(fen)
        let before = await analyze(fen)!
        let played = UCIMove("e1e2")!
        let afterP = p.applying(played)
        let after = await analyze(afterP.fen(sideToMove: .black))
        let rev = Coach.review(before: p, played: played, after: afterP, by: side, beforeResult: before, afterResult: after)!
        let an = MoveAnalyzer.analyze(MoveAnalysisInput(review: rev, beforeResult: before, afterResult: after, threatResult: nil, moveNumber: 30))
        print("  tactic: \(rev.rating.rawValue) loss \(rev.loss) -> \(an.mistakeType?.rawValue ?? "-")")
        check(an.observations.contains { $0.skill == .tactics && $0.value == 0 } && an.mistakeType == .missedTactic, "missed tactic observed")
    }
    do {
        let startFen = ChessPosition.startFEN
        let (p, side) = parse(startFen)
        let before = await analyze(startFen)!
        let mv = UCIMove("e2e4")!
        let afterP = p.applying(mv)
        let after = await analyze(afterP.fen(sideToMove: .black))
        let rev = Coach.review(before: p, played: mv, after: afterP, by: side, beforeResult: before, afterResult: after)!
        let an = MoveAnalyzer.analyze(MoveAnalysisInput(review: rev, beforeResult: before, afterResult: after, threatResult: nil, moveNumber: 1))
        check(an.mistakeType == nil && an.phase == .opening && an.observations.contains { $0.skill == .opening && $0.value > 0.9 }, "good opening move")
        check(!an.observations.contains { $0.skill == .pieceSafety }, "no piece safety observation without a loose piece")
        let ctx = PlayerContext(level: .intermediate, focus: .threats, typicalMistakes: ["Hanging piece"])
        let req = Coach.reviewRequest(rev, context: ctx, style: ExplanationStyle())
        print("  review prompt about \(req.approxTokens) tokens")
        check(req.approxTokens <= 600, "review prompt at most 600 tokens: \(req.approxTokens)")
        check(req.user.contains("Played move: Pawn e2–e4.") && req.user.contains("White moved."), "review facts are English")
        if let sug = Coach.suggestionRequest(placement: p, player: side, fen: startFen, result: before, context: ctx,
                                             style: ExplanationStyle(length: .long, depth: .concrete, examples: true)) {
            print("  suggestion prompt about \(sug.approxTokens) tokens")
            check(sug.approxTokens <= 600 && sug.maxTokens == 400, "suggestion prompt at most 600 tokens, max_tokens 400")
            check(sug.user.contains("Best move per engine: ") && sug.user.contains("(evaluation: +"), "suggestion facts: \(sug.user)")
        } else { check(false, "suggestion request is nil") }
    }
    do {
        var moves: [ReviewedMove] = []
        var p = parse(ChessPosition.startFEN).placement
        var side = PieceColor.white
        for u in ["f2f3", "e7e5", "g2g4", "d8h4"] {
            let fen = p.fen(sideToMove: side)
            let before = await analyze(fen, ms: 200)!
            let mv = UCIMove(u)!
            let np = p.applying(mv)
            let after = await analyze(np.fen(sideToMove: side.opposite), ms: 200)
            let rev = Coach.review(before: p, played: mv, after: np, by: side, beforeResult: before, afterResult: after)!
            moves.append(ReviewedMove(color: side == .white ? "w" : "b", uci: u, san: nil, fenBefore: fen, valueBefore: before.value,
                                      valueAfter: before.value - rev.loss, bestUci: before.bestMove))
            p = np; side = side.opposite
        }
        let rep = GameReport.build(moves: moves, date: t0, opening: "Test")
        print("  game: white \(Int(rep.white.accuracy)) % acpl \(Int(rep.white.acpl)), black \(Int(rep.black.accuracy)) % acpl \(Int(rep.black.acpl)); moments \(rep.keyMoments.map(\.moveText))")
        check(rep.moves.map(\.san) == ["f3", "e5", "g4", "Qh4#"], "SAN in report: \(rep.moves.map(\.san))")
        check(rep.moves.count == 4 && rep.white.moves == 2 && rep.black.moves == 2, "report counts")
        check(rep.black.accuracy > rep.white.accuracy, "black more accurate than white")
        check(rep.black.best >= 1 && rep.white.blunders + rep.white.mistakes >= 1, "ratings")
        check(rep.keyMoments.contains { $0.color == "w" }, "key moment for white")
        let rq = rep.summaryRequest(for: "w", context: PlayerContext(level: .beginner, focus: .pieceSafety), language: "German")
        check(rq.user.contains("Accuracy") && rq.user.contains("Answer in German. Use exactly:") && rq.approxTokens < 700, "summary prompt")
        check(rep.fallbackSummary(for: "w").hasPrefix("Accuracy "), "fallback summary")
    }
}
await runEngine()

section("simulation of 20 games")
do {
    var rng = SystemRandomNumberGenerator()
    var p = PlayerProfile(name: "Sim", createdAt: t0)
    var focusHistory: [String] = [p.focus]
    var ratings: [Double] = []
    for g in 0..<20 {
        let progress = Double(g) / 19
        for _ in 0..<12 { p.observe(.pieceSafety, value: Double.random(in: 0..<1, using: &rng) < 0.3 + 0.65 * min(1, progress * 2) ? 1 : 0) }
        for _ in 0..<12 { p.observe(.threats, value: Double.random(in: 0..<1, using: &rng) < 0.3 + 0.6 * max(0, progress - 0.3) / 0.7 ? 1 : 0) }
        p.updateFocus()
        focusHistory.append(p.focus)
        p.completeGame(performance: Accuracy.performanceRating(acpl: 120 - 70 * progress), date: t0.addingTimeInterval(Double(g) * day))
        ratings.append(p.rating)
    }
    print("  rating \(Int(ratings.first!)) -> \(Int(ratings.last!)), level \(p.level.title()), focus history \(Array(Set(focusHistory)))")
    check(ratings.last! > ratings[3] + 150, "rating rises")
    check(p.gamesPlayed == 20 && p.ratingHistory.count == 20 && !p.isPlacing, "game counter")
    check(focusHistory.first == "piece_safety" && (focusHistory.contains("threats") || focusHistory.contains("tactics")), "focus changes: \(focusHistory)")
    check(p.level != .beginner, "level rises")
}

struct SeededRNG: RandomNumberGenerator {
    var state: UInt64
    init(seed: UInt64) { state = seed &+ 0x9E3779B97F4A7C15 }
    mutating func next() -> UInt64 {
        state &+= 0x9E3779B97F4A7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58476D1CE4E5B9
        z = (z ^ (z >> 27)) &* 0x94D049BB133111EB
        return z ^ (z >> 31)
    }
}

section("learning style")
do {
    var g = SystemRandomNumberGenerator()
    var a = PlayerProfile(name: "A")
    check(a.style.current.length == .medium && a.style.current.depth == .principle, "starts medium/principle")
    for _ in 0..<12 { a.recordFeedback(.tooLong, usedStyle: a.style.styleForNextExplanation(using: &g).style) }
    check(a.style.current.length == .short, "always too long leads to short, is \(a.style.current.key)")
    var b = PlayerProfile(name: "B")
    for _ in 0..<12 { b.recordFeedback(.tooShort, usedStyle: b.style.styleForNextExplanation(using: &g).style) }
    check(b.style.current.length == .long, "too short leads to long, is \(b.style.current.key)")
    var c = PlayerProfile(name: "C", startStyle: .shortPrinciple)
    for _ in 0..<5 { c.recordFeedback(.helpful, usedStyle: c.style.current) }
    check(c.style.current.length == .short, "helpful keeps the style")
    var d = PlayerProfile(name: "D")
    for _ in 0..<12 { d.recordFeedback(.collapsedImmediately, usedStyle: d.style.styleForNextExplanation(using: &g).style) }
    check(d.style.current.length == .short, "collapsed panel leads to short, is \(d.style.current.key)")
    var e = PlayerProfile(name: "E")
    for _ in 0..<12 { e.recordFeedback(.askedAgain, usedStyle: e.style.styleForNextExplanation(using: &g).style) }
    check(e.style.current.length == .long, "asking again leads to longer, is \(e.style.current.key)")
    var f = PlayerProfile(name: "F")
    var seeded = SeededRNG(seed: 42)
    var explored = 0, total = 0
    for _ in 0..<400 {
        let (s, ex) = f.style.styleForNextExplanation(using: &seeded)
        total += 1; if ex { explored += 1 }
        let pCorrect = s.length == .long ? 0.9 : (s.length == .medium ? 0.5 : 0.3)
        f.recordPuzzleAfterExplanation(usedStyle: s, correct: Double.random(in: 0..<1, using: &seeded) < pCorrect)
    }
    check(f.style.current.length == .long, "success after long explanations leads to long, is \(f.style.current.key)")
    let rate = Double(explored) / Double(total)
    print("  exploration rate \(Int(rate * 100)) % (target 10 %)")
    check(rate > 0.04 && rate < 0.18, "exploration about 10 %")
    var h = PlayerProfile(name: "H")
    for _ in 0..<200 { let (s, _) = h.style.styleForNextExplanation(using: &seeded); h.recordFeedback(.tooLong, usedStyle: s) }
    check(h.style.current.length == .short, "stays short")
    check(ExplanationStyle().neighbors.count == 3 && ExplanationStyle(length: .short).neighbors.count == 2, "neighbour styles")
    check(StyleLength.short.maxTokens == 120 && StyleLength.medium.maxTokens == 220 && StyleLength.long.maxTokens == 400, "max_tokens per style")
    check(StyleLength.short.maxWords == 25 && StyleLength.medium.maxWords == 45 && StyleLength.long.maxWords == 110, "word limits")
}

section("prompts")
do {
    let ctx = PlayerContext(level: .beginner, focus: .pieceSafety, typicalMistakes: ["Hanging piece"])
    let card = Coach.styleCard(context: ctx, style: ExplanationStyle(length: .short, depth: .principle))
    print("  style card: \(card)")
    check(card == "Player: Beginner, focus Piece safety, mistake: Hanging piece. Style: short, principle, max 25 words. Tone: simple words, explain basics. Answer in English.", "style card text")
    var worst = 0
    for l in StyleLength.allCases { for d in StyleDepth.allCases { for lv in PlayerLevel.allCases { for s in Skill.allCases {
        let c = Coach.styleCard(context: PlayerContext(level: lv, focus: s, typicalMistakes: ["Opening principle"]),
                                style: ExplanationStyle(length: l, depth: d, examples: true), language: "Simplified Chinese")
        worst = max(worst, c.count / 4)
    } } } }
    print("  longest style card about \(worst) tokens")
    check(card.count / 4 <= 60 && worst <= 90, "style card at most 60 tokens (90 with glossary) (short \(card.count / 4), worst \(worst))")
    check(Coach.styleCard(context: PlayerContext(level: .beginner, focus: nil), style: ExplanationStyle()).contains("simple words")
          && Coach.styleCard(context: PlayerContext(level: .strong, focus: nil), style: ExplanationStyle(), language: "German").contains("technical terms fine. Answer in German. Use exactly: king=King"), "tone per level and language")
    check(Coach.styleCard(context: PlayerContext(level: .beginner, focus: nil), style: ExplanationStyle(length: .long, depth: .principle), language: "German").contains("; label=Takeaway."), "takeaway label in glossary")
    check(!Coach.styleCard(context: PlayerContext(level: .beginner, focus: nil), style: ExplanationStyle()).contains("Use exactly"), "no glossary in English")
    check(Coach.systemPrompt.hasPrefix("You are a patient chess coach for a hobby player. You receive facts computed by a chess engine."), "fixed system prompt")
    let p = parse(ChessPosition.startFEN).placement
    let r1 = MoveReview(before: p, played: UCIMove("e2e4")!, best: UCIMove("e2e4"), bestScore: .cp(30), playedScore: .cp(30),
                        rating: .best, loss: 0, replyMoves: [], player: .white)
    let q1 = Coach.reviewRequest(r1, context: ctx, style: .init(length: .short), language: "German")
    let q2 = Coach.reviewRequest(r1, context: PlayerContext(level: .strong, focus: .tactics), style: .init(length: .long))
    check(q1.system == q2.system && q1.system == Coach.systemPrompt, "system prompt is identical for all players")
    check(q1.user.contains("Style: short, principle, max 25 words. Tone: simple words, explain basics. Answer in German. Use exactly: king="), "style card at the end")
    check(q1.maxTokens == 120 && q2.maxTokens == 400, "max_tokens passed through")
    let hint = HintSet(move: UCIMove("g1f3")!, theme: .develop, threat: nil, stage1: "", stage2Square: "g1", stage2: "", stage3Fallback: "", stage3Prompt: "facts", stage4: "")
    let hr = Coach.hintRequest(hint, context: ctx, language: "French")
    check(hr.system == Coach.hintSystemPrompt && hr.user.contains("Answer in French. Use exactly:") && hr.maxTokens == 80, "hint request")
    let qr = Coach.questionRequest(placement: p, toMove: .white, question: "Why castle?", language: "Spanish")
    check(qr.system == Coach.questionSystemPrompt && qr.user.contains("Player's question: Why castle?\nAnswer in Spanish. Use exactly:") && qr.user.contains("White pieces: King e1, Queen d1, Rooks a1 h1"), "question request: \(qr.user)")
    check(Coach.cleanAnswer("<think>x</think> Hello") == "Hello" && Coach.cleanAnswer("a<think>b") == "a" && Coach.cleanAnswer("x</think>y") == "y", "think blocks removed")
}

section("storage")
do {
    let dir = FileManager.default.temporaryDirectory.appendingPathComponent("chesscoach-test-\(UUID().uuidString)")
    defer { try? FileManager.default.removeItem(at: dir) }
    let store = Storage(root: dir)
    var a = PlayerProfile(id: "aaa", name: "Tester", createdAt: t0)
    a.completeGame(performance: 1500, date: t0); a.recordMistake(.missedThreat, example: "x"); a.recordHintUse(level: 3)
    a.recordFeedback(.tooLong, usedStyle: a.style.current)
    var b = PlayerProfile(id: "bbb", name: "Partner", createdAt: t0.addingTimeInterval(10))
    b.observe(.tactics, value: 0)
    try! store.save(profile: a); try! store.save(profile: b)
    let loaded = store.loadProfiles()
    check(loaded.count == 2 && loaded[0].id == "aaa" && loaded[1].name == "Partner", "several profiles loaded")
    check(loaded[0] == a, "profile round trip")
    let json = String(data: try! Data(contentsOf: dir.appendingPathComponent("profiles/aaa.json")), encoding: .utf8)!
    check(json.contains("\"schemaVersion\" : 1") && json.contains("\"createdAt\" : \"2027-01-15T08:00:00Z\"") && json.contains("levelUses"), "JSON format: \(json.prefix(200))")
    try! "{\"id\":\"min\",\"name\":\"Min\"}".write(to: dir.appendingPathComponent("profiles/min.json"), atomically: true, encoding: .utf8)
    check(store.loadProfiles().count == 3 && (try? store.loadProfile(id: "min"))?.skills.count == 7, "minimal file readable")
    try! "broken".write(to: dir.appendingPathComponent("profiles/bad.json"), atomically: true, encoding: .utf8)
    check(store.loadProfiles().count == 3, "broken file skipped")
    var ps = PuzzleSet(); ps.add(puzzle()); ps.puzzles[0].attempt(move: "a1a8", now: t0)
    try! store.save(puzzles: ps, profileId: "aaa")
    check(store.loadPuzzles(profileId: "aaa") == ps && store.loadPuzzles(profileId: "none").puzzles.isEmpty, "puzzles save and load")
    let rep = GameReport.build(moves: [], id: "g1", date: t0)
    let url = try! store.save(game: rep, pgn: "1. e4 *")
    check(url.lastPathComponent == "2027-01-15-g1.json" && FileManager.default.fileExists(atPath: url.deletingPathExtension().appendingPathExtension("pgn").path), "game JSON and PGN")
    check(store.loadGames().first == rep, "game loads")
    check(Storage.defaultRoot.path.hasSuffix("Application Support/ChessCoach"), "default path")
}

section("test vectors")
do {
    let data = try! Data(contentsOf: URL(fileURLWithPath: "Tools/testdata/vectors.json"))
    let root = try! JSONSerialization.jsonObject(with: data) as! [String: Any]
    let acc = root["accuracy"] as! [String: Any]
    for v in acc["winChance"] as! [[String: Double]] { check(near(Accuracy.winChance(cp: v["cp"]!), v["win"]!, 1e-6), "vector win \(v)") }
    for v in acc["moveAccuracy"] as! [[String: Double]] { check(near(Accuracy.moveAccuracy(winBefore: v["winBefore"]!, winAfter: v["winAfter"]!), v["accuracy"]!, 1e-6), "vector accuracy \(v)") }
    for v in acc["performance"] as! [[String: Double]] { check(near(Accuracy.performanceRating(acpl: v["acpl"]!), v["rating"]!, 1e-6), "vector performance \(v)") }
    for v in acc["ratingDelta"] as! [[String: Double]] {
        check(near(Accuracy.ratingDelta(rating: v["rating"]!, performance: v["performance"]!, gameNumber: Int(v["gameNumber"]!)), v["delta"]!, 1e-6), "vector delta \(v)")
    }
    for v in (root["hints"] as! [String: Any])["themes"] as! [[String: String]] {
        let t = theme(v["fen"]!, v["best"]!)
        check(t.rawValue == v["theme"]! && t.stage1Text() == v["stage1"]!, "vector theme \(v["theme"]!) was \(t.rawValue)")
    }
    for v in (root["hints"] as! [String: Any])["stage2"] as! [[String: String]] {
        check(Hints.stage2Text(piece: Character(v["piece"]!), square: v["square"]!) == v["text"]!, "vector stage 2 \(v)")
    }
}


section("takeaways and report")
do {
    func mv(_ ply: Int, _ color: String, _ rating: String) -> MoveRecord {
        MoveRecord(ply: ply, color: color, uci: "a1a2", san: nil, text: "", winBefore: 50, winAfter: 50, loss: 0, accuracy: 100, rating: rating, bestText: nil)
    }
    let moves = (1...14).map { mv($0, $0 % 2 == 1 ? "w" : "b", "good") }
    let mistakes = [GameMistake(ply: 3, color: "w", type: "hanging_piece", loss: 300, example: "Qa2", puzzleId: "p1"),
                    GameMistake(ply: 5, color: "w", type: "hanging_piece", loss: 200, example: "Rb3", puzzleId: "p2"),
                    GameMistake(ply: 7, color: "w", type: "missed_tactic", loss: 900, example: "Nf3", puzzleId: "p3"),
                    GameMistake(ply: 4, color: "b", type: "other", loss: 400, example: nil, puzzleId: "p4")]
    let t = Takeaway.build(mistakes: mistakes, moves: moves, color: "w", focus: .threats)
    check(t.count == 3 && t[0].kind == .mistake && t[0].mistakeType == "hanging_piece" && t[0].count == 2, "top mistake by count")
    check(t[0].puzzleIds == ["p1", "p2"] && t[0].example == "Qa2", "puzzle ids and worst example")
    check(t[1].kind == .positive && t[1].streak == 7 && t[2].kind == .habit && t[2].skill == "threats", "positive streak and habit")
    check(t[0].text().contains("Hanging piece") && t[2].text().contains("check what the opponent"), "texts: \(t[0].text())")
    check(Takeaway.build(mistakes: [], moves: [mv(1, "w", "blunder")], color: "w", focus: .tactics).map(\.kind) == [.habit], "no data gives the habit only")
    let rm = [ReviewedMove(color: "w", uci: "e2e4", san: nil, fenBefore: ChessPosition.startFEN, valueBefore: 30, valueAfter: 30, bestUci: "e2e4")]
    let big = (0..<20).flatMap { i -> [ReviewedMove] in
        [ReviewedMove(color: "w", uci: "e2e4", san: nil, fenBefore: ChessPosition.startFEN, valueBefore: 30, valueAfter: i < 6 ? -300 : 30, bestUci: "e2e4", hint: i == 19 ? 4 : nil),
         ReviewedMove(color: "b", uci: "e7e5", san: nil, fenBefore: ChessPosition.startFEN, valueBefore: 30, valueAfter: 30, bestUci: "e7e5")]
    }
    let rep = GameReport.build(moves: big, date: t0)
    check(rep.white.ratedMoves == 13 && rep.white.countsForRating, "book moves and hinted moves are excluded: \(rep.white.ratedMoves ?? -1)")
    check(near(rep.white.assistedShare ?? 0, 1.0 / 20), "assisted share")
    check(!GameReport.build(moves: rm, date: t0).white.countsForRating, "one move is not rated")
    var ten = (0..<12).map { _ in ReviewedMove(color: "w", uci: "e2e4", san: nil, fenBefore: ChessPosition.startFEN, valueBefore: 30, valueAfter: 30, bestUci: "e2e4") }
    ten += ten.map { var m = $0; m.color = "b"; return m }
    check(GameReport.build(moves: ten, date: t0).white.countsForRating, "12 own moves are rated")
    check(MoveRating.from(valueBefore: 900, valueAfter: 600, wasBest: false) != .blunder, "+9 to +6 is not a blunder")
    check(MoveRating.from(valueBefore: 0, valueAfter: -400, wasBest: false) == .blunder && MoveRating.from(valueBefore: 0, valueAfter: -150, wasBest: false) == .inaccuracy, "win-chance thresholds")
    let colored = (0..<5).flatMap { i in [ReviewedMove(color: "w", uci: "e2e4", san: nil, fenBefore: ChessPosition.startFEN, valueBefore: 200, valueAfter: -300 + i, bestUci: "d2d4"),
                                         ReviewedMove(color: "b", uci: "e7e5", san: nil, fenBefore: ChessPosition.startFEN, valueBefore: 0, valueAfter: 0, bestUci: "e7e5")] }
    check(GameReport.build(moves: colored, date: t0).keyMoments.filter { $0.color == "w" }.count == 3, "three key moments for the player")
    check(Curriculum.needsThinkDelay(level: 1, elapsed: 5, seconds: 15, nudged: false) && !Curriculum.needsThinkDelay(level: 1, elapsed: 5, seconds: 15, nudged: true)
          && !Curriculum.needsThinkDelay(level: 0, elapsed: 0, seconds: 15, nudged: false) && !Curriculum.needsThinkDelay(level: 2, elapsed: 20, seconds: 15, nudged: false), "think delay")
    check(Curriculum.gainWeight(hintLevel: 0) == 1 && Curriculum.gainWeight(hintLevel: 2) == 0.5 && Curriculum.gainWeight(hintLevel: 3) == 0, "gain weights")
    check(Curriculum.showsThreatAutomatically(gamesPlayed: 2, skills: [:]) && !Curriculum.showsThreatAutomatically(gamesPlayed: 3, skills: ["threats": 0.5])
          && Curriculum.showsThreatAutomatically(gamesPlayed: 9, skills: ["threats": 0.3]), "threat auto rule")
    check(Curriculum.supportFaded(skills: ["threats": 0.85, "piece_safety": 0.9], counts: ["threats": 20, "piece_safety": 25])
          && !Curriculum.supportFaded(skills: ["threats": 0.85, "piece_safety": 0.9], counts: ["threats": 19, "piece_safety": 25]), "support fade rule")
    var cs = LearningStyle()
    cs.recordPuzzle(after: ExplanationStyle(), correct: true)
    var pp = PlayerProfile(name: "x")
    pp.recordPuzzleAfterExplanation(usedStyle: ExplanationStyle(), correct: true, assisted: true)
    check(pp.style.scores.isEmpty, "assisted solves do not feed the style bandit")
    check(Coach.cleanAnswer("Hello <thi") == "Hello" && Coach.cleanAnswer("a <think>x</think> b") == "a  b", "partial think tag held back")
    let (pl, sd) = parse("4k3/8/8/8/8/8/8/4K2R w - - 0 1")
    let res = EngineResult(fen: "x", bestMove: "h1h2", lines: [])
    check(Coach.review(before: pl, played: UCIMove("h1h2")!, after: pl.applying(UCIMove("h1h2")!), by: sd, beforeResult: res, afterResult: nil) == nil, "no review without an after result")
}

print("\(checks) checks, \(failures) failures")
exit(failures == 0 ? 0 : 1)
