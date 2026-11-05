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
    private readonly RelearnGuard _relearn = new();
    private readonly StuckRecovery _stuck = new();
    private readonly OrientationTracker _orientation = new();
    private bool _rectUnsettled, _captureFailedShown, _boardPresent, _mouseHeld, _forceResync, _windowCapture, _regionMissed;
    private DateTime _lastHiddenSearch = DateTime.MinValue;
    private DateTime? _verifyFailedSince;
    private static readonly TimeSpan HiddenSearchGap = TimeSpan.FromSeconds(15);
    private readonly BoardFingerprint _fp = new(), _lastFp = new(), _dirtyFp = new(), _lastDirtyFp = new();
    private bool _hasLastFp;
    private ScanResult? _cached;
    private RgbImage? _lastImage;
    private string? _lastMonitorKey;
    private char?[][]? _lastScreen;
    private DateTime _lastChangeAt = DateTime.UtcNow;
    private DateTime? _lostSince;
    private bool _settled, _pausedShown, _pausedStatus;
    private BoardSight _view = BoardSight.Readable, _lastView = BoardSight.Readable, _missView = BoardSight.Gone;
    private BoardColors? _colors;
    private readonly GameEndDetector _gameEnd = new();

    private char?[][]? _pendingScreen;
    private Placement? _stablePlacement;
    private GameUpdate? _update;
    private PieceColor? _sideToMove, _lastMe;
    private bool? _autoWhiteAtBottom
    {
        get => _orientation.WhiteAtBottom;
        set => _orientation.Set(value);
    }
    private string? _lastProblemFen;
    private Rectangle _boardRect;

    private const double LostGraceSeconds = 2.5, RegionMargin = 0.2;
    private char?[][]? _relearnTried;

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

    private static bool MouseDown => Native.PrimaryButtonDown();

    private void DropCapture() => _hasLastFp = false;

    private async Task<LocateOutcome> LocateAsync()
    {
        var templates = _templates;
        _regionMissed = false;
        if (_tracked is { } tr)
        {
            var monRect = new Rectangle(tr.Mon.X, tr.Mon.Y, tr.Mon.Width, tr.Mon.Height);
            var region = Inflate(tr.Pixels, RegionMargin, monRect);
            bool hasLast = _hasLastFp, hasCached = _cached != null;
            var colors = _colors;
            var probe = await Task.Run(() => ProbeRegion(tr, region, templates, hasLast, hasCached, colors));
            if (ApplyRegion(tr, probe) is { } outcome) return outcome;
            _tracked = null;
        }
        string? preferred = _lastMonitorKey;
        bool allowHide = DateTime.UtcNow - _lastHiddenSearch >= HiddenSearchGap;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = await Task.Run(() => FullSearch(templates, preferred, allowHide));
        return ApplyFullSearch(found, (int)clock.ElapsedMilliseconds);
    }

    private enum ProbeKind { CaptureFailed, Unchanged, Observed }

    private sealed record RegionProbe(ProbeKind Kind, RegionResult? Result, RgbImage? Image, Rectangle Region, bool ViaScreen);

    private RegionProbe ProbeRegion(TrackedBoard tr, Rectangle region, PieceTemplates templates, bool hasLast, bool hasCached, BoardColors? colors)
    {
        var img = _grabber.Window(ref region);
        var expected = new BoardRect(tr.Board.X - region.X, tr.Board.Y - region.Y, tr.Board.Size);
        bool viaScreen = img == null;
        if (img == null)
        {
            var visible = _grabber.Screen(region);
            if (visible == null) return new RegionProbe(ProbeKind.CaptureFailed, null, null, region, true);
            _dirtyFp.Compute(visible, expected, _overlay.Covered);
            if (hasLast && hasCached && !_dirtyFp.Differs(_lastDirtyFp)) return new RegionProbe(ProbeKind.Unchanged, null, null, region, true);
            img = CaptureScreen(region.X, region.Y, region.Width, region.Height);
            if (img == null) return new RegionProbe(ProbeKind.CaptureFailed, null, null, region, true);
            _fp.Compute(img, expected);
        }
        else
        {
            _fp.Compute(img, expected);
            if (hasLast && hasCached && !_fp.Differs(_lastFp)) return new RegionProbe(ProbeKind.Unchanged, null, null, region, false);
        }
        var result = RegionObserver.Observe(img, (region.X, region.Y), expected, templates, colors, _stabilizer);
        return new RegionProbe(ProbeKind.Observed, result, img, region, viaScreen);
    }

    private LocateOutcome? ApplyRegion(TrackedBoard tr, RegionProbe probe)
    {
        if (probe.Kind == ProbeKind.CaptureFailed) return LocateOutcome.Failed;
        _windowCapture = !probe.ViaScreen;
        if (probe.Kind == ProbeKind.Unchanged)
        {
            _verifyFailedSince = null;
            return LocateOutcome.Unchanged;
        }
        var img = probe.Image!;
        switch (probe.Result)
        {
            case RegionResult.Scanned scanned:
                if (scanned.Changed)
                {
                    _rectUnsettled = true;
                    string line = $"Board moved or resized: {(int)scanned.Rect.Size} px at {(int)scanned.Rect.X},{(int)scanned.Rect.Y}";
                    Log.Write(line);
                    _dump.Save(line, img);
                }
                return AcceptRegion(tr, scanned.Rect, scanned.Scan, probe);
            case RegionResult.Verified verified:
                return AcceptRegion(tr, tr.Board, verified.Scan, probe);
            case RegionResult.Covered:
                _missView = BoardSight.Covered;
                _verifyFailedSince = null;
                if (!_windowCapture) return RegionFailed("board covered, not captured from its own window", img);
                if (_cached == null) return RegionFailed("covered without a cached board", img);
                _regionMissed = true;
                return LocateOutcome.Unchanged;
            case RegionResult.Failed failed:
                return RegionFailed(failed.Reason, img);
            default:
                return RegionFailed("crop unreadable", img);
        }
    }

    private LocateOutcome AcceptRegion(TrackedBoard tr, BoardRect rect, ScanResult scan, RegionProbe probe)
    {
        var region = probe.Region;
        _tracked = new TrackedBoard(tr.Mon, rect);
        _fp.Compute(probe.Image!, new BoardRect(rect.X - region.X, rect.Y - region.Y, rect.Size));
        _lastFp.CopyFrom(_fp); _hasLastFp = true;
        if (probe.ViaScreen) _lastDirtyFp.CopyFrom(_dirtyFp);
        _cached = scan;
        _lastImage = probe.Image;
        _verifyFailedSince = null;
        return LocateOutcome.Changed;
    }

    private LocateOutcome? RegionFailed(string reason, RgbImage image)
    {
        _missView = BoardSight.Gone;
        _verifyFailedSince ??= DateTime.UtcNow;
        double failedFor = (DateTime.UtcNow - _verifyFailedSince.Value).TotalSeconds;
        _dump.Save($"Region miss: {reason}, verification failed for {failedFor:F1} s", image);
        if (failedFor < LostGraceSeconds && _cached != null)
        {
            _regionMissed = true;
            return LocateOutcome.Unchanged;
        }
        Log.Write($"Board area lost, searching the full screen: {reason}, verification failed for {failedFor:F1} s");
        return null;
    }

    private sealed record FullProbe(bool Any, MonitorInfo? Monitor, ScanResult? Scan, RgbImage? Image, int DetectMs);

    private FullProbe FullSearch(PieceTemplates templates, string? preferredMonitor, bool allowHide)
    {
        var mons = ScreenCapturer.Monitors();
        if (preferredMonitor != null) mons = mons.OrderBy(m => m.Key == preferredMonitor ? 0 : 1).ToList();
        var probe = SearchMonitors(mons, templates);
        if (probe.Scan != null || !allowHide || !_ui.Invoke(() => _overlay.IsVisible || _panel.IsVisible)) return probe;
        _lastHiddenSearch = DateTime.UtcNow;
        bool hidden = _ui.Invoke(() => HideForCapture(true));
        try
        {
            var second = SearchMonitors(mons, templates);
            return second with { DetectMs = second.DetectMs + probe.DetectMs };
        }
        finally { if (hidden) _ui.Invoke(RestoreAfterCapture); }
    }

    private FullProbe SearchMonitors(List<MonitorInfo> mons, PieceTemplates templates)
    {
        bool any = false;
        int detectMs = 0;
        foreach (var m in mons)
        {
            var img = _grabber.Screen(new Rectangle(m.X, m.Y, m.Width, m.Height));
            if (img == null) continue;
            any = true;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var scan = BoardScanner.Scan(img, templates);
            detectMs += (int)clock.ElapsedMilliseconds;
            if (scan == null) { _dump.Save("Full-screen search without a board", img); continue; }
            return new FullProbe(true, m, scan, img, detectMs);
        }
        return new FullProbe(any, null, null, null, detectMs);
    }

    private LocateOutcome ApplyFullSearch(FullProbe probe, int totalMs)
    {
        _windowCapture = false;
        if (probe.Scan is { } scan && probe.Monitor is { } m && probe.Image is { } img)
        {
            var b = scan.Board;
            _stabilizer.Reset();
            _relearn.Reset();
            var stable = _stabilizer.Accept(new BoardRect(m.X + b.X, m.Y + b.Y, b.Size)).Rect;
            _tracked = new TrackedBoard(m, stable);
            _fp.Compute(img, new BoardRect(stable.X - m.X, stable.Y - m.Y, stable.Size)); _lastFp.CopyFrom(_fp); _hasLastFp = true;
            _cached = scan;
            _lastImage = img;
            _verifyFailedSince = null;
            string line = $"Board found on the full screen: {(int)stable.Size} px at {(int)stable.X},{(int)stable.Y} in {totalMs} ms (detection {probe.DetectMs} ms)";
            Log.Write(line);
            _dump.Save(line, img);
            return LocateOutcome.Changed;
        }
        _tracked = null; _cached = null; _hasLastFp = false;
        if (totalMs > 300) Log.Write($"Full-screen search without a board took {totalMs} ms (detection {probe.DetectMs} ms)");
        return probe.Any ? LocateOutcome.Lost : LocateOutcome.Failed;
    }

    private bool _overlayHidden, _panelHidden;

    private bool HideForCapture(bool withPanel)
    {
        _overlayHidden = _overlay.IsVisible;
        _panelHidden = withPanel && _panel.IsVisible;
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
        var area = new Rectangle(x, y, w, h);
        bool hide = DateTime.UtcNow - _lastHiddenSearch >= HiddenSearchGap && _ui.Invoke(() => _overlay.IsVisible);
        if (!hide) return _grabber.Screen(area);
        _lastHiddenSearch = DateTime.UtcNow;
        bool hidden = _ui.Invoke(() => HideForCapture(false));
        try { return _grabber.Screen(area); }
        finally { if (hidden) _ui.Invoke(RestoreAfterCapture); }
    }

    private void ObserveEnd(BoardSight seen, GameResult? result = null, bool isStart = false)
    {
        _view = BoardReadability.Effective(seen, _windowCapture);
        if (_gameEnd.Observe(_view, _book.Plies, result, isStart, DateTime.UtcNow) is { } end) FinishGame(end);
    }

    private bool RecordDisagrees(Placement placement) =>
        placement.Equals(_stablePlacement) && _record.Current is { } current && !current.Board.Equals(placement);

    private async Task TickAsync(CancellationToken ct)
    {
        bool downAtStart = MouseDown;
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
            if (!_grabber.Failing) return;
            if (!_captureFailedShown)
            {
                _captureFailedShown = true;
                SetStatus(Loc.T("status.captureFailed"));
                HideAll();
                _tracked = null; _cached = null; _hasLastFp = false;
            }
            return;
        }
        if (_captureFailedShown) { _captureFailedShown = false; _settled = false; SetStatus(Loc.T("status.searching")); }

        if (outcome == LocateOutcome.Lost)
        {
            _lostSince ??= _verifyFailedSince ?? DateTime.UtcNow;
            if (_boardRect.Width > 0 && (DateTime.UtcNow - _lostSince.Value).TotalSeconds < LostGraceSeconds) return;
            ObserveEnd(BoardSight.Gone);
            SetStatus(Loc.T("status.searching"));
            HideAll();
            _pendingScreen = null;
            _settled = false;
            return;
        }
        _lostSince = null;
        bool held = downAtStart || MouseDown;
        if (held) { _mouseHeld = true; _stuck.Reset(); }
        if (_regionMissed)
        {
            ObserveEnd(_missView);
            return;
        }
        if (TickGate.CanSkip(outcome == LocateOutcome.Unchanged, _settled, _pendingMoves.Count > 0, _forceResync, _mouseHeld, _confirmSoon,
                _stablePlacement is { } sp && RecordDisagrees(sp), _orientation.HasEvidence))
        {
            if (_lastView != BoardSight.Readable) ObserveEnd(_lastView);
            await BackgroundWorkAsync(ct);
            return;
        }
        if (_cached is not { } scan || _tracked is not { } tracked) return;

        _lastMonitorKey = tracked.Mon.Key;
        if (!SameScreen(_lastScreen, scan.Screen)) { _lastScreen = scan.Screen; _lastChangeAt = DateTime.UtcNow; }
        _lastView = BoardReadability.Sight(scan.Features, _colors);
        if (_lastView != BoardSight.Readable)
        {
            ObserveEnd(_lastView);
            return;
        }
        _colors = BoardColors.Learn(scan.Features);
        _boardRect = tracked.Pixels;
        _boardPresent = true;
        if (_rectUnsettled) { _rectUnsettled = false; _relearn.Reset(); }

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
                _pendingScreen = null; _stablePlacement = null; _sideToMove = null;
                DropCapture();
                SetStatus(Loc.T("status.learned"));
                return;
            }
        }

        bool badFit = !scan.LooksLikeStart && (_templates.IsEmpty || _templates.FitsBadly(scan.Features));
        bool relearnDue = _relearn.Observe(badFit && !MouseDown, tracked.Board, _templates.IsEmpty ? 1 : RelearnGuard.BadFrames);
        if (relearnDue && !SameScreen(_relearnTried, scan.Screen))
        {
            _relearnTried = scan.Screen;
            string why = $"bad fit on {_relearn.Count} frames at {(int)tracked.Board.Size} px at {(int)tracked.Board.X},{(int)tracked.Board.Y}, {_record.Moves.Count} plies tracked";
            var known = new PieceTemplates();
            if (!_templates.IsEmpty && _record.Current is { } trackedPosition
                && known.LearnKnown(scan.Features, trackedPosition.Board, _autoWhiteAtBottom) is bool knownWhiteAtBottom)
            {
                _autoWhiteAtBottom = knownWhiteAtBottom;
                _templates = known;
                SaveTemplates(known);
                _pendingScreen = null; _stablePlacement = null;
                DropCapture();
                SetStatus(Loc.T("status.learned"));
                string line = $"Pieces relearned from the tracked position ({why}): {trackedPosition.Fen}";
                Log.Write(line);
                _dump.Save(line, _lastImage);
                return;
            }
            var early = new PieceTemplates();
            if (_record.Moves.Count <= EarlyStart.MaxPlies && early.LearnEarlyPosition(scan.Features) is { } found)
            {
                _autoWhiteAtBottom = found.WhiteAtBottom;
                _templates = early;
                SaveTemplates(early);
                _record.Seed(found.Position);
                _pendingScreen = null; _stablePlacement = null; _sideToMove = null;
                DropCapture();
                SetStatus(Loc.T("status.learned"));
                string line = $"Pieces relearned from an early position ({why}): {found.Position.Fen}";
                Log.Write(line);
                _dump.Save(line, _lastImage);
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
            DropCapture();
            return;
        }
        _confirmSoon = false;

        bool whiteAtBottom;
        switch (Side)
        {
            case SideChoice.White: whiteAtBottom = true; break;
            case SideChoice.Black: whiteAtBottom = false; break;
            default:
                var resolved = _orientation.Resolve(BoardScanner.GuessWhiteAtBottom(scan.Screen), scan.Screen, scan.LooksLikeStart);
                if (resolved.Reason is { } reason) Log.Write($"Orientation changed to {(resolved.WhiteAtBottom ? "white" : "black")} at the bottom: {reason}");
                whiteAtBottom = resolved.WhiteAtBottom;
                break;
        }
        var me = whiteAtBottom ? PieceColor.White : PieceColor.Black;
        _lastMe = me;
        var placement = new Placement(scan.Screen, whiteAtBottom);
        bool mouseDown = held || MouseDown;
        HandleLift(placement, me, whiteAtBottom, mouseDown);

        if (mouseDown)
        {
            _mouseHeld = true;
            _stuck.Reset();
            return;
        }
        if (_mouseHeld)
        {
            _mouseHeld = false;
            _pendingScreen = null;
            _confirmSoon = true;
            DropCapture();
            return;
        }

        if (_stuck.Observe(RecordDisagrees(placement), DateTime.UtcNow))
        {
            _forceResync = true;
            _stablePlacement = null;
            Log.Write("Screen and record disagreed for 3 s, resyncing");
            DropCapture();
            return;
        }

        if (!placement.Equals(_stablePlacement))
        {
            if (placement.IsStartPosition) ObserveEnd(BoardSight.Readable, isStart: true);
            var carried = _pendingMoves;
            _pendingMoves = new();
            var marking = SideToMove.Detect(scan, whiteAtBottom, _templates);
            var previousUpdate = _update;
            var u = _record.Update(placement, whiteAtBottom, marking.Mover, _forceResync);
            _forceResync = false;
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
        ObserveEnd(BoardSight.Readable, _record.DecidedResult);

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
        _cached = null; _hasLastFp = false; _stabilizer.Reset(); _relearn.Reset(); _stuck.Reset();
        _mouseHeld = false; _forceResync = false; _verifyFailedSince = null; _colors = null;
        _pendingMoves = new(); _reviewableKeys.Clear(); _reviewSeq++; _confirmSoon = false;
        Invalidate();
    }
}
