using System.Globalization;

namespace ChessCoach.Core;

public sealed record Grounding(List<string> Lines, List<ChessPosition> Positions, List<IReadOnlyList<string>> Pvs);

public static class PositionFacts
{
    private static CultureInfo En => CoreLocale.English;

    public sealed record Played(UciMove Move, ChessPosition Before);

    public sealed record Capture(PieceColor By, char Victim, string Square);

    public sealed record Outcome(List<Played> Played, List<Capture> Captures, int Delta, string Line);

    public static (string Text, List<Played> Played) SanLine(IReadOnlyList<string> pv, ChessPosition start, int plies)
    {
        var cur = start;
        var parts = new List<string>();
        var played = new List<Played>();
        int i = 0;
        foreach (var u in pv.Take(plies))
        {
            if (UciMove.Parse(u) is not UciMove m || !cur.IsLegal(m)) break;
            string token = San.Of(m, cur);
            if (cur.Turn == PieceColor.White) token = $"{cur.FullmoveNumber}.{token}";
            else if (i == 0) token = $"{cur.FullmoveNumber}...{token}";
            parts.Add(token);
            played.Add(new Played(m, cur));
            cur = cur.Applying(m);
            i++;
        }
        return (string.Join(" ", parts), played);
    }

    public static (char Piece, string Square)? CapturedPiece(Played p)
    {
        var m = p.Move;
        if (p.Before.Board.Piece(m.ToFile, m.ToRank) is char v) return (v, UciMove.SquareName(m.ToFile, m.ToRank));
        if (p.Before.IsEnPassant(m) && p.Before.Board.Piece(m.ToFile, m.FromRank) is char pawn) return (pawn, UciMove.SquareName(m.ToFile, m.FromRank));
        return null;
    }

    public static Outcome? OutcomeOf(IReadOnlyList<string> pv, ChessPosition start, PieceColor me)
    {
        int plies = Math.Min(pv.Count, 4);
        var line = SanLine(pv, start, plies);
        if (line.Played.Count == 0) return null;
        if (CapturedPiece(line.Played[^1]) != null && pv.Count > plies)
        {
            plies = Math.Min(pv.Count, 6);
            line = SanLine(pv, start, plies);
        }
        var end = start;
        foreach (var p in line.Played) end = end.Applying(p.Move);
        var captures = new List<Capture>();
        foreach (var p in line.Played)
            if (CapturedPiece(p) is { } c) captures.Add(new Capture(p.Before.Turn, c.Piece, c.Square));
        int delta = (end.Board.Material(me) - end.Board.Material(me.Opposite())) - (start.Board.Material(me) - start.Board.Material(me.Opposite()));
        return new Outcome(line.Played, captures, delta, line.Text);
    }

    public static (string Text, int Delta)? Consequence(IReadOnlyList<string> pv, ChessPosition start, PieceColor me)
    {
        if (OutcomeOf(pv, start, me) is not { } o) return null;
        var captures = o.Captures.Select(c => $"{c.By.Label(En)} captures {Placement.ColorOf(c.Victim).Label(En)} {PieceInfo.Name(c.Victim, En)} {c.Square}").ToList();
        string net = $"Net material change for {me.Label(En)}: {o.Delta:+0;-0;+0} (pawn 1, knight or bishop 3, rook 5, queen 9)";
        string text = captures.Count == 0 ? "No material changes hands in that line." : $"In that line: {string.Join("; ", captures)}. {net}.";
        return (o.Line + ". " + text, o.Delta);
    }

    public static List<string> ThreatLines(ChessPosition pos, EngineResult? nullResult)
    {
        var output = new List<string>();
        if (pos.IsCheck || nullResult is null || nullResult.Lines.Count == 0) return output;
        var line = nullResult.Lines[0];
        var me = pos.Turn;
        var nullPos = pos.NullMove;
        if (line.Pv.Count == 0 || UciMove.Parse(line.Pv[0]) is not UciMove first || !nullPos.IsLegal(first)) return output;
        if (Consequence(line.Pv, nullPos, me) is not { } result) return output;
        var afterFirst = nullPos.Board.Applying(first);
        var known = pos.Board.Hanging(me).Select(h => h.Square).ToHashSet();
        var pinsAfter = afterFirst.Pins(me);
        var extra = new List<string>();
        foreach (var h in afterFirst.Hanging(me))
        {
            if (known.Contains(h.Square)) continue;
            string text = $"After {San.Of(first, nullPos)}, {h.HangingText(En)}";
            if (pinsAfter.FirstOrDefault(p => p.File == h.File && p.Rank == h.Rank) is { Piece: not '\0' } pin)
                text += $" and it is pinned against {PieceInfo.Name(pin.Target, En)} {pin.TargetSquare}, so it cannot move away";
            extra.Add(text + ".");
        }
        bool serious = result.Delta <= -2 || extra.Count > 0 || (line.MateIn ?? 0) > 0;
        if (!serious) return output;
        output.Add($"Opponent's threat if {me.Label(En)} makes no useful move: {result.Text}");
        if ((line.MateIn ?? 0) > 0) output.Add($"This line leads to mate in {line.MateIn}.");
        output.AddRange(extra);
        return output;
    }

    public static List<string> Reasons(ChessPosition pos, UciMove best, ThreatFact? threat)
    {
        var me = pos.Turn;
        var after = pos.Applying(best);
        var output = new List<string>();
        if (pos.Board.Piece(best.FromFile, best.FromRank) is char p && char.ToLowerInvariant(p) == 'k' && Math.Abs(best.ToFile - best.FromFile) == 2)
            output.Add($"Castling puts the king on {UciMove.SquareName(best.ToFile, best.ToRank)} behind its pawns and brings the rook into play.");
        foreach (var pin in pos.Board.Unpinned(best, me))
            output.Add($"The best move ends the pin: {Placement.ColorOf(pin.Piece).Label(En)} {PieceInfo.Name(pin.Piece, En)} {pin.Square} is no longer pinned against {PieceInfo.Name(pin.Target, En)} {pin.TargetSquare}.");
        var before = pos.Board.Pins(me.Opposite());
        foreach (var pin in after.Board.Pins(me.Opposite()))
            if (!before.Contains(pin)) output.Add($"The best move creates a pin: {pin.Text(En)}.");
        if (threat is { } t && t.IsParried(best, pos.Board)) output.Add($"The best move deals with the opponent's threat ({t.PromptText}).");
        if (pos.Board.Piece(best.ToFile, best.ToRank) is char victim)
            output.Add($"The best move captures {PieceInfo.Name(victim, En)} {UciMove.SquareName(best.ToFile, best.ToRank)}.");
        if (after.IsCheck) output.Add("The best move gives check.");
        return output;
    }

    public static List<string> PurposeLines(ChessPosition pos, UciMove best)
    {
        var me = pos.Turn;
        var output = new List<string>();
        if (pos.Board.Piece(best.FromFile, best.FromRank) is not char piece) return output;
        char kind = char.ToLowerInvariant(piece);
        var after = pos.Board.Applying(best);
        if ((kind == 'r' || kind == 'q') && best.FromFile == best.ToFile)
            output.Add($"Destination area for the hint: rank {best.ToRank + 1} (do not name the exact square).");
        else if (kind != 'k' || Math.Abs(best.ToFile - best.FromFile) != 2)
            output.Add($"Destination area for the hint: the {"abcdefgh"[best.ToFile]}-file (do not name the exact square).");
        string name = PieceInfo.Name(piece, En);
        var reach = after.Attacks(best.ToFile, best.ToRank);
        var enemy = reach.Where(t => after.Piece(t.File, t.Rank) is char p && Placement.ColorOf(p) != me)
            .Select(t => $"{PieceInfo.Name(after.Piece(t.File, t.Rank)!.Value, En)} {UciMove.SquareName(t.File, t.Rank)}").ToList();
        if (enemy.Count > 0) output.Add($"From its new square the {name} attacks: {string.Join(", ", enemy)}.");
        var enemyAttackers = after.Attackers(me.Opposite());
        var guarded = reach.Where(t => after.Piece(t.File, t.Rank) is char p && Placement.ColorOf(p) == me && char.ToLowerInvariant(p) != 'k'
                && enemyAttackers[t.Rank][t.File].Count > 0)
            .Select(t => $"{PieceInfo.Name(after.Piece(t.File, t.Rank)!.Value, En)} {UciMove.SquareName(t.File, t.Rank)}").ToList();
        if (guarded.Count > 0) output.Add($"From its new square the {name} protects the attacked {string.Join(", ", guarded)}.");
        return output;
    }

    public static string EvalDrop(EngineLine best, int other)
    {
        int drop = best.Value - other;
        if (Math.Abs(best.Value) >= 9_000 || Math.Abs(other) >= 9_000) return drop > 0 ? "clearly worse than the best move" : "as good as the best move";
        return $"{Math.Max(0, drop) / 100.0:0.0} pawns worse than the best move";
    }

    public static Grounding Build(ChessPosition pos, EngineResult? result, EngineResult? nullResult, ThreatFact? threat, bool revealBest)
    {
        var me = pos.Turn;
        string name = me.Label(En), opponent = me.Opposite().Label(En);
        var lines = new List<string>
        {
            $"Position (FEN): {pos.Fen}",
            $"{name} to move. All evaluations are from {name}'s point of view.",
            $"{name} pieces: {pos.Board.PieceList(me)}",
            $"{opponent} pieces: {pos.Board.PieceList(me.Opposite())}",
        };
        var positions = new List<ChessPosition> { pos };
        var pvs = new List<IReadOnlyList<string>>();
        if (result is { Lines.Count: > 0 }) lines.Add($"Evaluation of the position: {result.Lines[0].ScoreTextEnglish}");
        if (pos.IsCheck) lines.Add($"{name}'s king is in check.");
        var pins = pos.Board.Pins(me).Concat(pos.Board.Pins(me.Opposite())).ToList();
        if (pins.Count > 0) lines.Add("Pins: " + string.Join("; ", pins.Select(p => p.Text(En))) + ".");
        foreach (var c in new[] { me, me.Opposite() })
        {
            var hanging = pos.Board.HangingPieces(c, En);
            if (hanging.Count > 0) lines.Add($"Unprotected or threatened for {c.Label(En)}: {string.Join("; ", hanging)}.");
        }
        var checks = pos.LegalMoves().Where(m => pos.Applying(m).IsCheck).Take(5).Select(m => San.Of(m, pos)).ToList();
        if (checks.Count > 0) lines.Add($"Checks available to {name}: {string.Join(", ", checks)}.");
        var threatFacts = ThreatLines(pos, nullResult);
        if (threatFacts.Count > 0)
        {
            lines.AddRange(threatFacts);
            positions.Add(pos.NullMove);
            if (nullResult?.Lines.Count > 0) pvs.Add(nullResult.Lines[0].Pv);
        }
        if (!revealBest || result is not { Lines.Count: > 0 }) return new Grounding(lines, positions, pvs);
        var first = result.Lines[0];
        if (UciMove.Parse(result.BestMove.Length == 0 ? first.Move : result.BestMove) is not UciMove best || !pos.IsLegal(best))
            return new Grounding(lines, positions, pvs);
        lines.Add($"Best move per engine: {San.Of(best, pos)} ({pos.Board.DescribeMove(best, En, check: false)}), evaluation {first.ScoreTextEnglish}.");
        var main = SanLine(first.Pv, pos, 6);
        if (main.Text.Length > 0) lines.Add($"Expected continuation: {main.Text}.");
        pvs.Add(first.Pv);
        lines.AddRange(Reasons(pos, best, threat));
        for (int i = 1; i < result.Lines.Count; i++)
        {
            var line = result.Lines[i];
            if (UciMove.Parse(line.Move) is not UciMove m || !pos.IsLegal(m)) continue;
            var alt = SanLine(line.Pv, pos, 4);
            lines.Add($"Alternative {i}: {San.Of(m, pos)}, evaluation {line.ScoreTextEnglish}, {EvalDrop(first, line.Value)}. Line: {alt.Text}.");
            pvs.Add(line.Pv);
        }
        return new Grounding(lines, positions, pvs);
    }

    public static UciMove? MentionedMove(string question, ChessPosition pos)
    {
        string display = CoreLocale.Get("san.pieces", null);
        foreach (var raw in question.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim(' ', ',', '.', '?', '!', ';', ':', '(', ')', '"', '\'')))
        {
            if (raw.Length < 3) continue;
            bool castle = raw.ToLowerInvariant().Replace('0', 'o').StartsWith("o-o", StringComparison.Ordinal);
            bool pieceFirst = (display.Contains(raw[0]) || "KQRBN".Contains(raw[0])) && char.IsUpper(raw[0]);
            var uci = UciMove.Parse(raw);
            bool isUci = uci is { } u && pos.IsLegal(u) && raw.Length <= 5;
            if (!(castle || pieceFirst || isUci)) continue;
            if (isUci) return uci;
            if ((San.Parse(raw, pos) ?? San.Parse(raw, pos, display)) is UciMove m) return m;
        }
        return null;
    }

    public static (List<string> Lines, ChessPosition Position, IReadOnlyList<string> Pv) MentionedLines(ChessPosition pos, UciMove move, EngineResult after, EngineLine? best)
    {
        var next = pos.Applying(move);
        int mine = -after.Value;
        string san = San.Of(move, pos);
        string text = $"The player asks about {san}: after it the engine evaluates the position as {EngineLine.Text(mine, null, En)} for {pos.Turn.Label(En)}";
        if (best != null) text += $" ({EvalDrop(best, mine)})";
        var lines = new List<string> { text + "." };
        if (after.Lines.Count == 0) return (lines, next, Array.Empty<string>());
        var line = after.Lines[0];
        var reply = SanLine(line.Pv, next, 4);
        if (reply.Text.Length > 0) lines.Add($"Likely continuation after {san}: {reply.Text}.");
        if (Consequence(line.Pv, next, pos.Turn) is { Delta: <= -2 } c) lines.Add($"That costs material: {c.Text}");
        return (lines, next, line.Pv);
    }

    public static List<string> HintLines(int level, HintSet? hints, ChessPosition pos)
    {
        if (hints == null) return new();
        if (level <= 0)
            return new() { "Hint rule: the player has not asked for a hint. Do not name or hint at the best move; you may explain threats, pins and loose pieces." };
        if (level >= 4) return new() { "The best move is shown on screen as an arrow. Explain it freely." };
        var output = new List<string> { $"Hint stage {level} of 4 is on screen for the player: \"{hints.Text(level)}\"" };
        if (level >= 2 && pos.Board.Piece(hints.Move.FromFile, hints.Move.FromRank) is char p)
            output.Add($"The highlighted piece is the {PieceInfo.Name(p, En)} on {hints.Stage2Square}.");
        output.Add("Hint rule: do not name the best move or its target square and do not reveal more than the hint stage shows, unless the player asks why the hint makes sense or what it means. Then explain the concrete reason from the lines above, for example which piece it frees or which threat it stops.");
        return output;
    }
}
