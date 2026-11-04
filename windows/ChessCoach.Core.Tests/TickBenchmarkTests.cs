using System.Diagnostics;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class TickBenchmarkTests
{
    private static unsafe void Swizzle(byte[] bgra, RgbImage dst, int w, int h)
    {
        fixed (byte* sb = bgra)
        fixed (byte* db = dst.Pixels)
        {
            byte* s = sb, d = db;
            for (int i = 0; i < w * h; i++, s += 4, d += 4) { d[0] = s[2]; d[1] = s[1]; d[2] = s[0]; d[3] = 255; }
        }
    }

    [Fact]
    public void IdleTickIsCheapAndAllocationFree()
    {
        var start = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_start_black.png"));
        var mid = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_check_f7.png"));
        var (_, _, templates) = TestSupport.Recognize(start, mid);
        var board = BoardDetector.Detect(mid)!.Value;
        int w = mid.Width, h = mid.Height;
        var bgra = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { bgra[i * 4] = mid.Pixels[i * 4 + 2]; bgra[i * 4 + 1] = mid.Pixels[i * 4 + 1]; bgra[i * 4 + 2] = mid.Pixels[i * 4]; }
        var reuse = new RgbImage(w, h, new byte[w * h * 4]);
        var fp = new BoardFingerprint(); var last = new BoardFingerprint();
        Swizzle(bgra, reuse, w, h); fp.Compute(reuse, board); last.CopyFrom(fp);

        const int n = 200;
        for (int i = 0; i < 20; i++) { Swizzle(bgra, reuse, w, h); fp.Compute(reuse, board); fp.Differs(last); }
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        bool differs = false;
        for (int i = 0; i < n; i++) { Swizzle(bgra, reuse, w, h); fp.Compute(reuse, board); differs |= fp.Differs(last); }
        double idleMs = sw.Elapsed.TotalMilliseconds / n;
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Assert.False(differs);

        sw.Restart();
        for (int i = 0; i < 20; i++) BoardScanner.Scan(reuse, templates);
        double fullMs = sw.Elapsed.TotalMilliseconds / 20;

        double cpuIdle = idleMs / 1500 * 100;
        var line = $"Tick benchmark {w}x{h}: idle tick (conversion + checksum) {idleMs:F2} ms, allocation {alloc / n} B/tick, " +
                   $"full tick (board search + recognition) {fullMs:F1} ms; idle CPU at 1.5 s interval about {cpuIdle:F2} % (without BitBlt)";
        Console.WriteLine(line);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "chesscoach-tick-benchmark.txt"), line);
        Assert.True(alloc / n < 64, $"Idle tick allocates {alloc / n} B");
        Assert.True(idleMs < 25);
    }
}
