using System.Globalization;

namespace ChessCoach.Core;

public static class Threats
{
    public const int Threshold = 150;

    public sealed record Threat(UciMove Move, IReadOnlyList<string> Line, int Gain, string Text);

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
        var nullPos = pos.NullMove;
        return new Threat(move, best.Pv, Math.Min(gain, 20_000), Describe(move, best, nullPos));
    }

    public static string Describe(UciMove move, EngineLine line, ChessPosition nullPos, CultureInfo? culture = null)
    {
        if (line.MateIn is int m && m > 0)
            return CoreLocale.Format("threat.mate", culture, nullPos.Board.DescribeMove(move, culture, check: false), m);
        return CoreLocale.Format("threat.text", culture, nullPos.Board.DescribeMove(move, culture));
    }
}
