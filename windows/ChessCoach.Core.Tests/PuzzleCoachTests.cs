using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class PuzzleCoachTests
{
    const string DiscFen = "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQR1K1 w kq - 7 9";
    static readonly DateTime T0 = new(2027, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    static ChessPosition Pos(string fen) => ChessPosition.FromFen(fen)!;
    static UciMove M(string s) => UciMove.Parse(s)!.Value;

    static Puzzle Disc() => new()
    {
        Id = "d", Fen = DiscFen, AcceptedMoves = new() { "e5c6" }, BestLine = new() { "e5c6", "b7c6", "e1e6", "f7e6" }, Theme = "discovered",
        MistakeType = "missedTactic", CreatedAt = T0, DueAt = T0, SourceDate = T0, SourceMove = 9, PlayedMove = "b5c6", PlayedValue = -320, ThemeChecked = true,
    };

    [Fact]
    public void SourceLineNamesTheMistakeButNotTheSolution()
    {
        var text = PuzzleCoach.Source(Disc());
        Assert.Contains("move 9", text);
        Assert.Contains("Bxc6", text);
        Assert.Contains("−3.2", text);
        Assert.DoesNotContain("Nxc6", text);
        Assert.StartsWith("From your game on", PuzzleCoach.Source(new Puzzle { Fen = DiscFen, CreatedAt = T0 }));
    }

    [Fact]
    public void EveryThemeHasQuestionAndHabit()
    {
        foreach (var t in HintThemeExt.All)
        {
            Assert.False(string.IsNullOrEmpty(PuzzleCoach.Question(t)));
            Assert.NotEqual(PuzzleCoach.Question(t), PuzzleCoach.Habit(t));
        }
    }

    [Fact]
    public void LessonExplainsTheDiscoveredAttackFromTheLine()
    {
        var lesson = PuzzleCoach.Lesson(Disc());
        Assert.Contains("Knight e5 moves away, so Rook e1 attacks Queen e6", lesson.Why);
        Assert.Contains("9.Nxc6 bxc6 10.Rxe6 fxe6", lesson.Why);
        Assert.Contains("win 4 points", lesson.Why);
        Assert.Contains("in front of your long-range pieces", lesson.Habit);
        var quiet = Disc();
        quiet.Theme = "pawnMove";
        Assert.DoesNotContain("moves away", PuzzleCoach.Lesson(quiet).Why);
    }

    [Fact]
    public void MateLineSaysCheckmate()
    {
        var p = new Puzzle { Fen = "6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1", AcceptedMoves = new() { "a1a8" }, BestLine = new() { "a1a8" }, Theme = "mate" };
        Assert.Contains("checkmate", PuzzleCoach.Lesson(p).Why);
    }

    [Fact]
    public void ClassifierAndHintLadderStayOffTheSolution()
    {
        var p = Disc();
        var result = new EngineResult(DiscFen, "e5c6", new[] { new EngineLine("e5c6", 400, null, p.BestLine) });
        Assert.Equal(HintTheme.Discovered, PuzzleCoach.Classify(p, result));
        var pos = Pos(DiscFen);
        var step2 = Hints.Stage2Text('N', "e5");
        var step3 = PuzzleCoach.Hint3(HintTheme.Discovered, M("e5c6"), pos);
        Assert.Contains("Rook e1 then attacks Queen e6", step3);
        foreach (var text in new[] { HintTheme.Discovered.Stage1Text(), step2, step3, PuzzleCoach.Question(HintTheme.Discovered) })
        {
            Assert.DoesNotContain("c6", text);
            Assert.DoesNotContain("Nxc6", text);
        }
    }

    [Fact]
    public void FailReasonNamesTheLossOrFallsBackToTheTheme()
    {
        var p = Disc();
        var pos = Pos(DiscFen);
        Assert.Contains("That loses", PuzzleCoach.FailReason(p, pos.Applying(M("b5c6")), new[] { "b7c6", "e1e6" }));
        Assert.Contains("Discovered attack", PuzzleCoach.FailReason(p, pos.Applying(M("a2a3")), new[] { "a7a6" }));
    }

    [Fact]
    public void ProgressCountsSolvedPerTheme()
    {
        var list = Enumerable.Range(0, 5).Select(i => new Puzzle { Id = "t" + i, Theme = "discovered" }).ToList();
        list[0].Box = 2; list[0].Attempts.Add(new PuzzleAttempt { Correct = true });
        list[1].Mastered = true; list[1].Attempts.Add(new PuzzleAttempt { Correct = true });
        list.Add(new Puzzle { Id = "f", Theme = "fork" });
        var progress = PuzzleCoach.Progress(list);
        Assert.Equal(new ThemeProgress(HintTheme.Discovered, 2, 5), progress[0]);
        Assert.Equal("Discovered attack: 2/5 solved", progress[0].Text());
    }

    [Fact]
    public void LlmRequestCarriesTheGroundedFacts()
    {
        var req = PuzzleCoach.WhyRequest(Disc(), new PlayerContext(PlayerLevel.Intermediate, null, new List<string>()), new ExplanationStyle())!;
        Assert.Contains("Knight e5 moves away", req.User);
        Assert.Contains("Theme: Discovered attack", req.User);
        Assert.Contains("9.Nxc6", req.User);
    }

    [Fact]
    public void OldPuzzleFilesStillLoadAndNeedAThemeCheck()
    {
        var old = ChessCoachJson.Deserialize<Puzzle>("""{"id":"o","fen":"x","acceptedMoves":[],"bestLine":[],"theme":"fork","mistakeType":"other","createdAt":"2027-01-15T12:00:00Z","box":1,"dueAt":"2027-01-15T12:00:00Z","attempts":[],"mastered":false}""")!;
        Assert.Null(old.SourceDate);
        Assert.True(PuzzleCoach.NeedsThemeCheck(old));
        Assert.False(PuzzleCoach.NeedsThemeCheck(Disc()));
        Assert.DoesNotContain("sourceDate", ChessCoachJson.Serialize(old));
    }
}
