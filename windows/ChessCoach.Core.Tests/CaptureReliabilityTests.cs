using System.Drawing;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class CaptureReliabilityTests
{
    private sealed class FakeCapture : IScreenCapture
    {
        public RgbImage? Window, Screen;
        public int ScreenCalls;

        public RgbImage? CaptureWindow(ref Rectangle region) => Window;

        public RgbImage? Capture(int x, int y, int w, int h) { ScreenCalls++; return Screen; }
    }

    private static readonly RgbImage Frame = new(20, 20, new byte[20 * 20 * 4]);

    [Fact]
    public void SingleFailedCaptureIsNotReportedAsFailure()
    {
        var now = DateTime.UtcNow;
        var fake = new FakeCapture();
        var grabber = new FrameGrabber(fake, new CaptureHealth(() => now));
        Assert.Null(grabber.Screen(new Rectangle(0, 0, 100, 100)));
        Assert.False(grabber.Failing);
        now += TimeSpan.FromSeconds(2);
        Assert.Null(grabber.Screen(new Rectangle(0, 0, 100, 100)));
        Assert.False(grabber.Failing);
    }

    [Fact]
    public void PersistentFailureIsReportedAfterTwoAndAHalfSecondsAndRecovers()
    {
        var now = DateTime.UtcNow;
        var fake = new FakeCapture();
        var grabber = new FrameGrabber(fake, new CaptureHealth(() => now));
        grabber.Screen(new Rectangle(0, 0, 100, 100));
        now += TimeSpan.FromSeconds(3);
        grabber.Screen(new Rectangle(0, 0, 100, 100));
        Assert.True(grabber.Failing);
        fake.Screen = Frame;
        Assert.NotNull(grabber.Screen(new Rectangle(0, 0, 100, 100)));
        Assert.False(grabber.Failing);
    }

    [Fact]
    public void WindowSuccessCountsAsHealthyAndFailedWindowAloneDoesNot()
    {
        var now = DateTime.UtcNow;
        var fake = new FakeCapture { Window = Frame };
        var grabber = new FrameGrabber(fake, new CaptureHealth(() => now));
        var region = new Rectangle(0, 0, 100, 100);
        Assert.NotNull(grabber.Window(ref region));
        fake.Window = null;
        Assert.Null(grabber.Window(ref region));
        Assert.False(grabber.Failing);
        Assert.Equal(0, fake.ScreenCalls);
    }

    private static RgbImage StartImage() => TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_start_black.png"));

    private static void Fill(RgbImage img, int x0, int y0, int size, byte r, byte g, byte b)
    {
        for (int y = y0; y < y0 + size; y++)
            for (int x = x0; x < x0 + size; x++)
            {
                int i = (y * img.Width + x) * 4;
                img.Pixels[i] = r; img.Pixels[i + 1] = g; img.Pixels[i + 2] = b;
            }
    }

    [Fact]
    public void VerifyAtLastRectKeepsTheBoardWhileAPieceIsDragged()
    {
        var img = StartImage();
        var board = BoardDetector.Detect(img)!.Value;
        var templates = new PieceTemplates();
        var colors = BoardColors.Learn(BoardScanner.Analyze(img, board, templates).Features);
        var (sx, sy, s) = board.SquareRect(3, 3);
        Fill(img, (int)sx, (int)sy, (int)s, 250, 30, 30);
        Fill(img, (int)(sx + s), (int)sy, (int)s, 30, 30, 250);
        Assert.Equal(Verification.Matched, BoardReadability.Verify(BoardScanner.Analyze(img, board, templates).Features, colors));
        Assert.Equal(Verification.Failed, BoardReadability.Verify(BoardScanner.Analyze(img, board, templates).Features, null));
    }

    [Fact]
    public void VerifyRejectsAWrongRect()
    {
        var img = StartImage();
        var board = BoardDetector.Detect(img)!.Value;
        var templates = new PieceTemplates();
        var colors = BoardColors.Learn(BoardScanner.Analyze(img, board, templates).Features);
        var shifted = new BoardRect(board.X + board.Square, board.Y, board.Size);
        Assert.Equal(Verification.Failed, BoardReadability.Verify(BoardScanner.Analyze(img, shifted, templates).Features, colors));
    }
}
