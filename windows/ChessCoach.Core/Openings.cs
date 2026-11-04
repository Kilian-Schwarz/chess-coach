namespace ChessCoach.Core;

public sealed record OpeningInfo(string Eco, string Name)
{
    public string Display => CoreLocale.Format("opening.display", null, Name, Eco);
}

public sealed class Openings
{
    private readonly Dictionary<string, OpeningInfo> _table = new();
    public int Count => _table.Count;

    public Openings(string tsv)
    {
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var cols = line.Split('\t');
            if (cols.Length < 3) continue;
            _table[cols[0]] = new OpeningInfo(cols[1], cols[2]);
        }
    }

    public static Openings? FromFile(string path)
    {
        try { return File.Exists(path) ? new Openings(File.ReadAllText(path)) : null; }
        catch { return null; }
    }

    public static Openings? FromAppDirectory(string? dir = null)
    {
        dir ??= AppContext.BaseDirectory;
        return FromFile(Path.Combine(dir, "openings.tsv")) ?? FromFile(Path.Combine(dir, "Resources", "openings.tsv"));
    }

    public OpeningInfo? Lookup(string epd) => _table.GetValueOrDefault(epd);

    public OpeningInfo? LongestName(IReadOnlyList<string> epds)
    {
        for (int i = epds.Count - 1; i >= 0; i--) if (_table.TryGetValue(epds[i], out var hit)) return hit;
        return null;
    }

    public static string Epd(Placement placement, PieceColor sideToMove, string castling, string? enPassant)
    {
        var f = placement.Fen(sideToMove).Split(' ');
        var c = castling.Length == 0 ? "-" : castling;
        return $"{f[0]} {f[1]} {c} {enPassant ?? "-"}";
    }

    public static string? EnPassantTarget(Placement placement, UciMove m)
    {
        if (placement.Squares[m.ToRank][m.ToFile] is not ('P' or 'p') || m.FromFile != m.ToFile || Math.Abs(m.ToRank - m.FromRank) != 2)
            return null;
        char enemy = placement.Squares[m.ToRank][m.ToFile] == 'P' ? 'p' : 'P';
        foreach (int df in new[] { -1, 1 })
        {
            int nf = m.ToFile + df;
            if (nf is >= 0 and < 8 && placement.Squares[m.ToRank][nf] == enemy)
                return UciMove.SquareName(m.ToFile, (m.ToRank + m.FromRank) / 2);
        }
        return null;
    }

    public static string Epd(ChessPosition pos, UciMove? lastMove)
    {
        string? ep = lastMove is UciMove m ? EnPassantTarget(pos.Board, m) : null;
        return Epd(pos.Board, pos.Turn, pos.Castling.Fen, ep);
    }
}
