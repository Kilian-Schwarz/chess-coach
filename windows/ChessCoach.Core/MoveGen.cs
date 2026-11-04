using System.Globalization;

namespace ChessCoach.Core;

public record struct CastlingRights(bool WhiteKing, bool WhiteQueen, bool BlackKing, bool BlackQueen)
{
    public static CastlingRights All => new(true, true, true, true);
    public static CastlingRights None => new(false, false, false, false);

    public bool IsEmpty => !(WhiteKing || WhiteQueen || BlackKing || BlackQueen);

    public string Fen
    {
        get
        {
            var s = (WhiteKing ? "K" : "") + (WhiteQueen ? "Q" : "") + (BlackKing ? "k" : "") + (BlackQueen ? "q" : "");
            return s.Length == 0 ? "-" : s;
        }
    }

    public static CastlingRights FromFen(string fen) =>
        new(fen.Contains('K'), fen.Contains('Q'), fen.Contains('k'), fen.Contains('q'));

    public CastlingRights LimitedTo(Placement board)
    {
        var r = this;
        if (board.Squares[0][4] != 'K') { r.WhiteKing = false; r.WhiteQueen = false; }
        if (board.Squares[7][4] != 'k') { r.BlackKing = false; r.BlackQueen = false; }
        if (board.Squares[0][7] != 'R') r.WhiteKing = false;
        if (board.Squares[0][0] != 'R') r.WhiteQueen = false;
        if (board.Squares[7][7] != 'r') r.BlackKing = false;
        if (board.Squares[7][0] != 'r') r.BlackQueen = false;
        return r;
    }
}

public enum GameResultKind { Checkmate, Stalemate, InsufficientMaterial, FiftyMoveRule }

public readonly record struct GameResult(GameResultKind Kind, PieceColor? Winner = null)
{
    public static GameResult Checkmate(PieceColor winner) => new(GameResultKind.Checkmate, winner);

    public string Pgn => Kind == GameResultKind.Checkmate ? (Winner == PieceColor.White ? "1-0" : "0-1") : "1/2-1/2";

    public string Label(CultureInfo? culture = null) => Kind switch
    {
        GameResultKind.Checkmate => CoreLocale.Format("result.checkmate", culture, Winner!.Value.Label(culture)),
        GameResultKind.Stalemate => CoreLocale.Get("result.stalemate", culture),
        GameResultKind.InsufficientMaterial => CoreLocale.Get("result.insufficientMaterial", culture),
        _ => CoreLocale.Get("result.fiftyMoveRule", culture),
    };
}

public sealed class ChessPosition : IEquatable<ChessPosition>
{
    public Placement Board { get; set; }
    public PieceColor Turn { get; set; }
    public CastlingRights Castling { get; set; }

    public int? EnPassantFile { get; set; }
    public int HalfmoveClock { get; set; }
    public int FullmoveNumber { get; set; }

    public ChessPosition(Placement board, PieceColor turn, CastlingRights castling, int? enPassantFile = null,
        int halfmoveClock = 0, int fullmoveNumber = 1)
    {
        Board = board; Turn = turn; Castling = castling; EnPassantFile = enPassantFile;
        HalfmoveClock = halfmoveClock; FullmoveNumber = fullmoveNumber;
    }

    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public static ChessPosition Start => FromFen(StartFen)!;

    public ChessPosition Clone() => new(Board.Clone(), Turn, Castling, EnPassantFile, HalfmoveClock, FullmoveNumber);

    public bool Equals(ChessPosition? o) => o is not null && Board.Equals(o.Board) && Turn == o.Turn && Castling == o.Castling
        && EnPassantFile == o.EnPassantFile && HalfmoveClock == o.HalfmoveClock && FullmoveNumber == o.FullmoveNumber;
    public override bool Equals(object? obj) => Equals(obj as ChessPosition);
    public override int GetHashCode() => HashCode.Combine(Board, Turn, Castling, EnPassantFile, HalfmoveClock, FullmoveNumber);

    public static ChessPosition? FromFen(string fen)
    {
        var f = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 2) return null;
        var rows = f[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length != 8) return null;
        var b = new Placement();
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i, file = 0;
            foreach (char ch in rows[i])
            {
                if (char.IsDigit(ch)) file += ch - '0';
                else
                {
                    if (file >= 8 || "kqrbnpKQRBNP".IndexOf(ch) < 0) return null;
                    b.Squares[rank][file] = ch;
                    file++;
                }
            }
            if (file != 8) return null;
        }
        if (f[1] != "w" && f[1] != "b") return null;
        var pos = new ChessPosition(b, f[1] == "w" ? PieceColor.White : PieceColor.Black,
            f.Length > 2 ? CastlingRights.FromFen(f[2]) : CastlingRights.None);
        if (f.Length > 3 && f[3].Length == 2 && "abcdefgh".IndexOf(f[3][0]) is int idx and >= 0) pos.EnPassantFile = idx;
        pos.HalfmoveClock = f.Length > 4 && int.TryParse(f[4], out var h) ? h : 0;
        pos.FullmoveNumber = f.Length > 5 && int.TryParse(f[5], out var n) ? Math.Max(1, n) : 1;
        return pos;
    }

    public string Fen
    {
        get
        {
            var placement = Board.Fen(Turn).Split(' ')[0];
            string ep = "-";
            if (EnPassantFile is int file)
            {
                int rank = Turn == PieceColor.White ? 5 : 2;
                if (LegalMoves().Any(m => m.ToFile == file && m.ToRank == rank && IsEnPassant(m)))
                    ep = UciMove.SquareName(file, rank);
            }
            return $"{placement} {(Turn == PieceColor.White ? "w" : "b")} {Castling.Fen} {ep} {HalfmoveClock} {FullmoveNumber}";
        }
    }

    public ChessPosition NullMove
    {
        get
        {
            var p = Clone();
            p.Turn = Turn.Opposite();
            p.EnPassantFile = null;
            p.HalfmoveClock += 1;
            if (Turn == PieceColor.Black) p.FullmoveNumber += 1;
            return p;
        }
    }

    public bool IsEnPassant(UciMove m)
    {
        if (Board.Squares[m.FromRank][m.FromFile] is not ('P' or 'p')) return false;
        return m.FromFile != m.ToFile && Board.Squares[m.ToRank][m.ToFile] == null;
    }

    private static readonly (int, int)[] KnightSteps = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
    private static readonly (int, int)[] BishopSteps = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    private static readonly (int, int)[] RookSteps = { (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly (int, int)[] QueenSteps = { (1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1) };
    private static readonly char[] Promos = { 'q', 'r', 'b', 'n' };

    private List<UciMove> PseudoMoves()
    {
        var output = new List<UciMove>(48);
        var sq = Board.Squares;
        bool white = Turn == PieceColor.White;
        for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
        {
            if (sq[r][f] is not char p || char.IsUpper(p) != white) continue;
            char kind = char.ToLowerInvariant(p);
            void Step((int, int)[] dirs, bool slide)
            {
                foreach (var (df, dr) in dirs)
                {
                    int tf = f + df, tr = r + dr;
                    while (tf is >= 0 and < 8 && tr is >= 0 and < 8)
                    {
                        if (sq[tr][tf] is char t)
                        {
                            if (char.IsUpper(t) != white) output.Add(new UciMove(f, r, tf, tr));
                            break;
                        }
                        output.Add(new UciMove(f, r, tf, tr));
                        if (!slide) break;
                        tf += df; tr += dr;
                    }
                }
            }
            switch (kind)
            {
                case 'n': Step(KnightSteps, false); break;
                case 'b': Step(BishopSteps, true); break;
                case 'r': Step(RookSteps, true); break;
                case 'q': Step(QueenSteps, true); break;
                case 'k':
                {
                    Step(QueenSteps, false);
                    int home = white ? 0 : 7;
                    var opp = Turn.Opposite();
                    if (r == home && f == 4 && !Board.IsAttacked(4, home, opp))
                    {
                        bool canKing = white ? Castling.WhiteKing : Castling.BlackKing;
                        bool canQueen = white ? Castling.WhiteQueen : Castling.BlackQueen;
                        char rook = white ? 'R' : 'r';
                        if (canKing && sq[home][7] == rook && sq[home][5] == null && sq[home][6] == null
                            && !Board.IsAttacked(5, home, opp)) output.Add(new UciMove(4, home, 6, home));
                        if (canQueen && sq[home][0] == rook && sq[home][1] == null && sq[home][2] == null && sq[home][3] == null
                            && !Board.IsAttacked(3, home, opp)) output.Add(new UciMove(4, home, 2, home));
                    }
                    break;
                }
                default:
                {
                    int dir = white ? 1 : -1, startRank = white ? 1 : 6, lastRank = white ? 7 : 0;
                    void AddPawn(int tf, int tr)
                    {
                        if (tr == lastRank) foreach (char promo in Promos) output.Add(new UciMove(f, r, tf, tr, promo));
                        else output.Add(new UciMove(f, r, tf, tr));
                    }
                    int one = r + dir;
                    if (one is < 0 or >= 8) break;
                    if (sq[one][f] == null)
                    {
                        AddPawn(f, one);
                        if (r == startRank && sq[one + dir][f] == null) output.Add(new UciMove(f, r, f, one + dir));
                    }
                    foreach (int df in new[] { -1, 1 })
                    {
                        if (f + df is < 0 or >= 8) continue;
                        if (sq[one][f + df] is char t && char.IsUpper(t) != white) AddPawn(f + df, one);
                        else if (sq[one][f + df] == null && EnPassantFile is int ep && ep == f + df && one == (white ? 5 : 2)
                                 && sq[r][f + df] == (white ? 'p' : 'P')) output.Add(new UciMove(f, r, f + df, one));
                    }
                    break;
                }
            }
        }
        return output;
    }

    public List<UciMove> LegalMoves()
    {
        var list = PseudoMoves();
        var result = new List<UciMove>(list.Count);
        foreach (var m in list) if (!Board.Applying(m).IsInCheck(Turn)) result.Add(m);
        return result;
    }

    public bool IsLegal(UciMove m) => LegalMoves().Contains(m);

    public ChessPosition Applying(UciMove m)
    {
        var p = Clone();
        char? piece = Board.Squares[m.FromRank][m.FromFile];
        bool isPawn = piece is 'P' or 'p';
        bool capture = Board.Squares[m.ToRank][m.ToFile] != null || IsEnPassant(m);
        p.Board = Board.Applying(m);
        p.Turn = Turn.Opposite();
        if (Turn == PieceColor.Black) p.FullmoveNumber += 1;
        p.HalfmoveClock = (isPawn || capture) ? 0 : HalfmoveClock + 1;
        p.EnPassantFile = (isPawn && Math.Abs(m.ToRank - m.FromRank) == 2) ? m.FromFile : null;
        var c = p.Castling;
        if (piece == 'K') { c.WhiteKing = false; c.WhiteQueen = false; }
        if (piece == 'k') { c.BlackKing = false; c.BlackQueen = false; }
        foreach (var (f, r) in new[] { (m.FromFile, m.FromRank), (m.ToFile, m.ToRank) })
        {
            if ((f, r) == (7, 0)) c.WhiteKing = false;
            if ((f, r) == (0, 0)) c.WhiteQueen = false;
            if ((f, r) == (7, 7)) c.BlackKing = false;
            if ((f, r) == (0, 7)) c.BlackQueen = false;
        }
        p.Castling = c;
        return p;
    }

    public bool IsCheck => Board.IsInCheck(Turn);

    public bool HasInsufficientMaterial
    {
        get
        {
            var minors = new List<(char Kind, int Color)>();
            for (int r = 0; r < 8; r++) for (int f = 0; f < 8; f++)
            {
                if (Board.Squares[r][f] is not char p) continue;
                char k = char.ToLowerInvariant(p);
                if (k == 'k') continue;
                if (k is 'b' or 'n') minors.Add((k, (r + f) % 2));
                else return false;
            }
            if (minors.Count <= 1) return true;
            return minors.All(x => x.Kind == 'b' && x.Color == minors[0].Color);
        }
    }

    public GameResult? Result
    {
        get
        {
            if (LegalMoves().Count == 0) return IsCheck ? GameResult.Checkmate(Turn.Opposite()) : new GameResult(GameResultKind.Stalemate);
            if (HasInsufficientMaterial) return new GameResult(GameResultKind.InsufficientMaterial);
            if (HalfmoveClock >= 100) return new GameResult(GameResultKind.FiftyMoveRule);
            return null;
        }
    }

    public long Perft(int depth)
    {
        if (depth == 0) return 1;
        var moves = LegalMoves();
        if (depth == 1) return moves.Count;
        long n = 0;
        foreach (var m in moves) n += Applying(m).Perft(depth - 1);
        return n;
    }
}
