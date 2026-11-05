using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class GroundingTests
{
    const string PinFen = "r1b1kbnr/pp2qppp/2n5/1B1pN3/3P4/8/PPP2PPP/RNBQK2R w KQkq - 0 7";
    const string DiscFen = "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQR1K1 w kq - 7 9";

    static ChessPosition Pos(string fen) => ChessPosition.FromFen(fen)!;
    static UciMove M(string s) => UciMove.Parse(s)!.Value;
    static EngineLine Line(string move, int cp, params string[] pv) => new(move, cp, null, pv);

    static (Grounding Grounding, HintSet Hints) PinFacts(bool reveal)
    {
        var pos = Pos(PinFen);
        var result = new EngineResult(PinFen, "e1g1", new[] { Line("e1g1", 550, "e1g1", "d8c7", "c2c4"), Line("c2c4", 410, "c2c4", "a7a6") });
        var nullResult = new EngineResult(pos.NullMove.Fen, "f7f6", new[] { Line("f7f6", 540, "f7f6", "c1g5") });
        var hints = Hints.Build(pos.Board, PieceColor.White, result, nullResult)!;
        return (PositionFacts.Build(pos, result, nullResult, hints.Threat, reveal), hints);
    }

    [Fact]
    public void PinsAreFoundFromKingAndQueen()
    {
        var board = Pos(PinFen).Board;
        var white = board.Pins(PieceColor.White);
        Assert.Single(white);
        Assert.Equal(("e5", "e7", "e1", true), (white[0].Square, white[0].PinnerSquare, white[0].TargetSquare, white[0].Absolute));
        Assert.Contains(board.Pins(PieceColor.Black), p => p.Square == "c6" && p.PinnerSquare == "b5");
        Assert.Empty(ChessPosition.Start.Board.Pins(PieceColor.White));
        Assert.Contains(Pos("4k3/8/8/8/1b6/2N5/3Q4/4K3 w - - 0 1").Board.Pins(PieceColor.White), p => p.Square == "c3" && !p.Absolute);
        Assert.DoesNotContain(Pos("4k3/8/8/8/1q6/2N5/3Q4/4K3 w - - 0 1").Board.Pins(PieceColor.White), p => p.TargetSquare == "d2");
    }

    [Fact]
    public void CastlingUnpinsTheKnightAndGetsTheRightHints()
    {
        var board = Pos(PinFen).Board;
        Assert.Single(board.Unpinned(M("e1g1"), PieceColor.White));
        Assert.Equal(HintTheme.Unpin, Hints.Theme(board, M("e1g1"), PieceColor.White));
        Assert.Equal("Bring your king to safety: castle.", Hints.Stage2Text('K', "e1", true));
        Assert.Equal("Move the king on e1.", Hints.Stage2Text('K', "e1", false));
        var (_, hints) = PinFacts(true);
        Assert.Equal("Bring your king to safety: castle.", hints.Stage2);
        Assert.Equal(HintTheme.Unpin, hints.Theme);
        Assert.Contains("pinned", hints.Stage1);
    }

    [Fact]
    public void FactsContainBestMovePinAndThreatConsequence()
    {
        var (g, _) = PinFacts(true);
        var text = string.Join("\n", g.Lines);
        Assert.Contains("Best move per engine: O-O", text);
        Assert.Contains("White Knight e5 is pinned against White King e1 by Black Queen e7", text);
        Assert.Contains("Opponent's threat", text);
        Assert.Contains("pinned against King e1, so it cannot move away", text);
        Assert.Contains("The best move ends the pin", text);
        Assert.Contains("Alternative 1: c4", text);
        Assert.DoesNotContain("Best move per engine", string.Join("\n", PinFacts(false).Grounding.Lines));
    }

    [Fact]
    public void HintLinesRespectTheStage()
    {
        var (_, hints) = PinFacts(true);
        var pos = Pos(PinFen);
        Assert.Contains("do not name the best move", string.Join(" ", PositionFacts.HintLines(2, hints, pos)));
        Assert.Contains("has not asked for a hint", string.Join(" ", PositionFacts.HintLines(0, hints, pos)));
        Assert.Contains("shown on screen", string.Join(" ", PositionFacts.HintLines(4, hints, pos)));
    }

    [Fact]
    public void PromptsAvoidMetaLanguage()
    {
        foreach (var s in new[] { Prompts.System, Prompts.HintSystem, Prompts.QuestionSystem, Prompts.SummarySystem })
        {
            var low = s.ToLowerInvariant();
            Assert.DoesNotContain("facts", low);
            Assert.DoesNotContain("information provided", low);
            Assert.DoesNotContain(" data", low);
        }
    }

    [Fact]
    public void DiscoveredAttackAndRookXRay()
    {
        var board = Pos(DiscFen).Board;
        Assert.Equal(HintTheme.Discovered, Hints.Theme(board, M("e5c6"), PieceColor.White));
        Assert.Contains(board.DiscoveredTargets(M("e5c6"), PieceColor.White), d => d.Target.Square == "e6" && d.Slider.Square == "e1");
        Assert.NotEqual(HintTheme.Discovered, Hints.Theme(ChessPosition.Start.Board, M("e2e4"), PieceColor.White));
        var before = Pos("r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQ1RK1 w kq - 7 9");
        var facts = Coach.MoveEffects(before.Board, M("f1e1"), PieceColor.White, "The played move");
        Assert.Contains(facts, f => f.Contains("x-rays Queen e6"));
        Assert.Contains(facts, f => f.Contains("attacks no enemy piece directly"));
    }

    [Fact]
    public void QuietThreatsAreNotShown()
    {
        var pos = Pos("1B2k2r/p3b1p1/b3pn2/3p3p/3P1B2/2N5/PPP2PPP/R2Q2K1 w - - 0 1");
        var current = new EngineResult(pos.Fen, "b8a7", new[] { Line("b8a7", 1000, "b8a7") });
        var quiet = new EngineResult(pos.NullMove.Fen, "e8d7", new[] { Line("e8d7", -800, "e8d7") });
        Assert.Null(Threats.Detect(pos, current, quiet));
        Assert.Equal(HintTheme.Convert, Hints.Theme(ChessPosition.Start.Board, M("g1f3"), PieceColor.White, value: 400));
    }

    [Fact]
    public void FactCheckRejectsFalseAttacksAndInventedSquares()
    {
        var pos = Pos(DiscFen);
        var allowed = FactCheck.Allowed(new[] { pos }, new IReadOnlyList<string>[] { new[] { "e5c6", "b7c6", "e1e6" } });
        Assert.Null(FactCheck.Clean("Your rook on e1 threatens the knight.", allowed).Text);
        Assert.Null(FactCheck.Clean("The Rook e1 attacks the Knight c6.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("Your Bishop b5 attacks the Knight c6.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("The rook on e1 attacks the queen on e6 once the knight moves.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("Play Nxc6 and the rook on e1 attacks the queen on e6.", allowed).Text);
        Assert.Null(FactCheck.Clean("Your Rook h3 is strong.", allowed).Text);
    }

    [Fact]
    public void CheckSuffixComesFromTheMoveGenerator()
    {
        var end = Pos("8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/6K1 b - - 0 1");
        Assert.Equal("Rxa2+", San.Of(M("f2a2"), end));
        Assert.EndsWith(", check", end.Board.DescribeMove(M("f2a2"), CoreLocale.English));
        var quiet = Pos("8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/7K b - - 0 1");
        Assert.Equal("Rxa2", San.Of(M("f2a2"), quiet));
        Assert.DoesNotContain("check", quiet.Board.DescribeMove(M("f2a2"), CoreLocale.English));
        var allowed = FactCheck.Allowed(new[] { end }, Array.Empty<IReadOnlyList<string>>());
        Assert.NotNull(FactCheck.Clean("Black plays Rxa2+ next.", allowed).Text);
        Assert.Null(FactCheck.Clean("Black plays Rxa2# next.", allowed).Text);
        var allowedQuiet = FactCheck.Allowed(new[] { quiet }, Array.Empty<IReadOnlyList<string>>());
        Assert.Null(FactCheck.Clean("Black plays Rxa2+ next.", allowedQuiet).Text);
    }

    [Fact]
    public void HintPurposeIsConcrete()
    {
        var pos = Pos("8/6p1/1k6/p1b4R/6P1/1P6/P4r1P/6K1 w - - 0 1");
        var lines = PositionFacts.PurposeLines(pos, M("h5f5"));
        Assert.Contains(lines, l => l.Contains("the f-file"));
        Assert.Contains(lines, l => l.Contains("attacks:") && l.Contains("Rook f2"));
    }

    [Fact]
    public void MentionedMovesAreParsed()
    {
        Assert.Equal(M("b5c6"), PositionFacts.MentionedMove("why not Bxc6?", Pos(PinFen)));
        Assert.Null(PositionFacts.MentionedMove("why should I move my king?", Pos(PinFen)));
    }
}
