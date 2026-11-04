using System.Globalization;

namespace ChessCoach.Core;

public static class PieceInfo
{
    public static readonly Dictionary<char, int> Values = new()
    { ['k'] = 100, ['q'] = 9, ['r'] = 5, ['b'] = 3, ['n'] = 3, ['p'] = 1 };

    public static readonly string[] Kinds = { "king", "queen", "rook", "bishop", "knight", "pawn" };
    private const string Letters = "kqrbnp";

    public static string Key(char p) => Kinds[Letters.IndexOf(char.ToLowerInvariant(p))];

    public static string Name(char p, CultureInfo? culture = null) =>
        Letters.IndexOf(char.ToLowerInvariant(p)) < 0 ? "?" : CoreLocale.Get("piece." + Key(p), culture);

    public static string Your(char p, CultureInfo? culture = null) => CoreLocale.Get("piece." + Key(p) + ".your", culture);
    public static int Value(char p) => Values.GetValueOrDefault(char.ToLowerInvariant(p), 0);
}

public static class Tactics
{
    public readonly record struct Attacker(char Piece, int File, int Rank);

    private static bool OnBoard(int f, int r) => f is >= 0 and < 8 && r is >= 0 and < 8;
    private static readonly (int, int)[] Knight = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
    private static readonly (int, int)[] Diag = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly (int, int)[] Orth = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    public static char? Piece(this Placement p, int f, int r) => OnBoard(f, r) ? p.Squares[r][f] : null;

    public static Placement Applying(this Placement self, UciMove m)
    {
        var p = self.Clone();
        if (self.Squares[m.FromRank][m.FromFile] is not char piece) return p;
        char kind = char.ToLowerInvariant(piece);
        p.Squares[m.FromRank][m.FromFile] = null;
        if (kind == 'p' && m.FromFile != m.ToFile && self.Squares[m.ToRank][m.ToFile] == null)
            p.Squares[m.FromRank][m.ToFile] = null;
        if (kind == 'k' && Math.Abs(m.ToFile - m.FromFile) == 2)
        {
            var (rf, rt) = m.ToFile > m.FromFile ? (7, 5) : (0, 3);
            p.Squares[m.FromRank][rt] = p.Squares[m.FromRank][rf];
            p.Squares[m.FromRank][rf] = null;
        }
        char placed = piece;
        if (m.Promotion is char promo) placed = char.IsUpper(piece) ? char.ToUpperInvariant(promo) : promo;
        p.Squares[m.ToRank][m.ToFile] = placed;
        return p;
    }

    public static List<(int File, int Rank)> Attacks(this Placement self, int file, int rank)
    {
        var result = new List<(int, int)>();
        if (self.Piece(file, rank) is not char p) return result;
        bool white = char.IsUpper(p);
        void Slide((int, int)[] dirs)
        {
            foreach (var (df, dr) in dirs)
            {
                int f = file + df, r = rank + dr;
                while (OnBoard(f, r))
                {
                    result.Add((f, r));
                    if (self.Squares[r][f] != null) break;
                    f += df; r += dr;
                }
            }
        }
        switch (char.ToLowerInvariant(p))
        {
            case 'p':
                int dr0 = white ? 1 : -1;
                foreach (int df in new[] { -1, 1 }) if (OnBoard(file + df, rank + dr0)) result.Add((file + df, rank + dr0));
                break;
            case 'n':
                foreach (var (df, dr) in Knight) if (OnBoard(file + df, rank + dr)) result.Add((file + df, rank + dr));
                break;
            case 'k':
                for (int df = -1; df <= 1; df++) for (int dr = -1; dr <= 1; dr++)
                    if (!(df == 0 && dr == 0) && OnBoard(file + df, rank + dr)) result.Add((file + df, rank + dr));
                break;
            case 'b': Slide(Diag); break;
            case 'r': Slide(Orth); break;
            case 'q': Slide(Diag); Slide(Orth); break;
        }
        return result;
    }

    public static List<Attacker>[][] Attackers(this Placement self, PieceColor color)
    {
        var map = new List<Attacker>[8][];
        for (int r = 0; r < 8; r++) { map[r] = new List<Attacker>[8]; for (int f = 0; f < 8; f++) map[r][f] = new(); }
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
        {
            if (self.Squares[r][f] is not char p || Placement.ColorOf(p) != color) continue;
            foreach (var (tf, tr) in self.Attacks(f, r)) map[tr][tf].Add(new Attacker(p, f, r));
        }
        return map;
    }

    public static List<string> HangingPieces(this Placement self, PieceColor color, CultureInfo? culture = null)
    {
        var enemy = self.Attackers(color.Opposite());
        var friends = self.Attackers(color);
        var output = new List<string>();
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
        {
            if (self.Squares[r][f] is not char p || Placement.ColorOf(p) != color || char.ToLowerInvariant(p) == 'k') continue;
            var att = enemy[r][f];
            if (att.Count == 0) continue;
            int cheapest = att.Min(a => PieceInfo.Value(a.Piece));
            string sq = UciMove.SquareName(f, r);
            if (friends[r][f].Count == 0) output.Add(CoreLocale.Format("hanging.unprotected", culture, PieceInfo.Name(p, culture), sq));
            else if (cheapest < PieceInfo.Value(p)) output.Add(CoreLocale.Format("hanging.cheaper", culture, PieceInfo.Name(p, culture), sq));
        }
        return output;
    }

    private static readonly string[] Singular = { "King", "Queen", "Rook", "Bishop", "Knight", "Pawn" };
    private static readonly string[] Plural = { "King", "Queens", "Rooks", "Bishops", "Knights", "Pawns" };

    public static string PieceList(this Placement self, PieceColor color)
    {
        var parts = new List<string>();
        for (int k = 0; k < 6; k++)
        {
            var sqs = new List<string>();
            for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
                if (self.Squares[r][f] is char p && Placement.ColorOf(p) == color && char.ToLowerInvariant(p) == "kqrbnp"[k])
                    sqs.Add(UciMove.SquareName(f, r));
            if (sqs.Count == 0) continue;
            parts.Add($"{(sqs.Count == 1 ? Singular[k] : Plural[k])} {string.Join(" ", sqs)}");
        }
        return string.Join(", ", parts);
    }

    public static UciMove? InferMove(this Placement self, Placement next, PieceColor color)
    {
        var froms = new List<(int F, int R)>(); var tos = new List<(int F, int R)>();
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
            if (self.Squares[r][f] != next.Squares[r][f])
            {
                if (self.Squares[r][f] is char a && Placement.ColorOf(a) == color) froms.Add((f, r));
                if (next.Squares[r][f] is char b && Placement.ColorOf(b) == color) tos.Add((f, r));
            }
        var candidates = new List<UciMove>();

        var kf = froms.FindIndex(x => self.Squares[x.R][x.F] is char c && char.ToLowerInvariant(c) == 'k');
        var kt = tos.FindIndex(x => next.Squares[x.R][x.F] is char c && char.ToLowerInvariant(c) == 'k');
        if (kf >= 0 && kt >= 0)
        {
            var k = froms[kf]; var t = tos[kt];
            if (k.R == t.R && Math.Abs(k.F - t.F) == 2) candidates.Add(new UciMove(k.F, k.R, t.F, t.R));
        }
        foreach (var fr in froms) foreach (var to in tos)
        {
            if (self.Squares[fr.R][fr.F] is not char moved || next.Squares[to.R][to.F] is not char arrived) continue;
            if (moved == arrived) candidates.Add(new UciMove(fr.F, fr.R, to.F, to.R));
            else if (char.ToLowerInvariant(moved) == 'p' && (to.R == 7 || to.R == 0))
                candidates.Add(new UciMove(fr.F, fr.R, to.F, to.R, char.ToLowerInvariant(arrived)));
        }
        foreach (var c in candidates) if (self.Applying(c).Equals(next)) return c;
        return null;
    }

    public static string DescribeMove(this Placement self, UciMove m, CultureInfo? culture = null, bool check = true)
    {
        if (self.Piece(m.FromFile, m.FromRank) is not char p) return m.Uci;
        string from = UciMove.SquareName(m.FromFile, m.FromRank), to = UciMove.SquareName(m.ToFile, m.ToRank);
        string text;
        if (char.ToLowerInvariant(p) == 'k' && Math.Abs(m.ToFile - m.FromFile) == 2)
            text = CoreLocale.Get(m.ToFile > m.FromFile ? "move.castleShort" : "move.castleLong", culture);
        else if (self.Piece(m.ToFile, m.ToRank) is char victim)
            text = CoreLocale.Format("move.capture", culture, PieceInfo.Name(p, culture), from, PieceInfo.Name(victim, culture), to);
        else if (char.ToLowerInvariant(p) == 'p' && m.FromFile != m.ToFile)
            text = CoreLocale.Format("move.enPassant", culture, from, to);
        else
            text = CoreLocale.Format("move.plain", culture, PieceInfo.Name(p, culture), from, to);
        if (m.Promotion is char promo) text += CoreLocale.Format("move.promotes", culture, PieceInfo.Name(promo, culture));
        if (check && self.Applying(m).IsInCheck(Placement.ColorOf(p).Opposite())) text += CoreLocale.Get("move.check", culture);
        return text;
    }

    public static string DescribeLine(this Placement self, IEnumerable<string> uciMoves, int maxMoves = 6, CultureInfo? culture = null)
    {
        var pos = self;
        var output = new List<string>();
        foreach (var u in uciMoves.Take(maxMoves))
        {
            if (UciMove.Parse(u) is not UciMove m) break;
            output.Add(pos.DescribeMove(m, culture));
            pos = pos.Applying(m);
        }
        return string.Join("; ", output);
    }
}
