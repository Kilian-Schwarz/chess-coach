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

    private const string StartPlacement = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

    private static string PlacementAfterEarlyLearning(string path, bool whiteBottom)
    {
        var img = TestSupport.LoadPng(path);
        var rect = BoardDetector.Detect(img)!.Value;
        var templates = new PieceTemplates();
        var scan = BoardScanner.Analyze(img, rect, templates);
        Assert.False(scan.LooksLikeStart);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var wb = templates.LearnEarly(scan.Features);
        Console.WriteLine($"early position search {sw.ElapsedMilliseconds} ms");
        Assert.Equal(whiteBottom, wb);
        var res = BoardScanner.Scan(img, templates)!;
        return TestSupport.PlacementOf(res.Screen, whiteBottom);
    }

    [Theory]
    [InlineData("early_d4_white", "rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", true)]
    [InlineData("early_e4e5_black", "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR", false)]
    [InlineData("early_nf3nc6_white", "r1bqkbnr/pppppppp/2n5/8/8/5N2/PPPPPPPP/RNBQKB1R", true)]
    [InlineData("early_four_plies_black", "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R", false)]
    public void EarlyPositionsLearnFromSyntheticBoards(string name, string expected, bool whiteBottom)
    {
        var dir = TestSupport.SyntheticDir();
        if (dir == null) return;
        Assert.Equal(expected, PlacementAfterEarlyLearning(Path.Combine(dir, name + ".png"), whiteBottom));
    }

    [Theory]
    [InlineData("chesscom_start_white.png")]
    [InlineData("lichess_start_white.png")]
    public void PrivateStartFixturesLearnAndClassify(string file)
    {
        var path = Path.Combine(TestSupport.Root, "intern", "fixtures", file);
        if (!File.Exists(path)) return;
        var img = TestSupport.LoadPng(path);
        var templates = new PieceTemplates();
        var scan = BoardScanner.Analyze(img, BoardDetector.Detect(img)!.Value, templates);
        Assert.True(scan.LooksLikeStart);
        Assert.True(templates.LearnStart(scan.Features));
        var res = BoardScanner.Scan(img, templates)!;
        Assert.Equal(StartPlacement, TestSupport.PlacementOf(res.Screen, true));
    }

    [Fact]
    public void ThemeChangeIsDetectedAndRelearnedFromEarlyPosition()
    {
        var start = Path.Combine(TestSupport.Root, "intern", "fixtures", "chesscom_start_white.png");
        var themed = Path.Combine(TestSupport.Root, "intern", "fixtures", "chesscom_theme_d4_nf6_white.png");
        if (!File.Exists(start) || !File.Exists(themed)) return;
        var (templates, _) = TestSupport.Learn(start);
        var img = TestSupport.LoadPng(themed);
        var scan = BoardScanner.Analyze(img, BoardDetector.Detect(img)!.Value, templates);
        Assert.True(templates.FitsBadly(scan.Features));
        var relearned = PieceTemplates.Relearned(scan.Features);
        Assert.NotNull(relearned);
        Assert.True(relearned!.Value.WhiteAtBottom);
        Assert.Equal(PieceColor.White, relearned.Value.Position!.Turn);
        Assert.Equal("rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR", relearned.Value.Position!.Board.Fen(PieceColor.White).Split(' ')[0]);
        Assert.False(relearned.Value.Templates.FitsBadly(scan.Features));
        var res = BoardScanner.Scan(img, relearned.Value.Templates)!;
        Assert.Equal("rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR", TestSupport.PlacementOf(res.Screen, true));
    }

    [Fact]
    public void ThemeChangeIsRelearnedFromTheTrackedPosition()
    {
        var start = Path.Combine(TestSupport.Root, "intern", "fixtures", "chesscom_start_white.png");
        var themed = Path.Combine(TestSupport.Root, "intern", "fixtures", "chesscom_theme_d4_nf6_white.png");
        if (!File.Exists(start) || !File.Exists(themed)) return;
        var (templates, _) = TestSupport.Learn(start);
        var img = TestSupport.LoadPng(themed);
        var scan = BoardScanner.Analyze(img, BoardDetector.Detect(img)!.Value, templates);
        Assert.True(templates.FitsBadly(scan.Features));
        var tracked = ChessPosition.FromFen("rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR w KQkq - 1 2")!;
        var wrong = tracked.Board.Clone();
        wrong.Squares[4][4] = 'p';
        Assert.Null(new PieceTemplates().LearnKnown(scan.Features, wrong, true));
        var learned = new PieceTemplates();
        Assert.True(learned.LearnKnown(scan.Features, tracked.Board, null));
        var res = BoardScanner.Scan(img, learned)!;
        Assert.Equal("rnbqkb1r/pppppppp/5n2/8/3P4/8/PPP1PPPP/RNBQKBNR", TestSupport.PlacementOf(res.Screen, true));
        Assert.False(learned.FitsBadly(res.Features));
    }

    [Fact]
    public void MatchingTemplatesDoNotFitBadly()
    {
        var start = Path.Combine(TestSupport.Root, "intern", "fixtures", "chesscom_start_white.png");
        if (!File.Exists(start)) return;
        var (templates, _) = TestSupport.Learn(start);
        var img = TestSupport.LoadPng(start);
        Assert.False(templates.FitsBadly(BoardScanner.Analyze(img, BoardDetector.Detect(img)!.Value, templates).Features));
    }

    [Fact]
    public void PrivateWorldChessFixtureLearnsFromEarlyPositionIgnoringArrow()
    {
        var path = Path.Combine(TestSupport.Root, "intern", "fixtures", "worldchess_d4_black_arrow.png");
        if (!File.Exists(path)) return;
        Assert.Equal("rnbqkbnr/pppppppp/8/8/3P4/8/PPP1PPPP/RNBQKBNR", PlacementAfterEarlyLearning(path, false));
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
