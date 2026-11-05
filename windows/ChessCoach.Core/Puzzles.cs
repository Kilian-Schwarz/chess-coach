using System.Text.Json.Serialization;

namespace ChessCoach.Core;

public sealed class PuzzleAttempt
{
    public DateTime Date { get; set; }
    public bool Correct { get; set; }
    public int HintLevel { get; set; }
    public bool Assisted { get; set; }
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

    public DateTime? SourceDate { get; set; }
    public int? SourceMove { get; set; }
    public string? PlayedMove { get; set; }
    public int? PlayedValue { get; set; }
    public bool? ThemeChecked { get; set; }

    public static readonly double[] IntervalsDays = { 1, 3, 7, 16, 35 };
    public const int AcceptTolerance = 30;
    public const int AlternativeTolerance = 40;
    public const int AssistedHintLevel = 4;

    [JsonIgnore] public bool IsNew => Attempts.Count == 0;
    [JsonIgnore] public Skill Skill => (Core.MistakeTypeExt.TryParse(MistakeType, out var m) ? m : Core.MistakeType.Other).Skill();

    public static Puzzle? Make(MoveReview review, EngineResult beforeResult, MistakeType? mistakeType, HintTheme? theme,
        PlayerLevel level, DateTime? now = null, string? id = null, int? moveNumber = null)
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
            SourceDate = t, SourceMove = moveNumber, PlayedMove = r.Played.Uci,
            PlayedValue = Math.Abs(top.Value) >= 9_000 ? null : top.Value - r.Loss, ThemeChecked = theme != null,
        };
    }

    public bool Accepts(string uci) => AcceptedMoves.Contains(uci.ToLowerInvariant());

    public bool TryAcceptAlternative(string uci, int lossCp)
    {
        if (lossCp > AlternativeTolerance || uci.Length == 0) return false;
        if (!Accepts(uci)) AcceptedMoves.Add(uci.ToLowerInvariant());
        return true;
    }

    public bool Attempt(string move, int hintLevel = 0, DateTime? now = null, double learningSpeed = 1.0)
    {
        var t = now ?? DateTime.UtcNow;
        bool correct = Accepts(move);
        Attempts.Add(new PuzzleAttempt { Date = t, Correct = correct, HintLevel = hintLevel, Assisted = hintLevel > 0 });
        double speed = Math.Min(1.5, Math.Max(0.6, learningSpeed));
        if (!correct) Box = 1;
        else if (hintLevel >= AssistedHintLevel) Box = Math.Max(1, Box - 1);
        else if (hintLevel == 0)
        {
            if (Box >= 5) Mastered = true; else Box += 1;
        }
        double days = IntervalsDays[Math.Min(5, Math.Max(1, Box)) - 1] * speed;
        DueAt = StartOfDay(t, Math.Max(1, (int)Math.Round(days, MidpointRounding.AwayFromZero)));
        return correct;
    }

    public static DateTime StartOfDay(DateTime t, int days) =>
        DateTime.SpecifyKind(t.ToLocalTime().Date.AddDays(days), DateTimeKind.Local).ToUniversalTime();
}

public sealed class PuzzleSet
{
    public int SchemaVersion { get; set; } = 1;
    public List<Puzzle> Puzzles { get; set; } = new();

    public bool Add(Puzzle p)
    {
        if (Puzzles.FirstOrDefault(x => x.Fen == p.Fen) is { } existing)
        {
            existing.Box = 1;
            existing.Mastered = false;
            existing.DueAt = p.DueAt;
            return false;
        }
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
    public const int DailyCap = 15;
    public const int RecentWindow = 30;
    public const int MinReviewAttempts = 10;
    public const int MaxThemeRun = 3;
    public const double FocusShare = 0.55;

    private static IEnumerable<PuzzleAttempt> ReviewAttempts(IEnumerable<Puzzle> puzzles) =>
        puzzles.SelectMany(p => p.Attempts.Skip(1)).Where(a => !a.Assisted);

    public static int ReviewCount(IEnumerable<Puzzle> puzzles) => ReviewAttempts(puzzles).Count();

    public static double? RecentSuccessRate(IEnumerable<Puzzle> puzzles)
    {
        var all = ReviewAttempts(puzzles).OrderBy(a => a.Date).TakeLast(RecentWindow).ToList();
        if (all.Count == 0) return null;
        return (double)all.Count(a => a.Correct) / all.Count;
    }

    public static double AdjustedLearningSpeed(double current, IReadOnlyCollection<Puzzle> puzzles)
    {
        if (ReviewCount(puzzles) < MinReviewAttempts || RecentSuccessRate(puzzles) is not double q) return current;
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

    public static List<Puzzle> TodaysSelection(IReadOnlyCollection<Puzzle> puzzles, Skill? focus, DateTime? now = null,
        int maxNew = MaxNewPerDay, Random? rng = null, int cap = DailyCap)
    {
        var t = now ?? DateTime.UtcNow;
        var live = puzzles.Where(p => !p.Mastered && p.DueAt <= t).ToList();
        var due = live.Where(p => !p.IsNew).OrderBy(p => p.DueAt).Take(cap).ToList();
        var today = t.ToLocalTime().Date;
        int startedToday = puzzles.Count(p => p.Attempts.Count > 0 && p.Attempts[0].Date.ToLocalTime().Date == today);
        int room = Math.Min(Math.Max(0, maxNew - startedToday), Math.Max(0, cap - due.Count));
        var fresh = live.Where(p => p.IsNew).OrderBy(p => p.DueAt).Take(room);
        return Interleave(due.Concat(fresh).ToList(), focus, rng ?? Random.Shared);
    }

    public static List<Puzzle> Interleave(List<Puzzle> items, Skill? focus, Random rng)
    {
        var focused = new Queue<Puzzle>(items.Where(p => focus != null && p.Skill == focus));
        var others = new Queue<Puzzle>(items.Where(p => focus == null || p.Skill != focus));
        var result = new List<Puzzle>();
        while (focused.Count + others.Count > 0)
        {
            var first = focused.Count > 0 && (others.Count == 0 || rng.NextDouble() < FocusShare) ? focused : others;
            var second = first == focused ? others : focused;
            var pick = Take(first, result) ?? Take(second, result) ?? first.Dequeue();
            result.Add(pick);
        }
        return result;
    }

    private static Puzzle? Take(Queue<Puzzle> queue, List<Puzzle> result)
    {
        if (queue.Count == 0) return null;
        bool run = result.Count >= MaxThemeRun && result.TakeLast(MaxThemeRun).All(p => p.Theme == queue.Peek().Theme);
        if (!run) return queue.Dequeue();
        var alt = queue.FirstOrDefault(p => p.Theme != queue.Peek().Theme);
        if (alt == null) return null;
        var rest = queue.Where(p => p != alt).ToList();
        queue.Clear();
        foreach (var p in rest) queue.Enqueue(p);
        return alt;
    }
}
