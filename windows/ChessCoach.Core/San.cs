using System.Globalization;
using System.Text;

namespace ChessCoach.Core;

public static class San
{
    private const string Standard = "KQRBN";

    public static string Core(UciMove m, ChessPosition pos)
    {
        if (pos.Board.Squares[m.FromRank][m.FromFile] is not char piece) return m.Uci;
        char kind = char.ToUpperInvariant(piece);
        string to = UciMove.SquareName(m.ToFile, m.ToRank);
        bool capture = pos.Board.Squares[m.ToRank][m.ToFile] != null || pos.IsEnPassant(m);
        if (kind == 'K' && Math.Abs(m.ToFile - m.FromFile) == 2) return m.ToFile > m.FromFile ? "O-O" : "O-O-O";
        var s = new StringBuilder();
        if (kind == 'P')
        {
            if (capture) s.Append("abcdefgh"[m.FromFile]).Append('x');
            s.Append(to);
            if (m.Promotion is char promo) s.Append('=').Append(char.ToUpperInvariant(promo));
            return s.ToString();
        }
        s.Append(kind);

        var others = pos.LegalMoves().Where(o =>
            o.ToFile == m.ToFile && o.ToRank == m.ToRank && !(o.FromFile == m.FromFile && o.FromRank == m.FromRank)
            && pos.Board.Squares[o.FromRank][o.FromFile] == piece).ToList();
        if (others.Count > 0)
        {
            bool sameFile = others.Any(o => o.FromFile == m.FromFile);
            bool sameRank = others.Any(o => o.FromRank == m.FromRank);
            if (!sameFile) s.Append("abcdefgh"[m.FromFile]);
            else if (!sameRank) s.Append(m.FromRank + 1);
            else s.Append(UciMove.SquareName(m.FromFile, m.FromRank));
        }
        if (capture) s.Append('x');
        return s.Append(to).ToString();
    }

    public static string Of(UciMove m, ChessPosition pos)
    {
        var s = Core(m, pos);
        var after = pos.Applying(m);
        if (after.IsCheck) s += after.LegalMoves().Count == 0 ? "#" : "+";
        return s;
    }

    public static string Localize(string san, CultureInfo? culture = null) =>
        Localize(san, CoreLocale.Get("san.pieces", culture), CoreLocale.Get("san.castleShort", culture), CoreLocale.Get("san.castleLong", culture));

    public static string Localize(string san, string pieces, string castleShort, string castleLong)
    {
        if (san.StartsWith("O-O-O", StringComparison.Ordinal)) return castleLong + san[5..];
        if (san.StartsWith("O-O", StringComparison.Ordinal)) return castleShort + san[3..];
        var sb = new StringBuilder(san.Length);
        foreach (char c in san)
        {
            int i = Standard.IndexOf(c);
            sb.Append(i >= 0 && i < pieces.Length ? pieces[i] : c);
        }
        return sb.ToString();
    }

    public static string Delocalize(string text, string pieces)
    {
        var chars = text.ToCharArray();
        if (chars.Length > 0 && pieces.IndexOf(chars[0]) is int i0 and >= 0) chars[0] = Standard[i0];
        int eq = Array.IndexOf(chars, '=');
        if (eq >= 0 && eq + 1 < chars.Length && pieces.IndexOf(chars[eq + 1]) is int i1 and >= 0) chars[eq + 1] = Standard[i1];
        return new string(chars);
    }

    private static string Norm(string s) => s.Replace("x", "").Replace("=", "");

    public static UciMove? Parse(string text, ChessPosition pos, string? pieces = null)
    {
        var t = text.Trim(' ', '\t').Replace("e.p.", "").Replace('0', 'O');
        t = t.TrimEnd('+', '#', '!', '?', ' ');
        if (pieces != null) t = Delocalize(t, pieces);
        if (t.Length == 0) return null;
        var legal = pos.LegalMoves();
        var exact = legal.Where(m => Core(m, pos) == t).ToList();
        if (exact.Count == 1) return exact[0];

        var loose = legal.Where(m => Norm(Core(m, pos)) == Norm(t)).ToList();
        if (loose.Count == 1) return loose[0];

        var chars = Norm(t);
        if (chars.Length == 3 && "KQRBN".Contains(chars[0]))
        {
            var dest = chars[1..];
            var cands = legal.Where(m => pos.Board.Squares[m.FromRank][m.FromFile] is char p && char.ToUpperInvariant(p) == chars[0]
                && UciMove.SquareName(m.ToFile, m.ToRank) == dest).ToList();
            if (cands.Count == 1) return cands[0];
        }

        if (chars.Length == 5 && "KQRBN".Contains(chars[0]))
        {
            var from = chars.Substring(1, 2); var dest = chars[3..];
            var cands = legal.Where(m => pos.Board.Squares[m.FromRank][m.FromFile] is char p && char.ToUpperInvariant(p) == chars[0]
                && UciMove.SquareName(m.FromFile, m.FromRank) == from && UciMove.SquareName(m.ToFile, m.ToRank) == dest).ToList();
            if (cands.Count == 1) return cands[0];
        }
        return null;
    }
}
