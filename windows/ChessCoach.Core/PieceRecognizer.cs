namespace ChessCoach.Core;

public sealed class SquareFeature
{
    public const int N = 32;
    public const int MaxPieceChroma = 80;
    public bool[] Mask { get; set; } = new bool[N * N];
    public byte[] Lum { get; set; } = new byte[N * N];

    public double Fill { get; set; }

    public double MeanLum { get; set; }

    public int[]? Bg { get; set; }

    public static SquareFeature Extract(RgbImage img, BoardRect board, int col, int row)
    {
        var (sx, sy, s) = board.SquareRect(col, row);
        int n = N;

        Span<int> rs = stackalloc int[96], gs = stackalloc int[96], bs = stackalloc int[96];
        int cnt = 0;
        double inset = Math.Max(1.5, s * 0.06);
        const int steps = 24;
        for (int i = 0; i < steps; i++)
        {
            double t = inset + (s - 2 * inset) * i / (steps - 1);
            for (int side = 0; side < 4; side++)
            {
                double px = side switch { 0 => sx + t, 1 => sx + t, 2 => sx + inset, _ => sx + s - inset };
                double py = side switch { 0 => sy + inset, 1 => sy + s - inset, 2 => sy + t, _ => sy + t };
                var c = img.Rgb((int)px, (int)py);
                rs[cnt] = c.R; gs[cnt] = c.G; bs[cnt] = c.B; cnt++;
            }
        }
        rs.Sort(); gs.Sort(); bs.Sort();
        var bg = (R: rs[cnt / 2], G: gs[cnt / 2], B: bs[cnt / 2]);

        var mask = new bool[n * n];
        var lum = new byte[n * n];
        int centralHits = 0, centralTotal = 0;
        double lumSum = 0; int lumCount = 0;
        double margin = s * 0.04;
        double span = s - 2 * margin;
        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < n; i++)
            {
                int hitsInCell = 0, lSum = 0;
                for (int sj = 0; sj < 3; sj++) for (int si = 0; si < 3; si++)
                {
                    double px = sx + margin + (i + (si + 0.5) / 3) * span / n;
                    double py = sy + margin + (j + (sj + 0.5) / 3) * span / n;
                    var c = img.Rgb((int)px, (int)py);

                    int chroma = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                    if (RgbImage.ColorDistance(c, bg) > 45 && chroma <= MaxPieceChroma) hitsInCell++;
                    lSum += (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
                }
                bool isPiece = hitsInCell >= 4;
                int l = lSum / 9;
                int k = j * n + i;
                mask[k] = isPiece;
                lum[k] = (byte)Math.Min(255, Math.Max(0, l));
                bool central = i >= n / 8 && i < n - n / 8 && j >= n / 8 && j < n - n / 8;
                if (central)
                {
                    centralTotal++;
                    if (isPiece) centralHits++;
                }
                if (isPiece) { lumSum += l; lumCount++; }
            }
        }
        return new SquareFeature
        {
            Mask = mask, Lum = lum,
            Fill = (double)centralHits / Math.Max(centralTotal, 1),
            MeanLum = lumCount > 0 ? lumSum / lumCount : 0,
            Bg = new[] { bg.R, bg.G, bg.B },
        };
    }

    private uint[]? _rows;

    private uint[] Rows
    {
        get
        {
            if (_rows != null) return _rows;
            var r = new uint[N];
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                    if (Mask[j * N + i]) r[j] |= 1u << i;
            return _rows = r;
        }
    }

    public double DistanceTo(SquareFeature other) => DistanceTo(other, double.PositiveInfinity);

    public double DistanceTo(SquareFeature other, double limit)
    {
        double best = double.PositiveInfinity;
        double bound = limit;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
        {
            double d = DistanceTo(other, dx, dy, bound);
            if (d < best) { best = d; if (d < bound) bound = d; }
        }
        return best;
    }

    private double DistanceTo(SquareFeature other, int dx, int dy, double bound)
    {
        const int n = N;
        var ra = Rows; var rb = other.Rows;
        int union = 0, mismatches = 0;
        for (int j = 0; j < n; j++)
        {
            int oj = j + dy;
            uint a = ra[j];
            uint b = (uint)oj < (uint)n ? (dx >= 0 ? rb[oj] >> dx : rb[oj] << -dx) : 0u;
            union += System.Numerics.BitOperations.PopCount(a | b);
            mismatches += System.Numerics.BitOperations.PopCount(a ^ b);
        }
        if (union == 0) return 0;

        if ((double)mismatches / union > bound) return double.PositiveInfinity;
        double cost = mismatches;
        var la = Lum; var lb = other.Lum;
        for (int j = 0; j < n; j++)
        {
            int oj = j + dy;
            if ((uint)oj >= (uint)n) continue;
            uint inter = ra[j] & (dx >= 0 ? rb[oj] >> dx : rb[oj] << -dx);
            while (inter != 0)
            {
                int i = System.Numerics.BitOperations.TrailingZeroCount(inter);
                inter &= inter - 1;
                cost += Math.Min(1, Math.Abs(la[j * n + i] - lb[oj * n + i + dx]) / 160.0);
            }
        }
        return Math.Min(cost / union, 2);
    }
}

public sealed class PieceTemplates
{
    public Dictionary<string, List<SquareFeature>> Templates { get; set; } = new();

    public SquareColors? SquareColors { get; set; }
    public bool IsEmpty => Templates.Count == 0;

    public const double OccupiedFill = 0.06;
    public const double MaxDistance = 0.42;

    public static char[][] StartLayout(bool whiteAtBottom)
    {
        char[] E = Array.Empty<char>();
        return whiteAtBottom
            ? new[] { "rnbqkbnr".ToCharArray(), "pppppppp".ToCharArray(), E, E, E, E, "PPPPPPPP".ToCharArray(), "RNBQKBNR".ToCharArray() }
            : new[] { "RNBKQBNR".ToCharArray(), "PPPPPPPP".ToCharArray(), E, E, E, E, "pppppppp".ToCharArray(), "rnbkqbnr".ToCharArray() };
    }

    public static bool LooksLikeStart(SquareFeature[][] features)
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                bool occ = features[r][c].Fill > OccupiedFill;
                if ((r == 0 || r == 1 || r == 6 || r == 7) != occ) return false;
            }
        return true;
    }

    public const int MinFitSquares = 4;

    public bool FitsBadly(SquareFeature[][] features)
    {
        if (IsEmpty) return false;
        int occupied = 0, unknown = 0;
        var screen = new char?[8][];
        for (int r = 0; r < 8; r++)
        {
            screen[r] = new char?[8];
            for (int c = 0; c < 8; c++)
            {
                var f = features[r][c];
                if (!(f.Fill > OccupiedFill)) continue;
                occupied++;
                screen[r][c] = Classify(f);
                if (screen[r][c] == null) unknown++;
            }
        }
        if (occupied < MinFitSquares) return false;
        if (unknown * 4 >= occupied) return true;
        foreach (bool whiteAtBottom in new[] { true, false })
        {
            var board = new Placement(screen, whiteAtBottom);
            if (board.Problem(PieceColor.White) == null || board.Problem(PieceColor.Black) == null) return false;
        }
        return true;
    }

    public static (PieceTemplates Templates, bool WhiteAtBottom, ChessPosition? Position)? Relearned(SquareFeature[][] features)
    {
        var t = new PieceTemplates();
        if (LooksLikeStart(features)) return (t, t.LearnStart(features), null);
        return t.LearnEarlyPosition(features) is { } found ? (t, found.WhiteAtBottom, found.Position) : null;
    }

    public bool LearnStart(SquareFeature[][] features)
    {
        double top = features[0].Concat(features[1]).Sum(f => f.MeanLum);
        double bottom = features[6].Concat(features[7]).Sum(f => f.MeanLum);
        bool whiteAtBottom = bottom > top;
        var layout = StartLayout(whiteAtBottom);
        Learn(features, (r, c) => layout[r].Length == 0 ? null : layout[r][c]);
        return whiteAtBottom;
    }

    public bool? LearnKnown(SquareFeature[][] features, Placement board, bool? preferWhiteAtBottom)
    {
        bool first = preferWhiteAtBottom ?? true;
        foreach (bool whiteAtBottom in new[] { first, !first })
        {
            var white = new List<double>(); var black = new List<double>();
            bool fits = true;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    var (file, rank) = Placement.ChessSquare(c, r, whiteAtBottom);
                    var piece = board.Squares[rank][file];
                    bool occupied = features[r][c].Fill > OccupiedFill;
                    if (occupied != (piece != null)) fits = false;
                    if (piece is char p && occupied) (char.IsUpper(p) ? white : black).Add(features[r][c].MeanLum);
                }
            if (!fits || white.Count == 0 || black.Count == 0 || white.Average() <= black.Average()) continue;
            Learn(features, (r, c) =>
            {
                var (file, rank) = Placement.ChessSquare(c, r, whiteAtBottom);
                return board.Squares[rank][file];
            });
            return whiteAtBottom;
        }
        return null;
    }

    public bool? LearnEarly(SquareFeature[][] features) => LearnEarlyPosition(features)?.WhiteAtBottom;

    public (bool WhiteAtBottom, ChessPosition Position)? LearnEarlyPosition(SquareFeature[][] features)
    {
        var occupied = features.Select(row => row.Select(f => f.Fill > OccupiedFill).ToArray()).ToArray();
        var lums = features.SelectMany((row, r) => row.Where((_, c) => occupied[r][c]).Select(f => f.MeanLum)).ToList();
        if (lums.Count == 0) return null;
        double lo = lums.Min(), hi = lums.Max();
        if (hi - lo <= 40) return null;
        for (int i = 0; i < 8; i++)
        {
            double t = (lo + hi) / 2;
            var low = lums.Where(x => x <= t).ToList();
            var high = lums.Where(x => x > t).ToList();
            lo = low.Count > 0 ? low.Average() : 0;
            hi = high.Count > 0 ? high.Average() : 0;
        }
        double threshold = (lo + hi) / 2;
        var bright = features.Select(row => row.Select(f => f.MeanLum > threshold).ToArray()).ToArray();
        if (EarlyStart.MatchPosition(occupied, bright) is not { } found) return null;
        var board = found.Position.Board;
        Learn(features, (r, c) =>
        {
            var (file, rank) = Placement.ChessSquare(c, r, found.WhiteAtBottom);
            return board.Squares[rank][file];
        });
        return found;
    }

    private void Learn(SquareFeature[][] features, Func<int, int, char?> label)
    {
        var t = new Dictionary<string, List<SquareFeature>>();
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                if (label(r, c) is not { } piece) continue;
                string key = piece.ToString();

                if ((key == "p" || key == "P") && t.TryGetValue(key, out var have) && have.Count >= 4) continue;
                if (!t.TryGetValue(key, out var list)) t[key] = list = new List<SquareFeature>();
                list.Add(features[r][c]);
            }
        }
        Templates = t;
        SquareColors = SquareColors.Learn(features);
    }

    public double Distance(SquareFeature f, char piece) =>
        Templates.TryGetValue(piece.ToString(), out var list) && list.Count > 0
            ? Min(f, list)
            : double.PositiveInfinity;

    private static double Min(SquareFeature f, List<SquareFeature> list)
    {
        double best = double.PositiveInfinity;
        foreach (var t in list) { double d = f.DistanceTo(t, best); if (d < best) best = d; }
        return best;
    }

    public char? Classify(SquareFeature f)
    {
        if (!(f.Fill > OccupiedFill)) return null;

        string? bestKey = null;
        double bestDist = MaxDistance;
        foreach (var (key, list) in Templates)
            foreach (var t in list)
            {
                double d = f.DistanceTo(t, bestDist);
                if (d < bestDist || (bestKey == null && d <= bestDist)) { bestDist = d; bestKey = key; }
            }
        return bestKey?[0];
    }
}
