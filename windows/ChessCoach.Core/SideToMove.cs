namespace ChessCoach.Core;

public sealed class SquareColors : IEquatable<SquareColors>
{
    public int[] Even { get; set; } = new int[3];
    public int[] Odd { get; set; } = new int[3];

    public bool Equals(SquareColors? o) => o is not null && Even.AsSpan().SequenceEqual(o.Even) && Odd.AsSpan().SequenceEqual(o.Odd);
    public override bool Equals(object? obj) => Equals(obj as SquareColors);
    public override int GetHashCode() => HashCode.Combine(Even.Length > 0 ? Even[0] : 0, Odd.Length > 0 ? Odd[0] : 0);

    public static SquareColors? Learn(SquareFeature[][] features)
    {
        int[]? Median(int parity)
        {
            var cols = new List<int[]>();
            for (int r = 2; r <= 5; r++)
                for (int c = 0; c < 8; c++)
                    if ((r + c) % 2 == parity && features[r][c].Bg is { } bg) cols.Add(bg);
            if (cols.Count < 4) return null;
            var res = new int[3];
            for (int k = 0; k < 3; k++)
            {
                var v = cols.Select(x => x[k]).OrderBy(x => x).ToArray();
                res[k] = v[cols.Count / 2];
            }
            return res;
        }
        var e = Median(0); var o = Median(1);
        return e is null || o is null ? null : new SquareColors { Even = e, Odd = o };
    }
}

public sealed class LastMoveMarking
{
    public List<(int File, int Rank)> Marked { get; set; } = new();

    public List<(int File, int Rank)> CheckSquares { get; set; } = new();

    public PieceColor? Mover { get; set; }

    public PieceColor? SideToMove => Mover?.Opposite();
}

public static class SideToMove
{
    public const int MarkThreshold = 40;

    public static LastMoveMarking Detect(SquareFeature[][] features, char?[][] screen, bool whiteAtBottom, SquareColors? learned = null)
    {
        var reference = new int[2][];
        Span<int> rs = stackalloc int[32], gs = stackalloc int[32], bs = stackalloc int[32];
        for (int parity = 0; parity < 2; parity++)
        {
            int n = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if ((r + c) % 2 == parity && features[r][c].Bg is { } bg)
                    {
                        if (n < 32) { rs[n] = bg[0]; gs[n] = bg[1]; bs[n] = bg[2]; }
                        n++;
                    }
            if (n < 16) return new LastMoveMarking();
            rs[..n].Sort(); gs[..n].Sort(); bs[..n].Sort();
            var median = new[] { rs[n / 2], gs[n / 2], bs[n / 2] };
            var l = learned is null ? null : (parity == 0 ? learned.Even : learned.Odd);
            if (l is not null)
            {
                int near = 0;
                for (int r = 0; r < 8; r++)
                    for (int c = 0; c < 8; c++)
                        if ((r + c) % 2 == parity && features[r][c].Bg is { } bg && Dist(bg, l) <= MarkThreshold) near++;
                reference[parity] = near * 2 >= n ? l : median;
            }
            else reference[parity] = median;
        }

        var result = new LastMoveMarking();
        bool anyWhite = false, anyBlack = false;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                if (features[r][c].Bg is not { } bg) continue;
                var rf = reference[(r + c) % 2];
                if (Dist(bg, rf) <= MarkThreshold) continue;
                var sq = Placement.ChessSquare(c, r, whiteAtBottom);

                int dRG = (bg[0] - bg[1]) - (rf[0] - rf[1]);
                if (dRG > 35 && bg[1] - rf[1] < -25) { result.CheckSquares.Add(sq); continue; }
                result.Marked.Add(sq);
                if (screen[r][c] is char p) { if (char.IsUpper(p)) anyWhite = true; else anyBlack = true; }
            }
        if ((result.Marked.Count is >= 2 and <= 4 || (result.Marked.Count == 1 && result.CheckSquares.Count > 0)) && (anyWhite ^ anyBlack))
            result.Mover = anyWhite ? PieceColor.White : PieceColor.Black;
        return result;
    }

    public static LastMoveMarking Detect(ScanResult scan, bool whiteAtBottom, PieceTemplates templates) =>
        Detect(scan.Features, scan.Screen, whiteAtBottom, templates.SquareColors);

    private static int Dist(int[] a, int[] b) => Math.Abs(a[0] - b[0]) + Math.Abs(a[1] - b[1]) + Math.Abs(a[2] - b[2]);
}
