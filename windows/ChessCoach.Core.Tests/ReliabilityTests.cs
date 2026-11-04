using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class ReliabilityTests
{
    private static int D(bool paused = false, bool soon = false, bool tracked = true, double lost = 0, double calm = 0, bool battery = false) =>
        PollingSchedule.DelayMs(paused, soon, tracked, lost, calm, battery);

    [Fact]
    public void PollingScheduleFollowsTheMacRules()
    {
        Assert.Equal(3000, D(paused: true));
        Assert.Equal(200, D(soon: true));
        Assert.Equal(400, D(calm: 1));
        Assert.InRange(D(calm: 9), 401, 1499);
        Assert.Equal(1500, D(calm: 60));
        Assert.Equal(4000, D(calm: 120));
        Assert.Equal(4000, D(calm: 3600));
        Assert.Equal(8000, D(calm: 3600, battery: true));
        Assert.Equal(1000, D(tracked: false, lost: 5));
        Assert.Equal(2000, D(tracked: false, lost: 60));
        Assert.Equal(4000, D(tracked: false, lost: 200));
        Assert.Equal(8000, D(tracked: false, lost: 400));
        Assert.Equal(16000, D(tracked: false, lost: 400, battery: true));
    }

    [Fact]
    public void PollingIsMonotonicWhileRamping()
    {
        int last = 0;
        for (int c = 0; c < 119; c++)
        {
            int v = D(calm: c);
            Assert.True(v >= last, $"at {c}");
            last = v;
        }
    }

    [Fact]
    public void ReviewConfirmationNeedsMouseUpAndMarkersOrStableFrames()
    {
        Assert.False(ReviewConfirmation.IsConfirmed(true, true, 10));
        Assert.True(ReviewConfirmation.IsConfirmed(false, true, 0));
        Assert.False(ReviewConfirmation.IsConfirmed(false, false, 3));
        Assert.True(ReviewConfirmation.IsConfirmed(false, false, 4));
        Assert.Equal(0, ReviewConfirmation.NextFrames(3, true));
        Assert.Equal(4, ReviewConfirmation.NextFrames(3, false));
    }
}
