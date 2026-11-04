using System.Globalization;
using System.Text;

namespace ChessCoach.Core;

public sealed record PlayedMove(UciMove Move, string San, PieceColor Mover,
    ChessPosition Before, ChessPosition After, int MoveNumber)
{
    public string SanDisplay => Core.San.Localize(San);
    public string Uci => Move.Uci;
    public string FenBefore => Before.Fen;
    public string FenAfter => After.Fen;
}

public enum GameChange
{
    Unchanged,

    NewGame,

    Moves,

    Takeback,

    Resync,

    FirstSight,
}

public sealed class GameUpdate
{
    public GameChange Change { get; init; }

    public IReadOnlyList<PlayedMove> NewMoves { get; init; } = Array.Empty<PlayedMove>();
    public ChessPosition Position { get; init; } = null!;
    public string Fen => Position.Fen;
    public PieceColor SideToMove => Position.Turn;

    public GameResult? Result { get; init; }

    public bool Estimated { get; init; }
    public PlayedMove? LastMove => NewMoves.Count > 0 ? NewMoves[^1] : null;

    public bool IsGameOver => Result is { Kind: GameResultKind.Checkmate or GameResultKind.Stalemate };
}

public sealed class GameRecord
{
    public ChessPosition? StartPosition { get; private set; }

    public bool IsEstimatedStart { get; private set; }
    private readonly List<PlayedMove> _moves = new();
    public IReadOnlyList<PlayedMove> Moves => _moves;

    public List<List<PlayedMove>> EarlierSegments { get; } = new();
    public ChessPosition? Current { get; private set; }

    public DateTime StartedAt { get; private set; } = DateTime.UtcNow;

    public bool IsEmpty => _moves.Count == 0;

    public List<ChessPosition> Positions
    {
        get
        {
            var l = new List<ChessPosition>();
            if (StartPosition is null) return l;
            l.Add(StartPosition);
            foreach (var m in _moves) l.Add(m.After);
            return l;
        }
    }

    public GameUpdate Update(Placement placement, bool whiteAtBottom, PieceColor? hintedMover = null)
    {
        if (placement.IsStartPosition)
        {
            if (Current is { } c0 && c0.Board.Equals(placement) && _moves.Count == 0 && !IsEstimatedStart)
                return MakeUpdate(GameChange.Unchanged, Array.Empty<PlayedMove>());
            if (_moves.Count > 0) EarlierSegments.Add(new List<PlayedMove>(_moves));
            Reset(ChessPosition.Start, false);
            return MakeUpdate(GameChange.NewGame, Array.Empty<PlayedMove>());
        }

        if (Current is not { } cur)
        {
            var pos = Estimate(placement, null, whiteAtBottom, hintedMover);
            Reset(pos, true);
            return MakeUpdate(GameChange.FirstSight, Array.Empty<PlayedMove>());
        }

        if (cur.Board.Equals(placement))
        {
            if (IsEstimatedStart && _moves.Count == 0 && hintedMover is { } h && cur.Turn != h.Opposite())
            {
                var p = cur.Clone();
                p.Turn = h.Opposite();
                StartPosition = p; Current = p;
            }
            return MakeUpdate(GameChange.Unchanged, Array.Empty<PlayedMove>());
        }

        var found = Explain(cur, placement, hintedMover);
        if (found != null)
        {
            Append(found);
            return MakeUpdate(GameChange.Moves, found);
        }

        if (IsEstimatedStart && _moves.Count == 0)
        {
            var flipped = cur.Clone();
            flipped.Turn = cur.Turn.Opposite();
            flipped.EnPassantFile = null;
            found = Explain(flipped, placement, hintedMover);
            if (found != null)
            {
                StartPosition = flipped; Current = flipped;
                Append(found);
                return MakeUpdate(GameChange.Moves, found);
            }
        }

        var all = Positions;
        for (int i = all.Count - 2; i >= Math.Max(0, all.Count - 8); i--)
        {
            if (!all[i].Board.Equals(placement)) continue;
            _moves.RemoveRange(i, _moves.Count - i);
            Current = all[i];
            return MakeUpdate(GameChange.Takeback, Array.Empty<PlayedMove>());
        }

        if (_moves.Count > 0) EarlierSegments.Add(new List<PlayedMove>(_moves));
        var est = Estimate(placement, cur, whiteAtBottom, hintedMover);
        Reset(est, true);
        return MakeUpdate(GameChange.Resync, Array.Empty<PlayedMove>());
    }

    public void Clear()
    {
        StartPosition = null; Current = null; _moves.Clear(); EarlierSegments.Clear(); IsEstimatedStart = false;
    }

    private void Reset(ChessPosition pos, bool estimated)
    {
        StartPosition = pos; Current = pos; _moves.Clear(); IsEstimatedStart = estimated;
        StartedAt = DateTime.UtcNow;
    }

    private void Append(List<PlayedMove> found)
    {
        _moves.AddRange(found);
        Current = found[^1].After;
    }

    private GameUpdate MakeUpdate(GameChange change, IReadOnlyList<PlayedMove> added)
    {
        var pos = Current!;
        return new GameUpdate
        {
            Change = change, NewMoves = added, Position = pos, Result = pos.Result,
            Estimated = IsEstimatedStart && _moves.Count == 0,
        };
    }

    private static ChessPosition Estimate(Placement placement, ChessPosition? previous, bool whiteAtBottom, PieceColor? hint)
    {
        PieceColor turn;
        if (hint is { } h) turn = h.Opposite();
        else if (previous != null && placement.Mover(previous.Board) is { } m) turn = m.Opposite();
        else turn = whiteAtBottom ? PieceColor.White : PieceColor.Black;
        if (placement.IsInCheck(turn.Opposite())) turn = turn.Opposite();
        var rights = (previous?.Castling ?? CastlingRights.All).LimitedTo(placement);
        return new ChessPosition(placement, turn, rights, null, 0, previous?.FullmoveNumber ?? 1);
    }

    private static PlayedMove Played(UciMove m, ChessPosition pos) =>
        new(m, San.Of(m, pos), pos.Turn, pos, pos.Applying(m), pos.FullmoveNumber);

    private static List<PlayedMove>? Explain(ChessPosition pos, Placement placement, PieceColor? hint)
    {
        var first = pos.LegalMoves();
        foreach (var m in first)
            if (pos.Applying(m).Board.Equals(placement)) return new List<PlayedMove> { Played(m, pos) };
        var candidates = new List<List<PlayedMove>>();
        foreach (var m in first)
        {
            var next = pos.Applying(m);
            foreach (var m2 in next.LegalMoves())
                if (next.Applying(m2).Board.Equals(placement))
                    candidates.Add(new List<PlayedMove> { Played(m, pos), Played(m2, next) });
        }
        if (candidates.Count == 0) return null;

        if (candidates.Count > 1 && hint is { } h)
            foreach (var c in candidates) if (c[^1].Mover == h) return c;
        return candidates[0];
    }

    public string Pgn(string white = "?", string black = "?", string @event = "Chess Coach",
        DateTime? date = null, string? opening = null, string? result = null)
    {
        var d = date ?? StartedAt;
        var local = d.Kind == DateTimeKind.Unspecified ? d : d.ToLocalTime();
        string res = result ?? Current?.Result?.Pgn ?? "*";
        var head = new List<(string, string)>
        {
            ("Event", @event), ("Site", "?"), ("Date", local.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)),
            ("Round", "?"), ("White", white), ("Black", black), ("Result", res),
        };
        if (!string.IsNullOrEmpty(opening)) head.Add(("Opening", opening));
        if (StartPosition is { } s && !s.Equals(ChessPosition.Start))
        {
            head.Add(("SetUp", "1")); head.Add(("FEN", s.Fen));
        }
        var text = new StringBuilder(string.Join("\n", head.Select(h => $"[{h.Item1} \"{h.Item2.Replace("\"", "'")}\"]")));
        var tokens = new List<string>();
        for (int i = 0; i < _moves.Count; i++)
        {
            var m = _moves[i];
            if (m.Mover == PieceColor.White) tokens.Add($"{m.MoveNumber}.");
            else if (i == 0) tokens.Add($"{m.MoveNumber}...");
            tokens.Add(m.San);
        }
        tokens.Add(res);
        var lines = new List<string>(); var line = "";
        foreach (var t in tokens)
        {
            if (line.Length > 0 && line.Length + 1 + t.Length > 80) { lines.Add(line); line = t; }
            else line += (line.Length == 0 ? "" : " ") + t;
        }
        if (line.Length > 0) lines.Add(line);
        text.Append("\n\n").Append(string.Join("\n", lines)).Append('\n');
        return text.ToString();
    }
}
