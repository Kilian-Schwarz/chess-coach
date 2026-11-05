using System.Text.Json;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class GameTests
{
    static JsonElement Vectors { get; } = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSupport.TestData, "vectors.json"))).RootElement;

    static Placement P(string fen) => ChessPosition.FromFen(fen)!.Board;

    [Fact]
    public void GamesMoveByMoveAndResync()
    {
        var games = Vectors.GetProperty("game").GetProperty("games").EnumerateArray().ToList();
        Assert.True(games.Count >= 6);
        foreach (var g in games)
        {
            string name = g.GetProperty("name").GetString()!;
            var moves = g.GetProperty("moves").EnumerateArray().Select(x => x.GetString()!).ToList();
            var sans = g.GetProperty("san").EnumerateArray().Select(x => x.GetString()!).ToList();
            var fens = g.GetProperty("fen").EnumerateArray().Select(x => x.GetString()!).ToList();
            string result = g.GetProperty("result").GetString()!;

            var rec = new GameRecord();
            var u = rec.Update(P(ChessPosition.StartFen), true);
            Assert.Equal(GameChange.NewGame, u.Change);
            for (int i = 0; i < moves.Count; i++)
            {
                u = rec.Update(P(fens[i]), true);
                Assert.True(u.Change == GameChange.Moves && u.NewMoves.Count == 1, $"{name} move {i + 1}: {u.Change}");
                var m = u.LastMove!;
                Assert.Equal(moves[i], m.Uci);
                Assert.Equal(sans[i], m.San);
                Assert.Equal(fens[i], u.Fen);
                Assert.Equal(sans[i], m.SanDisplay);
            }
            var pgnResult = u.Result?.Pgn;
            Assert.True(pgnResult == (result == "*" ? null : result) || (result == "*" && pgnResult != "1-0" && pgnResult != "0-1"),
                $"{name}: result {pgnResult} instead of {result}");
            var pgn = rec.Pgn("A", "B", date: new DateTime(1970, 1, 1, 12, 0, 0, DateTimeKind.Unspecified));
            Assert.Contains("[Result \"", pgn);
            Assert.Contains("[Date \"1970.01.01\"]", pgn);
            Assert.Contains("1. " + sans[0], pgn);
            if (name == "fools_mate") Assert.Contains("1. f3 e5 2. g4 Qh4# 0-1", pgn);

            var rec2 = new GameRecord();
            rec2.Update(P(ChessPosition.StartFen), true);
            for (int i = 1; i < moves.Count; i += 2)
            {
                u = rec2.Update(P(fens[i]), true);
                Assert.True(u.Change == GameChange.Moves && u.NewMoves.Count == 2, $"{name} Resync Halbzug {i + 1}: {u.Change}/{u.NewMoves.Count}");
                Assert.Equal(fens[i], u.Fen);
            }
            Assert.Equal(moves.Count % 2 == 0 ? moves.Count : moves.Count - 1, rec2.Moves.Count);
        }
    }

    [Fact]
    public void Takeback()
    {
        var g = Vectors.GetProperty("game").GetProperty("games").EnumerateArray().First(x => x.GetProperty("name").GetString() == "scholars_mate");
        var fens = g.GetProperty("fen").EnumerateArray().Select(x => x.GetString()!).ToList();
        var rec = new GameRecord();
        rec.Update(P(ChessPosition.StartFen), true);
        foreach (var f in fens.Take(4)) rec.Update(P(f), true);
        var u = rec.Update(P(fens[2]), true);
        Assert.True(u.Change == GameChange.Takeback && rec.Moves.Count == 3, $"{u.Change}, {rec.Moves.Count}");
        u = rec.Update(P(fens[3]), true);
        Assert.True(u.Change == GameChange.Moves && u.Fen == fens[3]);
    }

    [Fact]
    public void UnexplainableStartsNewSegment()
    {
        var rec = new GameRecord();
        rec.Update(P(ChessPosition.StartFen), true);
        rec.Update(P("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"), true);
        var jump = "r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQ1RK1 b kq - 0 5";
        var u = rec.Update(P(jump), true);
        Assert.True(u.Change == GameChange.Resync && u.Estimated);
        Assert.Contains(" kq ", u.Fen);
        Assert.Equal(new[] { 1 }, rec.Breaks);
        Assert.Single(rec.Moves);
        u = rec.Update(P("r1bq1rk1/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQ1RK1 w - - 1 6"), true);
        Assert.True(u.Change == GameChange.Moves && u.LastMove?.San == "O-O" && u.Fen.StartsWith("r1bq1rk1/"));
        Assert.Equal(2, rec.Moves.Count);
        Assert.Equal(1, rec.SegmentMoves);
        var pgn = rec.Pgn();
        Assert.Contains("{ moves missed }", pgn);
        Assert.Contains("1. e4", pgn);
        u = rec.Update(P(ChessPosition.StartFen), true);
        Assert.True(u.Change == GameChange.NewGame && rec.Moves.Count == 0 && !u.Estimated);
    }

    [Fact]
    public void GarbageFrameStartsFreshSegmentAndRecoveryRestoresMoves()
    {
        var rec = new GameRecord();
        rec.Update(P(ChessPosition.StartFen), true);
        string fen = "";
        foreach (var f in new[] { "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "rnbqkbnr/pp1ppppp/8/2p5/4P3/8/PPPP1PPP/RNBQKBNR w KQkq c6 0 2" })
        {
            fen = f;
            rec.Update(P(f), true);
        }
        var good = P(fen);
        var u = rec.Update(new Placement(), true);
        Assert.True(u.Change == GameChange.Resync && rec.Moves.Count == 2 && rec.Breaks.Count == 1);
        u = rec.Update(good, true);
        Assert.True(rec.Moves.Count == 2 && rec.Breaks.Count == 0 && u.Change != GameChange.NewGame);
        u = rec.Update(P("rnbqkbnr/pp1ppppp/8/2p5/4P3/5N2/PPPP1PPP/RNBQKB1R b KQkq - 1 2"), true);
        Assert.True(u.Change == GameChange.Moves && rec.Moves.Count == 3);
    }

    [Fact]
    public void ResyncContinuityRules()
    {
        var start = P(ChessPosition.StartFen);
        Assert.True(ResyncContinuity.IsPlausible(start, start));
        Assert.False(ResyncContinuity.IsPlausible(start, new Placement()));
        Assert.False(ResyncContinuity.IsPlausible(P("4k3/8/8/8/8/8/8/4K3 w - - 0 1"), start));
    }

    [Fact]
    public void FirstSightUsesHintAndCorrectsSide()
    {
        var rec = new GameRecord();
        var b = P("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 5");
        var u = rec.Update(b, true, PieceColor.Black);
        Assert.True(u.Change == GameChange.FirstSight && u.SideToMove == PieceColor.White && u.Estimated);
        u = rec.Update(b, true, PieceColor.White);
        Assert.True(u.Change == GameChange.Unchanged && u.SideToMove == PieceColor.Black);
        u = rec.Update(P("r1bqk2r/ppp2ppp/2np1n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 6"), true);
        Assert.True(u.Change == GameChange.Moves && u.LastMove?.Mover == PieceColor.Black);

        var rec2 = new GameRecord();
        rec2.Update(b, false);
        var afterWhite = P("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPPB1PPP/RN1QK2R b KQkq - 1 5");
        u = rec2.Update(afterWhite, false);
        Assert.True(u.Change == GameChange.Moves && u.LastMove?.Mover == PieceColor.White && u.SideToMove == PieceColor.Black, $"{u.Change}");
    }

    [Fact]
    public void CastlingRightsAreExact()
    {
        var rec = new GameRecord();
        rec.Update(P(ChessPosition.StartFen), true);
        foreach (var u in new[] { "e2e4", "e7e5", "e1e2", "e8e7", "e2e1", "e7e8" })
            rec.Update(rec.Current!.Applying(UciMove.Parse(u)!.Value).Board, true);
        Assert.True(rec.Current!.Castling.IsEmpty);
        Assert.Contains(" - - 4 4", rec.Current.Fen);
    }

    [Fact]
    public void PgnOfEstimatedSegmentHasSetUp()
    {
        var rec = new GameRecord();
        rec.Update(P("r1bqk2r/pppp1ppp/2n2n2/2b1p3/2B1P3/3P1N2/PPP2PPP/RNBQK2R w KQkq - 0 5"), true);
        var pgn = rec.Pgn(opening: "Italian Game");
        Assert.Contains("[SetUp \"1\"]", pgn);
        Assert.Contains("[FEN \"", pgn);
        Assert.Contains("[Opening \"Italian Game\"]", pgn);
    }

    [Fact]
    public void ThreatVectors()
    {
        var t = Vectors.GetProperty("threats");
        Assert.Equal(Threats.Threshold, t.GetProperty("threshold").GetInt32());
        foreach (var c in t.GetProperty("nullMove").EnumerateArray())
        {
            var pos = ChessPosition.FromFen(c.GetProperty("fen").GetString()!)!;
            var exp = c.GetProperty("nullFen");
            Assert.Equal(exp.ValueKind == JsonValueKind.Null ? null : exp.GetString(), Threats.NullMoveFen(pos));
        }
        EngineLine Line(string best, JsonElement r) => new(best,
            r.TryGetProperty("cp", out var cp) ? cp.GetInt32() : null,
            r.TryGetProperty("mate", out var mt) ? mt.GetInt32() : null, new[] { best });
        foreach (var c in t.GetProperty("detect").EnumerateArray())
        {
            var pos = ChessPosition.FromFen(c.GetProperty("fen").GetString()!)!;
            var best = c.GetProperty("opponentBest").GetString()!;
            var cur = new EngineResult(pos.Fen, "e2e4", new[] { Line("e2e4", c.GetProperty("current")) });
            var opp = new EngineResult("", best, new[] { Line(best, c.GetProperty("opponent")) });
            var th = Threats.Detect(pos, cur, opp);
            Assert.Equal(c.GetProperty("threat").GetBoolean(), th != null);
            if (c.TryGetProperty("text", out var txt)) Assert.Equal(txt.GetString(), th?.Text);
        }
    }

    [Fact]
    public async Task ThreatsWithRealEngine()
    {
        var path = TestSupport.StockfishPath();
        if (path == null) return;
        using var engine = new StockfishEngine(path);
        foreach (var c in Vectors.GetProperty("threats").GetProperty("engine").EnumerateArray())
        {
            var pos = ChessPosition.FromFen(c.GetProperty("fen").GetString()!)!;
            var nullFen = Threats.NullMoveFen(pos)!;
            var cur = await engine.AnalyzeAsync(pos.Fen, 400);
            var opp = await engine.AnalyzeAsync(nullFen, 400);
            Assert.NotNull(cur); Assert.NotNull(opp);
            var th = Threats.Detect(pos, cur!, opp!);
            Assert.Equal(c.GetProperty("threat").GetBoolean(), th != null);
            if (c.TryGetProperty("contains", out var s)) Assert.Contains(s.GetString()!, th!.Text);
        }
    }

    static string Names(IEnumerable<(int File, int Rank)> sq) =>
        string.Join(",", sq.Select(s => UciMove.SquareName(s.File, s.Rank)).OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void LastMoveMarkingOnLichessFixtures()
    {
        foreach (var fx in Vectors.GetProperty("sideToMove").GetProperty("fixtures").EnumerateArray())
        {
            var start = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, fx.GetProperty("start").GetString()!));
            var mid = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, fx.GetProperty("image").GetString()!));
            var b1 = BoardDetector.Detect(start)!.Value;
            var templates = new PieceTemplates();
            var s1 = BoardScanner.Analyze(start, b1, templates);
            Assert.True(s1.LooksLikeStart);
            bool wbBottom = templates.LearnStart(s1.Features);
            Assert.NotNull(templates.SquareColors);
            var startMark = SideToMove.Detect(s1, wbBottom, templates);
            Assert.Empty(startMark.Marked);
            Assert.Null(startMark.Mover);

            var res = BoardScanner.Scan(mid, templates)!;
            bool wb = BoardScanner.GuessWhiteAtBottom(res.Screen) ?? wbBottom;
            Assert.Equal(fx.GetProperty("whiteAtBottom").GetBoolean(), wb);
            var mark = SideToMove.Detect(res, wb, templates);
            string Sorted(string p) => string.Join(",", fx.GetProperty(p).EnumerateArray().Select(x => x.GetString()!).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(Sorted("marked"), Names(mark.Marked));
            Assert.Equal(Sorted("checks"), Names(mark.CheckSquares));
            Assert.Equal(fx.GetProperty("mover").GetString() == "white" ? PieceColor.White : PieceColor.Black, mark.Mover);
            Assert.Equal(mark.Mover!.Value.Opposite(), mark.SideToMove);

            var noLearn = SideToMove.Detect(res, wb, new PieceTemplates());
            Assert.Equal(Names(mark.Marked), Names(noLearn.Marked));
            Assert.Equal(mark.Mover, noLearn.Mover);
        }
    }

    [Fact]
    public void SquareColorsSurviveJson()
    {
        var start = TestSupport.LoadPng(Path.Combine(TestSupport.TestData, "lichess_start_black.png"));
        var t = new PieceTemplates();
        t.LearnStart(BoardScanner.Analyze(start, BoardDetector.Detect(start)!.Value, t).Features);
        var json = JsonSerializer.Serialize(t);
        var back = JsonSerializer.Deserialize<PieceTemplates>(json)!;
        Assert.Equal(t.SquareColors, back.SquareColors);
        Assert.NotNull(back.Templates["K"][0].Bg);
    }
}
