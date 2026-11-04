using System.Globalization;
using System.Text.Json;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class LearningTests
{
    static readonly DateTime T0 = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000).UtcDateTime;
    const double Day = 86_400;
    static JsonElement Vectors { get; } = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSupport.TestData, "vectors.json"))).RootElement;

    static void Near(double expected, double actual, double eps = 0.01) => Assert.True(Math.Abs(expected - actual) <= eps, $"expected {expected}, was {actual}");

    [Fact]
    public void AccuracyFormulas()
    {
        Near(50, Accuracy.WinChance(0));
        Assert.True(Accuracy.WinChance(10_000) > 99.9 && Accuracy.WinChance(-10_000) < 0.1);
        Near(100, Accuracy.MoveAccuracy(50, 50), 0.001);
        Assert.True(Accuracy.MoveAccuracy(90, 10) < 5);
        Assert.Equal(100, Accuracy.MoveAccuracy(50, 60));
        Assert.Equal(1000, Accuracy.CpLoss(500, -9000));
        Assert.Equal(0, Accuracy.CpLoss(10, 50));
        Near((10 + 20 + 1000) / 3.0, Accuracy.Acpl(new[] { 10, 20, 3000 }));
        Near(75, Accuracy.GameAccuracy(new[] { 100.0, 50 }));
        Near(2250, Accuracy.PerformanceRating(15));
        Near(1100, Accuracy.PerformanceRating(125));
        Near(50, Accuracy.RatingDelta(1000, 1400, 4));
        Near(25, Accuracy.RatingDelta(1000, 1400, 11));
        Near(150, Accuracy.RatingDelta(600, 2400, 4));
        Near(-150, Accuracy.RatingDelta(2400, 600, 4));
        Assert.Equal(PlayerLevel.Beginner, PlayerLevelExt.FromRating(1199));
        Assert.Equal(PlayerLevel.Intermediate, PlayerLevelExt.FromRating(1800));
        Assert.Equal(PlayerLevel.Strong, PlayerLevelExt.FromRating(1801));
    }

    [Fact]
    public void AccuracyVectors()
    {
        var a = Vectors.GetProperty("accuracy");
        foreach (var v in a.GetProperty("winChance").EnumerateArray()) Near(v.GetProperty("win").GetDouble(), Accuracy.WinChance(v.GetProperty("cp").GetDouble()), 1e-6);
        foreach (var v in a.GetProperty("moveAccuracy").EnumerateArray())
            Near(v.GetProperty("accuracy").GetDouble(), Accuracy.MoveAccuracy(v.GetProperty("winBefore").GetDouble(), v.GetProperty("winAfter").GetDouble()), 1e-6);
        foreach (var v in a.GetProperty("performance").EnumerateArray()) Near(v.GetProperty("rating").GetDouble(), Accuracy.PerformanceRating(v.GetProperty("acpl").GetDouble()), 1e-6);
        foreach (var v in a.GetProperty("ratingDelta").EnumerateArray())
            Near(v.GetProperty("delta").GetDouble(), Accuracy.RatingDelta(v.GetProperty("rating").GetDouble(), v.GetProperty("performance").GetDouble(), v.GetProperty("gameNumber").GetInt32()), 1e-6);
        foreach (var v in a.GetProperty("levels").EnumerateArray())
            Assert.Equal(v.GetProperty("level").GetString(), PlayerLevelExt.FromRating(v.GetProperty("rating").GetDouble()).Raw());
        foreach (var v in a.GetProperty("cpLoss").EnumerateArray())
            Assert.Equal(v.GetProperty("loss").GetInt32(), Accuracy.CpLoss(v.GetProperty("before").GetInt32(), v.GetProperty("after").GetInt32()));
    }

    [Fact]
    public void PlacementAndRating()
    {
        var p = new PlayerProfile("Alex", "p1", T0);
        Assert.Equal("Placement in progress: game 1 of 3", p.PlacementText);
        p.CompleteGame(1000, T0);
        Assert.Equal("Placement in progress: game 2 of 3", p.PlacementText); Near(1000, p.Rating);
        p.CompleteGame(1400, T0); Near(1200, p.Rating);
        p.CompleteGame(1600, T0);
        Assert.False(p.IsPlacing); Near(1333.33, p.Rating); Assert.Equal(PlayerLevel.Intermediate, p.Level);
        Assert.Null(p.PlacementText);
        double r3 = p.Rating;
        p.CompleteGame(1800, T0);
        Near(r3 + 0.5 * (1800 - r3) / 4, p.Rating);
        Assert.Equal(4, p.RatingHistory.Count); Assert.Equal(4, p.GamesPlayed);
    }

    [Fact]
    public void ProfileVectors()
    {
        var pr = Vectors.GetProperty("profile");
        var p = new PlayerProfile("X");
        p.Observe(Skill.Tactics, 1);
        Near(pr.GetProperty("skillUpdate").GetProperty("new").GetDouble(), p.Skills["tactics"], 1e-9);
        var perfs = pr.GetProperty("placement")[0].GetProperty("performances").EnumerateArray().Select(x => x.GetDouble()).ToList();
        var after = pr.GetProperty("placement")[0].GetProperty("ratingAfter").EnumerateArray().Select(x => x.GetDouble()).ToList();
        var q = new PlayerProfile("Q");
        for (int i = 0; i < perfs.Count; i++) { q.CompleteGame(perfs[i], T0); Near(after[i], q.Rating, 0.001); }
        Assert.Equal(pr.GetProperty("learningPath").EnumerateArray().Select(x => x.GetString()!).ToList(), Curriculum.Path.Select(s => s.Skill.Raw()).ToList());
        Assert.Equal(pr.GetProperty("mistakeKeys").EnumerateArray().Select(x => x.GetString()!).ToList(), MistakeTypeExt.All.Select(m => m.Raw()).ToList());
        Assert.Equal(pr.GetProperty("levelKeys").EnumerateArray().Select(x => x.GetString()!).ToList(), PlayerLevelExt.All.Select(l => l.Raw()).ToList());
        foreach (var l in StyleLengthExt.All) Assert.Equal(pr.GetProperty("styleMaxTokens").GetProperty(l.Raw()).GetInt32(), l.MaxTokens());
        Near(pr.GetProperty("focusThreshold").GetDouble(), Curriculum.FocusThreshold);
    }

    [Fact]
    public void SkillsHintsMistakes()
    {
        var p = new PlayerProfile("X");
        p.RecordHintUse(0); p.RecordHintUse(2); p.RecordHintUse(2); p.RecordHintUse(9);
        Assert.Equal(new[] { 1, 0, 2, 0, 1 }, p.HintStats.LevelUses);
        Near((2 + 2 + 4) / 3.0, p.AverageHintLevel ?? 0);
        p.RecordMistake(MistakeType.HangingPiece, "a"); p.RecordMistake(MistakeType.HangingPiece, "b"); p.RecordMistake(MistakeType.Other);
        Assert.Equal((MistakeType.HangingPiece, 2), p.TopMistakes(1)[0]);
        for (int i = 0; i < 7; i++) p.RecordMistake(MistakeType.MissedTactic, "e" + i);
        Assert.Equal(5, p.MistakeExamples!["missed_tactic"].Count);
        Assert.Equal("e6", p.MistakeExamples["missed_tactic"][^1]);
    }

    [Fact]
    public void FocusAndAdaptation()
    {
        var sk = SkillExt.All.ToDictionary(s => s.Raw(), _ => 0.9);
        sk["tactics"] = 0.6; sk["endgame"] = 0.2;
        Assert.Equal(Skill.Tactics, Curriculum.Focus(sk));
        sk["tactics"] = 0.9; sk["opening"] = 0.69;
        Assert.Equal(Skill.Opening, Curriculum.Focus(sk));
        sk["opening"] = 0.9; sk["endgame"] = 0.95; sk["middlegame"] = 0.75; sk["king_safety"] = 0.8;
        Assert.Equal(Skill.Middlegame, Curriculum.Focus(sk));
        var b = Curriculum.Adapt(PlayerLevel.Beginner, Skill.PieceSafety, new Dictionary<string, double> { ["piece_safety"] = 0.5 }, 0.78);
        Assert.True(b.DefaultHintLevel == 2 && b.DifficultyBias == 0 && b.IntervalFactor == 1);
        var hi = Curriculum.Adapt(PlayerLevel.Intermediate, Skill.Tactics, new Dictionary<string, double> { ["tactics"] = 0.5 }, 0.95);
        Assert.True(hi.DefaultHintLevel == 1 && hi.DifficultyBias == 1 && hi.IntervalFactor > 1);
        var lo = Curriculum.Adapt(PlayerLevel.Intermediate, Skill.Tactics, new Dictionary<string, double> { ["tactics"] = 0.5 }, 0.5);
        Assert.True(lo.DefaultHintLevel == 2 && lo.DifficultyBias == -1 && lo.IntervalFactor < 1 && lo.NewPuzzlesPerDay < 10);
        Assert.Equal("Learning focus: Piece safety", Curriculum.FocusLabel(Skill.PieceSafety));
    }

    static HintTheme Theme(string fen, string mv, ThreatFact? threat = null)
    {
        var (p, side) = FenReader.Parse(fen)!.Value;
        return Hints.Theme(p, UciMove.Parse(mv)!.Value, side, threat);
    }

    [Fact]
    public void HintThemes()
    {
        Assert.Equal(HintTheme.CaptureGain, Theme("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1", "d1d5"));
        Assert.Equal(HintTheme.Fork, Theme("r3k3/8/8/1N6/8/8/8/4K3 w - - 0 1", "b5c7"));
        Assert.Equal(HintTheme.Check, Theme("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", "a1a8"));
        Assert.Equal(HintTheme.Develop, Theme("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "g1f3"));
        Assert.Equal(HintTheme.Castle, Theme("r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 1", "e1g1"));
        Assert.Equal(HintTheme.PawnMove, Theme("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4"));
        Assert.Equal(HintTheme.KingActivate, Theme("8/8/4k3/8/8/4K3/8/8 w - - 0 1", "e3e4"));
        Assert.Equal(HintTheme.ImprovePiece, Theme("r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 0 1", "d1e2"));
        var fen = "4k3/8/8/7b/8/8/8/3RK3 w - - 0 1";
        var (p, _) = FenReader.Parse(fen)!.Value;
        var th = new ThreatFact(UciMove.Parse("h5d1")!.Value, 200, "Threat: Bishop h5 captures Rook d1");
        Assert.True(th.IsParried(UciMove.Parse("d1c1")!.Value, p));
        Assert.True(th.IsParried(UciMove.Parse("d1d5")!.Value, p));
        Assert.False(th.IsParried(UciMove.Parse("e8e7")!.Value, p));
        Assert.Equal(HintTheme.ParryThreat, Theme(fen, "d1c1", th));
        Assert.Equal(HintTheme.Mate, Hints.Theme(FenReader.Parse("6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1")!.Value.Placement, UciMove.Parse("a1a8")!.Value, PieceColor.White, null, true));
        Assert.Null(FenReader.Parse("x"));
        Assert.True(FenReader.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")!.Value.Placement.IsStartPosition);
        Assert.Equal("Move the knight on g1.", Hints.Stage2Text('N', "g1"));
        Assert.Equal("Move the queen on d8.", Hints.Stage2Text('q', "d8"));
        {
            var (sp, side) = FenReader.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")!.Value;
            var res = new EngineResult(sp.Fen(side), "d2d4", new[] { new EngineLine("d2d4", 30, null, new[] { "d2d4" }) });
            var h = Hints.Build(sp, side, res)!;
            Assert.Equal("d2", h.Stage2Square);
            Assert.Equal("Move the pawn on d2.", h.Stage2);
            Assert.Equal("Zieh mit dem Bauern auf d2.", Hints.Stage2Text('P', "d2", new CultureInfo("de")));
            Assert.Equal("Zieh mit der Dame auf d1.", Hints.Stage2Text('Q', "d1", new CultureInfo("de")));
            Assert.DoesNotContain(new[] { h.Stage1, h.Stage2, h.Stage3Fallback }, t => t.Contains("d4"));
        }
        Assert.True(Hints.RevealsTarget("Please go to f3", UciMove.Parse("g1f3")!.Value));
        Assert.False(Hints.RevealsTarget("Develop the knight", UciMove.Parse("g1f3")!.Value));
        foreach (var t in HintThemeExt.All) { Assert.NotEmpty(t.Stage1Text()); Assert.NotEmpty(Hints.Stage3Fallback(t, 'N')); }
    }

    [Fact]
    public void HintThemeVectors()
    {
        foreach (var v in Vectors.GetProperty("hints").GetProperty("themes").EnumerateArray())
        {
            var t = Theme(v.GetProperty("fen").GetString()!, v.GetProperty("best").GetString()!);
            Assert.Equal(v.GetProperty("theme").GetString(), t.Raw());
            Assert.Equal(v.GetProperty("stage1").GetString(), t.Stage1Text());
        }
    }

    static Puzzle MakePuzzle(string fen = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", string[]? accepted = null, MistakeType type = MistakeType.MissedTactic) =>
        new() { Id = "z", Fen = fen, AcceptedMoves = (accepted ?? new[] { "a1a8" }).ToList(), BestLine = new() { "a1a8" }, Theme = "check", MistakeType = type.Raw(), CreatedAt = T0, DueAt = T0 };

    [Fact]
    public void PuzzleBoxes()
    {
        var z = MakePuzzle();
        Assert.True(z.IsNew && z.Box == 1);
        z.Attempt("a1a8", now: T0);
        Assert.Equal(2, z.Box); Near(3 * Day, (z.DueAt - T0).TotalSeconds, 1);
        z.Attempt("a1a8", 2, T0); Assert.Equal(2, z.Box);
        z.Attempt("a1a8", now: T0); z.Attempt("a1a8", now: T0);
        Assert.Equal(4, z.Box); Near(16 * Day, (z.DueAt - T0).TotalSeconds, 1);
        z.Attempt("a1a2", now: T0);
        Assert.True(z.Box == 1 && !z.Mastered); Near(Day, (z.DueAt - T0).TotalSeconds, 1);
        for (int i = 0; i < 5; i++) z.Attempt("a1a8", now: T0, learningSpeed: 1.5);
        Assert.True(z.Mastered);
        var z2 = MakePuzzle(); z2.Attempt("a1a8", now: T0, learningSpeed: 1.5); Near(4.5 * Day, (z2.DueAt - T0).TotalSeconds, 1);
        var z3 = MakePuzzle(); z3.Attempt("a1a8", now: T0, learningSpeed: 0.6); Near(1.8 * Day, (z3.DueAt - T0).TotalSeconds, 1);
        Assert.True(MakePuzzle(accepted: new[] { "e2e4", "d2d4" }).Accepts("D2D4"));
        Assert.False(MakePuzzle().Accepts("a1a7"));
    }

    [Fact]
    public void PuzzleTransitionVectors()
    {
        var pz = Vectors.GetProperty("puzzles");
        foreach (var t in pz.GetProperty("transitions").EnumerateArray())
        {
            var z = MakePuzzle(); z.Box = t.GetProperty("box").GetInt32();
            var move = t.GetProperty("move").GetString() == "accepted" ? "a1a8" : "a1a2";
            z.Attempt(move, t.GetProperty("hintLevel").GetInt32(), T0, t.TryGetProperty("learningSpeed", out var ls) ? ls.GetDouble() : 1.0);
            var res = t.GetProperty("result");
            Assert.Equal(res.GetProperty("box").GetInt32(), z.Box);
            Assert.Equal(res.GetProperty("mastered").GetBoolean(), z.Mastered);
            Near(res.GetProperty("dueDays").GetDouble() * Day, (z.DueAt - T0).TotalSeconds, 1);
        }
        foreach (var t in pz.GetProperty("learningSpeed").EnumerateArray())
        {
            var p = MakePuzzle();
            for (int i = 0; i < t.GetProperty("attempts").GetInt32(); i++)
                p.Attempts.Add(new PuzzleAttempt { Date = T0.AddSeconds(i), Correct = i < t.GetProperty("correct").GetInt32() });
            Near(t.GetProperty("next").GetDouble(), PuzzleScheduler.AdjustedLearningSpeed(t.GetProperty("current").GetDouble(), new[] { p }));
        }
        Assert.Equal(PuzzleScheduler.MaxNewPerDay, pz.GetProperty("maxNewPerDay").GetInt32());
        Assert.Equal(Puzzle.AcceptTolerance, pz.GetProperty("acceptTolerance").GetInt32());
    }

    static List<Puzzle> WithAttempts(int n, int correct)
    {
        var p = MakePuzzle();
        for (int i = 0; i < n; i++) p.Attempts.Add(new PuzzleAttempt { Date = T0.AddSeconds(i), Correct = i < correct });
        return new() { p };
    }

    [Fact]
    public void LearningSpeedAndSelection()
    {
        Near(1.1, PuzzleScheduler.AdjustedLearningSpeed(1.0, WithAttempts(30, 28)));
        Near(0.9, PuzzleScheduler.AdjustedLearningSpeed(1.0, WithAttempts(30, 15)));
        Near(1.0, PuzzleScheduler.AdjustedLearningSpeed(1.0, WithAttempts(30, 23)));
        Near(1.5, PuzzleScheduler.AdjustedLearningSpeed(1.5, WithAttempts(30, 30)));
        Near(0.6, PuzzleScheduler.AdjustedLearningSpeed(0.6, WithAttempts(30, 0)));
        Near(1.0, PuzzleScheduler.AdjustedLearningSpeed(1.0, WithAttempts(5, 5)));

        var set = new PuzzleSet();
        for (int i = 0; i < 14; i++)
        {
            var p = MakePuzzle(type: i < 3 ? MistakeType.HangingPiece : MistakeType.MissedTactic);
            p.Id = "n" + i; p.Fen = "f" + i; p.DueAt = T0.AddSeconds(-i); set.Add(p);
        }
        var oldAttempt = new PuzzleAttempt { Date = T0.AddSeconds(-9 * Day), Correct = true };
        var dueNotFocus = MakePuzzle(type: MistakeType.MissedTactic); dueNotFocus.Id = "d1"; dueNotFocus.Fen = "fd1"; dueNotFocus.Attempts = new() { oldAttempt }; dueNotFocus.DueAt = T0.AddSeconds(-1);
        var dueFocus = MakePuzzle(type: MistakeType.HangingPiece); dueFocus.Id = "d2"; dueFocus.Fen = "fd2"; dueFocus.Attempts = new() { oldAttempt }; dueFocus.DueAt = T0.AddSeconds(-1);
        var future = MakePuzzle(); future.Id = "fut"; future.Fen = "ff"; future.Attempts = new() { oldAttempt }; future.DueAt = T0.AddSeconds(Day);
        Assert.False(set.Add(set.Puzzles[0]));
        set.Puzzles.AddRange(new[] { dueNotFocus, dueFocus, future });
        var sel = PuzzleScheduler.TodaysSelection(set.Puzzles, Skill.PieceSafety, T0);
        Assert.Equal(12, sel.Count);
        Assert.Equal("d2", sel[0].Id);
        Assert.All(sel.Skip(2).Take(3), x => Assert.Equal(Skill.PieceSafety, x.Skill));
        Assert.DoesNotContain(sel, x => x.Id == "fut");
        var started = MakePuzzle(); started.Id = "s"; started.Fen = "s"; started.Attempts = new() { new PuzzleAttempt { Date = T0, Correct = true } }; started.DueAt = T0.AddSeconds(3 * Day);
        var sel2 = PuzzleScheduler.TodaysSelection(set.Puzzles.Append(started).ToList(), null, T0);
        Assert.Equal(9, sel2.Count(x => x.IsNew));
        var st = PuzzleScheduler.Stats(set.Puzzles, T0);
        Assert.True(st.Total == 17 && st.New == 14 && st.Due == 2, $"{st.Total}/{st.New}/{st.Due}");
    }

    [Fact]
    public void StyleLearning()
    {
        var g = new Random(7);
        var a = new PlayerProfile("A");
        Assert.True(a.Style.Current.Length == StyleLength.Medium && a.Style.Current.Depth == StyleDepth.Principle);
        for (int i = 0; i < 12; i++) a.RecordFeedback(ExplanationFeedback.TooLong, a.Style.StyleForNextExplanation(g).Style);
        Assert.Equal(StyleLength.Short, a.Style.Current.Length);
        var b = new PlayerProfile("B");
        for (int i = 0; i < 12; i++) b.RecordFeedback(ExplanationFeedback.TooShort, b.Style.StyleForNextExplanation(g).Style);
        Assert.Equal(StyleLength.Long, b.Style.Current.Length);
        var c = new PlayerProfile("C", startStyle: ExplanationStyle.ShortPrinciple);
        Assert.Equal(StyleLength.Short, c.Style.Current.Length);
        for (int i = 0; i < 5; i++) c.RecordFeedback(ExplanationFeedback.Helpful, c.Style.Current);
        Assert.Equal(StyleLength.Short, c.Style.Current.Length);
        var d = new PlayerProfile("D");
        for (int i = 0; i < 12; i++) d.RecordFeedback(ExplanationFeedback.CollapsedImmediately, d.Style.StyleForNextExplanation(g).Style);
        Assert.Equal(StyleLength.Short, d.Style.Current.Length);
        var e = new PlayerProfile("E");
        for (int i = 0; i < 12; i++) e.RecordFeedback(ExplanationFeedback.AskedAgain, e.Style.StyleForNextExplanation(g).Style);
        Assert.Equal(StyleLength.Long, e.Style.Current.Length);

        var f = new PlayerProfile("F"); var seeded = new Random(42);
        int explored = 0, total = 0;
        for (int i = 0; i < 400; i++)
        {
            var (s, ex) = f.Style.StyleForNextExplanation(seeded);
            total++; if (ex) explored++;
            double pc = s.Length == StyleLength.Long ? 0.9 : s.Length == StyleLength.Medium ? 0.5 : 0.3;
            f.RecordPuzzleAfterExplanation(s, seeded.NextDouble() < pc);
        }
        Assert.Equal(StyleLength.Long, f.Style.Current.Length);
        double rate = (double)explored / total;
        Assert.InRange(rate, 0.04, 0.18);
        var h = new PlayerProfile("H");
        for (int i = 0; i < 200; i++) h.RecordFeedback(ExplanationFeedback.TooLong, h.Style.StyleForNextExplanation(seeded).Style);
        Assert.Equal(StyleLength.Short, h.Style.Current.Length);
        Assert.Equal(3, new ExplanationStyle().Neighbors.Count);
        Assert.Equal(2, new ExplanationStyle(StyleLength.Short).Neighbors.Count);
        Assert.Equal(new[] { 120, 220, 400 }, StyleLengthExt.All.Select(l => l.MaxTokens()).ToArray());
        Assert.Equal(new[] { 25, 45, 110 }, StyleLengthExt.All.Select(l => l.MaxWords()).ToArray());
    }

    [Fact]
    public void Simulation20Games()
    {
        var rng = new Random(3);
        var p = new PlayerProfile("Sim", createdAt: T0);
        var focusHistory = new List<string> { p.Focus };
        var ratings = new List<double>();
        for (int g = 0; g < 20; g++)
        {
            double progress = g / 19.0;
            for (int i = 0; i < 12; i++) p.Observe(Skill.PieceSafety, rng.NextDouble() < 0.3 + 0.65 * Math.Min(1, progress * 2) ? 1 : 0);
            for (int i = 0; i < 12; i++) p.Observe(Skill.Threats, rng.NextDouble() < 0.3 + 0.6 * Math.Max(0, progress - 0.3) / 0.7 ? 1 : 0);
            p.UpdateFocus(); focusHistory.Add(p.Focus);
            p.CompleteGame(Accuracy.PerformanceRating(120 - 70 * progress), T0.AddSeconds(g * Day));
            ratings.Add(p.Rating);
        }
        Assert.True(ratings[^1] > ratings[3] + 150);
        Assert.True(p.GamesPlayed == 20 && p.RatingHistory.Count == 20 && !p.IsPlacing);
        Assert.NotEqual(PlayerLevel.Beginner, p.Level);
    }

    [Fact]
    public void StyleCardAndPrompts()
    {
        var ctx = new PlayerContext(PlayerLevel.Beginner, Skill.PieceSafety, new[] { "Hanging piece" });
        var card = Coach.StyleCard(ctx, new ExplanationStyle(StyleLength.Short, StyleDepth.Principle));
        Assert.StartsWith("Player: Beginner, focus Piece safety, mistake: Hanging piece. Style: short, principle, max 25 words. Tone: simple words, explain basics.", card);
        Assert.True(card.Length / 4 <= 60);
        foreach (var l in StyleLengthExt.All) foreach (var d in StyleDepthExt.All) foreach (var lv in PlayerLevelExt.All)
            Assert.True(Coach.StyleCard(new PlayerContext(lv, Skill.Threats, new[] { "Missed threat" }), new ExplanationStyle(l, d, true)).Length / 4 <= 90);
        var de = new CultureInfo("de");
        var german = Coach.StyleCard(new PlayerContext(PlayerLevel.Beginner, Skill.PieceSafety, new[] { "Hanging piece" }), new ExplanationStyle(StyleLength.Long, StyleDepth.Concrete, true), de);
        Assert.Contains("king=König, queen=Dame, rook=Turm, bishop=Läufer, knight=Springer, pawn=Bauer", german);
        Assert.Contains("label=Merksatz.", german);
        Assert.True(german.Length / 4 <= 90);
        Assert.DoesNotContain("Use exactly", Coach.StyleCard(new PlayerContext(PlayerLevel.Beginner), new ExplanationStyle(), CoreLocale.English));
        Assert.Contains("simple words", Coach.StyleCard(new PlayerContext(PlayerLevel.Beginner), new ExplanationStyle()));
        Assert.Contains("technical terms", Coach.StyleCard(new PlayerContext(PlayerLevel.Strong), new ExplanationStyle()));
        Assert.StartsWith("You are a patient chess coach", Prompts.System);
        var p = FenReader.Parse("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")!.Value.Placement;
        var e2e4 = UciMove.Parse("e2e4")!.Value;
        var r1 = new MoveReview(p, e2e4, "Pawn e2–e4", e2e4, "Pawn e2–e4", "+0.3", "+0.3", MoveRating.Best, 0, "", PieceColor.White);
        var q1 = Coach.ReviewRequest(r1, ctx, new ExplanationStyle(StyleLength.Short));
        var q2 = Coach.ReviewRequest(r1, new PlayerContext(PlayerLevel.Strong, Skill.Tactics), new ExplanationStyle(StyleLength.Long));
        Assert.Equal(q1.System, q2.System);
        Assert.Contains("Style: short", q1.User);
        Assert.True(q1.MaxTokens == 120 && q2.MaxTokens == 400);
        Assert.Contains("Played move", q1.User);
        Assert.True(q1.ApproxTokens <= 600);
    }

    [Fact]
    public void StorageRoundTripAndSwiftFormat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chesscoach-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Storage(dir);
            var a = new PlayerProfile("Alex", "aaa", T0);
            a.CompleteGame(1500, T0); a.RecordMistake(MistakeType.MissedThreat, "x"); a.RecordHintUse(3);
            a.RecordFeedback(ExplanationFeedback.TooLong, a.Style.Current);
            var b = new PlayerProfile("Partner", "bbb", T0.AddSeconds(10)); b.Observe(Skill.Tactics, 0);
            store.Save(a); store.Save(b);
            var loaded = store.LoadProfiles();
            Assert.Equal(2, loaded.Count);
            Assert.True(loaded[0].Id == "aaa" && loaded[1].Name == "Partner");
            Assert.Equal(ChessCoachJson.Serialize(a), ChessCoachJson.Serialize(loaded[0]));
            var json = File.ReadAllText(Path.Combine(dir, "profiles", "aaa.json"));
            Assert.Contains("\"schemaVersion\": 1", json);
            Assert.Contains("\"createdAt\": \"2027-01-15T08:00:00Z\"", json);
            Assert.Contains("\"levelUses\"", json);
            Assert.Contains("\"level\": \"intermediate\"", json);
            Assert.Contains("\"missed_threat\"", json);

            File.WriteAllText(Path.Combine(dir, "profiles", "min.json"), "{\"id\":\"min\",\"name\":\"Min\"}");
            Assert.Equal(3, store.LoadProfiles().Count);
            Assert.Equal(7, store.LoadProfile("min").Skills.Count);
            File.WriteAllText(Path.Combine(dir, "profiles", "bad.json"), "kaputt");
            Assert.Equal(3, store.LoadProfiles().Count);

            File.WriteAllText(Path.Combine(dir, "profiles", "sw.json"),
                "{\"schemaVersion\":1,\"id\":\"sw\",\"name\":\"Mac\",\"createdAt\":\"2027-01-15T08:00:00Z\",\"rating\":1500,\"level\":\"intermediate\",\"skills\":{\"tactics\":0.3},\"focus\":\"tactics\",\"extra\":1,\"style\":{\"length\":\"short\",\"depth\":\"concrete\",\"examples\":false,\"tone\":\"auto\",\"scores\":{\"short|concrete\":{\"mean\":0.6,\"n\":2,\"puzzleCorrect\":1,\"puzzleTotal\":2}}}}");
            var sw = store.LoadProfile("sw");
            Assert.Equal(PlayerLevel.Intermediate, sw.Level); Assert.Equal(Skill.Tactics, sw.FocusSkill);
            Assert.Equal(StyleLength.Short, sw.Style.Length); Assert.Equal(0.6, sw.Style.Scores["short|concrete"].Mean);
            Assert.Equal(7, sw.Skills.Count);

            var ps = new PuzzleSet(); ps.Add(MakePuzzle()); ps.Puzzles[0].Attempt("a1a8", now: T0);
            store.Save(ps, "aaa");
            Assert.Equal(ChessCoachJson.Serialize(ps), ChessCoachJson.Serialize(store.LoadPuzzles("aaa")));
            Assert.Empty(store.LoadPuzzles("none").Puzzles);
            var pjson = File.ReadAllText(Path.Combine(dir, "puzzles", "aaa.json"));
            Assert.Contains("\"acceptedMoves\"", pjson); Assert.Contains("\"hintLevel\"", pjson); Assert.Contains("\"dueAt\"", pjson);

            var rep = GameReport.Build(Array.Empty<ReviewedMove>(), "g1", T0);
            var path = store.Save(rep, "1. e4 *");
            Assert.Equal("2027-01-15-g1.json", Path.GetFileName(path));
            Assert.True(File.Exists(Path.ChangeExtension(path, ".pgn")));
            Assert.Equal(ChessCoachJson.Serialize(rep), ChessCoachJson.Serialize(store.LoadGames()[0]));
            Assert.EndsWith("ChessCoach", Storage.DefaultRoot);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void GameReportFromFools()
    {
        var moves = new List<ReviewedMove>();
        var pos = ChessPosition.Start;

        var data = new (string Uci, int Before, int After)[] { ("f2f3", 20, -30), ("e7e5", 30, 40), ("g2g4", -40, -600), ("d8h4", 600, 10000) };
        foreach (var (u, b, a) in data)
        {
            moves.Add(new ReviewedMove(pos.Turn == PieceColor.White ? "w" : "b", u, null, pos.Fen, b, a, null));
            pos = pos.Applying(UciMove.Parse(u)!.Value);
        }
        var rep = GameReport.Build(moves, date: T0, opening: "Test");
        Assert.Equal(new[] { "f3", "e5", "g4", "Qh4#" }, rep.Moves.Select(m => m.San).ToArray());
        Assert.True(rep.White.Moves == 2 && rep.Black.Moves == 2);
        Assert.True(rep.Black.Accuracy > rep.White.Accuracy);
        Assert.Contains(rep.KeyMoments, k => k.Color == "w");
        var ctx = new PlayerContext(PlayerLevel.Beginner, Skill.PieceSafety);
        var rq = rep.SummaryRequest("w", ctx);
        Assert.Contains("Accuracy", rq.User); Assert.True(rq.ApproxTokens < 700); Assert.Equal(400, rq.MaxTokens);
        Assert.NotEmpty(rep.FallbackSummary("w"));
        Assert.Equal("Move 2, Black", GameReport.MoveLabel(4, "b"));
    }

    [Fact]
    public async Task HintsAndAnalysisWithStockfish()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        using var eng = new StockfishEngine(path);
        async Task<EngineResult> An(string fen, int ms = 400) => (await eng.AnalyzeAsync(fen, ms))!;

        foreach (var (fen, theme, mv) in new[] { ("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1", HintTheme.CaptureGain, "d1d5"), ("6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1", HintTheme.Mate, "a1a8") })
        {
            var (p, side) = FenReader.Parse(fen)!.Value;
            var h = Hints.Build(p, side, await An(fen));
            Assert.Equal(mv, h!.Move.Uci); Assert.Equal(theme, h.Theme);
            Assert.DoesNotContain(UciMove.SquareName(h.Move.ToFile, h.Move.ToRank), h.Stage3Fallback);
            Assert.Contains("Best move", h.Stage4);
        }
        {
            var fen = "4k3/pppp4/8/7b/8/8/PPPP4/3QK3 w - - 0 1";
            var (p, side) = FenReader.Parse(fen)!.Value;
            var h = Hints.Build(p, side, await An(fen), await An(p.Fen(PieceColor.Black)));
            Assert.StartsWith("Threat: Bishop h5 captures Queen d1", h!.Threat!.Text);
        }
        {
            var fen = "r3k3/8/8/8/8/8/3Q4/4K3 w - - 0 1";
            var (p, side) = FenReader.Parse(fen)!.Value;
            var before = await An(fen);
            var played = UciMove.Parse("d2a2")!.Value;
            var afterP = p.Applying(played);
            var after = await An(afterP.Fen(PieceColor.Black));
            var rev = Coach.Review(p, played, afterP, side, before, after);
            Assert.True(rev.Rating is MoveRating.Blunder or MoveRating.Mistake);
            var nullRes = await An(p.Fen(PieceColor.Black));
            var an = MoveAnalyzer.Analyze(new MoveAnalysisInput(rev, before, after, nullRes, 25));
            Assert.Equal(MistakeType.HangingPiece, an.MistakeType);
            Assert.Contains(an.Observations, o => o.Skill == Skill.PieceSafety && o.Value == 0);
            Assert.Equal(GamePhase.Endgame, an.Phase);
            var puz = Puzzle.Make(rev, before, an.MistakeType, an.Theme, PlayerLevel.Beginner, T0);
            Assert.NotNull(puz); Assert.Contains(before.BestMove, puz!.AcceptedMoves); Assert.False(puz.Accepts("d2a2"));
            var prof = new PlayerProfile("T");
            prof.Apply(an);
            Assert.Equal(1, prof.MistakeCounts["hanging_piece"]);
        }
        {
            var fen = "4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1";
            var (p, side) = FenReader.Parse(fen)!.Value;
            var before = await An(fen);
            var played = UciMove.Parse("e1e2")!.Value;
            var afterP = p.Applying(played);
            var rev = Coach.Review(p, played, afterP, side, before, await An(afterP.Fen(PieceColor.Black)));
            var an = MoveAnalyzer.Analyze(new MoveAnalysisInput(rev, before, null, null, 30));
            Assert.Contains(an.Observations, o => o.Skill == Skill.Tactics && o.Value == 0);
            Assert.Equal(MistakeType.MissedTactic, an.MistakeType);
        }
        {
            var start = ChessPosition.StartFen;
            var (p, side) = FenReader.Parse(start)!.Value;
            var before = await An(start);
            var mv = UciMove.Parse("e2e4")!.Value;
            var np = p.Applying(mv);
            var rev = Coach.Review(p, mv, np, side, before, await An(np.Fen(PieceColor.Black)));
            var an = MoveAnalyzer.Analyze(new MoveAnalysisInput(rev, before, null, null, 1));
            Assert.Null(an.MistakeType); Assert.Equal(GamePhase.Opening, an.Phase);
            var ctx = new PlayerContext(PlayerLevel.Intermediate, Skill.Threats, new[] { "Hanging piece" });
            var req = Coach.ReviewRequest(rev, ctx, new ExplanationStyle());
            Assert.True(req.ApproxTokens <= 600, $"{req.ApproxTokens}");
            var sug = Coach.SuggestionRequest(p, side, start, before, ctx, new ExplanationStyle(StyleLength.Long, StyleDepth.Concrete, true));
            Assert.True(sug!.ApproxTokens <= 600 && sug.MaxTokens == 400);
        }
    }
}

public class LlmMaxTokensTests
{
    [Fact]
    public void BuildBodyUsesMaxTokens()
    {
        Assert.Contains("\"max_tokens\":700", LlmClient.BuildBody("m", "s", "u", true));
        Assert.Contains("\"max_tokens\":120", LlmClient.BuildBody("m", "s", "u", true, 120));
    }
}
