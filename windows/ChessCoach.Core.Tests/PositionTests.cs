using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class PositionTests
{
    const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

    static Placement P(string fen) => Placement.FromFen(fen)!;

    [Fact]
    public void StartPositionFenAndFlags()
    {
        var p = P(StartFen);
        Assert.True(p.IsStartPosition);
        Assert.Equal(StartFen + " w KQkq - 0 1", p.Fen(PieceColor.White));
        Assert.Equal(StartFen + " b KQkq - 0 1", p.Fen(PieceColor.Black));
        Assert.Equal(32, p.PieceCount);
        Assert.Null(p.Problem(PieceColor.White));
    }

    [Fact]
    public void CastlingRightsFromPlacement()
    {
        var p = P("r3k2r/8/8/8/8/8/8/R3K2R");
        Assert.Equal("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", p.Fen(PieceColor.White));
        Assert.Equal("4k3/8/8/8/8/8/8/R3K3 w Q - 0 1", P("4k3/8/8/8/8/8/8/R3K3").Fen(PieceColor.White));
        Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", P("4k3/8/8/8/8/8/8/4K3").Fen(PieceColor.White));
    }

    [Fact]
    public void ScreenMapping()
    {
        var screen = new char?[8][];
        for (int i = 0; i < 8; i++) screen[i] = new char?[8];
        screen[7][4] = 'K'; screen[0][4] = 'k';
        var white = new Placement(screen, true);
        Assert.Equal('K', white.Squares[0][4]);
        var black = new Placement(screen, false);
        Assert.Equal('K', black.Squares[7][3]);
        Assert.Equal((3, 0), Placement.ChessSquare(4, 0, false));
    }

    [Theory]
    [InlineData("8/8/8/8/8/8/8/4K3", "Kings not clearly recognised. Did the piece set change? Menu: Relearn pieces")]
    [InlineData("4k3/8/8/8/8/8/8/P3K3", "Pawn on the back rank detected")]
    [InlineData("4k2p/8/8/8/8/8/8/4K3", "Pawn on the back rank detected")]
    [InlineData("4k3/pppppppp/p7/8/8/8/8/4K3", "Too many pawns detected")]
    [InlineData("4k3/8/8/8/8/PPPPPPPP/QQ6/4K3", "Impossible piece count, check the recognition")]
    public void Problems(string fen, string expected)
    {
        var p = P(fen);
        var problem = p.Problem(PieceColor.White);
        Assert.Equal(expected, problem);
    }

    [Fact]
    public void TooManyPiecesAndCheck()
    {
        Assert.Equal("Too many white pieces detected", P("4k3/8/8/8/PPPPPPPP/RRNNBBQQ/Q7/4K3").Problem(PieceColor.White));

        Assert.Equal("The side not to move is in check", P("4k3/8/8/8/8/8/4R3/4K3").Problem(PieceColor.White));
        Assert.Null(P("4k3/8/8/8/8/8/4R3/4K3").Problem(PieceColor.Black));
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/4R3/4K3", true)]
    [InlineData("4k3/8/3N4/8/8/8/8/4K3", true)]
    [InlineData("4k3/3P4/8/8/8/8/8/4K3", true)]
    [InlineData("4k3/5P2/8/8/8/8/8/4K3", true)]
    [InlineData("4k3/4P3/8/8/8/8/8/4K3", false)]
    [InlineData("4k3/8/8/4p3/8/8/4R3/4K3", false)]
    [InlineData("4k3/8/8/1B6/8/8/8/4K3", true)]
    public void CheckDetection(string fen, bool blackInCheck) =>
        Assert.Equal(blackInCheck, P(fen).IsInCheck(PieceColor.Black));

    [Fact]
    public void MoverDetection()
    {
        var before = P(StartFen);
        var after = before.Applying(UciMove.Parse("e2e4")!.Value);
        Assert.Equal(PieceColor.White, after.Mover(before));
        var after2 = after.Applying(UciMove.Parse("e7e5")!.Value);
        Assert.Equal(PieceColor.Black, after2.Mover(after));
        Assert.Null(before.Mover(before));
        Assert.Null(after2.Mover(before));
    }

    [Fact]
    public void UciParseAndDescribe()
    {
        Assert.Null(UciMove.Parse("(none)"));
        Assert.Null(UciMove.Parse("e2"));
        var m = UciMove.Parse("e7e8q")!.Value;
        Assert.Equal('q', m.Promotion);
        Assert.Equal("e7e8q", m.Uci);
        var p = P(StartFen);
        Assert.Equal("Knight g1–f3", p.DescribeMove(UciMove.Parse("g1f3")!.Value));
    }

    [Fact]
    public void FenRoundTrip()
    {
        var p = P("r1bq1b1r/ppp2kpp/5n2/1P1pN3/3n4/2NP4/PPP2PPP/R1BQ1RK1");
        Assert.Equal("r1bq1b1r/ppp2kpp/5n2/1P1pN3/3n4/2NP4/PPP2PPP/R1BQ1RK1", p.Fen(PieceColor.White).Split(' ')[0]);
        Assert.Null(Placement.FromFen("garbage"));
    }
}
