using System.Globalization;

namespace ChessCoach.Core;

public sealed record PuzzleLesson(string Why, string Habit);

public sealed record ThemeProgress(HintTheme Theme, int Solved, int Total)
{
    public string Text(CultureInfo? culture = null) => CoreLocale.Format("puzzle.progress", culture, Theme.Title(culture), Solved, Total);
}

public static class PuzzleCoach
{
    public static HintTheme ThemeOf(Puzzle p) => HintThemeExt.TryParse(p.Theme, out var t) ? t : HintTheme.ImprovePiece;

    public static bool NeedsThemeCheck(Puzzle p) => p.ThemeChecked != true;

    public static UciMove? BestMove(Puzzle p) => UciMove.Parse(p.BestLine.Count > 0 ? p.BestLine[0] : p.AcceptedMoves.FirstOrDefault() ?? "");

    public static HintTheme? Classify(Puzzle p, EngineResult result)
    {
        if (ChessPosition.FromFen(p.Fen) is not { } pos || BestMove(p) is not { } best) return null;
        var top = result.Lines.Count > 0 ? result.Lines[0] : null;
        var pv = p.BestLine.Count > 0 ? p.BestLine : top?.Pv ?? new List<string>();
        return Hints.Theme(pos.Board, best, pos.Turn, null, (top?.MateIn ?? 0) > 0, pv, top?.Value ?? 0);
    }

    public static string Source(Puzzle p, CultureInfo? culture = null)
    {
        string date = (p.SourceDate ?? p.CreatedAt).ToLocalTime().ToString("d", culture ?? CoreLocale.Culture);
        if (p.SourceMove is not int number || p.PlayedValue is not int value || UciMove.Parse(p.PlayedMove ?? "") is not { } played
            || ChessPosition.FromFen(p.Fen) is not { } pos || !pos.IsLegal(played))
            return CoreLocale.Format("puzzle.source.date", culture, date);
        return CoreLocale.Format("puzzle.source.move", culture, date, number, San.Localize(San.Of(played, pos), culture), Evaluation(value));
    }

    public static string Evaluation(int cp) =>
        Math.Abs(cp) >= 9_000 ? (cp > 0 ? "+M" : "−M") : (cp / 100.0).ToString("+0.0;−0.0;+0.0", CultureInfo.InvariantCulture);

    public static string Question(HintTheme t, CultureInfo? culture = null) => CoreLocale.Get("puzzle.ask." + t.Raw(), culture);

    public static string Habit(HintTheme t, CultureInfo? culture = null) => CoreLocale.Get("puzzle.habit." + t.Raw(), culture);

    public static string DisplayLine(string text, CultureInfo? culture = null) =>
        string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token =>
        {
            int dot = token.LastIndexOf('.');
            return dot < 0 ? San.Localize(token, culture) : token[..(dot + 1)] + San.Localize(token[(dot + 1)..], culture);
        }));

    public static string? Mechanism(Puzzle p, ChessPosition pos, UciMove best, CultureInfo? culture = null)
    {
        var me = pos.Turn;
        switch (ThemeOf(p))
        {
            case HintTheme.Discovered:
                if (pos.Board.DiscoveredTargets(best, me) is not { Count: > 0 } xs) return null;
                return CoreLocale.Format("puzzle.why.discovered", culture, xs[0].Blocker.Text(culture), xs[0].Slider.Text(culture), xs[0].Target.Text(culture));
            case HintTheme.Pin:
                if (pos.Board.CreatedPins(best, me.Opposite()) is not { Count: > 0 } pins) return null;
                var pin = pins[0];
                return CoreLocale.Format("puzzle.why.pin", culture, new PieceAt(pin.Piece, pin.Square).Text(culture), new PieceAt(pin.Target, pin.TargetSquare).Text(culture));
            case HintTheme.Fork:
                var targets = ForkTargets(pos, best);
                if (targets.Count < 2 || pos.Board.Piece(best.FromFile, best.FromRank) is not char mover) return null;
                return CoreLocale.Format("puzzle.why.fork", culture, new PieceAt(mover, UciMove.SquareName(best.ToFile, best.ToRank)).Text(culture),
                    string.Join(", ", targets.Take(3).Select(t => t.Text(culture))));
            default:
                return null;
        }
    }

    public static List<PieceAt> ForkTargets(ChessPosition pos, UciMove best)
    {
        var result = new List<PieceAt>();
        if (pos.Board.Piece(best.FromFile, best.FromRank) is not char mover) return result;
        var after = pos.Board.Applying(best);
        var opp = pos.Turn.Opposite();
        var defenders = after.Attackers(opp);
        foreach (var (f, r) in after.Attacks(best.ToFile, best.ToRank))
        {
            if (after.Piece(f, r) is not char t || Placement.ColorOf(t) != opp) continue;
            if (char.ToLowerInvariant(t) == 'k' || PieceInfo.Value(t) > PieceInfo.Value(mover) || defenders[r][f].Count == 0)
                result.Add(new PieceAt(t, UciMove.SquareName(f, r)));
        }
        return result;
    }

    public static PuzzleLesson Lesson(Puzzle p, CultureInfo? culture = null)
    {
        var parts = new List<string>();
        if (ChessPosition.FromFen(p.Fen) is { } pos && BestMove(p) is { } best)
        {
            if (Mechanism(p, pos, best, culture) is { } m) parts.Add(m);
            var line = p.BestLine.Count > 0 ? p.BestLine : new List<string> { best.Uci };
            if (PositionFacts.OutcomeOf(line, pos, pos.Turn) is { } o)
            {
                string text = DisplayLine(o.Line, culture);
                if (o.Line.EndsWith('#')) parts.Add(CoreLocale.Format("puzzle.why.mate", culture, text));
                else if (o.Delta > 0) parts.Add(CoreLocale.Format("puzzle.why.gain", culture, text, o.Delta));
                else parts.Add(CoreLocale.Format("puzzle.why.line", culture, text));
            }
        }
        return new PuzzleLesson(string.Join(" ", parts), Habit(ThemeOf(p), culture));
    }

    public static string Hint3(HintTheme theme, UciMove move, ChessPosition pos, CultureInfo? culture = null)
    {
        char piece = pos.Board.Piece(move.FromFile, move.FromRank) ?? 'P';
        string fallback = Hints.Stage3Fallback(theme, piece, culture);
        var area = AreaClause(theme, move, pos, culture);
        return area == null || Hints.RevealsTarget(area, move) ? fallback : fallback + " " + area;
    }

    public static string? AreaClause(HintTheme theme, UciMove best, ChessPosition pos, CultureInfo? culture)
    {
        var me = pos.Turn;
        string dest = UciMove.SquareName(best.ToFile, best.ToRank);
        if (theme == HintTheme.Discovered)
        {
            foreach (var x in pos.Board.DiscoveredTargets(best, me))
                if (x.Target.Square != dest) return CoreLocale.Format("puzzle.area.discovered", culture, x.Slider.Text(culture), x.Target.Text(culture));
        }
        var after = pos.Board.Applying(best);
        var enemy = after.Attacks(best.ToFile, best.ToRank)
            .Select(s => after.Piece(s.Item1, s.Item2) is char t && Placement.ColorOf(t) != me ? new PieceAt(t, UciMove.SquareName(s.Item1, s.Item2)).Text(culture) : null)
            .OfType<string>().Take(3).ToList();
        if (enemy.Count > 0) return CoreLocale.Format("puzzle.area.attacks", culture, string.Join(", ", enemy));
        if (pos.Board.Piece(best.FromFile, best.FromRank) is not char piece || Hints.IsCastling(best, piece)) return null;
        if (best.FromFile == best.ToFile) return CoreLocale.Format("puzzle.area.rank", culture, best.ToRank + 1);
        return CoreLocale.Format("puzzle.area.file", culture, "abcdefgh"[best.ToFile]);
    }

    public static string FailReason(Puzzle p, ChessPosition after, IReadOnlyList<string> refutation, CultureInfo? culture = null)
    {
        var me = after.Turn.Opposite();
        if (PositionFacts.OutcomeOf(refutation, after, me) is { } o)
        {
            if (o.Line.EndsWith('#')) return CoreLocale.Get("puzzle.fail.mate", culture);
            if (o.Delta < 0 && o.Captures.FirstOrDefault(c => c.By != me) is { } c)
                return CoreLocale.Format("puzzle.fail.loss", culture, PieceInfo.Name(c.Victim, culture), c.Square);
        }
        return CoreLocale.Format("puzzle.fail.miss", culture, ThemeOf(p).Title(culture));
    }

    public static List<ThemeProgress> Progress(IEnumerable<Puzzle> puzzles) =>
        puzzles.GroupBy(ThemeOf)
            .Select(g => new ThemeProgress(g.Key, g.Count(p => p.Mastered || (!p.IsNew && p.Box >= 2)), g.Count()))
            .OrderByDescending(t => t.Total).ThenBy(t => (int)t.Theme).ToList();

    public static Coach.ExplanationRequest? WhyRequest(Puzzle p, PlayerContext context, ExplanationStyle style, CultureInfo? answerCulture = null)
    {
        if (ChessPosition.FromFen(p.Fen) is not { } pos || BestMove(p) is not { } best) return null;
        var en = CoreLocale.English;
        var line = p.BestLine.Count > 0 ? p.BestLine : new List<string> { best.Uci };
        string me = pos.Turn.Label(en);
        var facts = new List<string>
        {
            $"Position (FEN): {p.Fen}",
            $"{me} to move. This is a training puzzle; the player just solved it.",
            $"Theme: {ThemeOf(p).Title(en)}.",
            $"Best move: {pos.Board.DescribeMove(best, en)}.",
        };
        if (Mechanism(p, pos, best, en) is { } m) facts.Add(m);
        if (PositionFacts.Consequence(line, pos, pos.Turn) is { } c) facts.Add("Best line: " + c.Text);
        var capped = style.Length == StyleLength.Long ? style with { Length = StyleLength.Medium } : style;
        string ask = $"Explain to the {me} player in plain words why the best move works and what it wins. Do not list the moves again.";
        return new Coach.ExplanationRequest(Prompts.System, string.Join("\n", facts) + "\n\n" + ask + "\n" + Coach.StyleCard(context, capped, answerCulture), capped.MaxTokens);
    }
}
