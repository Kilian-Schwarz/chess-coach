using System.Text.Json.Serialization;

namespace ChessCoach.Core;

[JsonConverter(typeof(StyleLengthConverter))]
public enum StyleLength { Short, Medium, Long }

public sealed class StyleLengthConverter : RawEnumConverter<StyleLength>
{
    protected override string ToRaw(StyleLength v) => v.Raw();
    protected override bool TryFromRaw(string raw, out StyleLength v)
    {
        switch (raw)
        {
            case "short": v = StyleLength.Short; return true;
            case "medium": v = StyleLength.Medium; return true;
            case "long": v = StyleLength.Long; return true;
            default: v = default; return false;
        }
    }
}

public static class StyleLengthExt
{
    public static readonly StyleLength[] All = { StyleLength.Short, StyleLength.Medium, StyleLength.Long };
    public static string Raw(this StyleLength l) => l switch { StyleLength.Short => "short", StyleLength.Medium => "medium", _ => "long" };

    public static int MaxTokens(this StyleLength l) => l switch { StyleLength.Short => 120, StyleLength.Medium => 220, _ => 400 };
    public static int MaxWords(this StyleLength l) => l switch { StyleLength.Short => 25, StyleLength.Medium => 45, _ => 110 };
    public static StyleLength? Shorter(this StyleLength l) => l == StyleLength.Long ? StyleLength.Medium : l == StyleLength.Medium ? StyleLength.Short : null;
    public static StyleLength? Longer(this StyleLength l) => l == StyleLength.Short ? StyleLength.Medium : l == StyleLength.Medium ? StyleLength.Long : null;
}

[JsonConverter(typeof(StyleDepthConverter))]
public enum StyleDepth { Principle, Concrete }

public sealed class StyleDepthConverter : RawEnumConverter<StyleDepth>
{
    protected override string ToRaw(StyleDepth v) => v.Raw();
    protected override bool TryFromRaw(string raw, out StyleDepth v)
    {
        switch (raw)
        {
            case "principle": v = StyleDepth.Principle; return true;
            case "concrete": v = StyleDepth.Concrete; return true;
            default: v = default; return false;
        }
    }
}

public static class StyleDepthExt
{
    public static readonly StyleDepth[] All = { StyleDepth.Principle, StyleDepth.Concrete };
    public static string Raw(this StyleDepth d) => d == StyleDepth.Principle ? "principle" : "concrete";
    public static StyleDepth Other(this StyleDepth d) => d == StyleDepth.Principle ? StyleDepth.Concrete : StyleDepth.Principle;
}

public sealed record ExplanationStyle(StyleLength Length = StyleLength.Medium, StyleDepth Depth = StyleDepth.Principle,
    bool Examples = false, string Tone = "auto")
{
    public string Key => $"{Length.Raw()}|{Depth.Raw()}";
    public int MaxTokens => Length.MaxTokens();

    public List<ExplanationStyle> Neighbors
    {
        get
        {
            var o = new List<ExplanationStyle>();
            if (Length.Shorter() is { } s) o.Add(this with { Length = s });
            if (Length.Longer() is { } l) o.Add(this with { Length = l });
            o.Add(this with { Depth = Depth.Other() });
            return o;
        }
    }

    public static readonly ExplanationStyle ShortPrinciple = new(StyleLength.Short, StyleDepth.Principle);
}

public enum ExplanationFeedback { TooLong, TooShort, Helpful, AskedAgain, CollapsedImmediately }

public sealed class StyleScore
{
    public double Mean { get; set; } = 0.5;
    public int N { get; set; }
    public int PuzzleCorrect { get; set; }
    public int PuzzleTotal { get; set; }

    [JsonIgnore]
    public double Combined => PuzzleTotal >= 3 ? 0.7 * Mean + 0.3 * PuzzleCorrect / PuzzleTotal : Mean;
}

public sealed class LearningStyle
{
    public const double Alpha = 0.25;
    public const double ExplorationRate = 0.10;
    public const double SwitchMargin = 0.05;

    public StyleLength Length { get; set; } = StyleLength.Medium;
    public StyleDepth Depth { get; set; } = StyleDepth.Principle;
    public bool Examples { get; set; }
    public string Tone { get; set; } = "auto";

    public Dictionary<string, StyleScore> Scores { get; set; } = new();

    public LearningStyle() { }
    public LearningStyle(ExplanationStyle start) { Length = start.Length; Depth = start.Depth; Examples = start.Examples; Tone = start.Tone; }

    [JsonIgnore]
    public ExplanationStyle Current => new(Length, Depth, Examples, Tone);

    public StyleScore Score(ExplanationStyle s) => Scores.TryGetValue(s.Key, out var v) ? v : new StyleScore();

    public (ExplanationStyle Style, bool Explored) StyleForNextExplanation(Random? rng = null)
    {
        rng ??= Random.Shared;
        if (rng.NextDouble() < ExplorationRate)
        {
            var n = Current.Neighbors;
            if (n.Count > 0) return (n[rng.Next(n.Count)], true);
        }
        return (Current, false);
    }

    private void Nudge(ExplanationStyle s, double reward, double weight = 1)
    {
        if (!Scores.TryGetValue(s.Key, out var sc)) sc = new StyleScore();
        sc.Mean += Alpha * weight * (reward - sc.Mean);
        sc.N += 1;
        Scores[s.Key] = sc;
    }

    public void Record(ExplanationFeedback f, ExplanationStyle used)
    {
        switch (f)
        {
            case ExplanationFeedback.Helpful:
                Nudge(used, 1);
                break;
            case ExplanationFeedback.TooLong:
            case ExplanationFeedback.CollapsedImmediately:
                {
                    double w = f == ExplanationFeedback.TooLong ? 1.0 : 0.5;
                    if (used.Length.Shorter() is { } s)
                    {
                        Nudge(used, 0, w);
                        Nudge(used with { Length = s }, 0.8, w);
                    }
                    break;
                }
            case ExplanationFeedback.TooShort:
            case ExplanationFeedback.AskedAgain:
                {
                    double w = f == ExplanationFeedback.TooShort ? 1.0 : 0.5;
                    if (used.Length.Longer() is { } l)
                    {
                        Nudge(used, 0, w);
                        Nudge(used with { Length = l }, 0.8, w);
                    }
                    break;
                }
        }
        Reselect();
    }

    public void RecordPuzzle(ExplanationStyle used, bool correct)
    {
        if (!Scores.TryGetValue(used.Key, out var sc)) sc = new StyleScore();
        sc.PuzzleTotal += 1;
        if (correct) sc.PuzzleCorrect += 1;
        Scores[used.Key] = sc;
        Reselect();
    }

    private void Reselect()
    {
        double cur = Score(Current).Combined;
        var best = Current; double bestScore = cur;
        foreach (var l in StyleLengthExt.All)
            foreach (var d in StyleDepthExt.All)
            {
                var s = new ExplanationStyle(l, d, Examples, Tone);
                double v = Score(s).Combined;
                if (v > bestScore + 1e-9 && v > cur + SwitchMargin) { best = s; bestScore = v; }
            }
        Length = best.Length; Depth = best.Depth;
    }
}
