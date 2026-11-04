using System.Globalization;
using System.Text.Json.Serialization;

namespace ChessCoach.Core;

public sealed class HintStats
{
    public List<int> LevelUses { get; set; } = new() { 0, 0, 0, 0, 0 };
}

public sealed class RatingEntry
{
    public DateTime Date { get; set; }
    public double Rating { get; set; }
    public double Performance { get; set; }
}

public sealed record PlayerContext(PlayerLevel Level, Skill? Focus = null, IReadOnlyList<string>? TypicalMistakes = null)
{
    public IReadOnlyList<string> Mistakes => TypicalMistakes ?? Array.Empty<string>();
}

public sealed class PlayerProfile
{
    public const int CurrentSchemaVersion = 1;
    public const int PlacementTotal = 3;
    public const double StartRating = 1100.0;
    public const double AlphaSkill = 0.15;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Id { get; set; } = Guid.NewGuid().ToString().ToLowerInvariant();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int GamesPlayed { get; set; }

    public int PlacementGames { get; set; }
    public double Rating { get; set; } = StartRating;
    public PlayerLevel Level { get => _level ?? PlayerLevelExt.FromRating(Rating); set => _level = value; }
    private PlayerLevel? _level;

    public Dictionary<string, double> Skills { get; set; } = new();

    public Dictionary<string, int> MistakeCounts { get; set; } = new();

    public Dictionary<string, List<string>>? MistakeExamples { get; set; }

    public string Focus { get => _focus ?? Curriculum.Focus(Skills).Raw(); set => _focus = value; }
    private string? _focus;

    public double LearningSpeed { get; set; } = 1.0;
    public HintStats HintStats { get; set; } = new();
    public List<RatingEntry> RatingHistory { get; set; } = new();

    public LearningStyle Style { get; set; } = new();

    public PlayerProfile()
    {
        foreach (var k in SkillExt.All) Skills[k.Raw()] = 0.5;
    }

    public PlayerProfile(string name, string? id = null, DateTime? createdAt = null, ExplanationStyle? startStyle = null) : this()
    {
        Name = name;
        if (id != null) Id = id;
        if (createdAt != null) CreatedAt = createdAt.Value;
        Style = new LearningStyle(startStyle ?? new ExplanationStyle());
    }

    public PlayerProfile Normalize()
    {
        Skills ??= new();
        foreach (var k in SkillExt.All) if (!Skills.ContainsKey(k.Raw())) Skills[k.Raw()] = 0.5;
        MistakeCounts ??= new();
        RatingHistory ??= new();
        HintStats ??= new HintStats();
        while (HintStats.LevelUses.Count < 5) HintStats.LevelUses.Add(0);
        Style ??= new LearningStyle();
        return this;
    }

    public PlayerProfile Clone() => ChessCoachJson.Clone(this);

    [JsonIgnore] public bool IsPlacing => PlacementGames < PlacementTotal;

    [JsonIgnore] public string? PlacementText => IsPlacing ? CoreLocale.Format("profile.placement", null, PlacementGames + 1, PlacementTotal) : null;

    [JsonIgnore] public Skill FocusSkill => SkillExt.TryParse(Focus, out var s) ? s : Skill.PieceSafety;

    public void Observe(Skill skill, double value)
    {
        double v = Math.Min(1, Math.Max(0, value));
        double old = Skills.TryGetValue(skill.Raw(), out var o) ? o : 0.5;
        Skills[skill.Raw()] = (1 - AlphaSkill) * old + AlphaSkill * v;
    }

    public void Apply(MoveAnalysis analysis)
    {
        foreach (var o in analysis.Observations) Observe(o.Skill, o.Value);
        if (analysis.MistakeType is { } m && analysis.Example is { } ex) RecordMistake(m, ex);
        UpdateFocus();
    }

    public void RecordMistake(MistakeType type, string? example = null)
    {
        MistakeCounts[type.Raw()] = MistakeCounts.GetValueOrDefault(type.Raw()) + 1;
        if (example != null)
        {
            MistakeExamples ??= new();
            if (!MistakeExamples.TryGetValue(type.Raw(), out var list)) MistakeExamples[type.Raw()] = list = new();
            list.Add(example);
            if (list.Count > 5) list.RemoveRange(0, list.Count - 5);
        }
    }

    public void RecordHintUse(int level)
    {
        while (HintStats.LevelUses.Count < 5) HintStats.LevelUses.Add(0);
        HintStats.LevelUses[Math.Min(4, Math.Max(0, level))] += 1;
    }

    public void UpdateFocus() => _focus = Curriculum.Focus(Skills).Raw();

    public List<(MistakeType Type, int Count)> TopMistakes(int n = 3)
    {
        var list = new List<(MistakeType Type, int Count)>();
        foreach (var (k, v) in MistakeCounts)
            if (v > 0 && MistakeTypeExt.TryParse(k, out var t)) list.Add((t, v));
        list.Sort((a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : string.CompareOrdinal(a.Type.Raw(), b.Type.Raw()));
        return list.Take(n).ToList();
    }

    [JsonIgnore]
    public double? AverageHintLevel
    {
        get
        {
            var uses = HintStats.LevelUses.Skip(1).ToList();
            int used = uses.Sum();
            if (used == 0) return null;
            int sum = 0;
            for (int i = 0; i < uses.Count; i++) sum += (i + 1) * uses[i];
            return (double)sum / used;
        }
    }

    public double CompleteGame(double performance, DateTime? date = null)
    {
        GamesPlayed += 1;
        if (IsPlacing)
        {
            PlacementGames += 1;
            var perfs = RatingHistory.Take(PlacementGames - 1).Select(r => r.Performance).Append(performance).ToList();
            Rating = perfs.Sum() / perfs.Count;
        }
        else Rating += Accuracy.RatingDelta(Rating, performance, GamesPlayed);
        Level = PlayerLevelExt.FromRating(Rating);
        RatingHistory.Add(new RatingEntry { Date = date ?? DateTime.UtcNow, Rating = Rating, Performance = performance });
        return Rating;
    }

    [JsonIgnore]
    public PlayerContext PromptContext => new(Level, FocusSkill, TopMistakes(3).Select(m => m.Type.Label(CoreLocale.English)).ToList());

    public (ExplanationStyle Style, bool Explored) NextExplanationStyle(Random? rng = null) => Style.StyleForNextExplanation(rng);

    public void RecordFeedback(ExplanationFeedback f, ExplanationStyle usedStyle) => Style.Record(f, usedStyle);

    public void RecordPuzzleAfterExplanation(ExplanationStyle usedStyle, bool correct) => Style.RecordPuzzle(usedStyle, correct);
}
