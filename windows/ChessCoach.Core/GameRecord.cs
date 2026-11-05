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
    public IReadOnlyList<PlayedMove> Reviewable { get; init; } = Array.Empty<PlayedMove>();
    public bool Gap { get; init; }
    public IReadOnlyList<string> Log { get; init; } = Array.Empty<string>();
    public PlayedMove? LastMove => NewMoves.Count > 0 ? NewMoves[^1] : null;

    public bool IsGameOver => Result is { Kind: GameResultKind.Checkmate or GameResultKind.Stalemate };
}

public static class ResyncContinuity
{
    public const int MaxLostPieces = 6;
    public const int MaxChangedSquares = 16;

    public static bool IsPlausible(Placement old, Placement next)
    {
        int lost = old.PieceCount - next.PieceCount;
        if (lost < 0 || lost > MaxLostPieces) return false;
        int changed = 0;
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
                if (old.Squares[r][f] != next.Squares[r][f]) changed++;
        return changed <= MaxChangedSquares;
    }
}

public sealed class GameRecord
{
    private sealed record Stash(List<PlayedMove> Moves, List<int> Breaks, ChessPosition? Start,
        ChessPosition? SegmentStart, bool Estimated, ChessPosition Current);

    public ChessPosition? StartPosition { get; private set; }

    public bool IsEstimatedStart { get; private set; }
    private readonly List<PlayedMove> _moves = new();
    public IReadOnlyList<PlayedMove> Moves => _moves;

    private readonly List<int> _breaks = new();
    public IReadOnlyList<int> Breaks => _breaks;
    private ChessPosition? _segmentStart;
    private Stash? _stash;
    public int SegmentMoves => _moves.Count - (_breaks.Count > 0 ? _breaks[^1] : 0);
    public ChessPosition? Current { get; private set; }
    public GameResult? DecidedResult => IsEstimatedStart && SegmentMoves == 0 ? null : Current?.Result;

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

    public GameUpdate Update(Placement placement, bool whiteAtBottom, PieceColor? hintedMover = null, bool forced = false)
    {
        var none = Array.Empty<PlayedMove>();
        if (placement.IsStartPosition)
        {
            if (Current is { } c0 && c0.Board.Equals(placement) && _moves.Count == 0 && !IsEstimatedStart)
                return MakeUpdate(GameChange.Unchanged, none);
            Reset(ChessPosition.Start, false);
            return MakeUpdate(GameChange.NewGame, none, log: new[] { "New game: start position" });
        }

        if (Current is not { } cur)
        {
            var pos = Estimate(placement, null, whiteAtBottom, hintedMover);
            Reset(pos, true);
            return MakeUpdate(GameChange.FirstSight, none, log: new[] { $"First sight: {pos.Fen}, side to move guessed" });
        }

        if (cur.Board.Equals(placement))
        {
            if (IsEstimatedStart && SegmentMoves == 0 && hintedMover is { } h && cur.Turn != h.Opposite())
            {
                var p = cur.Clone();
                p.Turn = h.Opposite();
                if (_moves.Count == 0) StartPosition = p;
                _segmentStart = p; Current = p;
                return MakeUpdate(GameChange.Unchanged, none, log: new[] { $"Side override: last-move marker says {h.Opposite()} to move" });
            }
            return MakeUpdate(GameChange.Unchanged, none);
        }

        var found = Explain(cur, placement, hintedMover);
        if (found != null)
        {
            Append(found.Value.Moves);
            return MakeUpdate(GameChange.Moves, found.Value.Moves,
                reviewable: found.Value.Unique ? found.Value.Moves : none, log: new[] { MoveLog(found.Value) });
        }

        if (IsEstimatedStart && SegmentMoves == 0)
        {
            var flipped = cur.Clone();
            flipped.Turn = cur.Turn.Opposite();
            flipped.EnPassantFile = null;
            found = Explain(flipped, placement, hintedMover);
            if (found != null)
            {
                if (_moves.Count == 0) StartPosition = flipped;
                _segmentStart = flipped; Current = flipped;
                Append(found.Value.Moves);
                return MakeUpdate(GameChange.Moves, found.Value.Moves,
                    log: new[] { $"Side override: {flipped.Turn} to move fits the move", MoveLog(found.Value) });
            }
        }

        if (_segmentStart is { } first)
        {
            int baseIndex = _breaks.Count > 0 ? _breaks[^1] : 0;
            var all = new List<ChessPosition> { first };
            for (int k = baseIndex; k < _moves.Count; k++) all.Add(_moves[k].After);
            for (int i = all.Count - 2; i >= Math.Max(0, all.Count - 8); i--)
            {
                if (!all[i].Board.Equals(placement)) continue;
                int dropped = all.Count - 1 - i;
                _moves.RemoveRange(_moves.Count - dropped, dropped);
                Current = all[i];
                return MakeUpdate(GameChange.Takeback, none, log: new[] { $"Take-back of {dropped} ply: {all[i].Fen}" });
            }
        }

        int missing = cur.Board.MissingPieces(placement);
        if (!forced && missing is > 0 and <= 2)
            return MakeUpdate(GameChange.Unchanged, none, log: new[] { $"Partial frame ignored: {missing} piece(s) missing" });

        if (_stash is { } s && _moves.Count == s.Moves.Count && ResyncContinuity.IsPlausible(s.Current.Board, placement))
        {
            Restore(s);
            return Update(placement, whiteAtBottom, hintedMover, forced);
        }
        var est = Estimate(placement, cur, whiteAtBottom, hintedMover);
        if (_moves.Count == 0)
        {
            Reset(est, true, keepStash: true);
            return MakeUpdate(GameChange.Resync, none, gap: true,
                log: new[] { $"Resync: board not reachable within 2 plies from {cur.Fen}, new game segment at {est.Fen}" });
        }
        if (!ResyncContinuity.IsPlausible(cur.Board, placement))
            _stash = new Stash(new List<PlayedMove>(_moves), new List<int>(_breaks), StartPosition, _segmentStart, IsEstimatedStart, cur);
        _breaks.Add(_moves.Count);
        _segmentStart = est; Current = est; IsEstimatedStart = true;
        return MakeUpdate(GameChange.Resync, none, gap: true,
            log: new[] { $"Resync: board not reachable within 2 plies from {cur.Fen}, segment break at ply {_moves.Count}, now {est.Fen}" });
    }

    private static string MoveLog((List<PlayedMove> Moves, bool Unique) found)
    {
        var sans = string.Join(" ", found.Moves.Select(m => m.San));
        var kind = found.Moves.Count == 1 ? "Move" : found.Unique ? "Moves (2 plies, missed scan)" : "Moves (2 plies, ambiguous, not rated)";
        return $"{kind} {sans} -> {found.Moves[^1].After.Fen}";
    }

    public void Seed(ChessPosition pos)
    {
        Clear();
        Reset(pos, true);
    }

    public void Clear()
    {
        StartPosition = null; _segmentStart = null; Current = null; _moves.Clear(); _breaks.Clear(); _stash = null;
        IsEstimatedStart = false;
    }

    private void Reset(ChessPosition pos, bool estimated, bool keepStash = false)
    {
        StartPosition = pos; _segmentStart = pos; Current = pos; _moves.Clear(); _breaks.Clear();
        IsEstimatedStart = estimated;
        if (!keepStash) _stash = null;
        StartedAt = DateTime.UtcNow;
    }

    private void Restore(Stash s)
    {
        _moves.Clear(); _moves.AddRange(s.Moves);
        _breaks.Clear(); _breaks.AddRange(s.Breaks);
        StartPosition = s.Start; _segmentStart = s.SegmentStart; IsEstimatedStart = s.Estimated; Current = s.Current;
        _stash = null;
    }

    private void Append(List<PlayedMove> found)
    {
        _moves.AddRange(found);
        Current = found[^1].After;
    }

    private GameUpdate MakeUpdate(GameChange change, IReadOnlyList<PlayedMove> added,
        IReadOnlyList<PlayedMove>? reviewable = null, bool gap = false, IReadOnlyList<string>? log = null)
    {
        var pos = Current!;
        return new GameUpdate
        {
            Change = change, NewMoves = added, Position = pos, Result = pos.Result,
            Estimated = IsEstimatedStart && SegmentMoves == 0,
            Reviewable = reviewable ?? Array.Empty<PlayedMove>(), Gap = gap, Log = log ?? Array.Empty<string>(),
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

    private static (List<PlayedMove> Moves, bool Unique)? Explain(ChessPosition pos, Placement placement, PieceColor? hint)
    {
        var first = pos.LegalMoves();
        foreach (var m in first)
            if (pos.Applying(m).Board.Equals(placement)) return (new List<PlayedMove> { Played(m, pos) }, true);
        var candidates = new List<List<PlayedMove>>();
        foreach (var m in first)
        {
            var next = pos.Applying(m);
            foreach (var m2 in next.LegalMoves())
                if (next.Applying(m2).Board.Equals(placement))
                    candidates.Add(new List<PlayedMove> { Played(m, pos), Played(m2, next) });
        }
        if (candidates.Count == 0) return null;

        bool unique = candidates.Select(c => string.Join(" ", c.Select(x => x.Uci))).Distinct().Count() == 1;
        if (candidates.Count > 1 && hint is { } h)
            foreach (var c in candidates) if (c[^1].Mover == h) return (c, unique);
        return (candidates[0], unique);
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
            bool brk = _breaks.Contains(i);
            if (brk) tokens.Add("{ moves missed }");
            if (m.Mover == PieceColor.White) tokens.Add($"{m.MoveNumber}.");
            else if (i == 0 || brk) tokens.Add($"{m.MoveNumber}...");
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
