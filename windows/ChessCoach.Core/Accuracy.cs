using System.Globalization;
using System.Text.Json.Serialization;

namespace ChessCoach.Core;

[JsonConverter(typeof(PlayerLevelConverter))]
public enum PlayerLevel { Beginner, Intermediate, Strong }

public sealed class PlayerLevelConverter : RawEnumConverter<PlayerLevel>
{
    protected override string ToRaw(PlayerLevel v) => v.Raw();
    protected override bool TryFromRaw(string raw, out PlayerLevel v)
    {
        switch (raw)
        {
            case "beginner": v = PlayerLevel.Beginner; return true;
            case "intermediate": v = PlayerLevel.Intermediate; return true;
            case "strong": v = PlayerLevel.Strong; return true;
            default: v = default; return false;
        }
    }
}

public static class PlayerLevelExt
{
    public static string Raw(this PlayerLevel l) => l switch { PlayerLevel.Beginner => "beginner", PlayerLevel.Intermediate => "intermediate", _ => "strong" };
    public static string Label(this PlayerLevel l, CultureInfo? culture = null) => CoreLocale.Get("level." + l.Raw(), culture);

    public static PlayerLevel FromRating(double rating) => rating < 1200 ? PlayerLevel.Beginner : rating <= 1800 ? PlayerLevel.Intermediate : PlayerLevel.Strong;

    public static readonly PlayerLevel[] All = { PlayerLevel.Beginner, PlayerLevel.Intermediate, PlayerLevel.Strong };
}

public static class Accuracy
{
    public const int MateValue = 10_000;
    public const int MaxLoss = 1000;

    public static double WinChance(double cp) => 50 + 50 * (2 / (1 + Math.Exp(-0.00368208 * cp)) - 1);

    public static double MoveAccuracy(double winBefore, double winAfter) =>
        Math.Min(100, Math.Max(0, 103.1668 * Math.Exp(-0.04354 * (winBefore - winAfter)) - 3.1669));

    public static double MoveAccuracyFromValues(int valueBefore, int valueAfter) => MoveAccuracy(WinChance(valueBefore), WinChance(valueAfter));

    public static int CpLoss(int valueBefore, int valueAfter) => Math.Min(MaxLoss, Math.Max(0, valueBefore - valueAfter));

    public static double GameAccuracy(IReadOnlyCollection<double> moveAccuracies) => moveAccuracies.Count == 0 ? 0 : moveAccuracies.Sum() / moveAccuracies.Count;

    public static double Acpl(IReadOnlyCollection<int> losses) =>
        losses.Count == 0 ? 0 : (double)losses.Sum(l => Math.Min(MaxLoss, Math.Max(0, l))) / losses.Count;

    private static readonly (double Acpl, double Rating)[] PerformanceTable =
        { (10, 2400), (20, 2100), (35, 1800), (50, 1600), (70, 1400), (100, 1200), (150, 1000), (250, 800) };

    public static double PerformanceRating(double acpl)
    {
        var t = PerformanceTable;
        if (acpl <= t[0].Acpl) return t[0].Rating;
        if (acpl > t[^1].Acpl) return 600;
        for (int i = 1; i < t.Length; i++)
            if (acpl <= t[i].Acpl)
            {
                var (a0, r0) = t[i - 1]; var (a1, r1) = t[i];
                return r0 + (r1 - r0) * (acpl - a0) / (a1 - a0);
            }
        return 600;
    }

    public static double RatingDelta(double rating, double performance, int gameNumber)
    {
        double k = gameNumber <= 10 ? 0.5 : 0.25;
        return Math.Min(150, Math.Max(-150, k * (performance - rating) / 400 * 100));
    }
}
