namespace ChessCoach.Core;

public enum GameEndReason { Checkmate, Stalemate, Overlay, BoardGone, NewStart, Manual }

public enum BoardSight { Readable, Covered, Gone }

public sealed record GameEnd(GameEndReason Reason, string Result, bool Silent = false)
{
    public bool KnownResult => Result != "*";
    public string ReasonKey => Reason switch
    {
        GameEndReason.Checkmate => "checkmate",
        GameEndReason.Stalemate => "stalemate",
        GameEndReason.Overlay => "overlay",
        GameEndReason.BoardGone => "boardGone",
        GameEndReason.NewStart => "newStart",
        _ => "manual",
    };
}

public sealed record BoardColors(double[] Light, double[] Dark)
{
    public static BoardColors Learn(SquareFeature[][] features) =>
        new(BoardReadability.ParityMedian(features, 1), BoardReadability.ParityMedian(features, 0));
}

public static class BoardReadability
{
    public const int CoveredLimit = 4;
    public const double DarkerBy = 40, Distance = 40, RingTolerance = 45, RingShare = 0.6;

    private static double[]? Background(SquareFeature f) => f.Bg is { Length: 3 } b ? new double[] { b[0], b[1], b[2] } : null;

    public static double[] ParityMedian(SquareFeature[][] features, int parity)
    {
        var values = new List<double[]>();
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if ((r + c) % 2 == parity && Background(features[r][c]) is { } bg) values.Add(bg);
        return Enumerable.Range(0, 3).Select(i => values.Count == 0 ? 0 : values.Select(v => v[i]).OrderBy(x => x).ElementAt(values.Count / 2)).ToArray();
    }

    private static double Luminance(double[] c) => 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2];

    private static double Dist(double[] a, double[] b) => Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (a[i] - b[i]) * (a[i] - b[i])));

    public static int CoveredSquares(SquareFeature[][] features)
    {
        var refs = new[] { ParityMedian(features, 0), ParityMedian(features, 1) };
        int covered = 0;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                if (Background(features[r][c]) is not { } px) continue;
                var re = refs[(r + c) % 2];
                if (Luminance(re) - Luminance(px) >= DarkerBy && Dist(re, px) >= Distance) covered++;
            }
        return covered;
    }

    public static bool IsReadable(SquareFeature[][] features) => CoveredSquares(features) < CoveredLimit;

    public static double RingMatch(SquareFeature[][] features, BoardColors colors)
    {
        int total = 0, hits = 0;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                if (r is not (0 or 7) && c is not (0 or 7)) continue;
                total++;
                if (Background(features[r][c]) is { } px && Dist(px, (r + c) % 2 == 1 ? colors.Light : colors.Dark) <= RingTolerance) hits++;
            }
        return (double)hits / total;
    }

    public static BoardSight Sight(SquareFeature[][] features, BoardColors? colors)
    {
        if (IsReadable(features)) return BoardSight.Readable;
        return colors != null && RingMatch(features, colors) >= RingShare ? BoardSight.Covered : BoardSight.Gone;
    }
}

public sealed class GameEndDetector
{
    public const int MinPlies = 6;
    public const double CoveredSeconds = 3, GoneSeconds = 600;

    private DateTime? _coveredSince, _goneSince;

    public void Reset() { _coveredSince = null; _goneSince = null; }

    public GameEnd? Observe(BoardSight view, int plies, GameResult? result, bool isStart, DateTime now)
    {
        if (view != BoardSight.Covered) _coveredSince = null;
        if (view != BoardSight.Gone) _goneSince = null;
        switch (view)
        {
            case BoardSight.Readable:
                if (plies >= 1 && result is { } r && For(r) is { } end) return end;
                if (isStart && plies >= MinPlies) return new GameEnd(GameEndReason.NewStart, "*");
                return null;
            case BoardSight.Covered:
                _coveredSince ??= now;
                if (plies < MinPlies || (now - _coveredSince.Value).TotalSeconds < CoveredSeconds) return null;
                _coveredSince = null;
                return new GameEnd(GameEndReason.Overlay, "*");
            default:
                _goneSince ??= now;
                if (plies < MinPlies || (now - _goneSince.Value).TotalSeconds < GoneSeconds) return null;
                _goneSince = null;
                return new GameEnd(GameEndReason.BoardGone, "*", true);
        }
    }

    public static GameEnd? For(GameResult result) => result.Kind switch
    {
        GameResultKind.Checkmate => new GameEnd(GameEndReason.Checkmate, result.Pgn),
        GameResultKind.Stalemate => new GameEnd(GameEndReason.Stalemate, result.Pgn),
        _ => null,
    };
}
