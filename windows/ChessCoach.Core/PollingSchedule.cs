namespace ChessCoach.Core;

public static class PollingSchedule
{
    public const int PausedMs = 3000, ConfirmMs = 200, ActiveMs = 400, CalmMs = 800, IdleMs = 1500, BatteryFactor = 2;
    public const double ActiveUntil = 3, RampSeconds = 12, IdleAfter = 120;

    private static readonly (double After, int Ms)[] SearchSteps = { (30, 2000), (0, 1000) };

    public static int DelayMs(bool paused, bool confirmSoon, bool tracked, double lostFor, double calmFor, bool onBattery)
    {
        int factor = onBattery ? BatteryFactor : 1;
        if (paused) return PausedMs;
        if (tracked)
        {
            if (confirmSoon) return ConfirmMs;
            if (calmFor < ActiveUntil) return ActiveMs;
            if (calmFor >= IdleAfter) return IdleMs * factor;
            return (int)Math.Min(CalmMs, ActiveMs + (calmFor - ActiveUntil) / RampSeconds * (CalmMs - ActiveMs)) * factor;
        }
        foreach (var (after, ms) in SearchSteps)
            if (lostFor > after) return ms * factor;
        return 1000 * factor;
    }
}
