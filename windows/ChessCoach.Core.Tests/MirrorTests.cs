using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class MirrorTests
{
    [Fact]
    public void GainWeightScalesOnlyGains()
    {
        var a = new PlayerProfile("a"); var b = new PlayerProfile("b"); var c = new PlayerProfile("c");
        a.Observe(Skill.Tactics, 1, Curriculum.GainWeight(0));
        b.Observe(Skill.Tactics, 1, Curriculum.GainWeight(2));
        c.Observe(Skill.Tactics, 1, Curriculum.GainWeight(3));
        double ga = a.Skills["tactics"] - 0.5, gb = b.Skills["tactics"] - 0.5;
        Assert.Equal(0.5, gb / ga, 6);
        Assert.Equal(0.5, c.Skills["tactics"], 6);
        a.Observe(Skill.Tactics, 0, Curriculum.GainWeight(3));
        Assert.True(a.Skills["tactics"] < 0.5 + ga);
        Assert.Equal(1, Curriculum.GainWeight(0));
        Assert.Equal(0.5, Curriculum.GainWeight(1));
        Assert.Equal(0.5, Curriculum.GainWeight(2));
        Assert.Equal(0, Curriculum.GainWeight(4));
    }

    [Fact]
    public void ThinkDelayRules()
    {
        Assert.False(Curriculum.NeedsThinkDelay(0, 1, 15, false));
        Assert.True(Curriculum.NeedsThinkDelay(1, 5, 15, false));
        Assert.False(Curriculum.NeedsThinkDelay(1, 5, 15, true));
        Assert.False(Curriculum.NeedsThinkDelay(2, 15, 15, false));
    }

    [Fact]
    public void ThinkSecondsFollowSuccessRate()
    {
        var skills = new Dictionary<string, double> { ["tactics"] = 0.5 };
        Assert.Equal(15, Curriculum.Adapt(PlayerLevel.Intermediate, Skill.Tactics, skills, 0.78).ThinkSeconds);
        Assert.Equal(15, Curriculum.Adapt(PlayerLevel.Intermediate, Skill.Tactics, skills, null).ThinkSeconds);
        Assert.Equal(8, Curriculum.Adapt(PlayerLevel.Intermediate, Skill.Tactics, skills, 0.5).ThinkSeconds);
        Assert.Equal(25, Curriculum.Adapt(PlayerLevel.Intermediate, Skill.Tactics, skills, 0.95).ThinkSeconds);
    }

    [Fact]
    public void AssistedShareCutsRatingGainsOnly()
    {
        PlayerProfile Settled()
        {
            var p = new PlayerProfile("p") { PlacementGames = PlayerProfile.PlacementTotal, Rating = 1100, GamesPlayed = 20 };
            return p;
        }
        var plain = Settled(); var assisted = Settled(); var losing = Settled(); var losingAssisted = Settled();
        plain.CompleteGame(1600, null, 30, 0);
        assisted.CompleteGame(1600, null, 30, 0.5);
        Assert.Equal((assisted.Rating - 1100) * 2, plain.Rating - 1100, 6);
        losing.CompleteGame(600, null, 30, 0);
        losingAssisted.CompleteGame(600, null, 30, 0.8);
        Assert.Equal(losing.Rating, losingAssisted.Rating, 6);
    }

    private static ReviewedMove Mv(string color, int hint, int before = 0, int after = -300) =>
        new(color, "e2e4", "e4", ChessPosition.StartFen, before, after, "e2e4", hint);

    [Fact]
    public void HintedMovesAreExcludedFromPerformanceAndCountAsAssisted()
    {
        var all = new List<ReviewedMove>();
        for (int i = 0; i < 14; i++) { all.Add(Mv("w", i < 8 ? 0 : 3, 0, i < 8 ? 0 : -600)); all.Add(Mv("b", 0)); }
        var report = GameReport.Build(all);
        Assert.Equal(14, report.White.Moves);
        Assert.Equal(6.0 / 14, report.White.AssistedShare, 6);
        Assert.Equal(0, report.Black.AssistedShare, 6);
        Assert.True(report.White.Performance > report.Black.Performance);

        var halves = new List<ReviewedMove>();
        for (int i = 0; i < 12; i++) halves.Add(Mv("w", i % 2 == 0 ? 1 : 0));
        Assert.Equal(0.25, GameReport.Build(halves).White.AssistedShare, 6);
    }

    [Fact]
    public void SupportFadesAfterManyGoodObservations()
    {
        var p = new PlayerProfile("p") { GamesPlayed = 5 };
        Assert.False(p.SupportFaded);
        for (int i = 0; i < 25; i++) { p.Observe(Skill.Threats, 1); p.Observe(Skill.PieceSafety, 1); }
        Assert.True(p.SupportFaded);
        Assert.False(p.ShowsThreatAutomatically);
    }

    [Fact]
    public void ImplicitFeedbackIsWeakerThanExplicit()
    {
        var a = new LearningStyle(new ExplanationStyle(StyleLength.Medium)); var b = new LearningStyle(new ExplanationStyle(StyleLength.Medium));
        var used = new ExplanationStyle(StyleLength.Medium);
        a.Record(ExplanationFeedback.TooLong, used);
        b.Record(ExplanationFeedback.CollapsedImmediately, used);
        Assert.True(Math.Abs(b.Score(used).Mean - 0.5) < Math.Abs(a.Score(used).Mean - 0.5) / 4);
        Assert.Equal(StyleLength.Short, ExplanationStyle.FirstLength);
        Assert.Equal(StyleLength.Long, ExplanationStyle.LengthFromKey("long|concrete"));
    }

    [Fact]
    public void WinChanceMistakeTypeNeedsRealDrop()
    {
        Assert.True(Accuracy.WinChance(300) - Accuracy.WinChance(200) < MoveRatingExt.InaccuracyDrop);
        Assert.True(Accuracy.WinChance(50) - Accuracy.WinChance(-50) < MoveRatingExt.InaccuracyDrop);
        Assert.True(Accuracy.WinChance(0) - Accuracy.WinChance(-200) >= MoveRatingExt.InaccuracyDrop);
    }
}

public class GameBookTests
{
    private static PlayedMove Move(string fen, string uci)
    {
        var pos = ChessPosition.FromFen(fen)!;
        var move = UciMove.Parse(uci)!.Value;
        return new PlayedMove(move, uci, pos.Turn, pos, pos.Applying(move), pos.FullmoveNumber);
    }

    private static (PlayedMove, PlayedMove) TwoMoves()
    {
        var a = Move(ChessPosition.StartFen, "e2e4");
        return (a, Move(a.FenAfter, "e7e5"));
    }

    [Fact]
    public void AddIgnoresRepeatedMoves()
    {
        var (a, _) = TwoMoves();
        var book = new GameBook();
        Assert.True(book.Add(a));
        Assert.False(book.Add(a));
        Assert.Equal(1, book.Plies);
    }

    [Fact]
    public void RetainDropsTakenBackMovesEverywhere()
    {
        var (a, b) = TwoMoves();
        var book = new GameBook();
        book.Add(a); book.Add(b);
        book.Queue.Dequeue();
        book.Reviewed.Add(new ReviewedMove("w", a.Uci, a.San, a.FenBefore, 0, 0, null));
        string ka = GameBook.Key(a.FenBefore, a.Uci), kb = GameBook.Key(b.FenBefore, b.Uci);
        book.Rated.Add(ka); book.Rated.Add(kb);
        book.Mistakes[PieceColor.White] = new() { new GameMistake(MistakeType.Other, 100, "x", null, ka), new GameMistake(MistakeType.Other, 90, "y", null, kb) };
        book.Retain(new HashSet<string> { ka });
        Assert.Empty(book.Queue);
        Assert.Single(book.Reviewed);
        Assert.Equal(new[] { ka }, book.Rated);
        Assert.Equal(new[] { ka }, book.Seen);
        Assert.Equal("x", Assert.Single(book.Mistakes[PieceColor.White]).Example);
        Assert.True(book.Add(b));
    }

    [Fact]
    public void RequeueRestoresMovesNotYetReviewed()
    {
        var (a, b) = TwoMoves();
        var book = new GameBook();
        book.Reviewed.Add(new ReviewedMove("w", a.Uci, a.San, a.FenBefore, 0, 0, null));
        book.Requeue(new[] { a, b });
        Assert.Equal(new[] { b.Uci }, book.Queue.Select(m => m.Uci));
        Assert.Equal(2, book.Plies);
    }
}

public class FocusPinTests
{
    [Fact]
    public void PinnedFocusHoldsUntilTheNextCompletedGame()
    {
        var p = new PlayerProfile("p") { PlacementGames = PlayerProfile.PlacementTotal, GamesPlayed = 10 };
        p.PinFocus(Skill.Endgame);
        for (int i = 0; i < 30; i++) p.Observe(Skill.PieceSafety, 0);
        p.UpdateFocus();
        Assert.Equal(Skill.Endgame, p.FocusSkill);
        p.CompleteGame(1200, null, 30);
        Assert.Null(p.PinnedFocus);
        p.UpdateFocus();
        Assert.Equal(Skill.PieceSafety, p.FocusSkill);
    }
}

public class PuzzleSetTests
{
    [Fact]
    public void AddingAnExistingPositionResetsIt()
    {
        var t0 = new DateTime(2027, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        Puzzle Make(DateTime due) => new() { Id = "z", Fen = "4k3/8/8/8/8/8/8/R3K3 w - - 0 1", AcceptedMoves = new() { "a1a8" }, CreatedAt = t0, DueAt = due };
        var set = new PuzzleSet();
        Assert.True(set.Add(Make(t0)));
        set.Puzzles[0].Box = 4;
        set.Puzzles[0].Mastered = true;
        Assert.False(set.Add(Make(t0.AddDays(2))));
        Assert.Single(set.Puzzles);
        Assert.Equal(1, set.Puzzles[0].Box);
        Assert.False(set.Puzzles[0].Mastered);
        Assert.Equal(t0.AddDays(2), set.Puzzles[0].DueAt);
    }
}
