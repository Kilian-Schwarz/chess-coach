using System.Text.Json.Serialization;

namespace ChessCoach.Core;

public sealed class PuzzleAttempt
{
    public DateTime Date { get; set; }
    public bool Correct { get; set; }
    public int HintLevel { get; set; }
}

public sealed class Puzzle
{
    public string Id { get; set; } = "";
    public string Fen { get; set; } = "";
    public List<string> AcceptedMoves { get; set; } = new();
    public List<string> BestLine { get; set; } = new();

    public string Theme { get; set; } = "";

    public string MistakeType { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public int Box { get; set; } = 1;
    public DateTime DueAt { get; set; }
    public List<PuzzleAttempt> Attempts { get; set; } = new();
    public bool Mastered { get; set; }

    public static readonly double[] IntervalsDays = { 1, 3, 7, 16, 35 };
    public const int AcceptTolerance = 30;

    [JsonIgnore] public bool IsNew => Attempts.Count == 0;
    [JsonIgnore] public Skill Skill => (Core.MistakeTypeExt.TryParse(MistakeType, out var m) ? m : Core.MistakeType.Other).Skill();

    public static Puzzle? Make(MoveReview review, EngineResult beforeResult, MistakeType? mistakeType, HintTheme? theme,
        PlayerLevel level, DateTime? now = null, string? id = null)
    {
        var r = review;
        bool qualifies = r.Rating is MoveRating.Mistake or MoveRating.Blunder || (level == PlayerLevel.Strong && r.Rating == MoveRating.Inaccuracy);
        if (!qualifies || beforeResult.Lines.Count == 0) return null;
        var top = beforeResult.Lines[0];
        var accepted = beforeResult.Lines.Where(l => l.Value >= top.Value - AcceptTolerance && l.Move.Length > 0).Select(l => l.Move).ToList();
        if (accepted.Count == 0) return null;
        var t = now ?? DateTime.UtcNow;
        return new Puzzle
        {
            Id = id ?? Guid.NewGuid().ToString().ToLowerInvariant(), Fen = r.Before.Fen(r.Player), AcceptedMoves = accepted,
            BestLine = top.Pv.ToList(), Theme = (theme ?? HintTheme.ImprovePiece).Raw(),
            MistakeType = (mistakeType ?? Core.MistakeType.Other).Raw(), CreatedAt = t, DueAt = t,
        };
    }

    public bool Accepts(string uci) => AcceptedMoves.Contains(uci.ToLowerInvariant());

    public bool Attempt(string move, int hintLevel = 0, DateTime? now = null, double learningSpeed = 1.0)
    {
        var t = now ?? DateTime.UtcNow;
        bool correct = Accepts(move);
        Attempts.Add(new PuzzleAttempt { Date = t, Correct = correct, HintLevel = hintLevel });
        double speed = Math.Min(1.5, Math.Max(0.6, learningSpeed));
        if (!correct) Box = 1;
        else if (hintLevel == 0)
        {
            if (Box >= 5) Mastered = true; else Box += 1;
        }
        double days = IntervalsDays[Math.Min(5, Math.Max(1, Box)) - 1] * speed;
        DueAt = t.AddSeconds(days * 86_400);
        return correct;
    }
}

public sealed class PuzzleSet
{
    public int SchemaVersion { get; set; } = 1;
    public List<Puzzle> Puzzles { get; set; } = new();

    public bool Add(Puzzle p)
    {
        if (Puzzles.Any(x => x.Fen == p.Fen)) return false;
        Puzzles.Add(p);
        return true;
    }
}

public sealed class PuzzleStats
{
    public int Total, Mastered, Due, New, Attempts;

    public double? RecentSuccessRate;
    public int[] ByBox = { 0, 0, 0, 0, 0 };
}

public static class PuzzleScheduler
{
    public const int MaxNewPerDay = 10;
    public const int RecentWindow = 30;

    public static double? RecentSuccessRate(IEnumerable<Puzzle> puzzles)
    {
        var all = puzzles.SelectMany(p => p.Attempts).OrderBy(a => a.Date).TakeLast(RecentWindow).ToList();
        if (all.Count == 0) return null;
        return (double)all.Count(a => a.Correct) / all.Count;
    }

    public static double AdjustedLearningSpeed(double current, IReadOnlyCollection<Puzzle> puzzles)
    {
        int n = puzzles.Sum(p => p.Attempts.Count);
        if (n < 10 || RecentSuccessRate(puzzles) is not double q) return current;
        double v = current;
        if (q > 0.85) v += 0.1; else if (q < 0.70) v -= 0.1;
        return Math.Min(1.5, Math.Max(0.6, Math.Round(v * 10, MidpointRounding.AwayFromZero) / 10));
    }

    public static PuzzleStats Stats(IReadOnlyCollection<Puzzle> puzzles, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        var s = new PuzzleStats { Total = puzzles.Count };
        foreach (var p in puzzles)
        {
            if (p.Mastered) { s.Mastered++; continue; }
            s.ByBox[Math.Min(5, Math.Max(1, p.Box)) - 1]++;
            if (p.DueAt <= t) { if (p.IsNew) s.New++; else s.Due++; }
        }
        s.Attempts = puzzles.Sum(p => p.Attempts.Count);
        s.RecentSuccessRate = RecentSuccessRate(puzzles);
        return s;
    }

    public static List<Puzzle> TodaysSelection(IReadOnlyCollection<Puzzle> puzzles, Skill? focus, DateTime? now = null, int maxNew = MaxNewPerDay)
    {
        var t = now ?? DateTime.UtcNow;
        IOrderedEnumerable<Puzzle> FocusFirst(IEnumerable<Puzzle> src) =>
            src.OrderByDescending(p => focus != null && p.Skill == focus).ThenBy(p => p.DueAt);
        var live = puzzles.Where(p => !p.Mastered && p.DueAt <= t).ToList();
        var due = FocusFirst(live.Where(p => !p.IsNew)).ToList();
        var today = t.ToLocalTime().Date;
        int startedToday = puzzles.Count(p => p.Attempts.Count > 0 && p.Attempts[0].Date.ToLocalTime().Date == today);
        int room = Math.Max(0, maxNew - startedToday);
        var fresh = FocusFirst(live.Where(p => p.IsNew)).Take(room);
        return due.Concat(fresh).ToList();
    }
}
