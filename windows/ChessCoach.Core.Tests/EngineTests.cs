using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class EngineTests
{
    const int LoadSlackMs = 20_000;
    const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public EngineTests() { Log.Directory = Path.Combine(Path.GetTempPath(), "chesscoach-test-logs"); }

    [Fact]
    public async Task StockfishAnalyzesStartPosition()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        using var e = new StockfishEngine(path) { TimeoutSlackMs = LoadSlackMs };
        var r = await e.AnalyzeAsync(StartFen, 1500);
        Assert.NotNull(r);
        var best = UciMove.Parse(r!.BestMove);
        Assert.NotNull(best);

        var p = Placement.FromFen(StartFen)!;
        Assert.True(p.Squares[best!.Value.FromRank][best.Value.FromFile] is 'P' or 'N');
        Assert.InRange(r.Lines.Count, 1, 3);
        Assert.True(r.Lines.Count == 3, "expected MultiPV 3");
        Assert.NotEmpty(r.Lines[0].Pv);
        Assert.InRange(r.Lines[0].ScoreCP!.Value, -150, 150);
    }

    [Fact]
    public async Task StockfishFindsMate()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        using var e = new StockfishEngine(path) { TimeoutSlackMs = LoadSlackMs };
        var r = await e.AnalyzeAsync("6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1", 1000);
        Assert.Equal("a1a8", r!.BestMove);
        Assert.Equal(1, r.MateIn);
        var mated = await e.AnalyzeAsync("R5k1/5ppp/8/8/8/8/8/4K3 b - - 0 1", 1000);
        Assert.Equal("", mated!.BestMove);
    }

    [Fact]
    public async Task SecondRequestCancelsFirst()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        using var e = new StockfishEngine(path) { TimeoutSlackMs = LoadSlackMs };
        var first = e.AnalyzeAsync(StartFen, 5000);
        var second = e.AnalyzeAsync("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", 1000);
        Assert.Null(await first);
        var r = await second;
        Assert.NotNull(r);
        Assert.Contains("b KQkq", r!.Fen);
    }

    [Fact]
    public async Task DeadEngineAnswersImmediately()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        var e = new StockfishEngine(path);
        e.Stop();
        Assert.False(e.IsRunning);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await e.AnalyzeAsync(StartFen, 1000));
        Assert.True(sw.ElapsedMilliseconds < 500);
    }

    [Fact]
    public async Task CrashingEngineResolvesNullAndSessionBlocksFen()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = TestSupport.FakeEngine("while read l; do case \"$l\" in go*) exit 3;; esac; done");
        try
        {
            using var session = new EngineSession(() => script);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.Null(await session.AnalyzeAsync(StartFen, 500));
            Assert.True(sw.ElapsedMilliseconds < 2500, $"Crash detected too late: {sw.ElapsedMilliseconds} ms");
            Assert.True(session.IsBlocked(StartFen));
            sw.Restart();
            Assert.Null(await session.AnalyzeAsync(StartFen, 500));
            Assert.True(sw.ElapsedMilliseconds < 100);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task TimedOutFenIsBlockedAndNotRetried()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = TestSupport.FakeEngine("while read l; do :; done");
        try
        {
            using var session = new EngineSession(() => script) { TimeoutSlackMs = 300 };
            Assert.Null(await session.AnalyzeAsync(StartFen, 200));
            Assert.True(session.IsBlocked(StartFen));
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task CacheIsOnlyReusedForEqualOrShorterThinkTime()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = TestSupport.FakeEngine("while read l; do case \"$l\" in uci*) echo uciok;; go*) echo \"info depth 1 score cp 10 pv e2e4\"; echo \"bestmove e2e4\";; esac; done");
        try
        {
            using var session = new EngineSession(() => script);
            var quick = await session.AnalyzeAsync(StartFen, 300);
            Assert.Same(quick, await session.AnalyzeAsync(StartFen, 300));
            Assert.Same(quick, await session.AnalyzeAsync(StartFen, 100));
            var longer = await session.AnalyzeAsync(StartFen, 1000);
            Assert.NotSame(quick, longer);
            Assert.Same(longer, await session.AnalyzeAsync(StartFen, 300));
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task HangingEngineIsKilledAfterThinkTimePlusSlack()
    {
        if (OperatingSystem.IsWindows()) return;
        var script = TestSupport.FakeEngine("while read l; do :; done");
        try
        {
            using var e = new StockfishEngine(script) { TimeoutSlackMs = 600 };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.Null(await e.AnalyzeAsync(StartFen, 300));
            Assert.InRange(sw.ElapsedMilliseconds, 800, 3000);
            await Task.Delay(300);
            Assert.False(e.IsRunning);
        }
        finally { File.Delete(script); }
    }

    [Fact]
    public async Task SessionEndsEngineAfterIdleAndRestarts()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        using var s = new EngineSession(() => path) { IdleTimeout = TimeSpan.FromMilliseconds(2500), TimeoutSlackMs = LoadSlackMs };
        Assert.False(s.IsEngineRunning);
        Assert.NotNull(await s.AnalyzeAsync(StartFen, 300));
        Assert.True(s.IsEngineRunning);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (s.IsEngineRunning && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.False(s.IsEngineRunning);
        Assert.NotNull(await s.AnalyzeAsync("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", 300));
        Assert.True(s.IsEngineRunning);
    }

    [Fact]
    public void LogWritesToConfiguredDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chesscoach-log-" + Guid.NewGuid().ToString("N"));
        Log.Directory = dir;
        Log.Write("Test entry äöü");
        var text = File.ReadAllText(Path.Combine(dir, "chesscoach.log"));
        Assert.Contains("Test entry äöü", text);
        Log.Directory = Path.Combine(Path.GetTempPath(), "chesscoach-test-logs");
        Directory.Delete(dir, true);
    }

    [Fact]
    public void DefaultLogPathEndsWithChessCoachLogs()
    {
        Log.Directory = null!;
        var p = Log.FilePath.Replace('\\', '/');
        Assert.EndsWith("ChessCoach/Logs/chesscoach.log", p);
        Log.Directory = Path.Combine(Path.GetTempPath(), "chesscoach-test-logs");
    }
}
