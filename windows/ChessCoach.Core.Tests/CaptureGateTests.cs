using System.Diagnostics;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class CaptureGateTests
{
    private static bool Skip(bool unchanged = true, bool settled = true, bool review = false, bool force = false, bool held = false,
        bool soon = false, bool disagree = false, bool evidence = false) =>
        TickGate.CanSkip(unchanged, settled, review, force, held, soon, disagree, evidence);

    [Fact]
    public void StaticScreenIsSkippedOnlyWhenNothingNeedsAFreshLook()
    {
        Assert.True(Skip());
        Assert.False(Skip(unchanged: false));
        Assert.False(Skip(settled: false));
        Assert.False(Skip(review: true));
        Assert.False(Skip(held: true));
        Assert.False(Skip(soon: true));
        Assert.False(Skip(force: true));
        Assert.False(Skip(disagree: true));
        Assert.False(Skip(evidence: true));
    }

    [Fact]
    public void OrientationEvidenceIsVisibleWhileCounting()
    {
        var t = new OrientationTracker();
        t.Set(true);
        var screen = new char?[8][];
        for (int r = 0; r < 8; r++) screen[r] = new char?[8];
        screen[3][4] = 'k'; screen[4][3] = 'P'; screen[6][3] = 'p'; screen[7][4] = 'K';
        Assert.False(t.HasEvidence);
        t.Resolve(null, screen, false);
        Assert.True(t.HasEvidence);
        for (int i = 0; i < OrientationTracker.EvidenceFrames - 1; i++) t.Resolve(null, screen, false);
        Assert.False(t.WhiteAtBottom);
        Assert.False(t.HasEvidence);
    }

    [Fact]
    public void EstimatedFirstSightNeverDecidesTheGame()
    {
        var mated = ChessPosition.FromFen("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3")!;
        var record = new GameRecord();
        record.Update(mated.Board, true);
        Assert.NotNull(record.Current?.Result);
        Assert.Null(record.DecidedResult);
    }

    [Fact]
    public void PollingNeverExceedsTheCaps()
    {
        for (int s = 0; s < 4000; s += 7)
        {
            Assert.True(PollingSchedule.DelayMs(false, false, true, 0, s, false) <= 1500);
            Assert.True(PollingSchedule.DelayMs(false, false, false, s, 0, false) <= 2000);
        }
    }

    [Fact]
    public void FingerprintIgnoresOverlayChangesButSeesBoardChanges()
    {
        var board = new BoardRect(0, 0, 160);
        RgbImage Make(int dark)
        {
            var px = new byte[160 * 160 * 4];
            Array.Fill(px, (byte)200);
            for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) { px[(y * 160 + x) * 4] = (byte)dark; px[(y * 160 + x) * 4 + 1] = (byte)dark; px[(y * 160 + x) * 4 + 2] = (byte)dark; }
            return new RgbImage(160, 160, px);
        }
        var covered = new bool[64];
        covered[0] = true;
        var before = BoardFingerprint.Of(Make(200), board);
        var withArrow = new BoardFingerprint();
        withArrow.Compute(Make(40), board, covered);
        Assert.False(withArrow.Differs(before));
        var plain = BoardFingerprint.Of(Make(40), board);
        Assert.True(plain.Differs(before));
    }

    [Fact]
    public void FullScreenSearchOnA5KFrameStaysUnder300Ms()
    {
        var dir = TestSupport.SyntheticDir();
        if (dir == null) return;
        var big = TestSupport.LoadPng(Path.Combine(dir, "big_start.png"));
        const int W = 5120, H = 2880;
        var px = new byte[W * H * 4];
        Array.Fill(px, (byte)40);
        for (int y = 0; y < big.Height; y++)
            Buffer.BlockCopy(big.Pixels, y * big.Width * 4, px, ((y + 700) * W + 1500) * 4, big.Width * 4);
        var frame = new RgbImage(W, H, px);
        BoardScanner.Scan(frame, new PieceTemplates());
        var clock = Stopwatch.StartNew();
        var found = BoardScanner.Scan(frame, new PieceTemplates());
        long ms = clock.ElapsedMilliseconds;
        Assert.NotNull(found);
#if DEBUG
        const long budget = 1500;
#else
        const long budget = 300;
#endif
        Assert.True(ms < budget, $"{ms} ms");
    }
}
