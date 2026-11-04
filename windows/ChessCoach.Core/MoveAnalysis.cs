namespace ChessCoach.Core;

public sealed record SkillObservation(Skill Skill, double Value);

public sealed record MoveAnalysisInput(
    MoveReview Review,

    EngineResult BeforeResult,

    EngineResult? AfterResult,

    EngineResult? ThreatResult,

    int MoveNumber);

public enum GamePhase { Opening, Middlegame, Endgame }

public sealed class MoveAnalysis
{
    public List<SkillObservation> Observations { get; init; } = new();
    public MistakeType? MistakeType { get; init; }

    public string? Example { get; init; }
    public HintTheme? Theme { get; init; }
    public ThreatFact? Threat { get; init; }
    public double Accuracy { get; init; }
    public int Loss { get; init; }
    public GamePhase Phase { get; init; }
}

public static class MoveAnalyzer
{
    public static GamePhase Phase(Placement p, int moveNumber)
    {
        if (p.PieceCount - 2 <= 12) return GamePhase.Endgame;
        return moveNumber <= 10 ? GamePhase.Opening : GamePhase.Middlegame;
    }

    private static HashSet<string> SquareKeys(List<string> list) =>
        new(list.Select(s => string.Join(" ", s.Split(' ').Take(2))));

    public static int PawnShield(Placement p, PieceColor color)
    {
        if (p.KingSquare(color) is not { } k) return 0;
        char pawn = color == PieceColor.White ? 'P' : 'p';
        int dir = color == PieceColor.White ? 1 : -1;
        int n = 0;
        for (int f = Math.Max(0, k.File - 1); f <= Math.Min(7, k.File + 1); f++)
            for (int step = 1; step <= 2; step++)
                if (p.Piece(f, k.Rank + dir * step) == pawn) n++;
        return n;
    }

    public static MoveAnalysis Analyze(MoveAnalysisInput input)
    {
        var r = input.Review; var me = r.Player;
        int beforeValue = input.BeforeResult.Value;
        int loss = r.Loss;
        double acc = Core.Accuracy.MoveAccuracyFromValues(beforeValue, beforeValue - loss);
        var after = r.Before.Applying(r.Played);
        var ph = Phase(r.Before, input.MoveNumber);
        var obs = new List<SkillObservation>();

        var hangingBefore = SquareKeys(r.Before.HangingPieces(me));
        var hangingAfter = SquareKeys(after.HangingPieces(me));
        bool newHanging = hangingAfter.Except(hangingBefore).Any();
        bool leftHanging = loss >= 50 && newHanging;
        obs.Add(new SkillObservation(Skill.PieceSafety, leftHanging ? 0 : 1));

        var threat = ThreatFact.Detect(r.Before, me, beforeValue, input.ThreatResult);
        if (threat != null) obs.Add(new SkillObservation(Skill.Threats, loss < 50 ? 1 : 0));

        var lines = input.BeforeResult.Lines;
        bool tacticAvailable = lines.Count >= 2 && lines[0].Value - lines[1].Value >= 150;
        bool foundBest = r.Best?.Uci == r.Played.Uci;
        if (tacticAvailable) obs.Add(new SkillObservation(Skill.Tactics, foundBest ? 1 : 0));

        double accValue = acc / 100;
        switch (ph)
        {
            case GamePhase.Opening: obs.Add(new SkillObservation(Skill.Opening, accValue)); break;
            case GamePhase.Middlegame: obs.Add(new SkillObservation(Skill.Middlegame, accValue)); break;
            default: obs.Add(new SkillObservation(Skill.Endgame, accValue)); break;
        }

        char? movedPiece = r.Before.Piece(r.Played.FromFile, r.Played.FromRank);
        char? kind = movedPiece is char mp ? char.ToLowerInvariant(mp) : null;
        bool castled = kind == 'k' && Math.Abs(r.Played.ToFile - r.Played.FromFile) == 2;
        bool weakened = false;
        if (kind == 'p' && r.Before.KingSquare(me) is { } ks && Math.Abs(r.Played.FromFile - ks.File) <= 1
            && Math.Abs(r.Played.FromRank - ks.Rank) <= 2 && PawnShield(after, me) < PawnShield(r.Before, me)) weakened = true;
        bool allowsMate = (input.AfterResult?.MateIn ?? 0) > 0;
        if (castled) obs.Add(new SkillObservation(Skill.KingSafety, 1));
        else if (weakened) obs.Add(new SkillObservation(Skill.KingSafety, loss >= 100 ? 0 : 1));
        else if (allowsMate && loss >= 100) obs.Add(new SkillObservation(Skill.KingSafety, 0));

        MistakeType? mistake = null;
        if (loss > 60)
        {
            if (newHanging && loss >= 100) mistake = Core.MistakeType.HangingPiece;
            else if (threat != null && loss >= 50) mistake = Core.MistakeType.MissedThreat;
            else if (tacticAvailable && !foundBest) mistake = Core.MistakeType.MissedTactic;
            else if ((weakened || allowsMate) && loss >= 100) mistake = Core.MistakeType.KingSafety;
            else if (ph == GamePhase.Opening) mistake = Core.MistakeType.OpeningPrinciple;
            else if (ph == GamePhase.Endgame) mistake = Core.MistakeType.EndgameTechnique;
            else mistake = Core.MistakeType.Other;
        }
        HintTheme? theme = null;
        if (r.Best is { } best)
            theme = Hints.Theme(r.Before, best, me, threat, (lines.Count > 0 ? lines[0].MateIn ?? 0 : 0) > 0);
        string lossText = Fmt.Decimal1(loss / 100.0);
        return new MoveAnalysis
        {
            Observations = obs, MistakeType = mistake,
            Example = mistake == null ? null : CoreLocale.Format("mistake.example", null, r.PlayedText, lossText),
            Theme = theme, Threat = threat, Accuracy = acc, Loss = loss, Phase = ph,
        };
    }
}
