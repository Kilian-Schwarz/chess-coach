using System.Globalization;
using System.Text;

namespace ChessCoach.Core;

public static class FactCheck
{
    private const string Kinds = "kqrbnp";

    private static readonly string[] AttackVerbs =
    {
        "attack", "threat", "angriff", "angreif", "greift", "bedroh", "attaqu", "menac", "ataca", "amenaz", "attacc", "minacc", "aanval", "bedreig",
        "atak", "zagra", "atac", "ameaç", "напад", "угрож", "атак", "загрожу", "saldır", "tehdit", "攻撃", "狙", "攻击", "威胁", "공격", "위협",
    };

    private static readonly string[] Blockers =
    {
        " by ", " von ", "wird", " ist ", " sind ", " is ", " are ", " not ", "n't", "nicht", "kein", " no ", " pas ", " par ", " por ", " door ",
        "przez", " не ", "x-ray", "röntgen", "xray", "once ", "if ", "wenn", "falls", "sobald", "would",
    };

    private static readonly HashSet<string> Prepositions = new()
        { "on", "auf", "en", "sur", "su", "op", "na", "em", "на", "de", "à", "a", "in", "an", "bei", "sulla", "sul", "в" };

    private static Dictionary<string, char> Names()
    {
        var map = new Dictionary<string, char>();
        foreach (char kind in Kinds)
            foreach (var culture in new CultureInfo?[] { CoreLocale.English, null })
                map[PieceInfo.Name(kind, culture).ToLowerInvariant()] = kind;
        return map;
    }

    private static Dictionary<char, char> Letters()
    {
        var map = new Dictionary<char, char>();
        foreach (var pieces in new[] { "KQRBN", CoreLocale.Get("san.pieces", null) })
            for (int i = 0; i < Math.Min(pieces.Length, 5); i++) map[pieces[i]] = "kqrbn"[i];
        return map;
    }

    private static string Sq(int f, int r) => UciMove.SquareName(f, r);

    private static void AddAttacks(Placement board, HashSet<string> set)
    {
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
        {
            if (board.Squares[r][f] is not char p) continue;
            char kind = char.ToLowerInvariant(p);
            string from = Sq(f, r);
            foreach (var (tf, tr) in board.Attacks(f, r))
            {
                set.Add($"a:{kind}{from}>{Sq(tf, tr)}");
                if (board.Squares[tr][tf] is char t && Placement.ColorOf(t) != Placement.ColorOf(p))
                    set.Add($"a:{kind}{from}>~{char.ToLowerInvariant(t)}");
            }
        }
    }

    public static HashSet<string> Allowed(IEnumerable<ChessPosition> positions, IEnumerable<IReadOnlyList<string>> lines)
    {
        var set = new HashSet<string>();
        void Add(ChessPosition pos, bool children)
        {
            AddAttacks(pos.Board, set);
            if (children)
                foreach (var side in new[] { pos, pos.NullMove })
                    foreach (var m in side.LegalMoves())
                    {
                        AddAttacks(side.Board.Applying(m), set);
                        set.Add("san:" + San.Of(m, side));
                    }
            for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
                if (pos.Board.Squares[r][f] is char p) set.Add($"{char.ToLowerInvariant(p)}:{Sq(f, r)}");
            foreach (var side in new[] { pos, pos.NullMove })
                foreach (var m in side.LegalMoves())
                    if (side.Board.Squares[m.FromRank][m.FromFile] is char p) set.Add($"{char.ToLowerInvariant(p)}:{Sq(m.ToFile, m.ToRank)}");
        }
        var list = lines.ToList();
        foreach (var pos in positions)
        {
            Add(pos, true);
            foreach (var pv in list)
            {
                var cur = pos;
                foreach (var u in pv.Take(6))
                {
                    if (UciMove.Parse(u) is not UciMove m || !cur.IsLegal(m)) break;
                    set.Add("san:" + San.Of(m, cur));
                    cur = cur.Applying(m);
                    Add(cur, false);
                }
            }
        }
        return set;
    }

    private static bool IsSquare(string w) => w.Length == 2 && "abcdefgh".IndexOf(w[0]) >= 0 && w[1] is >= '1' and <= '8';

    private sealed record Mention(char Kind, string? Square, int Start, int End);

    private static (List<(string Text, int Start, int End)> Words, List<Mention> Found) Mentions(string sentence)
    {
        var lower = sentence.ToLowerInvariant();
        var words = new List<(string, int, int)>();
        int start = -1;
        for (int i = 0; i <= lower.Length; i++)
        {
            bool inWord = i < lower.Length && char.IsLetterOrDigit(lower[i]);
            if (inWord) { if (start < 0) start = i; }
            else if (start >= 0) { words.Add((lower[start..i], start, i)); start = -1; }
        }
        var names = Names();
        var found = new List<Mention>();
        for (int i = 0; i < words.Count; i++)
        {
            if (!names.TryGetValue(words[i].Item1, out char kind)) continue;
            string? sq = null;
            int end = i;
            if (i + 1 < words.Count && IsSquare(words[i + 1].Item1)) { sq = words[i + 1].Item1; end = i + 1; }
            else if (i + 2 < words.Count && Prepositions.Contains(words[i + 1].Item1) && IsSquare(words[i + 2].Item1)) { sq = words[i + 2].Item1; end = i + 2; }
            found.Add(new Mention(kind, sq, i, end));
        }
        return (words, found);
    }

    public static bool ContradictsAttacks(string sentence, HashSet<string> allowed)
    {
        var (words, found) = Mentions(sentence);
        var lower = sentence.ToLowerInvariant();
        for (int i = 0; i + 1 < found.Count; i++)
        {
            var a = found[i];
            var b = found[i + 1];
            if (a.Square is not string from || a.End + 1 >= words.Count || b.Start <= a.End) continue;
            int lo = words[a.End].End, hi = words[b.Start].Start;
            if (lo > hi) continue;
            string between = " " + lower[lo..hi] + " ";
            if (!AttackVerbs.Any(between.Contains) || Blockers.Any(between.Contains)) continue;
            string key = $"a:{a.Kind}{from}>";
            bool ok = b.Square is string target ? allowed.Contains(key + target) : allowed.Contains(key + "~" + b.Kind);
            if (!ok) return true;
        }
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex SanPattern =
        new(@"^[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](=[QRBN])?[+#]$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool ContradictsCheckClaim(string sentence, HashSet<string> allowed)
    {
        string pieces = CoreLocale.Get("san.pieces", null);
        foreach (var raw in sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim('.', ',', ';', ':', '!', '?', ')', '(', '"', '“', '”', '«', '»');
            if (word.Length == 0 || (word[^1] != '+' && word[^1] != '#')) continue;
            var san = San.Delocalize(raw.Trim('.', ',', ';', ':', '!', '?', ')', '(', '"', '“', '”', '«', '»'), pieces);
            if (!SanPattern.IsMatch(san)) continue;
            if (!allowed.Contains("san:" + san)) return true;
        }
        return false;
    }

    public static bool MentionsInvented(string sentence, HashSet<string> allowed)
    {
        foreach (var (name, kind) in Names())
        {
            int from = 0;
            while (from < sentence.Length)
            {
                int idx = sentence.IndexOf(name + " ", from, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                int after = idx + name.Length + 1;
                if (after + 1 < sentence.Length)
                {
                    char c0 = sentence[after], c1 = sentence[after + 1];
                    bool trailing = after + 2 < sentence.Length && char.IsLetter(sentence[after + 2]);
                    if ("abcdefgh".IndexOf(c0) >= 0 && c1 is >= '1' and <= '8' && !trailing && !allowed.Contains($"{kind}:{c0}{c1}")) return true;
                }
                from = after;
            }
        }
        var letters = Letters();
        for (int i = 0; i < sentence.Length; i++)
        {
            if (!letters.TryGetValue(sentence[i], out char kind) || (i > 0 && char.IsLetter(sentence[i - 1]))) continue;
            int j = i + 1;
            if (j < sentence.Length && sentence[j] == 'x') j++;
            if (j + 1 < sentence.Length && "abcdefgh".IndexOf(sentence[j]) >= 0 && sentence[j + 1] is >= '1' and <= '8'
                && !(j + 2 < sentence.Length && char.IsLetter(sentence[j + 2])) && !allowed.Contains($"{kind}:{sentence[j]}{sentence[j + 1]}"))
                return true;
        }
        return false;
    }

    public static (string? Text, int Removed) Clean(string text, HashSet<string> allowed)
    {
        var sentences = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            current.Append(c);
            bool end = c is '.' or '!' or '?' or '\n';
            if (end && c == '.' && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1])) continue;
            if (end) { sentences.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) sentences.Add(current.ToString());
        var kept = new StringBuilder();
        int removed = 0;
        foreach (var s in sentences)
        {
            if (MentionsInvented(s, allowed) || ContradictsAttacks(s, allowed) || ContradictsCheckClaim(s, allowed)) removed++; else kept.Append(s);
        }
        var output = kept.ToString().Trim();
        return (output.Length == 0 ? null : output, removed);
    }
}
