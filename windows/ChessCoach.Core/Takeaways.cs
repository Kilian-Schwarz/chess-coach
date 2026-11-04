using System.Globalization;

namespace ChessCoach.Core;

public sealed record GameMistake(MistakeType Type, int Loss, string Example, string? PuzzleId, string? MoveKey = null);

public sealed class Takeaway
{
    public const int MinStreak = 5;
    public const int MaxCount = 3;

    public string Kind { get; set; } = "";
    public string? MistakeType { get; set; }
    public string? Skill { get; set; }
    public int Count { get; set; }
    public string? Example { get; set; }
    public List<string> PuzzleIds { get; set; } = new();

    public string Text(CultureInfo? culture = null)
    {
        _ = Core.MistakeTypeExt.TryParse(MistakeType ?? "", out var type);
        _ = SkillExt.TryParse(Skill ?? "", out var skill);
        return Kind switch
        {
            "mistake" => CoreLocale.Format("takeaway.mistake", culture, type.Label(culture), Count, Example ?? ""),
            "positive" => CoreLocale.Format("takeaway.positive", culture, Count),
            _ => CoreLocale.Format("takeaway.habit", culture, CoreLocale.Get("skill." + skill.Key() + ".summary", culture)),
        };
    }

    public static List<Takeaway> Compute(IReadOnlyList<GameMistake> mistakes, IReadOnlyList<MoveRecord> moves, string color, Skill focus)
    {
        var list = new List<Takeaway>();
        var top = mistakes.GroupBy(m => m.Type)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Sum(m => m.Loss)).FirstOrDefault();
        if (top != null)
        {
            var worst = top.OrderByDescending(m => m.Loss).First();
            list.Add(new Takeaway
            {
                Kind = "mistake", MistakeType = top.Key.Raw(), Skill = top.Key.Skill().Raw(), Count = top.Count(), Example = worst.Example,
                PuzzleIds = top.Where(m => m.PuzzleId != null).Select(m => m.PuzzleId!).ToList(),
            });
        }
        int streak = LongestStreak(moves.Where(m => m.Color == color));
        if (streak >= MinStreak) list.Add(new Takeaway { Kind = "positive", Count = streak });
        list.Add(new Takeaway { Kind = "habit", Skill = focus.Raw(), Count = 0 });
        return list.Take(MaxCount).ToList();
    }

    public static int LongestStreak(IEnumerable<MoveRecord> moves)
    {
        int best = 0, run = 0;
        foreach (var m in moves)
        {
            run = MoveRatingExt.FromRaw(m.Rating) is MoveRating.Best or MoveRating.Good ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return best;
    }
}
