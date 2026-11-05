using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class AssistGateTests
{
    static readonly MoveAnalysis Analysis = new()
    {
        Observations = { new SkillObservation(Skill.PieceSafety, 0) },
        MistakeType = MistakeType.HangingPiece,
        Example = "x",
        Loss = 500,
    };

    static Puzzle NewPuzzle() => new() { Id = "p1", Fen = "4k3/8/8/8/8/8/8/4K3 w - - 0 1", AcceptedMoves = { "e1e2" }, MistakeType = "hanging_piece" };

    static SideSummary LongSide() => new() { Moves = 30, RatedMoves = 30, Performance = 1400 };

    static void Play(AssistGate gate, PlayerProfile profile, PuzzleSet puzzles)
    {
        gate.Record(profile, Analysis, 2);
        gate.AddPuzzle(puzzles, NewPuzzle());
        gate.Complete(profile, LongSide());
        gate.AdjustLearningSpeed(profile, puzzles.Puzzles);
    }

    [Fact]
    public void TaintedGameLeavesProfileAndPuzzlesUnchanged()
    {
        var gate = new AssistGate();
        gate.Begin(true);
        var profile = new PlayerProfile("x");
        var puzzles = new PuzzleSet();
        var before = ChessCoachJson.Serialize(profile);
        Play(gate, profile, puzzles);
        Assert.Equal(before, ChessCoachJson.Serialize(profile));
        Assert.Empty(puzzles.Puzzles);
        Assert.Equal(0, profile.GamesPlayed);
        Assert.Empty(profile.RatingHistory);
    }

    [Fact]
    public void SwitchingOffMidGameKeepsTheTaintAndTheNextGameCounts()
    {
        var gate = new AssistGate();
        gate.Begin(true);
        gate.Observe(false, true);
        Assert.True(gate.Tainted);
        var profile = new PlayerProfile("x");
        var puzzles = new PuzzleSet();
        var before = ChessCoachJson.Serialize(profile);
        Play(gate, profile, puzzles);
        Assert.Equal(before, ChessCoachJson.Serialize(profile));

        gate.Begin(false);
        Play(gate, profile, puzzles);
        Assert.Equal(1, profile.GamesPlayed);
        Assert.Equal(1, profile.SkillCounts[Skill.PieceSafety.Raw()]);
        Assert.Equal(1, profile.HintStats.LevelUses[2]);
        Assert.Single(puzzles.Puzzles);
    }

    [Fact]
    public void SwitchingOnMidGameTaintsTheGame()
    {
        var gate = new AssistGate();
        gate.Begin(false);
        gate.Observe(false, true);
        Assert.True(gate.Counts);
        Assert.True(gate.Observe(true, true));
        Assert.True(gate.Tainted);
        var profile = new PlayerProfile("x");
        var puzzles = new PuzzleSet();
        var before = ChessCoachJson.Serialize(profile);
        Play(gate, profile, puzzles);
        Assert.Equal(before, ChessCoachJson.Serialize(profile));
        Assert.Empty(puzzles.Puzzles);
    }

    [Fact]
    public void SwitchingOffBeforeTheFirstMoveStartsClean()
    {
        var gate = new AssistGate();
        gate.Observe(true, false);
        gate.Observe(false, false);
        Assert.True(gate.Counts);
    }

    [Fact]
    public void BookStartsUntaintedAndReportFlagSurvivesSerialization()
    {
        Assert.True(new GameBook().Assist.Counts);
        var report = new GameReport { Id = "r", EngineAssisted = true };
        Assert.True(ChessCoachJson.Deserialize<GameReport>(ChessCoachJson.Serialize(report))!.EngineAssisted);
        Assert.False(new GameReport().EngineAssisted);
    }
}
