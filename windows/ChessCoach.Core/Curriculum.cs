using System.Globalization;
using System.Text.Json.Serialization;

namespace ChessCoach.Core;

[JsonConverter(typeof(SkillConverter))]
public enum Skill { PieceSafety, Threats, Tactics, Opening, Middlegame, Endgame, KingSafety }

public sealed class SkillConverter : RawEnumConverter<Skill>
{
    protected override string ToRaw(Skill v) => v.Raw();
    protected override bool TryFromRaw(string raw, out Skill v) => SkillExt.TryParse(raw, out v);
}

public static class SkillExt
{
    public static readonly Skill[] All = Enum.GetValues<Skill>();
    public static string Raw(this Skill s) => string.Concat(s.ToString().Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    public static bool TryParse(string raw, out Skill s)
    {
        foreach (var x in All) if (x.Raw() == raw) { s = x; return true; }
        s = default; return false;
    }
    public static string Key(this Skill s) => char.ToLowerInvariant(s.ToString()[0]) + s.ToString()[1..];
    public static string Label(this Skill s, CultureInfo? culture = null) => CoreLocale.Get("skill." + s.Key(), culture);
}

[JsonConverter(typeof(MistakeTypeConverter))]
public enum MistakeType { HangingPiece, MissedThreat, MissedTactic, KingSafety, OpeningPrinciple, EndgameTechnique, Other }

public sealed class MistakeTypeConverter : RawEnumConverter<MistakeType>
{
    protected override string ToRaw(MistakeType v) => v.Raw();
    protected override bool TryFromRaw(string raw, out MistakeType v) => MistakeTypeExt.TryParse(raw, out v);
}

public static class MistakeTypeExt
{
    public static readonly MistakeType[] All = Enum.GetValues<MistakeType>();
    public static string Raw(this MistakeType m) => m switch
    {
        MistakeType.HangingPiece => "hanging_piece",
        MistakeType.MissedThreat => "missed_threat",
        MistakeType.MissedTactic => "missed_tactic",
        MistakeType.KingSafety => "king_safety",
        MistakeType.OpeningPrinciple => "opening_principle",
        MistakeType.EndgameTechnique => "endgame_technique",
        _ => "other",
    };
    public static bool TryParse(string raw, out MistakeType m)
    {
        foreach (var x in All) if (x.Raw() == raw) { m = x; return true; }
        m = default; return false;
    }
    public static string Label(this MistakeType m, CultureInfo? culture = null) => CoreLocale.Get("mistake." + char.ToLowerInvariant(m.ToString()[0]) + m.ToString()[1..], culture);

    public static Skill Skill(this MistakeType m) => m switch
    {
        MistakeType.HangingPiece => Core.Skill.PieceSafety,
        MistakeType.MissedThreat => Core.Skill.Threats,
        MistakeType.MissedTactic => Core.Skill.Tactics,
        MistakeType.KingSafety => Core.Skill.KingSafety,
        MistakeType.OpeningPrinciple => Core.Skill.Opening,
        MistakeType.EndgameTechnique => Core.Skill.Endgame,
        _ => Core.Skill.Middlegame,
    };
}

public sealed record PathStage(int Number, Skill Skill)
{
    public string Title(CultureInfo? culture = null) => Skill.Label(culture);
    public string Summary(CultureInfo? culture = null) => CoreLocale.Get("skill." + Skill.Key() + ".summary", culture);
}

public sealed record Adaptation(int DefaultHintLevel, int NewPuzzlesPerDay);

public static class Curriculum
{
    public const double FocusThreshold = 0.7;
    public const double TargetLow = 0.70, TargetHigh = 0.85;

    public static readonly PathStage[] Path =
    {
        new(1, Skill.PieceSafety), new(2, Skill.Threats), new(3, Skill.Tactics), new(4, Skill.Opening),
        new(5, Skill.KingSafety), new(6, Skill.Endgame), new(7, Skill.Middlegame),
    };

    public const int ThreatAutoGames = 3;
    public const double ThreatAutoSkill = 0.4;

    public static bool ShowThreatAutomatically(int gamesPlayed, double threatSkill) => gamesPlayed < ThreatAutoGames || threatSkill < ThreatAutoSkill;

    public static PathStage Stage(Skill skill) => Path.First(p => p.Skill == skill);

    public static double Value(IReadOnlyDictionary<string, double> skills, Skill s) => skills.TryGetValue(s.Raw(), out var v) ? v : 0.5;

    public static Skill Focus(IReadOnlyDictionary<string, double> skills)
    {
        foreach (var p in Path) if (Value(skills, p.Skill) < FocusThreshold) return p.Skill;
        var best = Path[0];
        foreach (var p in Path) if (Value(skills, p.Skill) < Value(skills, best.Skill)) best = p;
        return best.Skill;
    }

    public static string FocusLabel(Skill skill, CultureInfo? culture = null) => CoreLocale.Format("focus.label", culture, Stage(skill).Title(culture));

    public static string FocusPromptHint(Skill skill) => skill switch
    {
        Skill.PieceSafety => Prompts.FocusPieceSafety,
        Skill.Threats => Prompts.FocusThreats,
        Skill.Tactics => Prompts.FocusTactics,
        Skill.Opening => Prompts.FocusOpening,
        Skill.KingSafety => Prompts.FocusKingSafety,
        Skill.Endgame => Prompts.FocusEndgame,
        _ => Prompts.FocusPlan,
    };

    public static Adaptation Adapt(PlayerLevel level, Skill focus, IReadOnlyDictionary<string, double> skills, double? successRate)
    {
        int hint = level == PlayerLevel.Beginner ? 2 : 1;
        if (Value(skills, focus) < 0.4) hint += 1;
        int perDay = PuzzleScheduler.MaxNewPerDay;
        if (successRate is double q)
        {
            if (q > TargetHigh) hint -= 1;
            else if (q < TargetLow) { hint += 1; perDay = PuzzleScheduler.MaxNewPerDay / 2; }
        }
        return new Adaptation(Math.Min(2, Math.Max(1, hint)), perDay);
    }
}
