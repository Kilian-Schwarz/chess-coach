using System.Globalization;

namespace ChessCoach.Core;

public enum MoveRating { Best, Good, Inaccuracy, Mistake, Blunder }

public static class MoveRatingExt
{
    public static string Raw(this MoveRating r) => r switch
    {
        MoveRating.Best => "best", MoveRating.Good => "good", MoveRating.Inaccuracy => "inaccuracy",
        MoveRating.Mistake => "mistake", _ => "blunder",
    };

    public static MoveRating? FromRaw(string raw) => raw switch
    {
        "best" => MoveRating.Best,
        "good" => MoveRating.Good,
        "inaccuracy" => MoveRating.Inaccuracy,
        "mistake" => MoveRating.Mistake,
        "blunder" => MoveRating.Blunder,
        _ => null,
    };

    public static string Label(this MoveRating r, CultureInfo? culture = null) => CoreLocale.Get("rating." + r.Raw(), culture);

    public static MoveRating From(int loss, bool wasBest)
    {
        if (wasBest || loss <= 15) return MoveRating.Best;
        if (loss <= 60) return MoveRating.Good;
        if (loss <= 130) return MoveRating.Inaccuracy;
        if (loss <= 300) return MoveRating.Mistake;
        return MoveRating.Blunder;
    }
}

public sealed record ReviewTexts(string Played, string? Best, string BestScore, string PlayedScore, string ReplyLine);

public sealed record MoveReview(Placement Before, UciMove Played, string PlayedText, UciMove? Best, string? BestText,
    string BestScore, string PlayedScore, MoveRating Rating, int Loss, string ReplyLine, PieceColor Player,
    ReviewTexts? English = null)
{
    public ReviewTexts PromptTexts => English ?? new ReviewTexts(PlayedText, BestText, BestScore, PlayedScore, ReplyLine);
}

public static class Coach
{
    public static MoveReview Review(Placement prev, UciMove played, Placement next, PieceColor mover,
        EngineResult beforeResult, EngineResult? afterResult)
    {
        int playedValue;
        int? cp = null, mate = null;
        string? mateKey = null;
        if (afterResult != null && afterResult.BestMove.Length > 0)
        {
            playedValue = -afterResult.Value;
            cp = afterResult.ScoreCP is int c ? -c : null;
            mate = afterResult.MateIn is int m ? -m : null;
        }
        else if (next.IsInCheck(mover.Opposite())) { playedValue = 10_000; mateKey = "score.checkmate"; }
        else { playedValue = 0; mateKey = "score.stalemate"; }
        var best = UciMove.Parse(beforeResult.BestMove);
        int loss = Math.Max(0, beforeResult.Value - playedValue);
        var pv = afterResult?.Lines.Count > 0 ? afterResult.Lines[0].Pv : Array.Empty<string>();
        ReviewTexts Texts(CultureInfo? culture) => new(
            prev.DescribeMove(played, culture), best is UciMove b ? prev.DescribeMove(b, culture) : null,
            EngineLine.Text(beforeResult.ScoreCP, beforeResult.MateIn, culture),
            mateKey != null ? CoreLocale.Get(mateKey, culture) : EngineLine.Text(cp, mate, culture),
            next.DescribeLine(pv, 4, culture));
        var ui = Texts(null);
        return new MoveReview(prev, played, ui.Played, best, ui.Best, ui.BestScore, ui.PlayedScore,
            MoveRatingExt.From(loss, best?.Uci == played.Uci), loss, ui.ReplyLine, mover, Texts(CoreLocale.English));
    }

    public sealed record ExplanationRequest(string System, string User, int MaxTokens)
    {
        public int ApproxTokens => (System.Length + User.Length) / 4;
    }

    public static string ToneInstruction(PlayerContext context) => context.Level switch
    {
        PlayerLevel.Beginner => Prompts.ToneBeginner,
        PlayerLevel.Intermediate => Prompts.ToneIntermediate,
        _ => Prompts.ToneStrong,
    };

    public static string StyleCard(PlayerContext context, ExplanationStyle style, CultureInfo? answerCulture = null)
    {
        var en = CoreLocale.English;
        var card = $"Player: {context.Level.Label(en)}";
        if (context.Focus is { } f) card += $", focus {f.Label(en)}";
        if (context.Mistakes.Count > 0) card += $", mistake: {context.Mistakes[0]}";
        card += $". Style: {style.Length.Raw()}, {(style.Depth == StyleDepth.Principle ? "principle" : "concrete+variation")}";
        card += $", max {style.Length.MaxWords()} words";
        if (style.Length == StyleLength.Long) card += ", takeaway";
        if (style.Examples) card += ", example";
        card += ". " + (style.Tone == "auto" ? ToneInstruction(context) : $"Tone: {style.Tone}.");
        if (context.Focus is { } f2 && style.Length != StyleLength.Short) card += " " + Curriculum.FocusPromptHint(f2);
        return card + " " + AnswerIn(answerCulture);
    }

    private static string AnswerIn(CultureInfo? culture) => $"Answer in {CoreLocale.AnswerLanguage(culture)}.";

    private static List<string>? SuggestionFacts(Placement placement, PieceColor me, string fen, EngineResult result)
    {
        var en = CoreLocale.English;
        if (result.Lines.Count == 0 || UciMove.Parse(result.Lines[0].Move) is not UciMove best) return null;
        var first = result.Lines[0];
        var side = me.Label(en);
        var facts = new List<string>
        {
            $"Position (FEN): {fen}",
            $"{side} to move. All evaluations are from {side}'s point of view.",
            $"Best move per engine: {placement.DescribeMove(best, en)} (evaluation: {first.ScoreTextEnglish})",
            $"Expected continuation: {placement.DescribeLine(first.Pv, 4, en)}",
        };
        for (int i = 1; i < result.Lines.Count; i++)
            if (UciMove.Parse(result.Lines[i].Move) is UciMove m)
                facts.Add($"Alternative {i}: {placement.DescribeMove(m, en)} (evaluation: {result.Lines[i].ScoreTextEnglish})");
        AppendTactics(facts, placement, best, me, "The best move");
        return facts;
    }

    private static List<string> ReviewFacts(MoveReview r)
    {
        var en = CoreLocale.English;
        var t = r.PromptTexts;
        var side = r.Player.Label(en);
        var facts = new List<string>
        {
            $"Position before the move (FEN): {r.Before.Fen(r.Player)}",
            $"{side} moved. All evaluations are from {side}'s point of view.",
            $"Played move: {t.Played}. Evaluation after it: {t.PlayedScore}.",
            $"Rating: {r.Rating.Label(en)} (loss against the best move: {Fmt.Decimal1(r.Loss / 100.0)} pawns).",
        };
        if (t.Best != null && r.Rating != MoveRating.Best) facts.Add($"Better was: {t.Best} (evaluation: {t.BestScore}).");
        if (t.ReplyLine.Length > 0) facts.Add($"Best reply for {r.Player.Opposite().Label(en)}: {t.ReplyLine}");
        var hanging = r.Before.Applying(r.Played).HangingPieces(r.Player, en);
        if (hanging.Count > 0) facts.Add($"After the move, {side} has: {string.Join("; ", hanging)}.");
        if (r.Best is UciMove best && r.Rating != MoveRating.Best) AppendTactics(facts, r.Before, best, r.Player, "The better move");
        return facts;
    }

    public static ExplanationRequest? SuggestionRequest(Placement placement, PieceColor me, string fen, EngineResult result,
        PlayerContext context, ExplanationStyle style, CultureInfo? answerCulture = null)
    {
        var facts = SuggestionFacts(placement, me, fen, result);
        if (facts == null) return null;
        return Request(facts, $"Explain to the {me.Label(CoreLocale.English)} player why the best move is good.", context, style, answerCulture);
    }

    public static ExplanationRequest ReviewRequest(MoveReview r, PlayerContext context, ExplanationStyle style, CultureInfo? answerCulture = null)
    {
        var name = r.Player.Label(CoreLocale.English);
        var ask = r.Rating is MoveRating.Best or MoveRating.Good
            ? $"Explain to the {name} player why their move was good."
            : $"Explain to the {name} player why their move was weaker and what idea the better move had.";
        return Request(ReviewFacts(r), ask, context, style, answerCulture);
    }

    private static ExplanationRequest Request(List<string> facts, string ask, PlayerContext context, ExplanationStyle style, CultureInfo? answerCulture) =>
        new(Prompts.System, string.Join("\n", facts) + "\n\n" + ask + "\n" + StyleCard(context, style, answerCulture), style.MaxTokens);

    public static ExplanationRequest HintRequest(HintSet hint, PlayerContext context, CultureInfo? answerCulture = null) =>
        new(Prompts.HintSystem, hint.Stage3Prompt + "\n" + ToneInstruction(context) + " " + AnswerIn(answerCulture), 80);

    public static ExplanationRequest QuestionRequest(Placement placement, PieceColor toMove, string question, string? score = null,
        string? bestMove = null, string? threat = null, CultureInfo? answerCulture = null)
    {
        var en = CoreLocale.English;
        var name = toMove.Label(en);
        var facts = new List<string>
        {
            $"Position (FEN): {placement.Fen(toMove)}",
            $"{name} to move. All evaluations are from {name}'s point of view.",
            $"{name} pieces: {placement.PieceList(toMove)}",
            $"{toMove.Opposite().Label(en)} pieces: {placement.PieceList(toMove.Opposite())}",
        };
        if (score != null) facts.Add($"Evaluation of the position: {score}");
        if (bestMove != null) facts.Add($"Best move per engine: {bestMove}");
        if (threat != null) facts.Add(threat);
        foreach (var c in new[] { toMove, toMove.Opposite() })
        {
            var hanging = placement.HangingPieces(c, en);
            if (hanging.Count > 0) facts.Add($"Unprotected or threatened for {c.Label(en)}: {string.Join("; ", hanging)}");
        }
        return new(Prompts.QuestionSystem, string.Join("\n", facts) + $"\n\nPlayer's question: {question}\n{AnswerIn(answerCulture)}", 260);
    }

    public static void AppendTactics(List<string> facts, Placement before, UciMove move, PieceColor me, string label)
    {
        var en = CoreLocale.English;
        var after = before.Applying(move);
        if (before.Piece(move.ToFile, move.ToRank) is char victim)
            facts.Add($"{label} captures {PieceInfo.Name(victim, en)} {UciMove.SquareName(move.ToFile, move.ToRank)}.");
        var targets = new List<string>();
        foreach (var (f, r) in after.Attacks(move.ToFile, move.ToRank))
            if (after.Piece(f, r) is char p && Placement.ColorOf(p) != me)
                targets.Add($"{PieceInfo.Name(p, en)} {UciMove.SquareName(f, r)}");
        if (targets.Count > 0) facts.Add($"{label} then attacks: {string.Join(", ", targets)}.");
        foreach (var color in new[] { me, me.Opposite() })
        {
            var hanging = before.HangingPieces(color, en);
            if (hanging.Count > 0) facts.Add($"Before the move, {color.Label(en)} has: {string.Join("; ", hanging)}.");
        }
        if (after.IsInCheck(me.Opposite())) facts.Add($"{label} gives check.");
    }

    public static string CleanAnswer(string text)
    {
        var t = text;
        int start;
        while ((start = t.IndexOf("<think>", StringComparison.Ordinal)) >= 0)
        {
            int end = t.IndexOf("</think>", start + 7, StringComparison.Ordinal);
            t = end >= 0 ? t.Remove(start, end + 8 - start) : t.Remove(start);
        }
        int close = t.IndexOf("</think>", StringComparison.Ordinal);
        if (close >= 0) t = t.Substring(close + 8);
        return t.Trim();
    }
}
