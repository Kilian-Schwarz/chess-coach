using System.Globalization;

namespace ChessCoach.Core;

public static class Threats
{
    public const int Threshold = 150;

    public sealed record Threat(UciMove Move, IReadOnlyList<string> Line, int Gain, string Text, string? Reason = null);

    public sealed record Analysis(List<PieceAt> Lost, List<PieceAt> Attacked, List<PieceAt> Pinned, bool Concrete)
    {
        public string? Reason(int? mateIn, CultureInfo? culture)
        {
            if ((mateIn ?? 0) > 0) return null;
            string List(List<PieceAt> items) => string.Join(", ", items.Select(i => i.Text(culture)));
            if (Lost.Count > 0) return CoreLocale.Format("threat.reason.lose", culture, List(Lost));
            if (Pinned.Count > 0) return CoreLocale.Format("threat.reason.pinned", culture, List(Pinned));
            if (Attacked.Count > 0) return CoreLocale.Format("threat.reason.attack", culture, List(Attacked));
            return null;
        }
    }

    public static Analysis Analyse(Placement board, UciMove move, EngineLine line)
    {
        var lost = new List<PieceAt>();
        var attacked = new List<PieceAt>();
        var pinned = new List<PieceAt>();
        bool concrete = (line.MateIn ?? 0) > 0;
        if (board.Piece(move.FromFile, move.FromRank) is not char mover) return new Analysis(lost, attacked, pinned, concrete);
        var moverColor = Placement.ColorOf(mover);
        var me = moverColor.Opposite();
        var start = new ChessPosition(board, moverColor, CastlingRights.None);
        if (!start.IsLegal(move)) return new Analysis(lost, attacked, pinned, concrete);
        if (PositionFacts.OutcomeOf(line.Pv, start, me) is { } o)
        {
            var seen = new HashSet<string>();
            foreach (var c in o.Captures)
                if (Placement.ColorOf(c.Victim) == me && seen.Add(c.Square)) lost.Add(new PieceAt(c.Victim, c.Square));
            if (o.Delta <= -1 && lost.Count > 0) concrete = true;
        }
        var after = board.Applying(move);
        var known = board.Hanging(me).Select(h => h.Square).ToHashSet();
        var pins = after.Pins(me);
        foreach (var h in after.Hanging(me))
        {
            if (known.Contains(h.Square)) continue;
            var at = new PieceAt(h.Piece, h.Square);
            if (pins.Any(p => p.File == h.File && p.Rank == h.Rank)) pinned.Add(at); else attacked.Add(at);
            concrete = true;
        }
        return new Analysis(lost, attacked, pinned, concrete);
    }

    public static string? NullMoveFen(ChessPosition pos)
    {
        if (pos.IsCheck || pos.Board.Problem(pos.Turn) != null) return null;
        var n = pos.NullMove;
        if (n.Board.Problem(n.Turn) != null) return null;
        return n.Fen;
    }

    public static Threat? Detect(ChessPosition pos, EngineResult current, EngineResult opponent, int threshold = Threshold)
    {
        if (pos.IsCheck || opponent.Lines.Count == 0 || string.IsNullOrEmpty(opponent.BestMove)) return null;
        var best = opponent.Lines[0];
        if (UciMove.Parse(opponent.BestMove) is not { } move) return null;

        int gain = opponent.Value + current.Value;
        if (gain < threshold) return null;
        var analysis = Analyse(pos.Board, move, best);
        if (!analysis.Concrete) return null;
        var nullPos = pos.NullMove;
        return new Threat(move, best.Pv, Math.Min(gain, 20_000), Describe(move, best, nullPos, analysis), analysis.Reason(best.MateIn, null));
    }

    public static string Describe(UciMove move, EngineLine line, ChessPosition nullPos, Analysis? analysis = null, CultureInfo? culture = null)
    {
        string head = line.MateIn is int m && m > 0
            ? CoreLocale.Format("threat.mate", culture, nullPos.Board.DescribeMove(move, culture, check: false), m)
            : CoreLocale.Format("threat.text", culture, nullPos.Board.DescribeMove(move, culture));
        return analysis?.Reason(line.MateIn, culture) is { } reason ? head + ". " + reason : head;
    }
}
