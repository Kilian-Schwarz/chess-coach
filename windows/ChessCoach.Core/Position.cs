using System.Globalization;
using System.Text;

namespace ChessCoach.Core;

public enum PieceColor { White, Black }

public static class PieceColorExt
{
    public static PieceColor Opposite(this PieceColor c) => c == PieceColor.White ? PieceColor.Black : PieceColor.White;
    public static string Label(this PieceColor c, CultureInfo? culture = null) => CoreLocale.Get(c == PieceColor.White ? "color.white" : "color.black", culture);
}

public sealed class Placement : IEquatable<Placement>
{
    public char?[][] Squares { get; }

    public Placement()
    {
        Squares = new char?[8][];
        for (int i = 0; i < 8; i++) Squares[i] = new char?[8];
    }

    public Placement(char?[][] screen, bool whiteAtBottom) : this()
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var (file, rank) = ChessSquare(c, r, whiteAtBottom);
                Squares[rank][file] = screen[r][c];
            }
    }

    public Placement Clone()
    {
        var p = new Placement();
        for (int r = 0; r < 8; r++) Array.Copy(Squares[r], p.Squares[r], 8);
        return p;
    }

    public static (int File, int Rank) ChessSquare(int col, int row, bool whiteAtBottom) =>
        whiteAtBottom ? (col, 7 - row) : (7 - col, row);

    public static (int Col, int Row) ScreenSquare(int file, int rank, bool whiteAtBottom) =>
        whiteAtBottom ? (file, 7 - rank) : (7 - file, rank);

    public static PieceColor ColorOf(char p) => char.IsUpper(p) ? PieceColor.White : PieceColor.Black;

    public int Count(char p)
    {
        int n = 0;
        foreach (var row in Squares) foreach (var s in row) if (s == p) n++;
        return n;
    }

    public int MissingPieces(Placement other)
    {
        int missing = 0;
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
            {
                if (other.Squares[r][f] is not { } o) { if (Squares[r][f] != null) missing++; continue; }
                if (Squares[r][f] != o) return 0;
            }
        return missing;
    }

    public int PieceCount
    {
        get { int n = 0; foreach (var row in Squares) foreach (var s in row) if (s != null) n++; return n; }
    }

    public bool IsStartPosition
    {
        get
        {
            const string back = "RNBQKBNR";
            for (int f = 0; f < 8; f++)
                if (Squares[0][f] != back[f] || Squares[1][f] != 'P' || Squares[6][f] != 'p'
                    || Squares[7][f] != char.ToLowerInvariant(back[f])) return false;
            for (int r = 2; r <= 5; r++) for (int f = 0; f < 8; f++) if (Squares[r][f] != null) return false;
            return true;
        }
    }

    public bool Equals(Placement? other)
    {
        if (other is null) return false;
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++) if (Squares[r][f] != other.Squares[r][f]) return false;
        return true;
    }
    public override bool Equals(object? obj) => Equals(obj as Placement);
    public override int GetHashCode()
    {
        var h = new HashCode();
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++) h.Add(Squares[r][f]);
        return h.ToHashCode();
    }

    public string Fen(PieceColor sideToMove)
    {
        var rows = new List<string>();
        for (int rank = 7; rank >= 0; rank--)
        {
            var row = new StringBuilder(); int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                if (Squares[rank][file] is char p)
                {
                    if (empty > 0) { row.Append(empty); empty = 0; }
                    row.Append(p);
                }
                else empty++;
            }
            if (empty > 0) row.Append(empty);
            rows.Add(row.ToString());
        }
        string castling = "";
        if (Squares[0][4] == 'K')
        {
            if (Squares[0][7] == 'R') castling += "K";
            if (Squares[0][0] == 'R') castling += "Q";
        }
        if (Squares[7][4] == 'k')
        {
            if (Squares[7][7] == 'r') castling += "k";
            if (Squares[7][0] == 'r') castling += "q";
        }
        if (castling.Length == 0) castling = "-";
        return $"{string.Join("/", rows)} {(sideToMove == PieceColor.White ? "w" : "b")} {castling} - 0 1";
    }

    public static Placement? FromFen(string fen)
    {
        var rows = fen.Split(' ')[0].Split('/');
        if (rows.Length != 8) return null;
        var p = new Placement();
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i, file = 0;
            foreach (char ch in rows[i])
            {
                if (char.IsDigit(ch)) file += ch - '0';
                else if ("KQRBNPkqrbnp".IndexOf(ch) >= 0 && file < 8) p.Squares[rank][file++] = ch;
                else return null;
            }
            if (file != 8) return null;
        }
        return p;
    }

    public string? Problem(PieceColor sideToMove)
    {
        if (Count('K') != 1 || Count('k') != 1) return CoreLocale.Get("problem.kings");
        if (Squares[0].Any(p => p == 'P' || p == 'p') || Squares[7].Any(p => p == 'P' || p == 'p'))
            return CoreLocale.Get("problem.backRankPawn");
        if (Count('P') > 8 || Count('p') > 8) return CoreLocale.Get("problem.tooManyPawns");

        foreach (var (color, letters) in new[] { (PieceColor.White, "QRBNP"), (PieceColor.Black, "qrbnp") })
        {
            int total = 0;
            foreach (var row in Squares) foreach (var s in row) if (s is char c && ColorOf(c) == color) total++;
            if (total > 16) return CoreLocale.Get(color == PieceColor.White ? "problem.tooManyWhite" : "problem.tooManyBlack");
            int pawns = Count(letters[4]);
            int promoted = Math.Max(0, Count(letters[0]) - 1) + Math.Max(0, Count(letters[1]) - 2)
                + Math.Max(0, Count(letters[2]) - 2) + Math.Max(0, Count(letters[3]) - 2);
            if (pawns + promoted > 8) return CoreLocale.Get("problem.impossibleMaterial");
        }
        if (IsInCheck(sideToMove.Opposite())) return CoreLocale.Get("problem.opponentInCheck");
        return null;
    }

    public (int File, int Rank)? KingSquare(PieceColor color)
    {
        char k = color == PieceColor.White ? 'K' : 'k';
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++) if (Squares[r][f] == k) return (f, r);
        return null;
    }

    public bool IsInCheck(PieceColor color)
    {
        if (KingSquare(color) is not var (f, r)) return false;
        return IsAttacked(f, r, color.Opposite());
    }

    private static readonly (int, int)[] KnightSteps = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
    private static readonly (int, int, string)[] Lines =
    {
        (1, 0, "rq"), (-1, 0, "rq"), (0, 1, "rq"), (0, -1, "rq"),
        (1, 1, "bq"), (1, -1, "bq"), (-1, 1, "bq"), (-1, -1, "bq"),
    };

    public bool IsAttacked(int file, int rank, PieceColor attacker)
    {
        char? At(int f, int r) => f is >= 0 and < 8 && r is >= 0 and < 8 ? Squares[r][f] : null;
        bool IsOwn(char? p, string letters) =>
            p is char c && ColorOf(c) == attacker && letters.IndexOf(char.ToLowerInvariant(c)) >= 0;
        int dir = attacker == PieceColor.White ? -1 : 1;
        foreach (int df in new[] { -1, 1 }) if (IsOwn(At(file + df, rank + dir), "p")) return true;
        foreach (var (df, dr) in KnightSteps) if (IsOwn(At(file + df, rank + dr), "n")) return true;
        for (int df = -1; df <= 1; df++) for (int dr = -1; dr <= 1; dr++)
            if (!(df == 0 && dr == 0) && IsOwn(At(file + df, rank + dr), "k")) return true;
        foreach (var (df, dr, letters) in Lines)
        {
            int f = file + df, r = rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                if (Squares[r][f] is char p)
                {
                    if (IsOwn(p, letters)) return true;
                    break;
                }
                f += df; r += dr;
            }
        }
        return false;
    }

    public PieceColor? Mover(Placement previous)
    {
        bool white = false, black = false;
        int changed = 0;
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
            if (Squares[r][f] != previous.Squares[r][f])
            {
                changed++;
                if (Squares[r][f] is char p) { if (ColorOf(p) == PieceColor.White) white = true; else black = true; }
            }
        if (changed < 2 || changed > 4 || (white ? 1 : 0) + (black ? 1 : 0) != 1) return null;
        return white ? PieceColor.White : PieceColor.Black;
    }
}

public readonly record struct UciMove(int FromFile, int FromRank, int ToFile, int ToRank, char? Promotion = null)
{
    public static UciMove? Parse(string s)
    {
        if (s == null || s.Length < 4) return null;
        int ff = "abcdefgh".IndexOf(s[0]), tf = "abcdefgh".IndexOf(s[2]);
        if (ff < 0 || tf < 0 || !char.IsDigit(s[1]) || !char.IsDigit(s[3])) return null;
        return new UciMove(ff, s[1] - '1', tf, s[3] - '1', s.Length > 4 ? s[4] : null);
    }

    public static string SquareName(int f, int r) => $"{"abcdefgh"[f]}{r + 1}";

    public string Uci => $"{SquareName(FromFile, FromRank)}{SquareName(ToFile, ToRank)}{Promotion?.ToString() ?? ""}";

    public override string ToString() => Uci;
}
