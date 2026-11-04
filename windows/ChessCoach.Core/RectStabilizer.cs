namespace ChessCoach.Core;

public sealed class RectStabilizer
{
    public BoardRect? Accepted { get; private set; }

    public static double Tolerance(double size) => Math.Max(3, size * 0.015);

    public (BoardRect Rect, bool Changed) Accept(BoardRect rect)
    {
        var snapped = new BoardRect(Math.Round(rect.X, MidpointRounding.AwayFromZero), Math.Round(rect.Y, MidpointRounding.AwayFromZero),
            Math.Round(rect.Size, MidpointRounding.AwayFromZero));
        if (Accepted is { } a)
        {
            double tol = Tolerance(a.Size);
            if (Math.Abs(a.X - snapped.X) <= tol && Math.Abs(a.Y - snapped.Y) <= tol && Math.Abs(a.Size - snapped.Size) <= tol)
                return (a, false);
        }
        Accepted = snapped;
        return (snapped, true);
    }

    public void Reset() => Accepted = null;
}
