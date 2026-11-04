using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class RecognitionTests
{
    public static IEnumerable<object[]> LichessCases()
    {
        foreach (var line in File.ReadAllLines(Path.Combine(TestSupport.TestData, "expected.txt")))
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            yield return new object[] { p[0], p[1] };
        }
    }

    [Theory]
    [MemberData(nameof(LichessCases))]
    public void LichessImages(string file, string expected)
    {
        var start = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_start_black.png"));
        var mid = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, file));
        var (placement, whiteBottom, _) = TestSupport.Recognize(start, mid);
        Assert.False(whiteBottom);
        Assert.Equal(expected, placement);
    }

    [Fact]
    public void LichessStartBoardIsFoundAndLooksLikeStart()
    {
        var start = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_start_black.png"));
        var b = BoardDetector.Detect(start);
        Assert.NotNull(b);
        var s = BoardScanner.Analyze(start, b.Value, new PieceTemplates());
        Assert.True(s.LooksLikeStart);
    }

    [Theory]
    [InlineData("start_white", "mid_white", true)]
    [InlineData("start_black", "mid_black", false)]
    public void SyntheticBoards(string startName, string midName, bool whiteBottom)
    {
        var dir = TestSupport.SyntheticDir();
        if (dir == null) return;
        var expected = File.ReadAllText(Path.Combine(dir, "expected.txt")).Trim();
        var start = TestSupport.LoadPng(Path.Combine(dir, startName + ".png"));
        var mid = TestSupport.LoadPng(Path.Combine(dir, midName + ".png"));
        var (placement, wb, _) = TestSupport.Recognize(start, mid);
        Assert.Equal(whiteBottom, wb);
        Assert.Equal(expected, placement);
    }

    [Fact]
    public void FingerprintDetectsChangeAndIgnoresSameImage()
    {
        var dir = TestSupport.SyntheticDir();
        if (dir == null) return;
        var a = TestSupport.LoadPng(Path.Combine(dir, "start_white.png"));
        var b = TestSupport.LoadPng(Path.Combine(dir, "mid_white.png"));
        var board = BoardDetector.Detect(a)!.Value;
        var fa = BoardFingerprint.Of(a, board);
        Assert.False(fa.Differs(BoardFingerprint.Of(a, board)));
        Assert.True(fa.Differs(BoardFingerprint.Of(b, board)));
    }

    [Fact]
    public void Benchmark()
    {
        var start = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_start_black.png"));
        var mid = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_check_f7.png"));
        var (_, _, templates) = TestSupport.Recognize(start, mid);
        const int n = 20;
        for (int i = 0; i < 3; i++) { var b = BoardDetector.Detect(mid)!.Value; BoardScanner.Analyze(mid, b, templates); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        BoardRect? rect = null;
        for (int i = 0; i < n; i++) rect = BoardDetector.Detect(mid);
        double detectMs = sw.Elapsed.TotalMilliseconds / n;
        sw.Restart();
        for (int i = 0; i < n; i++) BoardScanner.Analyze(mid, rect!.Value, templates);
        double analyzeMs = sw.Elapsed.TotalMilliseconds / n;
        sw.Restart();
        var feats = new SquareFeature[64];
        for (int i = 0; i < n; i++)
            for (int k = 0; k < 64; k++) feats[k] = SquareFeature.Extract(mid, rect!.Value, k % 8, k / 8);
        double extractMs = sw.Elapsed.TotalMilliseconds / n;
        var fp = new BoardFingerprint();
        sw.Restart();
        for (int i = 0; i < 1000; i++) fp.Compute(mid, rect!.Value);
        double fpUs = sw.Elapsed.TotalMilliseconds;
        var line = $"Benchmark {mid.Width}x{mid.Height}: board detection {detectMs:F1} ms, piece recognition (64 squares) {analyzeMs:F1} ms (feature extraction {extractMs:F1} ms), checksum {fpUs / 1000 * 1000:F1} µs";
        Console.WriteLine(line);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "chesscoach-benchmark.txt"), line);
        Assert.True(detectMs < 500 && analyzeMs < 500);
    }
}
