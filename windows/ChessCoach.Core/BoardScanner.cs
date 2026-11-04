namespace ChessCoach.Core;

public sealed class ScanResult
{
    public BoardRect Board { get; init; }
    public SquareFeature[][] Features { get; init; } = Array.Empty<SquareFeature[]>();

    public char?[][] Screen { get; init; } = Array.Empty<char?[]>();
    public bool LooksLikeStart { get; init; }
}

public static class BoardScanner
{
    public static ScanResult? Scan(RgbImage img, PieceTemplates templates)
    {
        var board = BoardDetector.Detect(img);
        return board is null ? null : Analyze(img, board.Value, templates);
    }

    public static ScanResult Analyze(RgbImage img, BoardRect board, PieceTemplates templates)
    {
        var features = new SquareFeature[8][];
        for (int r = 0; r < 8; r++)
        {
            features[r] = new SquareFeature[8];
            for (int c = 0; c < 8; c++) features[r][c] = SquareFeature.Extract(img, board, c, r);
        }
        var screen = new char?[8][];
        for (int r = 0; r < 8; r++)
        {
            screen[r] = new char?[8];
            for (int c = 0; c < 8; c++) screen[r][c] = templates.Classify(features[r][c]);
        }

        foreach (char king in new[] { 'K', 'k' })
        {
            if (screen.SelectMany(row => row).Any(p => p == king)) continue;
            (int r, int c, double d)? best = null;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (screen[r][c] == null && features[r][c].Fill > PieceTemplates.OccupiedFill)
                    {
                        double d = templates.Distance(features[r][c], king);
                        if (d < (best?.d ?? 0.9)) best = (r, c, d);
                    }
            if (best is { } b) screen[b.r][b.c] = king;
        }
        return new ScanResult
        {
            Board = board, Features = features, Screen = screen,
            LooksLikeStart = PieceTemplates.LooksLikeStart(features),
        };
    }

    public static bool? GuessWhiteAtBottom(char?[][] screen)
    {
        double wSum = 0, wN = 0, bSum = 0, bN = 0;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                if (screen[r][c] is not char p) continue;

                double weight = (p == 'K' || p == 'k') ? 3.0 : 1.0;
                if (char.IsUpper(p)) { wSum += r * weight; wN += weight; } else { bSum += r * weight; bN += weight; }
            }
        if (!(wN > 0 && bN > 0)) return null;
        double diff = wSum / wN - bSum / bN;
        if (Math.Abs(diff) < 0.5) return null;
        return diff > 0;
    }
}
