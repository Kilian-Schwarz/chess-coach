namespace ChessCoach.Core;

public enum RectOutcome { Same, Changed, Pending }

public sealed class RectStabilizer
{
    public const int ConfirmFrames = 2;

    private BoardRect? _candidate;
    private int _candidateFrames;

    public BoardRect? Accepted { get; private set; }

    public static double Tolerance(double size) => Math.Max(3, size * 0.015);

    private static BoardRect Snap(BoardRect rect) => new(Math.Round(rect.X, MidpointRounding.AwayFromZero),
        Math.Round(rect.Y, MidpointRounding.AwayFromZero), Math.Round(rect.Size, MidpointRounding.AwayFromZero));

    public static bool Near(BoardRect a, BoardRect b)
    {
        double tol = Tolerance(a.Size);
        return Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol && Math.Abs(a.Size - b.Size) <= tol;
    }

    public (BoardRect Rect, bool Changed) Accept(BoardRect rect)
    {
        var snapped = Snap(rect);
        if (Accepted is { } a && Near(a, snapped)) return (a, false);
        Accepted = snapped;
        _candidate = null;
        _candidateFrames = 0;
        return (snapped, true);
    }

    public (BoardRect Rect, RectOutcome Outcome) Propose(BoardRect rect, bool colorsMatch)
    {
        var snapped = Snap(rect);
        if (Accepted is not { } a) return (Accept(snapped).Rect, RectOutcome.Changed);
        if (Near(a, snapped))
        {
            _candidate = null;
            _candidateFrames = 0;
            return (a, RectOutcome.Same);
        }
        if (_candidate is { } c && Near(c, snapped)) _candidateFrames++;
        else { _candidate = snapped; _candidateFrames = 1; }
        if (_candidateFrames < ConfirmFrames || !colorsMatch) return (a, RectOutcome.Pending);
        return (Accept(snapped).Rect, RectOutcome.Changed);
    }

    public void Reset()
    {
        Accepted = null;
        _candidate = null;
        _candidateFrames = 0;
    }
}
