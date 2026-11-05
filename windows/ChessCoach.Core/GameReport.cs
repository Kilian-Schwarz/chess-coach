using System.Globalization;
using System.Text.Json.Serialization;

namespace ChessCoach.Core;

public sealed record ReviewedMove(string Color, string Uci, string? San, string FenBefore, int ValueBefore, int ValueAfter, string? BestUci, int Hint = 0);

public sealed class MoveRecord
{
    public int Ply { get; set; }
    public string Color { get; set; } = "w";
    public string Uci { get; set; } = "";
    public string? San { get; set; }
    public string Text { get; set; } = "";
    public double WinBefore { get; set; }
    public double WinAfter { get; set; }
    public int Loss { get; set; }
    public int RatedLoss { get; set; }
    public int Hint { get; set; }
    public double Accuracy { get; set; }

    public string Rating { get; set; } = "";
    public string? BestText { get; set; }
}

public sealed class SideSummary
{
    public int Moves { get; set; }
    public int RatedMoves { get; set; }
    public double Accuracy { get; set; }
    public double Acpl { get; set; }
    public double Performance { get; set; }
    public int Best { get; set; }
    public int Good { get; set; }
    public int Inaccuracies { get; set; }
    public int Mistakes { get; set; }
    public int Blunders { get; set; }
    public double AssistedShare { get; set; }
}

public sealed class KeyMoment
{
    public int Ply { get; set; }
    public string Color { get; set; } = "w";
    public string MoveText { get; set; } = "";
    public string? BestText { get; set; }
    public double WinBefore { get; set; }
    public double WinAfter { get; set; }
    public string Facts { get; set; } = "";
    public string? PromptText { get; set; }
}

public sealed class GameReport
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public DateTime Date { get; set; }
    public string? WhiteProfileId { get; set; }
    public string? BlackProfileId { get; set; }
    public string? Opening { get; set; }
    public List<MoveRecord> Moves { get; set; } = new();
    public SideSummary White { get; set; } = new();
    public SideSummary Black { get; set; } = new();
    public List<KeyMoment> KeyMoments { get; set; } = new();
    public List<Takeaway> Takeaways { get; set; } = new();
    public string? Summary { get; set; }
    public string? Result { get; set; }
    public string? EndReason { get; set; }
    public bool EngineAssisted { get; set; }

    public const int BookMoves = 6;
    public const int EvalClamp = 600, AssistedHintLevel = 3;

    public SideSummary Side(string color) => color == "w" ? White : Black;

    public static MoveRating RatingOf(int valueBefore, int valueAfter, string? bestUci, string uci) =>
        MoveRatingExt.From(valueBefore, valueAfter, bestUci != null && bestUci == uci);

    public static GameReport Build(IReadOnlyList<ReviewedMove> reviewed, string? id = null, DateTime? date = null,
        string? whiteProfileId = null, string? blackProfileId = null, string? opening = null)
    {
        var records = new List<MoveRecord>();
        var moments = new List<(double Drop, KeyMoment M)>();
        for (int i = 0; i < reviewed.Count; i++)
        {
            var m = reviewed[i];
            var place = FenReader.Parse(m.FenBefore)?.Placement;
            var mv = UciMove.Parse(m.Uci);
            string Text(CultureInfo? culture) => place != null && mv != null ? place.DescribeMove(mv.Value, culture) : m.Uci;
            string? BestText(CultureInfo? culture) =>
                m.BestUci is { } b && b != m.Uci && place != null && UciMove.Parse(b) is { } bm ? place.DescribeMove(bm, culture) : null;
            string text = Text(null);
            string? bestText = BestText(null);
            double wb = Core.Accuracy.WinChance(m.ValueBefore), wa = Core.Accuracy.WinChance(m.ValueAfter);
            int loss = Core.Accuracy.CpLoss(m.ValueBefore, m.ValueAfter);
            int ratedLoss = Core.Accuracy.CpLoss(Math.Clamp(m.ValueBefore, -EvalClamp, EvalClamp), Math.Clamp(m.ValueAfter, -EvalClamp, EvalClamp));
            double acc = Core.Accuracy.MoveAccuracy(wb, wa);
            var rating = RatingOf(m.ValueBefore, m.ValueAfter, m.BestUci, m.Uci);
            string? san = m.San;
            if (san == null && mv != null && ChessPosition.FromFen(m.FenBefore) is { } cp) san = Core.San.Of(mv.Value, cp);
            records.Add(new MoveRecord
            {
                Ply = i + 1, Color = m.Color, Uci = m.Uci, San = san, Text = text, WinBefore = wb, WinAfter = wa,
                Loss = loss, RatedLoss = ratedLoss, Hint = m.Hint, Accuracy = acc, Rating = rating.Raw(), BestText = bestText,
            });
            double drop = wb - wa;
            if (drop >= 10 && place != null && mv != null)
            {
                var color = m.Color == "w" ? PieceColor.White : PieceColor.Black;
                var after = place.Applying(mv.Value);
                bool mateFollows = (i + 1 < reviewed.Count ? reviewed[i + 1].ValueBefore : 0) >= 9000;
                string Facts(CultureInfo? culture, string? better)
                {
                    var facts = new List<string>();
                    var hang = after.HangingPieces(color, culture);
                    if (hang.Count > 0) facts.Add(CoreLocale.Format("report.after", culture, string.Join("; ", hang)));
                    if (better != null) facts.Add(CoreLocale.Format("report.better", culture, better));
                    if (mateFollows) facts.Add(CoreLocale.Get("report.opponentMates", culture));
                    facts.Add(CoreLocale.Format("report.winChance", culture, (int)Math.Round(wb, MidpointRounding.AwayFromZero), (int)Math.Round(wa, MidpointRounding.AwayFromZero)));
                    return string.Join(". ", facts);
                }
                var en = CoreLocale.English;
                moments.Add((drop, new KeyMoment
                {
                    Ply = i + 1, Color = m.Color, MoveText = text, BestText = bestText, WinBefore = wb, WinAfter = wa,
                    Facts = Facts(null, bestText), PromptText = $"{Text(en)}. {Facts(en, BestText(en))}",
                }));
            }
        }
        SideSummary Summary(string c)
        {
            var mine = records.Where(r => r.Color == c).ToList();
            var s = new SideSummary { Moves = mine.Count };
            s.Accuracy = Core.Accuracy.GameAccuracy(mine.Select(r => r.Accuracy).ToList());
            s.Acpl = Core.Accuracy.Acpl(mine.Select(r => r.Loss).ToList());
            int skip = mine.Count > BookMoves + PlayerProfile.MinRatedMoves / 2 ? BookMoves : 0;
            var rated = mine.Where(r => r.Hint < AssistedHintLevel).Skip(skip).ToList();
            s.RatedMoves = mine.Count;
            s.Performance = rated.Count == 0 ? 0 : Core.Accuracy.PerformanceRating(Core.Accuracy.Acpl(rated.Select(r => r.RatedLoss).ToList()));
            s.AssistedShare = mine.Count == 0 ? 0 : mine.Sum(r => r.Hint >= AssistedHintLevel ? 1.0 : r.Hint > 0 ? 0.5 : 0) / mine.Count;
            foreach (var r in mine)
                switch (MoveRatingExt.FromRaw(r.Rating))
                {
                    case MoveRating.Best: s.Best++; break;
                    case MoveRating.Good: s.Good++; break;
                    case MoveRating.Inaccuracy: s.Inaccuracies++; break;
                    case MoveRating.Mistake: s.Mistakes++; break;
                    case MoveRating.Blunder: s.Blunders++; break;
                }
            return s;
        }
        var top = moments.GroupBy(x => x.M.Color).SelectMany(g => g.OrderByDescending(x => x.Drop).Take(3))
            .Select(x => x.M).OrderBy(k => k.Ply).ToList();
        return new GameReport
        {
            Id = id ?? Guid.NewGuid().ToString().ToLowerInvariant(), Date = date ?? DateTime.UtcNow,
            WhiteProfileId = whiteProfileId, BlackProfileId = blackProfileId, Opening = opening,
            Moves = records, White = Summary("w"), Black = Summary("b"), KeyMoments = top,
        };
    }

    public void Rebuild(IReadOnlyList<ReviewedMove> reviewed)
    {
        var fresh = Build(reviewed, Id, Date, WhiteProfileId, BlackProfileId, Opening);
        Moves = fresh.Moves; White = fresh.White; Black = fresh.Black; KeyMoments = fresh.KeyMoments;
    }

    public static string MoveLabel(int ply, string color, CultureInfo? culture = null) =>
        CoreLocale.Format("report.moveLabel", culture, (ply + 1) / 2, (color == "w" ? PieceColor.White : PieceColor.Black).Label(culture));

    public Coach.ExplanationRequest SummaryRequest(string color, PlayerContext context, CultureInfo? answerCulture = null)
    {
        var me = Side(color); var you = Side(color == "w" ? "b" : "w");
        static string F(double d) => Fmt.Round0(d);
        var en = CoreLocale.English;
        var lines = new List<string>();
        if (Opening is { } o) lines.Add($"Opening: {o}");
        lines.Add($"The player is {(color == "w" ? "White" : "Black")}, {Moves.Count} half-moves in total.");
        lines.Add($"Accuracy {F(me.Accuracy)} %, average loss {F(me.Acpl)} centipawns, performance rating {F(me.Performance)}.");
        lines.Add($"Opponent: accuracy {F(you.Accuracy)} %.");
        lines.Add($"Player's moves: {me.Best} best, {me.Good} good, {me.Inaccuracies} inaccuracies, {me.Mistakes} mistakes, {me.Blunders} blunders.");
        foreach (var k in KeyMoments.Where(k => k.Color == color))
            lines.Add($"Key moment ({MoveLabel(k.Ply, k.Color, en)}): {k.PromptText ?? k.MoveText + ". " + k.Facts}.");
        for (int i = 0; i < Takeaways.Count; i++) lines.Add($"Takeaway {i + 1}: {Takeaways[i].Text(en)}");
        var user = string.Join("\n", lines) + "\n\n" + Coach.StyleCard(context, new ExplanationStyle(StyleLength.Long, StyleDepth.Concrete), answerCulture);
        return new Coach.ExplanationRequest(Prompts.SummarySystem, user, 400);
    }

    public string FallbackSummary(string color)
    {
        var me = Side(color);
        var text = CoreLocale.Format("report.fallback", null, (int)Math.Round(me.Accuracy, MidpointRounding.AwayFromZero), me.Mistakes + me.Blunders);
        var k = KeyMoments.FirstOrDefault(x => x.Color == color);
        if (k != null) text += CoreLocale.Format("report.fallbackMoment", null, MoveLabel(k.Ply, k.Color), k.MoveText);
        return text + ".";
    }
}
