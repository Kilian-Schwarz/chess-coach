namespace ChessCoach.Core;

public readonly record struct BoardRect(double X, double Y, double Size)
{
    public double Square => Size / 8;

    public (double X, double Y, double S) SquareRect(int col, int row) =>
        (X + col * Square, Y + row * Square, Square);
}

public static class BoardDetector
{
    public readonly record struct Segment(int Pos, int Start, int End)
    {
        public int Length => End - Start;
    }

    public sealed class Group
    {
        public int Start; public int End; public List<double> Positions = new();
    }

    private static double RoundAway(double v) => Math.Round(v, MidpointRounding.AwayFromZero);

    public static BoardRect? Detect(RgbImage img, int minSize = 160, int threshold = 36)
    {
        var vertical = Segments(img, true, minSize, threshold);
        var horizontal = Segments(img, false, minSize, threshold);

        var vIndex = vertical.GroupBy(s => s.Pos).ToDictionary(g => g.Key, g => g.ToList());
        var hIndex = horizontal.GroupBy(s => s.Pos).ToDictionary(g => g.Key, g => g.ToList());
        BoardRect? best = null;
        int bestScore = 0;

        void Consider(BoardRect rect)
        {
            double s = rect.Square;

            int vHits = Covered(vIndex, rect.X, s, rect.Y, rect.Y + rect.Size);
            int hHits = Covered(hIndex, rect.Y, s, rect.X, rect.X + rect.Size);
            if (vHits < 5 || hHits < 5) return;
            int score = (vHits + hHits) * 10_000 + (int)rect.Size;
            if (best == null || score > bestScore) { best = rect; bestScore = score; }
        }

        foreach (var g in MakeGroups(vertical).Where(g => g.Positions.Count >= 4))
        {
            double size = g.End - g.Start, s = size / 8;
            foreach (var p in g.Positions) for (int k = 1; k <= 7; k++)
                Consider(new BoardRect(RoundAway(p - k * s), g.Start, size));
        }
        foreach (var g in MakeGroups(horizontal).Where(g => g.Positions.Count >= 4))
        {
            double size = g.End - g.Start, s = size / 8;
            foreach (var p in g.Positions) for (int k = 1; k <= 7; k++)
                Consider(new BoardRect(g.Start, RoundAway(p - k * s), size));
        }
        return best;
    }

    private static int Covered(Dictionary<int, List<Segment>> index, double origin, double step, double from, double to)
    {
        double tol = Math.Max(2.5, step * 0.04);
        int n = 0;
        for (int k = 1; k <= 7; k++)
        {
            double expected = origin + k * step - 0.5;
            int lo = (int)Math.Floor(expected - tol - 0.5), hi = (int)Math.Ceiling(expected + tol + 0.5);
            bool found = false;
            for (int pos = lo; pos <= hi; pos++)
            {
                if (index.TryGetValue(pos, out var list) && list.Any(s => s.Start <= from + 4 && s.End >= to - 4))
                { found = true; break; }
            }
            if (found) n++;
        }
        return n;
    }

    private static List<Group> MakeGroups(List<Segment> segs)
    {
        var groups = new List<(int Start, int End, List<int> Pos)>();
        foreach (var s in segs.OrderByDescending(s => s.Length))
        {
            int i = groups.FindIndex(g => Math.Abs(g.Start - s.Start) <= 4 && Math.Abs(g.End - s.End) <= 4);
            if (i >= 0) groups[i].Pos.Add(s.Pos);
            else groups.Add((s.Start, s.End, new List<int> { s.Pos }));
        }
        var result = new List<Group>();
        foreach (var g in groups)
        {
            var sorted = g.Pos.OrderBy(p => p).ToList();
            var clusters = new List<List<int>>();
            foreach (int p in sorted)
            {
                if (clusters.Count > 0 && p - clusters[^1][^1] <= 2) clusters[^1].Add(p);
                else clusters.Add(new List<int> { p });
            }

            var positions = clusters.Select(c => (double)c.Sum() / c.Count + 0.5).ToList();
            result.Add(new Group { Start = g.Start, End = g.End, Positions = positions });
        }
        return result;
    }

    public static List<Segment> Segments(RgbImage img, bool vertical, int minLength, int threshold)
    {
        int w = img.Width, h = img.Height;
        int outer = vertical ? w : h, inner = vertical ? h : w;
        var result = new List<Segment>();
        if (!(outer > 2 && inner > minLength)) return result;
        const int maxGap = 6;
        var p = img.Pixels;
        for (int o = 1; o < outer - 1; o++)
        {
            int runStart = -1, lastHit = -1;
            for (int t = 0; t < inner; t++)
            {
                int a, b;
                if (vertical) { a = (t * w + o - 1) * 4; b = (t * w + o + 1) * 4; }
                else { a = ((o - 1) * w + t) * 4; b = ((o + 1) * w + t) * 4; }
                int d = Math.Abs(p[a] - p[b]) + Math.Abs(p[a + 1] - p[b + 1]) + Math.Abs(p[a + 2] - p[b + 2]);
                if (d > threshold)
                {
                    if (runStart < 0) runStart = t;
                    lastHit = t;
                }
                else if (runStart >= 0 && t - lastHit > maxGap)
                {
                    if (lastHit - runStart >= minLength) result.Add(new Segment(o, runStart, lastHit + 1));
                    runStart = -1;
                }
            }
            if (runStart >= 0 && lastHit - runStart >= minLength) result.Add(new Segment(o, runStart, lastHit + 1));
        }
        return result;
    }
}
