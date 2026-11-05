namespace ChessCoach.Core;

public abstract record RegionResult
{
    public sealed record Scanned(ScanResult Scan, BoardRect Rect, bool Changed) : RegionResult;
    public sealed record Verified(ScanResult Scan) : RegionResult;
    public sealed record Covered : RegionResult;
    public sealed record Failed(string Reason) : RegionResult;
}

public static class RegionObserver
{
    public static RegionResult Observe(RgbImage crop, (double X, double Y) origin, BoardRect expected, PieceTemplates templates,
        BoardColors? colors, RectStabilizer stabilizer)
    {
        string reason = $"no board in a {crop.Width}x{crop.Height} crop";
        if (BoardScanner.Scan(crop, templates) is { } scan)
        {
            var b = scan.Board;
            bool colorsMatch = colors == null || BoardReadability.MatchShare(scan.Features, colors) >= BoardReadability.VerifyShare;
            var found = new BoardRect(origin.X + b.X, origin.Y + b.Y, b.Size);
            var (rect, outcome) = stabilizer.Propose(found, colorsMatch);
            if (outcome != RectOutcome.Pending) return new RegionResult.Scanned(scan, rect, outcome == RectOutcome.Changed);
            reason = $"rect change to {(int)b.Size} px at {(int)found.X},{(int)found.Y} not confirmed yet (colours match: {colorsMatch})";
        }
        if (expected.X < -1 || expected.Y < -1 || expected.X + expected.Size > crop.Width + 1 || expected.Y + expected.Size > crop.Height + 1)
            return new RegionResult.Failed(reason);
        var atExpected = BoardScanner.Analyze(crop, expected, templates);
        return BoardReadability.Verify(atExpected.Features, colors) switch
        {
            Verification.Matched => new RegionResult.Verified(atExpected),
            Verification.Covered => new RegionResult.Covered(),
            _ => new RegionResult.Failed(reason),
        };
    }
}
