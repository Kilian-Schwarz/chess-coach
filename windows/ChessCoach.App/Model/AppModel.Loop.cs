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
    private readonly RectStabilizer _stabilizer = new();
    private int _regionMisses;
    private DateTime _lastFullSearch = DateTime.MinValue;
    private readonly BoardFingerprint _fp = new(), _lastFp = new(), _dirtyFp = new(), _lastDirtyFp = new();
    private bool _hasLastFp;
    private ScanResult? _cached;
    private string? _lastMonitorKey;
    private char?[][]? _lastScreen;
    private DateTime _lastChangeAt = DateTime.UtcNow;
    private DateTime? _lostSince;
    private bool _settled, _pausedShown, _pausedStatus;
    private BoardSight _view = BoardSight.Readable;
    private bool _ringSeen;
    private BoardColors? _colors;
    private readonly GameEndDetector _gameEnd = new();

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
        _tracked = null; _cached = null; _hasLastFp = false; _stabilizer.Reset(); _gameEnd.Reset(); _view = BoardSight.Readable;
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
        _view = BoardSight.Readable;
        _ringSeen = false;
        if (_tracked is { } tr && (DateTime.UtcNow - _lastFullSearch).TotalSeconds < 5)
        {
            var monRect = new Rectangle(tr.Mon.X, tr.Mon.Y, tr.Mon.Width, tr.Mon.Height);
            string reason = "no capture";
            foreach (var factor in new[] { 0.12, 0.3 })
            {
                var region = Inflate(tr.Pixels, factor, monRect);
                bool allowUnchanged = factor == 0.12, hasLast = _hasLastFp, hasCached = _cached != null;
                var colors = _colors;
                var probe = await Task.Run(() => ProbeRegion(tr, region, templates, allowUnchanged, hasLast, hasCached, colors));
                var outcome = ApplyRegion(tr, probe);
                if (outcome != LocateOutcome.Lost)
                {
                    if (outcome == LocateOutcome.Failed) reason = "region capture failed";
                    else { _regionMisses = 0; return outcome; }
                    break;
                }
                reason = $"no board in a {region.Width}x{region.Height} crop (margin {(int)(factor * 100)} %)";
            }
            if (_ringSeen && _cached != null)
            {
                _view = BoardSight.Covered;
                return LocateOutcome.Unchanged;
            }
            _regionMisses++;
            if (_regionMisses < 3 && _cached != null) return LocateOutcome.Unchanged;
            Log.Write($"Board area lost, searching all screens: {reason}, {_regionMisses} misses in a row");
            _tracked = null;
        }
        _regionMisses = 0;
        _lastFullSearch = DateTime.UtcNow;
        string? preferred = _lastMonitorKey;
        var knownColors = _colors;
        return ApplyFullSearch(await Task.Run(() => FullSearch(templates, preferred, knownColors)));
    }

    private sealed record RegionProbe(LocateOutcome Outcome, bool RingSeen, ScanResult? Scan, RgbImage? Image, Rectangle Region, bool ViaScreen);

    private RegionProbe ProbeRegion(TrackedBoard tr, Rectangle region, PieceTemplates templates, bool allowUnchanged, bool hasLast, bool hasCached, BoardColors? colors)
    {
        var img = _capturer.CaptureWindow(ref region);
        var expected = new BoardRect(tr.Board.X - region.X, tr.Board.Y - region.Y, tr.Board.Size);
        bool viaScreen = img == null;
        if (img == null)
        {
            var visible = _capturer.Capture(region.X, region.Y, region.Width, region.Height);
            if (visible == null) return new RegionProbe(LocateOutcome.Failed, false, null, null, region, true);
            _dirtyFp.Compute(visible, expected);
            if (allowUnchanged && hasLast && hasCached && !_dirtyFp.Differs(_lastDirtyFp)) return new RegionProbe(LocateOutcome.Unchanged, false, null, null, region, true);
            img = CaptureScreen(region.X, region.Y, region.Width, region.Height);
            if (img == null) return new RegionProbe(LocateOutcome.Failed, false, null, null, region, true);
            _fp.Compute(img, expected);
        }
        else
        {
            _fp.Compute(img, expected);
            if (allowUnchanged && hasLast && hasCached && !_fp.Differs(_lastFp)) return new RegionProbe(LocateOutcome.Unchanged, false, null, null, region, false);
        }
        var scan = BoardScanner.Scan(img, templates);
        if (scan == null)
        {
            bool covered = BoardReadability.Sight(BoardScanner.Analyze(img, expected, templates).Features, colors) == BoardSight.Covered;
            return new RegionProbe(LocateOutcome.Lost, covered, null, null, region, viaScreen);
        }
        if (!BoardReadability.IsReadable(scan.Features))
            return new RegionProbe(LocateOutcome.Lost, BoardReadability.Sight(scan.Features, colors) == BoardSight.Covered, null, null, region, viaScreen);
        return new RegionProbe(LocateOutcome.Changed, false, scan, img, region, viaScreen);
    }

    private LocateOutcome ApplyRegion(TrackedBoard tr, RegionProbe probe)
    {
        _ringSeen |= probe.RingSeen;
        if (probe.Outcome != LocateOutcome.Changed || probe.Scan is not { } scan || probe.Image is not { } img) return probe.Outcome;
        var region = probe.Region;
        var b = scan.Board;
        var stable = _stabilizer.Accept(new BoardRect(region.X + b.X, region.Y + b.Y, b.Size));
        if (stable.Changed) Log.Write($"Board moved or resized: {(int)stable.Rect.Size} px at {(int)stable.Rect.X},{(int)stable.Rect.Y}");
        _colors = BoardColors.Learn(scan.Features);
        _tracked = new TrackedBoard(tr.Mon, stable.Rect);
        _fp.Compute(img, new BoardRect(stable.Rect.X - region.X, stable.Rect.Y - region.Y, stable.Rect.Size));
        _lastFp.CopyFrom(_fp); _hasLastFp = true;
        if (probe.ViaScreen) _lastDirtyFp.CopyFrom(_dirtyFp);
        _cached = scan;
        return LocateOutcome.Changed;
    }

    private sealed record FullProbe(bool Any, bool Covered, MonitorInfo? Monitor, ScanResult? Scan, RgbImage? Image);

    private FullProbe FullSearch(PieceTemplates templates, string? preferredMonitor, BoardColors? colors)
    {
        var mons = ScreenCapturer.Monitors();
        if (preferredMonitor != null) mons = mons.OrderBy(m => m.Key == preferredMonitor ? 0 : 1).ToList();
        bool any = false, covered = false;
        bool hidden = _ui.Invoke(HideForCapture);
        try
        {
            foreach (var m in mons)
            {
                var img = _capturer.Capture(m.X, m.Y, m.Width, m.Height);
                if (img == null) continue;
                any = true;
                var scan = BoardScanner.Scan(img, templates);
                if (scan == null) continue;
                var sight = BoardReadability.Sight(scan.Features, colors);
                if (sight != BoardSight.Readable)
                {
                    covered |= sight == BoardSight.Covered;
                    continue;
                }
                return new FullProbe(true, covered, m, scan, img);
            }
        }
        finally { if (hidden) _ui.Invoke(RestoreAfterCapture); }
        return new FullProbe(any, covered, null, null, null);
    }

    private LocateOutcome ApplyFullSearch(FullProbe probe)
    {
        if (probe.Covered) _view = BoardSight.Covered;
        if (probe.Scan is { } scan && probe.Monitor is { } m && probe.Image is { } img)
        {
            _colors = BoardColors.Learn(scan.Features);
            var b = scan.Board;
            _stabilizer.Reset();
            var stable = _stabilizer.Accept(new BoardRect(m.X + b.X, m.Y + b.Y, b.Size)).Rect;
            _tracked = new TrackedBoard(m, stable);
            Log.Write($"Board found on the full screen: {(int)stable.Size} px at {(int)stable.X},{(int)stable.Y}");
            _fp.Compute(img, b); _lastFp.CopyFrom(_fp); _hasLastFp = true;
            _cached = scan;
            return LocateOutcome.Changed;
        }
        _tracked = null; _cached = null; _hasLastFp = false;
        if (_view != BoardSight.Covered) _view = BoardSight.Gone;
        return probe.Any ? LocateOutcome.Lost : LocateOutcome.Failed;
    }

    private const double BoardGoneGraceSeconds = 2.5;
    private char?[][]? _relearnTried;

    private bool _overlayHidden, _panelHidden;

    private bool HideForCapture()
    {
        _overlayHidden = _overlay.IsVisible;
        _panelHidden = _panel.IsVisible;
        if (_overlayHidden) _overlay.Visibility = System.Windows.Visibility.Hidden;
        if (_panelHidden) _panel.Visibility = System.Windows.Visibility.Hidden;
        if (_overlayHidden || _panelHidden) Native.DwmFlush();
        return _overlayHidden || _panelHidden;
    }

    private void RestoreAfterCapture()
    {
        if (_overlayHidden) _overlay.Visibility = System.Windows.Visibility.Visible;
        if (_panelHidden) _panel.Visibility = System.Windows.Visibility.Visible;
    }

    private RgbImage? CaptureScreen(int x, int y, int w, int h)
    {
        bool hidden = _ui.Invoke(HideForCapture);
        try { return _capturer.Capture(x, y, w, h); }
        finally { if (hidden) _ui.Invoke(RestoreAfterCapture); }
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

        if (_pendingEnd is { } requested) { _pendingEnd = null; FinishGame(requested); }
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
        if (_view != BoardSight.Readable && _gameEnd.Observe(_view, _book.Plies, null, false, DateTime.UtcNow) is { } hiddenEnd)
            FinishGame(hiddenEnd);
        if (outcome == LocateOutcome.Lost || _cached == null || _tracked == null)
        {
            _lostSince ??= DateTime.UtcNow;
            if (_view == BoardSight.Gone && (DateTime.UtcNow - _lostSince.Value).TotalSeconds >= BoardGoneGraceSeconds)
            {
                SetStatus(Loc.T("status.searching"));
                HideAll();
            }
            _pendingScreen = null;
            _settled = false;
            return;
        }
        _lostSince = null;
        if (outcome == LocateOutcome.Unchanged && _settled && _pendingMoves.Count == 0)
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
                _pendingScreen = null; _stablePlacement = null; _cached = null; _hasLastFp = false; _sideToMove = null;
                SetStatus(Loc.T("status.learned"));
                return;
            }
        }

        if (!scan.LooksLikeStart && !SameScreen(_relearnTried, scan.Screen) && (_templates.IsEmpty || _templates.FitsBadly(scan.Features)))
        {
            _relearnTried = scan.Screen;
            if (!_templates.IsEmpty && _record.Current is { } trackedPosition)
            {
                var known = new PieceTemplates();
                if (known.LearnKnown(scan.Features, trackedPosition.Board, _autoWhiteAtBottom) is bool knownWhiteAtBottom)
                {
                    _autoWhiteAtBottom = knownWhiteAtBottom;
                    _templates = known;
                    SaveTemplates(known);
                    _pendingScreen = null; _stablePlacement = null; _cached = null; _hasLastFp = false;
                    SetStatus(Loc.T("status.learned"));
                    Log.Write($"Pieces relearned from the tracked position: {trackedPosition.Fen}");
                    return;
                }
            }
            if (PieceTemplates.Relearned(scan.Features) is { } relearned)
            {
                _autoWhiteAtBottom = relearned.WhiteAtBottom;
                _templates = relearned.Templates;
                SaveTemplates(_templates);
                if (relearned.Position is { } seeded) _record.Seed(seeded);
                _pendingScreen = null; _stablePlacement = null; _cached = null; _hasLastFp = false; _sideToMove = null;
                SetStatus(Loc.T("status.learned"));
                Log.Write($"Pieces relearned from an early position: {relearned.Position?.Fen}");
                return;
            }
        }

        if (_templates.IsEmpty)
        {
            SetStatus(Loc.T("status.unknownPieces"));
            ShowOverlay(_boardRect, new OverlayState { Hint = Loc.T("overlay.learn") });
            _settled = true;
            return;
        }

        if (scan.TooManyUnclassified)
        {
            SetStatus(CoreLocale.Get("problem.kings"));
            return;
        }

        if (!SameScreen(_pendingScreen, scan.Screen))
        {
            _pendingScreen = scan.Screen;
            _confirmSoon = _tracked != null;
            _cached = null; _hasLastFp = false;
            return;
        }
        _confirmSoon = false;

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
        if (HandleLift(placement, me, whiteAtBottom)) return;
        if (!placement.Equals(_stablePlacement) && Native.PrimaryButtonDown())
        {
            _pendingScreen = null; _cached = null; _hasLastFp = false; _confirmSoon = true;
            return;
        }

        if (!placement.Equals(_stablePlacement))
        {
            var carried = _pendingMoves;
            _pendingMoves = new();
            var marking = SideToMove.Detect(scan, whiteAtBottom, _templates);
            var previousUpdate = _update;
            var u = _record.Update(placement, whiteAtBottom, marking.Mover);
            foreach (var line in u.Log) Log.Write(line);
            _stablePlacement = placement;
            _update = u;
            _sideToMove = u.SideToMove;
            _suggestionCard = null; _answerCard = null; _threat = null; _threatFen = null;
            _questionCts?.Cancel(); _suggestionCts?.Cancel(); _threatCts?.Cancel();
            OnPositionChanged(previousUpdate, u, whiteAtBottom, me);
            ConfirmReviews(u, marking, me, carried);
        }
        else if (_pendingMoves.Count > 0)
            ConfirmPending(SideToMove.Detect(scan, whiteAtBottom, _templates), me);
        if (_view == BoardSight.Readable && _gameEnd.Observe(BoardSight.Readable, _book.Plies, _update?.Result, placement.IsStartPosition, DateTime.UtcNow) is { } end)
            FinishGame(end);

        var toMove = (EffectiveAlwaysMyTurn && !Settings.DuoTraining) ? me : (_sideToMove ?? me);
        if (_update == null) return;
        if (!IsCoached(toMove, me))
        {
            SetStatus(Loc.T("status.opponent", Loc.Color(me)));
            ClearHints();
            ShowOverlay(_boardRect, new OverlayState { Hint = Loc.T("overlay.opponent") });
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
            ShowOverlay(_boardRect, new OverlayState { Hint = problem });
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
            ShowOverlay(_boardRect, new OverlayState { Hint = text });
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
        _overlayEnd = null;
        _sideToMove = null;
        _sideOverride = null;
        _autoWhiteAtBottom = null;
        _reviewCts?.Cancel(); _suggestionCts?.Cancel(); _questionCts?.Cancel(); _threatCts?.Cancel();
        _reviewCard = null; _suggestionCard = null; _answerCard = null; _threat = null; _threatFen = null;
        ClearLift();
        _hint = null; _hintFen = null; _hintLevel = 0;
        _panel.HintLevel = 0; _panel.CanHint = false; _panel.CanExplain = false; _panel.IsAsking = false;
        _cached = null; _hasLastFp = false; _stabilizer.Reset();
        _pendingMoves = new(); _reviewableKeys.Clear(); _reviewSeq++; _confirmSoon = false;
        Invalidate();
    }
}
