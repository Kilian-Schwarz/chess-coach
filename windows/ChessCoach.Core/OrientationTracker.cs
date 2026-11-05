namespace ChessCoach.Core;

public sealed class OrientationTracker
{
    public const int EvidenceFrames = 5;

    private int _contrary;

    public bool? WhiteAtBottom { get; private set; }

    public void Set(bool? value)
    {
        WhiteAtBottom = value;
        _contrary = 0;
    }

    public static bool Impossible(char?[][] screen, bool whiteAtBottom)
    {
        var placement = new Placement(screen, whiteAtBottom);
        return placement.Problem(PieceColor.White) != null && placement.Problem(PieceColor.Black) != null;
    }

    public (bool WhiteAtBottom, string? Reason) Resolve(bool? guess, char?[][] screen, bool isStart)
    {
        if (WhiteAtBottom is not { } current)
        {
            WhiteAtBottom = guess;
            return (guess ?? true, null);
        }
        if (isStart && guess is { } g && g != current)
        {
            Set(g);
            return (g, "start position seen with the other orientation");
        }
        if (Impossible(screen, current) && !Impossible(screen, !current))
        {
            if (++_contrary >= EvidenceFrames)
            {
                Set(!current);
                return (!current, $"position impossible in the current orientation for {EvidenceFrames} frames");
            }
        }
        else _contrary = 0;
        return (current, null);
    }
}
