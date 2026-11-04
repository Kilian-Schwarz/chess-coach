using System.Globalization;
using System.Text.Json.Serialization;

namespace ChessCoach.Core;

public static class FenReader
{
    public static (Placement Placement, PieceColor SideToMove)? Parse(string fen)
    {
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        var rows = parts[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length != 8) return null;
        var p = new Placement();
        for (int i = 0; i < 8; i++)
        {
            int file = 0;
            foreach (char ch in rows[i])
            {
                if (char.IsDigit(ch)) { file += ch - '0'; continue; }
                if ("kqrbnpKQRBNP".IndexOf(ch) < 0 || file >= 8) return null;
                p.Squares[7 - i][file] = ch;
                file++;
            }
            if (file != 8) return null;
        }
        return (p, parts[1] == "b" ? PieceColor.Black : PieceColor.White);
    }
}

public sealed record ThreatFact(UciMove Move, int Gain, string Text, string? EnglishText = null)
{
    public string PromptText => EnglishText ?? Text;


    public static ThreatFact? Detect(Placement placement, PieceColor player, int beforeValue, EngineResult? nullResult)
    {
        if (nullResult is null || string.IsNullOrEmpty(nullResult.BestMove) || UciMove.Parse(nullResult.BestMove) is not { } m) return null;
        int gain = nullResult.Value + beforeValue;
        if (gain < 150 || placement.Piece(m.FromFile, m.FromRank) == null) return null;
        return new ThreatFact(m, gain, CoreLocale.Format("threat.text", null, placement.DescribeMove(m)),
            CoreLocale.Format("threat.text", CoreLocale.English, placement.DescribeMove(m, CoreLocale.English)));
    }

    public bool IsParried(UciMove answer, Placement placement)
    {
        var after = placement.Applying(answer);
        var victim = placement.Piece(Move.ToFile, Move.ToRank);
        bool attackerStays = after.Piece(Move.FromFile, Move.FromRank) == placement.Piece(Move.FromFile, Move.FromRank);
        bool targetOk = after.Piece(Move.ToFile, Move.ToRank) != null || victim == null;
        if (!attackerStays || !targetOk) return true;
        if (victim != null && after.Piece(Move.ToFile, Move.ToRank) != victim) return true;
        return !after.Attacks(Move.FromFile, Move.FromRank).Contains((Move.ToFile, Move.ToRank));
    }
}

public enum HintTheme { Mate, CaptureGain, Fork, Check, ParryThreat, Castle, Develop, KingActivate, PawnMove, ImprovePiece }

public static class HintThemeExt
{
    public static readonly HintTheme[] All = Enum.GetValues<HintTheme>();

    public static string Raw(this HintTheme t) { var n = t.ToString(); return char.ToLowerInvariant(n[0]) + n[1..]; }

    public static bool TryParse(string raw, out HintTheme t)
    {
        foreach (var x in All) if (x.Raw() == raw) { t = x; return true; }
        t = default; return false;
    }

    public static string Title(this HintTheme t, CultureInfo? culture = null) => CoreLocale.Get("theme." + t.Raw(), culture);

    public static string Stage1Text(this HintTheme t, CultureInfo? culture = null) => CoreLocale.Get("hint.stage1." + t.Raw(), culture);
}

public sealed record HintSet(UciMove Move, HintTheme Theme, ThreatFact? Threat, string Stage1, string Stage2Square, string Stage2,
    string Stage3Fallback, string Stage3Prompt, string Stage4)
{
    public string Text(int level) => level switch { 1 => Stage1, 2 => Stage2, 3 => Stage3Fallback, _ => Stage4 };
}

public static class Hints
{
    public static HintTheme Theme(Placement placement, UciMove best, PieceColor player, ThreatFact? threat = null, bool bestMate = false)
    {
        if (placement.Piece(best.FromFile, best.FromRank) is not char mover) return HintTheme.ImprovePiece;
        char kind = char.ToLowerInvariant(mover);
        var after = placement.Applying(best);
        var opp = player.Opposite();
        bool givesCheck = after.IsInCheck(opp);
        if (bestMate) return HintTheme.Mate;
        var enemyDefenders = after.Attackers(opp);
        if (placement.Piece(best.ToFile, best.ToRank) is char victim && Placement.ColorOf(victim) == opp && char.ToLowerInvariant(victim) != 'k')
        {
            if (PieceInfo.Value(victim) > PieceInfo.Value(mover) || enemyDefenders[best.ToRank][best.ToFile].Count == 0) return HintTheme.CaptureGain;
        }
        int forkTargets = 0;
        foreach (var (f, r) in after.Attacks(best.ToFile, best.ToRank))
        {
            if (after.Piece(f, r) is not char t || Placement.ColorOf(t) != opp) continue;
            char tk = char.ToLowerInvariant(t);
            if (tk == 'k' || PieceInfo.Value(t) > PieceInfo.Value(mover) || enemyDefenders[r][f].Count == 0) forkTargets++;
        }
        if (forkTargets >= 2 && kind != 'k') return HintTheme.Fork;
        if (givesCheck) return HintTheme.Check;
        if (threat != null && threat.IsParried(best, placement)) return HintTheme.ParryThreat;
        if (kind == 'k' && Math.Abs(best.ToFile - best.FromFile) == 2) return HintTheme.Castle;
        int nonKings = placement.PieceCount - 2;
        int homeRank = player == PieceColor.White ? 0 : 7;
        if ((kind == 'n' || kind == 'b') && best.FromRank == homeRank && nonKings > 12) return HintTheme.Develop;
        if (kind == 'k' && nonKings <= 12) return HintTheme.KingActivate;
        if (kind == 'p') return HintTheme.PawnMove;
        return HintTheme.ImprovePiece;
    }

    public static string Stage2Text(char piece, string square, CultureInfo? culture = null) =>
        CoreLocale.Format("hint.stage2." + PieceInfo.Key(piece), culture, square);

    public static string Stage3Fallback(HintTheme theme, char piece, CultureInfo? culture = null) =>
        CoreLocale.Format("hint.stage3." + theme.Raw(), culture, PieceInfo.Your(piece, culture));

    public static bool RevealsTarget(string answer, UciMove move) =>
        answer.ToLowerInvariant().Contains(UciMove.SquareName(move.ToFile, move.ToRank));

    public static HintSet? Build(Placement placement, PieceColor player, EngineResult result, EngineResult? threatResult = null,
        PlayerContext? context = null)
    {
        if (result.Lines.Count == 0) return null;
        var first = result.Lines[0];
        if (UciMove.Parse(result.BestMove.Length == 0 ? first.Move : result.BestMove) is not { } best
            || placement.Piece(best.FromFile, best.FromRank) is not char piece) return null;
        var en = CoreLocale.English;
        var threat = ThreatFact.Detect(placement, player, result.Value, threatResult);
        var theme = Theme(placement, best, player, threat, (first.MateIn ?? 0) > 0);
        var from = UciMove.SquareName(best.FromFile, best.FromRank);
        var side = player.Label(en);

        var facts = new List<string>
        {
            $"Position (FEN): {placement.Fen(player)}",
            $"{side} to move. Evaluation from {side}'s point of view: {first.ScoreTextEnglish}.",
            $"Theme of the best move: {theme.Title(en)}.",
            $"The piece that should move: {PieceInfo.Name(piece, en)} on {from}.",
        };
        if (threat != null) facts.Add($"{threat.PromptText}.");
        var hanging = placement.HangingPieces(player.Opposite(), en);
        if (hanging.Count > 0) facts.Add($"Hanging for the opponent: {string.Join("; ", hanging)}.");
        var mine = placement.HangingPieces(player, en);
        if (mine.Count > 0) facts.Add($"Hanging for {side}: {string.Join("; ", mine)}.");
        if (placement.Applying(best).IsInCheck(player.Opposite())) facts.Add("The best move gives check.");
        var prompt = string.Join("\n", facts) + $"\n\nGive the {side} player a one-sentence hint without naming the target square.";

        var solution = CoreLocale.Format("hint.stage4", null, placement.DescribeMove(best), first.ScoreText);
        return new HintSet(best, theme, threat, theme.Stage1Text(), from, Stage2Text(piece, from),
            Stage3Fallback(theme, piece), prompt, solution);
    }
}
