namespace ChessCoach.Core;

public static class TickGate
{
    public static bool CanSkip(bool unchanged, bool settled, bool pendingReview, bool forceResync, bool mouseHeld,
        bool confirmSoon, bool recordDisagrees, bool orientationEvidence) =>
        unchanged && settled && !pendingReview && !forceResync && !mouseHeld && !confirmSoon && !recordDisagrees && !orientationEvidence;
}
