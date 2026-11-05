using System.Drawing;

namespace ChessCoach.Core;

public interface IScreenCapture
{
    RgbImage? CaptureWindow(ref Rectangle region);
    RgbImage? Capture(int x, int y, int w, int h);
}

public sealed class CaptureHealth(Func<DateTime> clock)
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2.5);

    private DateTime? _since;

    public bool Failing => _since is { } since && clock() - since > Tolerance;

    public void Report(bool ok)
    {
        if (ok) _since = null;
        else _since ??= clock();
    }
}

public sealed class FrameGrabber(IScreenCapture capture, CaptureHealth health)
{
    public bool Failing => health.Failing;

    public RgbImage? Window(ref Rectangle region)
    {
        var image = capture.CaptureWindow(ref region);
        if (image != null) health.Report(true);
        return image;
    }

    public RgbImage? Screen(Rectangle region)
    {
        var image = capture.Capture(region.X, region.Y, region.Width, region.Height);
        health.Report(image != null);
        return image;
    }
}
