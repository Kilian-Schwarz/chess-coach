using System.Text.Json;
using ChessCoach.Core;

namespace ChessCoach.Core.Tests;

public class VectorTests
{
    static JsonElement Vectors { get; } = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestSupport.TestData, "vectors.json"))).RootElement;

    static ChessPosition Pos(string fen) => ChessPosition.FromFen(fen) ?? throw new Exception("FEN: " + fen);

    public static IEnumerable<object[]> PerftCases()
    {
        foreach (var p in Vectors.GetProperty("movegen").GetProperty("perft").EnumerateArray())
            yield return new object[] { p.GetProperty("name").GetString()!, p.GetProperty("fen").GetString()!,
                p.GetProperty("counts").EnumerateArray().Select(x => x.GetInt64()).ToArray() };
    }

    [Theory]
    [MemberData(nameof(PerftCases))]
    public void Perft(string name, string fen, long[] counts)
    {
        var pos = Pos(fen);
        for (int d = 1; d <= counts.Length; d++)
            Assert.True(counts[d - 1] == pos.Perft(d), $"{name} depth {d}: expected {counts[d - 1]}");
    }

    [Fact]
    public void GameResults()
    {
        foreach (var r in Vectors.GetProperty("movegen").GetProperty("results").EnumerateArray())
        {
            var fen = r.GetProperty("fen").GetString()!;
            var res = Pos(fen).Result;
            string got = res == null ? "none" : res.Value.Kind switch
            {
                GameResultKind.Checkmate => res.Value.Winner == PieceColor.White ? "mate-white" : "mate-black",
                GameResultKind.Stalemate => "stalemate",
                GameResultKind.InsufficientMaterial => "material",
                _ => "fifty",
            };
            Assert.Equal(r.GetProperty("result").GetString(), got);
        }
    }

    const string German = "KDTLS";

    [Fact]
    public void SanGenerate()
    {
        int n = 0;
        foreach (var c in Vectors.GetProperty("san").GetProperty("generate").EnumerateArray())
        {
            var pos = Pos(c.GetProperty("fen").GetString()!);
            var m = UciMove.Parse(c.GetProperty("uci").GetString()!)!.Value;
            Assert.Equal(c.GetProperty("san").GetString(), San.Of(m, pos));
            Assert.Equal(c.GetProperty("german").GetString(), San.Localize(San.Of(m, pos), German, "0-0", "0-0-0"));
            Assert.Equal(m, San.Parse(c.GetProperty("san").GetString()!, pos));
            Assert.Equal(m, San.Parse(c.GetProperty("german").GetString()!, pos, German));
            n++;
        }
        Assert.True(n > 0);
    }

    [Fact]
    public void SanParse()
    {
        foreach (var c in Vectors.GetProperty("san").GetProperty("parseOnly").EnumerateArray())
        {
            var pos = Pos(c.GetProperty("fen").GetString()!);
            var text = c.GetProperty("text").GetString()!;
            var u = c.GetProperty("uci");
            bool ger = c.TryGetProperty("german", out var gp) && gp.ValueKind == JsonValueKind.True;
            var got = San.Parse(text, pos, ger ? German : null);
            Assert.Equal(u.ValueKind == JsonValueKind.Null ? null : u.GetString(), got?.Uci);
        }
    }

    [Fact]
    public void SanGermanRoundTrip()
    {
        var pos = ChessPosition.Start;
        var m = San.Parse("Sf3", pos, German);
        Assert.Equal("g1f3", m?.Uci);
        Assert.Equal("0-0", San.Localize("O-O", German, "0-0", "0-0-0"));
        Assert.Equal("0-0-0+", San.Localize("O-O-O+", German, "0-0", "0-0-0"));
        Assert.Equal("e8=D+", San.Localize("e8=Q+", German, "0-0", "0-0-0"));
        Assert.Equal("e8=Q", San.Delocalize("e8=D", German));
        Assert.Equal("Sf3", San.Localize("Nf3", new System.Globalization.CultureInfo("de")));
    }

    [Fact]
    public void GameSanAndFensFollowVectors()
    {
        foreach (var g in Vectors.GetProperty("game").GetProperty("games").EnumerateArray())
        {
            var moves = g.GetProperty("moves").EnumerateArray().Select(x => x.GetString()!).ToList();
            var sans = g.GetProperty("san").EnumerateArray().Select(x => x.GetString()!).ToList();
            var pos = ChessPosition.Start;
            for (int i = 0; i < moves.Count; i++)
            {
                var m = UciMove.Parse(moves[i])!.Value;
                Assert.True(pos.IsLegal(m), $"Move {i} {moves[i]} illegal");
                Assert.Equal(sans[i], San.Of(m, pos));
                pos = pos.Applying(m);
            }
            if (g.TryGetProperty("fen", out var fen) && fen.ValueKind == JsonValueKind.String)
                Assert.Equal(fen.GetString(), pos.Fen);
            if (g.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.String)
            {
                var r = pos.Result;
                var pgn = r?.Pgn;
                if (pgn != null) Assert.Equal(res.GetString(), pgn);
            }
        }
    }

    [Fact]
    public void FenRoundTripAndEnPassantOnlyWhenCapturable()
    {
        var p = ChessPosition.Start.Applying(UciMove.Parse("e2e4")!.Value);
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", p.Fen);
        var q = Pos("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3");
        Assert.Contains(" d6 ", q.Fen);
        var nm = Pos("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3").NullMove;
        Assert.Equal("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR b KQkq - 1 3", nm.Fen);
    }

    [Fact]
    public void NullMoveVectors()
    {
        foreach (var c in Vectors.GetProperty("threats").GetProperty("nullMove").EnumerateArray())
        {
            var pos = Pos(c.GetProperty("fen").GetString()!);
            var expected = c.GetProperty("nullFen");
            if (expected.ValueKind == JsonValueKind.Null) { Assert.True(pos.IsCheck); continue; }
            Assert.Equal(expected.GetString(), pos.NullMove.Fen);
        }
    }

    [Fact]
    public void OpeningVectors()
    {
        var path = Path.Combine(TestSupport.Root, "ChessCoach", "Resources", "openings.tsv");
        var book = Openings.FromFile(path);
        Assert.NotNull(book);
        Assert.True(book!.Count > 3000);
        foreach (var c in Vectors.GetProperty("openings").EnumerateArray())
        {
            var pos = ChessPosition.Start;
            var epds = new List<string>();
            foreach (var u in c.GetProperty("moves").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var m = UciMove.Parse(u)!.Value;
                pos = pos.Applying(m);
                epds.Add(Openings.Epd(pos, m));
            }
            var hit = book.LongestName(epds);
            Assert.NotNull(hit);
            Assert.Equal(c.GetProperty("eco").GetString(), hit!.Eco);
            Assert.Equal(c.GetProperty("name").GetString(), hit.Name);
        }
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq -",
            Openings.Epd(ChessPosition.Start.Applying(UciMove.Parse("e2e4")!.Value), UciMove.Parse("e2e4")));
        Assert.Equal("Italian Game: Two Knights Defense (C55)", new OpeningInfo("C55", "Italian Game: Two Knights Defense").Display);
    }
}
