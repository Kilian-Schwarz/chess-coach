using System.Drawing;
using ChessCoach.App.Platform;
using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    private enum LocateOutcome { Lost, Unchanged, Changed, Failed }

    private readonly record struct TrackedBoard(MonitorInfo Mon, BoardRect Board)
    {
        public Rectangle Pixels => new((int)Math.Round(Board.X), (int)Math.Round(Board.Y), (int)Math.Round(Board.Size), (int)Math.Round(Board.Size));
    }

    private TrackedBoard? _tracked;
    private DateTime _lastFullSearch = DateTime.MinValue;
    private readonly BoardFingerprint _fp = new(), _lastFp = new();
    private bool _hasLastFp;
    private ScanResult? _cached;
    private string? _lastMonitorKey;
    private char?[][]? _lastScreen;
    private DateTime _lastChangeAt = DateTime.UtcNow;
    private DateTime? _lostSince;
    private bool _settled, _pausedShown, _pausedStatus;
    private DateTime _lastBoardSeen = DateTime.UtcNow;

    private char?[][]? _pendingScreen;
    private Placement? _stablePlacement;
    private GameUpdate? _update;
    private PieceColor? _sideToMove, _lastMe;
    private bool? _autoWhiteAtBottom;
    private string? _lastProblemFen;
    private Rectangle _boardRect;

    private void EnterPause()
    {
        if (_pausedShown) return;
        _pausedShown = true;
        _pausedStatus = true;
        HideAll();
        _tracked = null; _cached = null; _hasLastFp = false;
        SetStatus(Loc.T("status.paused", _session.Reason));
        Invalidate();
    }

    private static bool SameScreen(char?[][]? a, char?[][]? b)
    {
        if (a == null || b == null) return false;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (a[r][c] != b[r][c]) return false;
        return true;
    }

    private async Task<LocateOutcome> LocateAsync()
    {
        var templates = _templates;
        if (_tracked is { } tr && (DateTime.UtcNow - _lastFullSearch).TotalSeconds < 5)
        {
            var monRect = new Rectangle(tr.Mon.X, tr.Mon.Y, tr.Mon.Width, tr.Mon.Height);
            var region = Inflate(tr.Pixels, 0.1, monRect);
            var outcome = await Task.Run(() => ScanRegion(tr, region, templates));
            if (outcome != LocateOutcome.Lost) return outcome;
            Log.Write("Board area lost, searching all screens");
            _tracked = null;
        }
        return await Task.Run(() => FullSearch(templates));
    }

    private LocateOutcome ScanRegion(TrackedBoard tr, Rectangle region, PieceTemplates templates)
    {
        var img = _capturer.Capture(region.X, region.Y, region.Width, region.Height);
        if (img == null) return LocateOutcome.Failed;
        var expected = new BoardRect(tr.Board.X - region.X, tr.Board.Y - region.Y, tr.Board.Size);
        _fp.Compute(img, expected);
        if (_hasLastFp && _cached != null && !_fp.Differs(_lastFp)) return LocateOutcome.Unchanged;
        var scan = BoardScanner.Scan(img, templates);
        if (scan == null) return LocateOutcome.Lost;
        var b = scan.Board;
        _tracked = new TrackedBoard(tr.Mon, new BoardRect(region.X + b.X, region.Y + b.Y, b.Size));
        _fp.Compute(img, b); _lastFp.CopyFrom(_fp); _hasLastFp = true;
        _cached = scan;
        return LocateOutcome.Changed;
    }

    private LocateOutcome FullSearch(PieceTemplates templates)
    {
        var mons = ScreenCapturer.Monitors();
        if (_lastMonitorKey != null) mons = mons.OrderBy(m => m.Key == _lastMonitorKey ? 0 : 1).ToList();
        _lastFullSearch = DateTime.UtcNow;
        bool any = false;
        foreach (var m in mons)
        {
            var img = _capturer.Capture(m.X, m.Y, m.Width, m.Height);
            if (img == null) continue;
            any = true;
            var scan = BoardScanner.Scan(img, templates);
            if (scan == null) continue;
            var b = scan.Board;
            _tracked = new TrackedBoard(m, new BoardRect(m.X + b.X, m.Y + b.Y, b.Size));
            _fp.Compute(img, b); _lastFp.CopyFrom(_fp); _hasLastFp = true;
            _cached = scan;
            return LocateOutcome.Changed;
        }
        _tracked = null; _cached = null; _hasLastFp = false;
        return any ? LocateOutcome.Lost : LocateOutcome.Failed;
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (_session.IsPaused())
        {
            EnterPause();
            return;
        }
        _pausedShown = false;
        if (_pausedStatus) { _pausedStatus = false; SetStatus(Loc.T("status.searching")); Invalidate(); }

        if (_endReason != null) await FinishGameAsync(true);
        var outcome = await LocateAsync();
        ct.ThrowIfCancellationRequested();
        if (!IsActive) return;

        if (outcome == LocateOutcome.Failed)
        {
            SetStatus(Loc.T("status.captureFailed"));
            HideAll();
            _tracked = null;
            return;
        }
        if (outcome == LocateOutcome.Lost || _cached == null || _tracked == null)
        {
            _lostSince ??= DateTime.UtcNow;
            if (_endReason == null && (DateTime.UtcNow - _lastBoardSeen).TotalSeconds > 60 && (_reviewed.Count + _moveQueue.Count) >= 4) _endReason = "board gone";
            SetStatus(Loc.T("status.searching"));
            HideAll();
            _pendingScreen = null;
            _settled = false;
            return;
        }
        _lostSince = null;
        _lastBoardSeen = DateTime.UtcNow;
        if (outcome == LocateOutcome.Unchanged && _settled)
        {
            await BackgroundWorkAsync(ct);
            return;
        }

        var scan = _cached;
        var tracked = _tracked.Value;
        _lastMonitorKey = tracked.Mon.Key;
        if (!SameScreen(_lastScreen, scan.Screen)) { _lastScreen = scan.Screen; _lastChangeAt = DateTime.UtcNow; }
        _boardRect = tracked.Pixels;

        if (scan.LooksLikeStart)
        {
            bool? wb = _templates.IsEmpty ? null : BoardScanner.GuessWhiteAtBottom(scan.Screen);
            var expected = PieceTemplates.StartLayout(wb ?? true);
            bool recognised = true;
            for (int r = 0; r < 8 && recognised; r++)
                for (int c = 0; c < 8; c++)
                    if (expected[r].Length == 0 ? scan.Screen[r][c] != null : scan.Screen[r][c] != expected[r][c]) { recognised = false; break; }
            if (!recognised)
            {
                var t = new PieceTemplates();
                _autoWhiteAtBottom = t.LearnStart(scan.Features);
                _templates = t;
                SaveTemplates(t);
                _pendingScreen = null; _stablePlacement = null; _cached = null; _hasLastFp = false;
                SetStatus(Loc.T("status.learned"));
                return;
            }
        }

        if (_templates.IsEmpty && !scan.LooksLikeStart)
        {
            var t = new PieceTemplates();
            if (t.LearnEarly(scan.Features) is bool wb)
            {
                _autoWhiteAtBottom = wb;
                _templates = t;
                SaveTemplates(t);
                _pendingScreen = null; _stablePlacement = null; _cached = null; _hasLastFp = false;
                SetStatus(Loc.T("status.learned"));
                return;
            }
        }

        if (_templates.IsEmpty)
        {
            SetStatus(Loc.T("status.unknownPieces"));
            _overlay.Show(_boardRect, new OverlayState { Hint = Loc.T("overlay.learn") });
            _settled = true;
            return;
        }

        if (!SameScreen(_pendingScreen, scan.Screen))
        {
            _pendingScreen = scan.Screen;
            _cached = null; _hasLastFp = false;
            return;
        }

        bool whiteAtBottom;
        switch (Side)
        {
            case SideChoice.White: whiteAtBottom = true; break;
            case SideChoice.Black: whiteAtBottom = false; break;
            default:
                if (BoardScanner.GuessWhiteAtBottom(scan.Screen) is bool g) _autoWhiteAtBottom = g;
                whiteAtBottom = _autoWhiteAtBottom ?? true;
                break;
        }
        var me = whiteAtBottom ? PieceColor.White : PieceColor.Black;
        _lastMe = me;
        var placement = new Placement(scan.Screen, whiteAtBottom);

        if (!placement.Equals(_stablePlacement))
        {
            var marking = SideToMove.Detect(scan, whiteAtBottom, _templates);
            var previousUpdate = _update;
            var u = _record.Update(placement, whiteAtBottom, marking.Mover);
            _stablePlacement = placement;
            _update = u;
            _sideToMove = u.SideToMove;
            _suggestionCard = null; _answerCard = null; _threat = null; _threatFen = null;
            _questionCts?.Cancel(); _suggestionCts?.Cancel(); _threatCts?.Cancel();
            OnPositionChanged(previousUpdate, u, whiteAtBottom, me);
        }
        if (_pendingReview != null) await ReviewPendingAsync(me);
        if (_endReason != null) await FinishGameAsync(false);

        var toMove = (EffectiveAlwaysMyTurn && !Settings.DuoTraining) ? me : (_sideToMove ?? me);
        if (_update == null) return;
        if (!IsCoached(toMove, me))
        {
            SetStatus(Loc.T("status.opponent", Loc.Color(me)));
            ClearHints();
            _overlay.Show(_boardRect, new OverlayState { Hint = Loc.T("overlay.opponent") });
            _settled = true;
            UpdatePanel();
            await BackgroundWorkAsync(ct);
            return;
        }
        bool partner = toMove != me;
        string who = PlayerName(toMove, me);

        var pos = PositionFor(_update.Position, toMove);
        string fen = pos.Fen;
        var problem = placement.Problem(toMove);
        if (problem != null)
        {
            if (fen != _lastProblemFen) { _lastProblemFen = fen; Log.Write($"Position not checkable ({problem}): {fen}"); }
            SetStatus($"{who} · {problem}");
            ClearHints();
            _overlay.Show(_boardRect, new OverlayState { Hint = problem });
            _settled = true;
            UpdatePanel();
            return;
        }

        var result = await _engine.AnalyzeAsync(fen, EffectiveThinkMs);
        ct.ThrowIfCancellationRequested();
        if (result == null)
        {
            if (!IsActive) return;
            if (StockfishEngine.FindBinary(Settings.StockfishPath, AppPaths.AppDir) == null)
                SetStatus(Loc.T("status.noEngine"));
            return;
        }
        if (!IsActive || !ReferenceEquals(_stablePlacement, placement)) return;

        if (UciMove.Parse(result.BestMove) is not UciMove move)
        {
            string text = placement.IsInCheck(toMove) ? Loc.T("result.mate") : Loc.T("result.stalemate");
            ClearHints();
            _overlay.Show(_boardRect, new OverlayState { Hint = text });
            SetStatus($"{who} · {text}");
            _settled = true;
            UpdatePanel();
            return;
        }

        SetStatus(Loc.T("status.playing", Loc.Color(me), Loc.T(partner ? "who.partner" : "who.you")));
        var ctx = new HintContext(placement, pos, toMove, me, whiteAtBottom, result, move, fen, _boardRect, who, partner,
            placement.DescribeMove(move), result.ScoreText);
        if (_hintFen != fen) { _hintFen = fen; _hintLevel = 0; _panel.HintLevel = 0; }
        _hint = ctx;
        _panel.CanHint = !EffectiveShowBest;
        RenderHints(ctx);
        _settled = true;

        if (Settings.ShowThreats && _threatFen != fen) await ComputeThreatAsync(ctx, ct);
        await BackgroundWorkAsync(ct);
    }

    private bool IsCoached(PieceColor color, PieceColor me) => Settings.DuoTraining || color == me;

    private string PlayerName(PieceColor color, PieceColor me)
    {
        if (!Settings.DuoTraining) return Loc.T("player.you");
        return Loc.T(color == me ? "player.colorYou" : "player.colorPartner", Loc.Color(color));
    }

    private static ChessPosition PositionFor(ChessPosition pos, PieceColor toMove)
    {
        if (pos.Turn == toMove) return pos;
        var c = pos.Clone();
        c.Turn = toMove;
        c.EnPassantFile = null;
        return c;
    }

    public void ToggleSideToMove()
    {
        if (!IsActive) return;
        var current = _sideToMove ?? _lastMe;
        if (current is not PieceColor c) return;
        _sideOverride = c.Opposite();
        _sideToMove = _sideOverride;
        _suggestionCard = null;
        _suggestionCts?.Cancel();
        _hint = null; _hintFen = null; _hintLevel = 0; _panel.HintLevel = 0;
        Invalidate();
    }

    private PieceColor? _sideOverride;

    public void ResetTracking()
    {
        _pendingScreen = null;
        _stablePlacement = null;
        _update = null;
        _record = new GameRecord();
        _sideToMove = null;
        _sideOverride = null;
        _autoWhiteAtBottom = null;
        _reviewCts?.Cancel(); _suggestionCts?.Cancel(); _questionCts?.Cancel(); _threatCts?.Cancel();
        _reviewCard = null; _suggestionCard = null; _answerCard = null; _threat = null; _threatFen = null;
        _hint = null; _hintFen = null; _hintLevel = 0;
        _panel.HintLevel = 0; _panel.CanHint = false; _panel.CanExplain = false; _panel.IsAsking = false;
        _cached = null; _hasLastFp = false;
        Invalidate();
    }
}
