using System.Globalization;

namespace ChessCoach.Core;

public enum UnsafeReason { Undefended, CheaperAttacker, LeavesHanging }

public sealed record UnsafeSquare(int File, int Rank, UnsafeReason Reason, string? OtherSquare = null, char? OtherPiece = null)
{
    public string Square => UciMove.SquareName(File, Rank);

    public string Text(char piece, CultureInfo? culture = null)
    {
        string name = PieceInfo.Name(piece, culture);
        return Reason switch
        {
            UnsafeReason.Undefended => CoreLocale.Format("lift.undefended", culture, name, Square),
            UnsafeReason.CheaperAttacker => CoreLocale.Format("lift.cheaper", culture, name, Square),
            _ => CoreLocale.Format("lift.leaves", culture, name, Square, PieceInfo.Name(OtherPiece ?? 'p', culture), OtherSquare ?? ""),
        };
    }
}

public sealed record LiftedPiece(int File, int Rank, char Piece)
{
    public string Square => UciMove.SquareName(File, Rank);

    public static LiftedPiece? Detect(ChessPosition last, Placement current)
    {
        LiftedPiece? missing = null;
        int missingCount = 0, disturbed = 0;
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
            {
                if (last.Board.Squares[r][f] == current.Squares[r][f]) continue;
                if (last.Board.Squares[r][f] is char p && current.Squares[r][f] == null && Placement.ColorOf(p) == last.Turn)
                {
                    missing = new LiftedPiece(f, r, p);
                    missingCount++;
                }
                else disturbed++;
            }
        if (missingCount != 1 || disturbed > 1) return null;
        if (last.LegalMoves().Any(m => last.Applying(m).Board.Equals(current))) return null;
        return missing;
    }

    public static string? Summary(IReadOnlyList<UnsafeSquare> unsafeSquares, char piece, CultureInfo? culture = null) =>
        unsafeSquares.Count switch
        {
            0 => null,
            1 => unsafeSquares[0].Text(piece, culture),
            var n => CoreLocale.Format("lift.many", culture, n, PieceInfo.Name(piece, culture)),
        };

    public List<UnsafeSquare> UnsafeSquares(ChessPosition pos)
    {
        var output = new List<UnsafeSquare>();
        if (pos.Board.Squares[Rank][File] != Piece || Placement.ColorOf(Piece) != pos.Turn) return output;
        var color = pos.Turn;
        var before = pos.Board.Hanging(color).Select(h => h.Square).ToHashSet();
        foreach (var m in pos.LegalMoves().Where(m => m.FromFile == File && m.FromRank == Rank))
        {
            var after = pos.Board.Applying(m);
            if (after.Piece(m.ToFile, m.ToRank) is not char placed) continue;
            string target = UciMove.SquareName(m.ToFile, m.ToRank);
            int gain = pos.IsEnPassant(m) ? 1 : pos.Board.Piece(m.ToFile, m.ToRank) is char c ? PieceInfo.Value(c) : 0;
            int mine = PieceInfo.Value(placed);
            var attackers = after.Attackers(color.Opposite())[m.ToRank][m.ToFile];
            bool defended = after.Attackers(color)[m.ToRank][m.ToFile].Count > 0;
            UnsafeSquare? found = null;
            if (char.ToLowerInvariant(placed) != 'k' && attackers.Count > 0 && gain < mine)
            {
                if (!defended) found = new UnsafeSquare(m.ToFile, m.ToRank, UnsafeReason.Undefended);
                else if (attackers.Min(a => PieceInfo.Value(a.Piece)) < mine) found = new UnsafeSquare(m.ToFile, m.ToRank, UnsafeReason.CheaperAttacker);
            }
            if (found == null && after.Hanging(color).FirstOrDefault(h => !before.Contains(h.Square) && h.Square != target) is { Piece: not '\0' } h)
                found = new UnsafeSquare(m.ToFile, m.ToRank, UnsafeReason.LeavesHanging, h.Square, h.Piece);
            if (found != null) output.Add(found);
        }
        return output.GroupBy(u => u.Square).Select(g => g.First()).ToList();
    }
}
