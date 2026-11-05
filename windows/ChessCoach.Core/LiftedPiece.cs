using System.Globalization;

namespace ChessCoach.Core;

public enum UnsafeReason { Attacked, LeavesHanging }

public sealed record UnsafeSquare(int File, int Rank, UnsafeReason Reason, string OtherSquare, char OtherPiece)
{
    public string Square => UciMove.SquareName(File, Rank);

    public string Text(char piece, CultureInfo? culture = null)
    {
        string name = PieceInfo.Name(piece, culture), other = PieceInfo.Name(OtherPiece, culture);
        return Reason == UnsafeReason.Attacked
            ? CoreLocale.Format("lift.taken", culture, name, Square, other, OtherSquare)
            : CoreLocale.Format("lift.leaves", culture, name, Square, other, OtherSquare);
    }
}

public sealed record LiftedPiece(int File, int Rank, char Piece)
{
    public string Square => UciMove.SquareName(File, Rank);

    public static LiftedPiece? Detect(ChessPosition last, Placement current, bool held = false)
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
        if (!held && last.LegalMoves().Any(m => last.Applying(m).Board.Equals(current))) return null;
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
        foreach (var m in pos.LegalMoves().Where(m => m.FromFile == File && m.FromRank == Rank))
        {
            var after = pos.Board.Applying(m);
            if (after.Piece(m.ToFile, m.ToRank) is not char placed) continue;
            int gain = pos.IsEnPassant(m) ? 1 : pos.Board.Piece(m.ToFile, m.ToRank) is char c ? PieceInfo.Value(c) : 0;
            UnsafeSquare? found = null;
            if (char.ToLowerInvariant(placed) != 'k' && after.ExchangeLoss(m.ToFile, m.ToRank, color) is { } lost && lost.Loss > gain)
                found = new UnsafeSquare(m.ToFile, m.ToRank, UnsafeReason.Attacked, UciMove.SquareName(lost.Attacker.File, lost.Attacker.Rank), lost.Attacker.Piece);
            if (found == null)
            {
                for (int r = 0; r < 8; r++)
                    for (int f = 0; f < 8; f++)
                    {
                        if (after.Piece(f, r) is not char other || Placement.ColorOf(other) != color || char.ToLowerInvariant(other) == 'k') continue;
                        if (f == m.ToFile && r == m.ToRank) continue;
                        if ((after.ExchangeLoss(f, r, color)?.Loss ?? 0) > 0 && (pos.Board.ExchangeLoss(f, r, color)?.Loss ?? 0) == 0)
                            found = new UnsafeSquare(m.ToFile, m.ToRank, UnsafeReason.LeavesHanging, UciMove.SquareName(f, r), other);
                    }
            }
            if (found != null) output.Add(found);
        }
        return output.GroupBy(u => u.Square).Select(g => g.First()).ToList();
    }
}

public static class ExchangeExtensions
{
    public static (int Loss, Tactics.Attacker Attacker)? ExchangeLoss(this Placement board, int file, int rank, PieceColor color)
    {
        if (board.Piece(file, rank) is not char target) return null;
        var attackers = board.Attackers(color.Opposite())[rank][file].OrderBy(a => PieceInfo.Value(a.Piece)).ToList();
        if (attackers.Count == 0) return null;
        var defenders = board.Attackers(color)[rank][file].Select(a => PieceInfo.Value(a.Piece)).OrderBy(v => v).ToList();
        int loss = Exchange(PieceInfo.Value(target), attackers.Select(a => PieceInfo.Value(a.Piece)).ToList(), defenders);
        return loss > 0 ? (loss, attackers[0]) : null;
    }

    private static int Exchange(int onSquare, List<int> attacking, List<int> defending) =>
        attacking.Count == 0 ? 0 : Math.Max(0, onSquare - Exchange(attacking[0], defending, attacking.GetRange(1, attacking.Count - 1)));
}
