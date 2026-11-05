namespace ChessCoach.Core;

public sealed class AssistGate
{
    public bool Tainted { get; private set; }

    public bool Counts => !Tainted;

    public void Begin(bool assisted) => Tainted = assisted;

    public bool Observe(bool assisted, bool started)
    {
        bool before = Tainted;
        if (assisted) Tainted = true;
        else if (!started) Tainted = false;
        return Tainted != before;
    }

    public bool Record(PlayerProfile profile, MoveAnalysis analysis, int hint)
    {
        if (Tainted) return false;
        profile.Apply(analysis, Curriculum.GainWeight(hint));
        profile.RecordHintUse(hint);
        return true;
    }

    public bool AddPuzzle(PuzzleSet set, Puzzle? puzzle) => Counts && puzzle != null && set.Add(puzzle);

    public double? Complete(PlayerProfile profile, SideSummary side)
    {
        if (Tainted) return null;
        return profile.CompleteGame(side.Performance, null, side.RatedMoves, side.AssistedShare);
    }

    public bool AdjustLearningSpeed(PlayerProfile profile, IReadOnlyCollection<Puzzle> puzzles)
    {
        if (Tainted) return false;
        profile.AdjustLearningSpeed(puzzles);
        return true;
    }
}
