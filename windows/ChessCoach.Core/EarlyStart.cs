using System.Numerics;

namespace ChessCoach.Core;

public static class EarlyStart
{
    public const int MaxPlies = 4;

    public static (bool WhiteAtBottom, Placement Board)? Match(bool[][] occupied, bool[][] bright) =>
        MatchPosition(occupied, bright) is { } m ? (m.WhiteAtBottom, m.Position.Board) : null;

    public static (bool WhiteAtBottom, ChessPosition Position)? MatchPosition(bool[][] occupied, bool[][] bright)
    {
        (bool WhiteAtBottom, ChessPosition Position)? found = null;
        foreach (bool whiteAtBottom in new[] { true, false })
        {
            ulong occ = 0, white = 0;
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                {
                    if (!occupied[r][c]) continue;
                    var (file, rank) = Placement.ChessSquare(c, r, whiteAtBottom);
                    ulong bit = 1UL << (rank * 8 + file);
                    occ |= bit;
                    if (bright[r][c]) white |= bit;
                }
            var hits = new List<ChessPosition>();
            Search(ChessPosition.Start, MaxPlies, occ, white, hits);
            foreach (var hit in hits)
            {
                if (found is { } f && (f.WhiteAtBottom != whiteAtBottom || !f.Position.Board.Equals(hit.Board))) return null;
                found = (whiteAtBottom, hit);
            }
        }
        return found;
    }

    private static (ulong Occ, ulong White) Masks(Placement board)
    {
        ulong occ = 0, white = 0;
        for (int rank = 0; rank < 8; rank++)
            for (int file = 0; file < 8; file++)
            {
                if (board.Squares[rank][file] is not { } p) continue;
                ulong bit = 1UL << (rank * 8 + file);
                occ |= bit;
                if (char.IsUpper(p)) white |= bit;
            }
        return (occ, white);
    }

    private static void Search(ChessPosition pos, int plies, ulong occ, ulong white, List<ChessPosition> hits)
    {
        if (plies == 0 || BitOperations.PopCount(Masks(pos.Board).Occ ^ occ) > 2 * plies) return;
        foreach (var move in pos.LegalMoves())
        {
            var next = pos.Applying(move);
            var m = Masks(next.Board);
            if (m.Occ == occ && m.White == white) hits.Add(next);
            Search(next, plies - 1, occ, white, hits);
        }
    }
}
