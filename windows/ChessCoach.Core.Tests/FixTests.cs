using System.Globalization;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class CoachingFixTests
{
    const string PinFen = "r1b1kbnr/pp2qppp/2n5/1B1pN3/3P4/8/PPP2PPP/RNBQK2R w KQkq - 0 7";
    const string DiscFen = "r1b1k1nr/pp2bppp/2n1q3/1B1pN3/3P4/8/PPP2PPP/RNBQR1K1 w kq - 7 9";
    const string MateFen = "6k1/5ppp/8/8/8/8/8/R3K3 w - - 0 1";
    static readonly CultureInfo De = new("de");

    static ChessPosition Pos(string fen) => ChessPosition.FromFen(fen)!;
    static UciMove M(string s) => UciMove.Parse(s)!.Value;
    static EngineLine Line(string move, int cp, params string[] pv) => new(move, cp, null, pv);

    static (HintSet Hints, Grounding Facts, Grounding Hidden) PinCase()
    {
        var pos = Pos(PinFen);
        var result = new EngineResult(PinFen, "e1g1", new[] { Line("e1g1", 550, "e1g1", "d8c7", "c2c4"), Line("c2c4", 410, "c2c4", "a7a6") });
        var nullResult = new EngineResult(pos.NullMove.Fen, "f7f6", new[] { Line("f7f6", 540, "f7f6", "e1g1", "f6e5", "f1e1") });
        var hints = Hints.Build(pos.Board, PieceColor.White, result, nullResult)!;
        return (hints, PositionFacts.Build(pos, result, nullResult, hints.Threat, true), PositionFacts.Build(pos, result, nullResult, hints.Threat, false));
    }

    static (HintSet Hints, Grounding Facts) DiscCase()
    {
        var pos = Pos(DiscFen);
        var result = new EngineResult(DiscFen, "e5c6", new[] { Line("e5c6", 980, "e5c6", "b7c6", "e1e6", "c8e6") });
        var hints = Hints.Build(pos.Board, PieceColor.White, result)!;
        return (hints, PositionFacts.Build(pos, result, null, null, true));
    }

    [Fact]
    public void LevelZeroThreatLineWithholdsTheAnswer()
    {
        var (_, facts, hidden) = PinCase();
        string full = string.Join("\n", facts.Lines).Split('\n').First(l => l.StartsWith("Opponent's threat"));
        string limited = string.Join("\n", hidden.Lines).Split('\n').First(l => l.StartsWith("Opponent's threat"));
        Assert.Contains("O-O", full);
        Assert.Contains("7...f6", limited);
        Assert.DoesNotContain("O-O", limited);
        Assert.DoesNotContain("8.", limited);
        Assert.DoesNotContain("Best move per engine", string.Join("\n", hidden.Lines));
    }

    [Fact]
    public void PinHintNamesThePinAndDiscoveredFactsCarryTheReason()
    {
        var hints = PinCase().Hints;
        Assert.Equal(HintTheme.Unpin, hints.Theme);
        string hint3 = PuzzleCoach.Hint3(hints.Theme, hints.Move, Pos(PinFen));
        Assert.Contains("Pinned: Knight e5 against King e1, by Queen e7.", hint3);
        Assert.DoesNotContain("..", hints.Stage3Prompt);
        var (disc, facts) = DiscCase();
        Assert.Contains("Moving the Knight uncovers an attack by Rook e1 on Queen e6", string.Join("\n", facts.Lines));
        Assert.Contains("uncovers an attack by Rook e1 on Queen e6", disc.Stage3Prompt);
        Assert.DoesNotContain("captures Knight c6", disc.Stage3Prompt);
    }

    [Fact]
    public void PossessiveIsLowerCaseMidSentence()
    {
        Assert.Equal("When your knight moves, a piece behind it suddenly attacks something valuable.", Hints.Stage3Fallback(HintTheme.Discovered, 'N', CoreLocale.English));
        Assert.Contains("wenn dein springer", Hints.Stage3Fallback(HintTheme.Discovered, 'N', De).ToLowerInvariant());
        Assert.StartsWith("Wenn dein Springer zieht", Hints.Stage3Fallback(HintTheme.Discovered, 'N', De));
    }

    [Fact]
    public void ForcedMateAndLineWordingsAreTruthful()
    {
        var board = Pos(MateFen).Board;
        Assert.Equal(HintVariant.Standard, Hints.Variant(HintTheme.Mate, board, PieceColor.White, M("a1a8")));
        Assert.Equal(HintVariant.ForcedMate, Hints.Variant(HintTheme.Mate, board, PieceColor.White, M("e1d2")));
        Assert.Equal("You have a forced mate.", HintTheme.Mate.Stage1Text(CoreLocale.English, HintVariant.ForcedMate));
        Assert.Equal("You can checkmate your opponent here.", HintTheme.Mate.Stage1Text(CoreLocale.English));
        Assert.Equal("Dein Springer leitet ein erzwungenes Matt ein.", Hints.Stage3Fallback(HintTheme.Mate, 'N', De, HintVariant.ForcedMate));
        Assert.Equal("Du hast ein erzwungenes Matt.", HintTheme.Mate.Stage1Text(De, HintVariant.ForcedMate));
        Assert.Equal(HintVariant.Line, Hints.Variant(HintTheme.CaptureGain, board, PieceColor.White, M("e1d2")));
        Assert.NotEqual(Hints.Stage3Fallback(HintTheme.CaptureGain, 'N', CoreLocale.English), Hints.Stage3Fallback(HintTheme.CaptureGain, 'N', CoreLocale.English, HintVariant.Line));
    }

    [Fact]
    public void WinningMaterialOutranksTheThreat()
    {
        var pos = Pos("rnb1kbnr/pppp1ppp/8/8/4P2q/5N2/PPPP1PPP/RNBQKB1R w KQkq - 0 1");
        var threat = new ThreatFact(M("h4e4"), 300, "t", "t", new List<PieceAt> { new('P', "e4") });
        Assert.True(Hints.OutranksThreat(threat, pos, new[] { "f3h4" }));
        Assert.False(Hints.OutranksThreat(threat, pos, new[] { "a2a3" }));
        Assert.Equal("Dein Gegner hat eine Drohung, aber du kannst zuerst selbst Material gewinnen.", Hints.OpportunityText(De));
    }

    [Fact]
    public void BlockingAQuietThreatSquareParriesIt()
    {
        var pos = Pos("4k3/8/8/8/8/3q4/8/R3K2R w - - 0 1");
        Assert.True(new ThreatFact(M("d3d1"), 300, "t").IsParried(M("a1d1"), pos.Board));
    }

    [Fact]
    public void FactCheckVerifiesCurrentPositionNamedMovesAndProse()
    {
        var allowed = FactCheck.Allowed(new[] { Pos(DiscFen) }, new IReadOnlyList<string>[] { new[] { "e5c6", "b7c6", "e1e6" } });
        Assert.Null(FactCheck.Clean("The queen on d1 is attacking the rook on a8.", allowed).Text);
        Assert.Null(FactCheck.Clean("The queen on d1 hits the rook on a8.", allowed).Text);
        Assert.Null(FactCheck.Clean("The queen on d1 puts pressure on the rook on a8.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("The bishop on b5 is attacking the knight on c6.", allowed).Text);
        Assert.Null(FactCheck.Clean("Your rook on e1 threatens the queen on e6 right now.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("After Nxc6, the rook on e1 attacks the queen on e6.", allowed).Text);
        Assert.Null(FactCheck.Clean("After Nxc6, the rook on e1 attacks the queen on a8.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("When the knight moves, the rook on e1 attacks the queen on e6.", allowed).Text);
        Assert.Null(FactCheck.Clean("Der Turm auf e1 greift den Bauern auf f7 an.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("Der Läufer auf b5 greift den Springer auf c6 an.", allowed).Text);
        Assert.Null(FactCheck.Clean("Nxc6 gives check and wins the knight.", allowed).Text);
        Assert.Null(FactCheck.Clean("Nxc6 is checkmate.", allowed).Text);
        Assert.Null(FactCheck.Clean("That is checkmate.", allowed).Text);
        Assert.NotNull(FactCheck.Clean("Check whether your knight is pinned.", allowed).Text);
        var pin = FactCheck.Allowed(new[] { Pos(PinFen) }, new IReadOnlyList<string>[] { new[] { "e1g1" } });
        Assert.NotNull(FactCheck.Clean("Bxc6+ gives check.", pin).Text);
        var mate = FactCheck.Allowed(new[] { Pos(MateFen) }, new IReadOnlyList<string>[] { new[] { "a1a8" } });
        Assert.NotNull(FactCheck.Clean("Ra8 is checkmate.", mate).Text);
        Assert.NotNull(FactCheck.Clean("The move gives check.", mate).Text);
    }

    [Fact]
    public void WithholdingDropsSentencesThatNameTheBestMove()
    {
        var kept = FactCheck.Withholding("Castling is wise. Play O-O now. The pin on e5 matters. Move to g1.", M("e1g1"), "O-O");
        Assert.Equal("Castling is wise. The pin on e5 matters.", kept.Text);
        Assert.Equal(2, kept.Removed);
    }

    [Fact]
    public void GermanTextsReadNaturally()
    {
        var lesson = PuzzleCoach.Lesson(new Puzzle
        {
            Id = "d", Fen = DiscFen, AcceptedMoves = new() { "e5c6" }, BestLine = new() { "e5c6", "b7c6", "e1e6", "f7e6" }, Theme = "discovered", ThemeChecked = true,
        }, De);
        Assert.Contains("Der Springer auf e5 zieht weg und gibt die Linie frei, sodass der Turm auf e1 jetzt die Dame auf e6 angreift.", lesson.Why);
        Assert.Contains("Material im Wert von 4 Bauern", lesson.Why);
        Assert.Contains("Welche Schachgebote", PuzzleCoach.Question(HintTheme.Mate, De));
        Assert.Equal("Dein Gegner hat eine Drohung. Dein Läufer kann sie abwehren.", Hints.Stage3Fallback(HintTheme.ParryThreat, 'B', De));
        Assert.Equal("Welche gegnerischen Figuren stehen gerade nicht sicher?", PuzzleCoach.Question(HintTheme.Fork, De));
    }

    [Fact]
    public void PluralsAndDecimalSeparatorsFollowTheLanguage()
    {
        var pawn = new Puzzle { Id = "p", Fen = "4k3/8/8/3p4/8/4N3/8/4K3 w - - 0 1", AcceptedMoves = new() { "e3d5" }, BestLine = new() { "e3d5" }, Theme = "pawnMove" };
        Assert.Contains("win 1 point of material", PuzzleCoach.Lesson(pawn).Why);
        Assert.DoesNotContain("1 points", PuzzleCoach.Lesson(pawn).Why);
        Assert.Contains("im Wert von einem Bauern", PuzzleCoach.Lesson(pawn, De).Why);
        Assert.Equal("+1,5", PuzzleCoach.Evaluation(150, De));
        Assert.Equal("−1.5", PuzzleCoach.Evaluation(-150, CoreLocale.English));
        Assert.Equal("+5,7", EngineLine.Text(570, null, De));
        Assert.Equal("+5.7", EngineLine.Text(570, null, CoreLocale.English));
        var fail = PuzzleCoach.FailReason(new Puzzle { Fen = DiscFen }, Pos(DiscFen).Applying(M("b5c6")), new[] { "b7c6", "e1e6" }, De);
        Assert.Contains("Dein Gegner antwortet mit bxc6", fail);
        Assert.Contains("3 Punkte Material", fail);
    }

    [Fact]
    public void RussianAndUkrainianParryThreatAreGrammatical()
    {
        Assert.Contains("есть угроза", CoreLocale.Get("hint.stage3.parryThreat", new CultureInfo("ru")));
        Assert.Contains("є загроза", CoreLocale.Get("hint.stage3.parryThreat", new CultureInfo("uk")));
    }
}
