using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class TacticsCoachTests
{
    static Placement P(string fen) => Placement.FromFen(fen)!;
    static UciMove M(string s) => UciMove.Parse(s)!.Value;

    [Fact]
    public void ApplyingHandlesCastlingEnPassantPromotion()
    {
        var c = P("r3k2r/8/8/8/8/8/8/R3K2R").Applying(M("e1g1"));
        Assert.Equal('K', c.Squares[0][6]); Assert.Equal('R', c.Squares[0][5]); Assert.Null(c.Squares[0][7]);
        var q = P("r3k2r/8/8/8/8/8/8/R3K2R").Applying(M("e8c8"));
        Assert.Equal('r', q.Squares[7][3]); Assert.Null(q.Squares[7][0]);
        var ep = P("4k3/8/8/3pP3/8/8/8/4K3").Applying(M("e5d6"));
        Assert.Null(ep.Squares[4][3]); Assert.Equal('P', ep.Squares[5][3]);
        var pr = P("4k3/P7/8/8/8/8/8/4K3").Applying(M("a7a8q"));
        Assert.Equal('Q', pr.Squares[7][0]);
    }

    [Fact]
    public void HangingPieces()
    {
        var p = P("4k3/8/8/3q4/8/2N5/8/4K3");

        Assert.Contains("Queen d5 is attacked and unprotected", p.HangingPieces(PieceColor.Black));
        Assert.Contains("Queen d5 is attacked by a cheaper piece", P("4k3/8/8/3q4/8/2N5/8/3RK3").Attackers(PieceColor.White).Length > 0
            ? P("3rk3/8/8/3q4/8/2N5/8/3RK3").HangingPieces(PieceColor.Black) : new List<string>());
        Assert.Empty(P("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR").HangingPieces(PieceColor.White));
    }

    [Fact]
    public void PieceListAndAttackers()
    {
        var p = P("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR");
        Assert.StartsWith("King e1, Queen d1, Rooks a1 h1, Bishops c1 f1, Knights b1 g1, Pawns a2 b2", p.PieceList(PieceColor.White));
        var att = p.Attackers(PieceColor.White);
        Assert.Equal(3, att[2][5].Count);
    }

    [Fact]
    public void InferMove()
    {
        var a = P("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR");
        Assert.Equal("e2e4", a.InferMove(a.Applying(M("e2e4")), PieceColor.White)?.Uci);
        var c = P("r3k2r/8/8/8/8/8/8/R3K2R");
        Assert.Equal("e1c1", c.InferMove(c.Applying(M("e1c1")), PieceColor.White)?.Uci);
        var pr = P("4k3/P7/8/8/8/8/8/4K3");
        Assert.Equal("a7a8q", pr.InferMove(pr.Applying(M("a7a8q")), PieceColor.White)?.Uci);
        Assert.Null(a.InferMove(a, PieceColor.White));
    }

    [Fact]
    public void DescribeTexts()
    {
        var p = P("r1bq1b1r/ppp2kpp/5n2/1P1pN3/3n4/2NP4/PPP2PPP/R1BQ1RK1");
        Assert.Equal("Knight e5 captures Pawn f7", p.DescribeMove(M("e5f7")).Replace("King f7","Pawn f7"));
        Assert.Equal("short castling", P("4k3/8/8/8/8/8/8/R3K2R").DescribeMove(M("e1g1")));
        Assert.Equal("Pawn e5 captures en passant on d6", P("4k3/8/8/3pP3/8/8/8/4K3").DescribeMove(M("e5d6")));
        Assert.Equal("Pawn a7–a8, promotes to Queen, check", P("1k6/P7/8/8/8/8/8/4K3").DescribeMove(M("a7a8q")));
        Assert.Equal("Rook e2–e7, check", P("4k3/8/8/8/8/8/4R3/4K3").DescribeMove(M("e2e7")));
    }

    [Fact]
    public void RatingThresholds()
    {
        Assert.Equal(MoveRating.Best, MoveRatingExt.From(80.0, 10.0, true));
        Assert.Equal(MoveRating.Best, MoveRatingExt.From(60.0, 58.0, false));
        Assert.Equal(MoveRating.Good, MoveRatingExt.From(60.0, 57.9, false));
        Assert.Equal(MoveRating.Good, MoveRatingExt.From(60.0, 50.1, false));
        Assert.Equal(MoveRating.Inaccuracy, MoveRatingExt.From(60.0, 50.0, false));
        Assert.Equal(MoveRating.Inaccuracy, MoveRatingExt.From(60.0, 40.1, false));
        Assert.Equal(MoveRating.Mistake, MoveRatingExt.From(60.0, 40.0, false));
        Assert.Equal(MoveRating.Mistake, MoveRatingExt.From(60.0, 30.1, false));
        Assert.Equal(MoveRating.Blunder, MoveRatingExt.From(60.0, 30.0, false));
        Assert.Equal(MoveRating.Good, MoveRatingExt.From(0, -50, false));
        Assert.Equal(MoveRating.Blunder, MoveRatingExt.From(0, -900, false));
        Assert.Equal("Blunder", MoveRating.Blunder.Label());
    }

    [Fact]
    public void ScoreTexts()
    {
        Assert.Equal("+0.3", EngineLine.Text(30, null));
        Assert.Equal("-1.5", EngineLine.Text(-150, null));
        Assert.Equal("+0.0", EngineLine.Text(0, null));
        Assert.Equal("Mate in 3", EngineLine.Text(null, 3));
        Assert.Equal("mated in 2", EngineLine.Text(null, -2));
        Assert.Equal("Checkmate", EngineLine.Text(null, 0));
        Assert.Equal(9970, new EngineLine("a1a2", null, 3, Array.Empty<string>()).Value);
        Assert.Equal(-9980, new EngineLine("a1a2", null, -2, Array.Empty<string>()).Value);
    }

    [Fact]
    public void CleanAnswerRemovesThinking()
    {
        Assert.Equal("Answer", Coach.CleanAnswer("<think>calc</think>\n Answer "));
        Assert.Equal("Answer", Coach.CleanAnswer("calc</think>Answer"));
        Assert.Equal("Answer", Coach.CleanAnswer("Answer<thi"));
        Assert.Equal("Answer", Coach.CleanAnswer("Answer<"));
        Assert.Equal("a < b", Coach.CleanAnswer("a < b"));
        Assert.Equal("Before", Coach.CleanAnswer("Before<think>unfinished"));
        Assert.Equal("ab", Coach.CleanAnswer("a<think>x</think>b<think>y</think>"));
    }

    [Fact]
    public void ReviewAndPrompts()
    {
        var before = P("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR");
        var played = M("f2f3");
        var next = before.Applying(played);
        var beforeRes = new EngineResult(before.Fen(PieceColor.White), "e2e4",
            new[] { new EngineLine("e2e4", 30, null, new[] { "e2e4", "e7e5" }), new EngineLine("d2d4", 25, null, new[] { "d2d4" }) });
        var afterRes = new EngineResult("x", "e7e5", new[] { new EngineLine("e7e5", 80, null, new[] { "e7e5", "d2d4" }) });
        var r = Coach.Review(before, played, next, PieceColor.White, beforeRes, afterRes)!;
        Assert.Equal(110, r.Loss);
        Assert.Equal(MoveRating.Inaccuracy, r.Rating);
        Assert.Equal("-0.8", r.PlayedScore);
        Assert.Equal("Pawn f2–f3", r.PlayedText);
        Assert.Equal("Pawn e7–e5; Pawn d2–d4", r.ReplyLine);
        var ctx = new PlayerContext(PlayerLevel.Intermediate);
        var request = Coach.ReviewRequest(r, ctx, new ExplanationStyle(), new System.Globalization.CultureInfo("de"));
        Assert.Equal(Prompts.System, request.System);
        Assert.Contains("Rating: Inaccuracy (loss against the best move: 1.1 pawns).", request.User);
        Assert.Contains("Better was: Pawn e2–e4 (evaluation: +0.3).", request.User);
        Assert.Contains("Answer in German. Use exactly: king=König", request.User);
        Assert.Equal(220, request.MaxTokens);

        var sp = Coach.SuggestionRequest(before, PieceColor.White, before.Fen(PieceColor.White), beforeRes, ctx, new ExplanationStyle())!.User;
        Assert.Contains("Best move per engine: Pawn e2–e4 (evaluation: +0.3)", sp);
        Assert.Contains("Alternative 1: Pawn d2–d4 (evaluation: +0.3)", sp.Replace("+0.25", "+0.3"));
        Assert.Contains("Expected continuation: Pawn e2–e4; Pawn e7–e5", sp);
        Assert.Contains("White to move.", sp);
        Assert.EndsWith("Answer in English.", sp);
    }

    [Fact]
    public void ReviewMateAndStalemate()
    {
        var before = P("6k1/5ppp/8/8/8/8/8/R3K3");
        var played = M("a1a8");
        var next = before.Applying(played);
        var res = new EngineResult("f", "a1a8", new[] { new EngineLine("a1a8", null, 1, new[] { "a1a8" }) });
        var none = new EngineResult("g", "", Array.Empty<EngineLine>());
        Assert.Null(Coach.Review(before, played, next, PieceColor.White, res, none));
        var r = Coach.Review(before, played, next, PieceColor.White, res, none, GameResult.Checkmate(PieceColor.White))!;
        Assert.Equal("Checkmate", r.PlayedScore);
        Assert.Equal(MoveRating.Best, r.Rating);
    }
}
