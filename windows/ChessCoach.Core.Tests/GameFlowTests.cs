using System.Text.Json;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class GameFlowTests
{
    static JsonElement Flow { get; } = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSupport.TestData, "vectors.json")))
        .RootElement.GetProperty("gameFlow");

    const string Mid = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R";
    static ChessPosition MidPos => ChessPosition.FromFen(Mid + " w KQkq - 0 1")!;

    static string Describe(UnsafeSquare u) => u.Reason switch
    {
        UnsafeReason.Undefended => "undefended",
        UnsafeReason.CheaperAttacker => "cheaperAttacker",
        _ => "leavesHanging:" + u.OtherSquare,
    };

    [Fact]
    public void UnsafeSquaresMatchVectors()
    {
        foreach (var v in Flow.GetProperty("unsafe").EnumerateArray())
        {
            var pos = ChessPosition.FromFen(v.GetProperty("fen").GetString()!)!;
            string from = v.GetProperty("from").GetString()!;
            int file = from[0] - 'a', rank = from[1] - '1';
            var lifted = new LiftedPiece(file, rank, pos.Board.Squares[rank][file]!.Value);
            var got = lifted.UnsafeSquares(pos).ToDictionary(u => u.Square, Describe);
            var want = v.GetProperty("unsafe").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
            Assert.Equal(want.OrderBy(x => x.Key), got.OrderBy(x => x.Key));
        }
    }

    [Fact]
    public void SummaryTexts()
    {
        var bishop = new LiftedPiece(2, 3, 'B');
        var many = bishop.UnsafeSquares(MidPos);
        Assert.Equal("Careful: 3 squares are unsafe for the Bishop, marked in red.", LiftedPiece.Summary(many, 'B'));
        var pawn = ChessPosition.FromFen("4k3/8/3p4/8/4P3/8/8/4K3 w - - 0 1")!;
        Assert.Equal("Careful: Pawn on e5 would be undefended.", LiftedPiece.Summary(new LiftedPiece(4, 3, 'P').UnsafeSquares(pawn), 'P'));
        Assert.Empty(new LiftedPiece(4, 3, 'P').UnsafeSquares(MidPos));
        Assert.Equal(new[] { "d2" }, new LiftedPiece(4, 0, 'K').UnsafeSquares(MidPos).Select(u => u.Square));
        Assert.Contains("leaves", LiftedPiece.Summary(new LiftedPiece(4, 0, 'K').UnsafeSquares(MidPos), 'K'));
        var black = ChessPosition.FromFen(MidPos.Fen.Replace(" w ", " b "))!;
        Assert.Empty(bishop.UnsafeSquares(black));
        Assert.Null(LiftedPiece.Summary(Array.Empty<UnsafeSquare>(), 'B'));
    }

    [Fact]
    public void LiftedPieceFromPlacements()
    {
        foreach (var v in Flow.GetProperty("lifted").EnumerateArray())
        {
            var last = ChessPosition.FromFen(v.GetProperty("last").GetString()!)!;
            var current = Placement.FromFen(v.GetProperty("current").GetString()!)!;
            string? want = v.GetProperty("lifted").GetString();
            Assert.True(want == LiftedPiece.Detect(last, current)?.Square, v.TryGetProperty("comment", out var c) ? c.GetString() : want);
        }
    }

    [Fact]
    public void GameEndObservations()
    {
        var ge = Flow.GetProperty("gameEnd");
        Assert.Equal(GameEndDetector.MinPlies, ge.GetProperty("minPlies").GetInt32());
        Assert.Equal(GameEndDetector.CoveredSeconds, ge.GetProperty("coveredSeconds").GetDouble());
        Assert.Equal(GameEndDetector.GoneSeconds, ge.GetProperty("goneSeconds").GetDouble());
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var c in ge.GetProperty("cases").EnumerateArray())
        {
            var detector = new GameEndDetector();
            (int Step, GameEnd End)? fired = null;
            int i = 0;
            foreach (var s in c.GetProperty("steps").EnumerateArray())
            {
                var view = s.GetProperty("view").GetString() switch { "readable" => BoardSight.Readable, "gone" => BoardSight.Gone, _ => BoardSight.Covered };
                GameResult? result = s.TryGetProperty("result", out var r) ? r.GetString() switch
                {
                    "checkmate:w" => GameResult.Checkmate(PieceColor.White),
                    "checkmate:b" => GameResult.Checkmate(PieceColor.Black),
                    "stalemate" => new GameResult(GameResultKind.Stalemate),
                    _ => null,
                } : null;
                bool isStart = s.TryGetProperty("isStart", out var st) && st.GetBoolean();
                var end = detector.Observe(view, s.GetProperty("plies").GetInt32(), result, isStart, t0.AddSeconds(s.GetProperty("t").GetDouble()));
                if (end != null && fired == null) fired = (i, end);
                i++;
            }
            string name = c.GetProperty("name").GetString()!;
            if (c.GetProperty("end").ValueKind == JsonValueKind.Null) { Assert.True(fired == null, name); continue; }
            var want = c.GetProperty("end");
            Assert.True(fired != null, name);
            Assert.Equal(want.GetProperty("step").GetInt32(), fired!.Value.Step);
            Assert.Equal(want.GetProperty("reason").GetString(), fired.Value.End.ReasonKey);
            Assert.Equal(want.GetProperty("result").GetString(), fired.Value.End.Result);
            Assert.Equal(want.TryGetProperty("silent", out var silent) && silent.GetBoolean(), fired.Value.End.Silent);
        }
    }

    [Fact]
    public void ManualEndKnowsOnlyMatedOrStalematePositions()
    {
        var mated = ChessPosition.FromFen("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1")!;
        Assert.Equal("1-0", GameEndDetector.For(mated.Result!.Value)?.Result);
        Assert.Null(GameEndDetector.For(new GameResult(GameResultKind.FiftyMoveRule)));
    }

    static Placement? Seen(RgbImage img, PieceTemplates t, bool whiteBottom, out ScanResult? scan)
    {
        scan = BoardScanner.Scan(img, t);
        return scan == null ? null : new Placement(scan.Screen, whiteBottom);
    }

    [Fact]
    public void SyntheticPopupsAreCoveredNotGone()
    {
        var dir = TestSupport.SyntheticDir();
        if (dir == null) return;
        var (t, white) = TestSupport.Learn(Path.Combine(dir, "start_white.png"));
        var plainImage = TestSupport.LoadPng(Path.Combine(dir, "popup_none.png"));
        var plain = Seen(plainImage, t, white, out var plainScan);
        Assert.Equal(Mid, plain!.Fen(PieceColor.White).Split(' ')[0]);
        Assert.Equal(BoardSight.Readable, BoardReadability.Sight(plainScan!.Features, null));
        var colors = BoardColors.Learn(plainScan.Features);
        var rect = BoardDetector.Detect(plainImage)!.Value;
        foreach (var name in new[] { "popup_blank", "popup_blur" })
        {
            var img = TestSupport.LoadPng(Path.Combine(dir, name + ".png"));
            var found = BoardScanner.Scan(img, t);
            Assert.True(found == null || !BoardReadability.IsReadable(found.Features), name);
            var known = BoardScanner.Analyze(img, rect, t);
            Assert.False(BoardReadability.IsReadable(known.Features), $"{name} covered {BoardReadability.CoveredSquares(known.Features)}");
            Assert.Equal(BoardSight.Covered, BoardReadability.Sight(known.Features, colors));
            Assert.Equal(BoardSight.Gone, BoardReadability.Sight(known.Features, null));
        }
    }

    [Fact]
    public void SyntheticLiftedPieces()
    {
        var dir = TestSupport.SyntheticDir();
        if (dir == null) return;
        var (t, white) = TestSupport.Learn(Path.Combine(dir, "start_white.png"));
        foreach (var name in new[] { "lift_dots", "lift_offgrid", "lift_dots_only" })
        {
            var placement = Seen(TestSupport.LoadPng(Path.Combine(dir, name + ".png")), t, white, out var scan);
            Assert.NotNull(placement);
            if (name == "lift_dots") Assert.True(BoardReadability.IsReadable(scan!.Features));
            Assert.Equal("c4", LiftedPiece.Detect(MidPos, placement!)?.Square);
            if (name == "lift_dots_only")
                Assert.Equal(new[] { "a6", "e6", "f7" }, new LiftedPiece(2, 3, 'B').UnsafeSquares(MidPos).Select(u => u.Square).OrderBy(x => x));
        }
    }

    [Fact]
    public void PrivateGameOverDialogIsCoveredAndEndsTheGameWithoutGuessing()
    {
        var dir = TestSupport.PrivateFixtures();
        if (dir == null) return;
        var startImage = TestSupport.LoadPng(Path.Combine(dir, "chesscom_start_white.png"));
        var (t, _) = TestSupport.Learn(Path.Combine(dir, "chesscom_start_white.png"));
        var rect = BoardDetector.Detect(startImage)!.Value;
        var colors = BoardColors.Learn(BoardScanner.Analyze(startImage, rect, t).Features);
        var dialog = TestSupport.LoadPng(Path.Combine(dir, "chesscom_gameover_popup_white_mated.png"));
        Assert.Null(BoardScanner.Scan(dialog, t));
        var features = BoardScanner.Analyze(dialog, new BoardRect(33, 53, 1136), t).Features;
        Assert.True(BoardReadability.RingMatch(features, colors) >= BoardReadability.RingShare, $"ring {BoardReadability.RingMatch(features, colors)} rect {rect}");
        Assert.Equal(BoardSight.Covered, BoardReadability.Sight(features, colors));
        var detector = new GameEndDetector();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameEnd? end = null;
        for (int i = 0; i < 5 && end == null; i++) end = detector.Observe(BoardSight.Covered, 40, null, false, t0.AddSeconds(i));
        Assert.Equal(new GameEnd(GameEndReason.Overlay, "*"), end);
        var lichess = Path.Combine(dir, "lichess_start_white.png");
        if (File.Exists(lichess))
        {
            var other = BoardScanner.Analyze(TestSupport.LoadPng(lichess), rect, t).Features;
            Assert.Equal(BoardSight.Gone, BoardReadability.Sight(other, colors));
        }
    }

    [Fact]
    public void ReportKeepsResultAndReasonAndReadsOldFiles()
    {
        var report = new GameReport { Id = "x", Result = "1-0", EndReason = "checkmate" };
        var json = ChessCoachJson.Serialize(report);
        Assert.Contains("\"endReason\"", json);
        Assert.Equal("1-0", ChessCoachJson.Deserialize<GameReport>(json)!.Result);
        Assert.Null(ChessCoachJson.Deserialize<GameReport>("{\"id\":\"old\"}")!.Result);
    }

    private static SquareFeature[][] Board(Func<int, int, int[]> background) =>
        Enumerable.Range(0, 8).Select(r => Enumerable.Range(0, 8).Select(c => new SquareFeature { Bg = background(r, c) }).ToArray()).ToArray();

    [Fact]
    public void LightCardOverBoardCountsAsCovered()
    {
        int[] Plain(int r, int c) => (r + c) % 2 == 1 ? new[] { 240, 217, 181 } : new[] { 181, 136, 99 };
        var plain = Board(Plain);
        Assert.True(BoardReadability.IsReadable(plain));
        var withCard = Board((r, c) => r is >= 2 and <= 4 && c is >= 2 and <= 5 ? new[] { 255, 255, 255 } : Plain(r, c));
        Assert.False(BoardReadability.IsReadable(withCard));
        var darkCard = Board((r, c) => r is >= 2 and <= 4 && c is >= 2 and <= 5 ? new[] { 20, 20, 20 } : Plain(r, c));
        Assert.False(BoardReadability.IsReadable(darkCard));
    }
}
