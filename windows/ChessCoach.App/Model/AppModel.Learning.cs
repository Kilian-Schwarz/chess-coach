using ChessCoach.App.Platform;
using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    private GameRecord _record = new();
    public Storage Storage { get; } = new(AppPaths.DataDir);
    public List<PlayerProfile> Profiles { get; private set; } = new();
    private readonly Dictionary<string, PuzzleSet> _puzzles = new();
    private GameBook _book = new();
    private readonly Dictionary<string, RecapProgress> _progress = new();
    private readonly CancellationTokenSource _recapCts = new();
    private readonly Dictionary<string, ExplanationStyle> _styles = new();
    private readonly Dictionary<string, ExplanationStyle> _reviewStyleByFen = new();
    private readonly Dictionary<string, ExplanationStyle> _themeStyle = new();
    private ExplanationStyle? _lastStyle;
    private PlayerProfile? _lastStyleProfile;
    private DateTime _lastExplainAt = DateTime.MinValue;
    private string _pgnCache = "";
    private string? _openingName;
    private GameEnd? _pendingEnd;
    private HintSet? _hintSet;
    private string? _hintSetFen;

    public GameReport? LastReport { get; private set; }
    public event Action? ReportChanged;
    public Action? OpenRecord, OpenTraining, OpenReport;
    public string? RecapSelection { get; private set; }
    public int GamePlies => _book.Plies;

    public RecapProgress? ProgressFor(string id) => _progress.GetValueOrDefault(id);

    public void ShowRecap(string? id = null)
    {
        RecapSelection = id ?? LastReport?.Id;
        OpenReport?.Invoke();
    }

    private void InitLearning()
    {
        try { Profiles = Storage.LoadProfiles(); } catch (Exception e) { Log.Write("Profiles unreadable: " + e.Message); }
        if (Profiles.Count == 0)
        {
            var first = new PlayerProfile(DefaultProfileName(), startStyle: ExplanationStyle.ShortPrinciple);
            Profiles.Add(first);
            SaveProfile(first);
        }
        if (Profiles.All(p => p.Id != Settings.ActiveProfileId)) { Settings.ActiveProfileId = Profiles[0].Id; Settings.Save(); }
    }

    public PlayerProfile Active => Profiles.FirstOrDefault(p => p.Id == Settings.ActiveProfileId) ?? Profiles[0];

    private static string DefaultProfileName()
    {
        var buffer = new System.Text.StringBuilder(256);
        int size = buffer.Capacity;
        if (Native.GetUserNameEx(3, buffer, ref size) && buffer.Length > 0) return buffer.ToString();
        return Loc.T("player.default");
    }

    public PlayerProfile? Partner
    {
        get
        {
            if (!Settings.DuoTraining) return null;
            var p = Profiles.FirstOrDefault(x => x.Id == Settings.PartnerProfileId && x.Id != Active.Id);
            if (p != null) return p;
            p = Profiles.FirstOrDefault(x => x.Id != Active.Id);
            if (p == null)
            {
                p = new PlayerProfile(Loc.T("player.partner"));
                Profiles.Add(p);
                SaveProfile(p);
            }
            Settings.PartnerProfileId = p.Id;
            Settings.Save();
            return p;
        }
    }

    public PlayerProfile AddProfile(string name)
    {
        var p = new PlayerProfile(name);
        Profiles.Add(p);
        SaveProfile(p);
        Notify();
        return p;
    }

    public void SelectProfile(string id, bool partner)
    {
        if (partner) Settings.PartnerProfileId = id; else Settings.ActiveProfileId = id;
        Settings.Save();
        Notify();
    }

    public void SaveProfile(PlayerProfile p)
    {
        try { Storage.Save(p); } catch (Exception e) { Log.Write("Profile not saved: " + e.Message); }
    }

    private PlayerProfile? ProfileFor(PieceColor color) => ProfileFor(color, _lastMe ?? PieceColor.White);

    private PlayerProfile? ProfileFor(PieceColor color, PieceColor me) => color == me ? Active : Partner;

    private PlayerContext ContextFor(PieceColor color) => (ProfileFor(color) ?? Active).PromptContext;

    public PuzzleSet PuzzlesFor(string profileId)
    {
        if (_puzzles.TryGetValue(profileId, out var s)) return s;
        try { s = Storage.LoadPuzzles(profileId); } catch { s = new PuzzleSet(); }
        return _puzzles[profileId] = s;
    }

    public void SavePuzzles(string profileId)
    {
        if (_puzzles.TryGetValue(profileId, out var s))
            try { Storage.Save(s, profileId); } catch (Exception e) { Log.Write("Puzzles not saved: " + e.Message); }
    }

    private HintSet? HintSetFor(HintContext ctx)
    {
        if (_hintSetFen == ctx.Fen) return _hintSet;
        _hintSet = Hints.Build(ctx.Placement, ctx.ToMove, ctx.Result, _threatResult, ContextFor(ctx.ToMove));
        _hintSetFen = ctx.Fen;
        return _hintSet;
    }

    private string HintTopicText(HintContext ctx) => HintSetFor(ctx)?.Stage1 ?? Loc.T("hint.fallbackTopic");
    private string HintStage2Text(HintContext ctx) => HintSetFor(ctx)?.Stage2 ?? Loc.T("hint.fallbackPiece");
    private string HintIdeaFallback(HintContext ctx) => HintSetFor(ctx)?.Stage3Fallback ?? Loc.T("hint.fallbackIdea");
    private Coach.ExplanationRequest? HintRequestFor(HintContext ctx) =>
        HintSetFor(ctx) is { } hs ? Coach.HintRequest(hs, ContextFor(ctx.ToMove)) : null;

    private void RecordHintUse(HintContext ctx, int level)
    {
        if (!_book.HintUsed.TryGetValue(ctx.Fen, out var old) || level > old) _book.HintUsed[ctx.Fen] = level;
    }

    private ExplanationStyle PickStyle(PieceColor color)
    {
        var prof = ProfileFor(color) ?? Active;
        var (style, _) = prof.NextExplanationStyle();
        _styles[style.Key] = style;
        _lastStyle = style;
        _lastStyleProfile = prof;
        _lastExplainAt = DateTime.UtcNow;
        return style;
    }

    private string? CurrentStyleKey => _lastStyle?.Key;

    private Coach.ExplanationRequest? SuggestionRequestFor(HintContext ctx) =>
        Coach.SuggestionRequest(ctx.Placement, ctx.ToMove, ctx.Fen, ctx.Result, ContextFor(ctx.ToMove), PickStyle(ctx.ToMove));

    private Coach.ExplanationRequest ReviewRequestFor(MoveReview review, PlayedMove pm)
    {
        var style = PickStyle(pm.Mover);
        _reviewStyleByFen[pm.FenBefore] = style;
        if (_reviewStyleByFen.Count > 40) _reviewStyleByFen.Clear();
        return Coach.ReviewRequest(review, ContextFor(pm.Mover), style);
    }

    private void Feedback(ExplanationFeedback f, string? key)
    {
        var style = key != null && _styles.TryGetValue(key, out var s) ? s : _lastStyle;
        var prof = _lastStyleProfile ?? Active;
        if (style == null) return;
        prof.RecordFeedback(f, style);
        SaveProfile(prof);
    }

    private void OnFeedback(CoachCard card, string key)
    {
        var f = key switch { "long" => ExplanationFeedback.TooLong, "short" => ExplanationFeedback.TooShort, _ => ExplanationFeedback.Helpful };
        Feedback(f, card.StyleKey);
    }

    private void RegisterExplainAgain() => Feedback(ExplanationFeedback.AskedAgain, _lastStyle?.Key);
    private void RegisterQuestion() => Feedback(ExplanationFeedback.AskedAgain, _lastStyle?.Key);

    private void OnPanelCollapsed(bool collapsed)
    {
        if (collapsed && (DateTime.UtcNow - _lastExplainAt).TotalSeconds < 4) Feedback(ExplanationFeedback.CollapsedImmediately, _lastStyle?.Key);
    }

    public void PuzzleAttempted(PlayerProfile prof, Puzzle p, bool correct)
    {
        if (_themeStyle.TryGetValue(p.Theme, out var st)) prof.RecordPuzzleAfterExplanation(st, correct);
    }

    private string? PanelSubtitle()
    {
        var parts = new List<string>();
        if (_openingName != null) parts.Add(Loc.T("panel.opening", _openingName));
        var p = Active;
        parts.Add(p.PlacementText ?? Curriculum.FocusLabel(p.FocusSkill));
        return string.Join(" · ", parts);
    }

    private void EnqueueForLearning(PlayedMove pm)
    {
        _book.Queue.Enqueue(pm);
        _threatResult = null;
    }

    private void LearningPositionChanged(GameUpdate? previous, GameUpdate u, PieceColor me)
    {
        if (u.NewMoves.Count > 0)
        {
            UpdateOpening();
            var white = me == PieceColor.White ? Active.Name : Partner?.Name ?? "?";
            var black = me == PieceColor.Black ? Active.Name : Partner?.Name ?? "?";
            _pgnCache = _record.Pgn(white, black, opening: _openingName);
        }
    }

    private void UpdateOpening()
    {
        if (_openings == null) return;
        var positions = _record.Positions;
        var moves = _record.Moves;
        var epds = new List<string>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
            epds.Add(Openings.Epd(positions[i], i > 0 && i - 1 < moves.Count ? moves[i - 1].Move : null));
        _openingName = _openings.LongestName(epds)?.Display;
    }

    public string CurrentPgn() => _record.IsEmpty ? _pgnCache : _record.Pgn(Active.Name, Partner?.Name ?? "?", opening: _openingName);

    public void RequestEndGame()
    {
        var known = _record.Current?.Result is { } r ? GameEndDetector.For(r) : null;
        _pendingEnd = new GameEnd(GameEndReason.Manual, known?.Result ?? "*");
        Invalidate();
    }

    public bool CanFinish => _book.Plies >= 2;

    private async Task BackgroundWorkAsync(CancellationToken ct)
    {
        var me = _lastMe ?? PieceColor.White;
        for (int i = 0; i < 3 && _book.Queue.Count > 0 && IsActive && !ct.IsCancellationRequested; i++)
            await AnalyzeMoveAsync(_book, _book.Queue.Dequeue(), me, 300);
    }

    private async Task AnalyzeMoveAsync(GameBook book, PlayedMove pm, PieceColor me, int ms)
    {
        if (pm.Before.Board.Problem(pm.Mover) != null) return;
        var before = await _engine.AnalyzeAsync(pm.FenBefore, ms, false);
        if (before == null) return;
        bool over = pm.After.Result is { Kind: GameResultKind.Checkmate or GameResultKind.Stalemate };
        EngineResult? after = over ? null : await _engine.AnalyzeAsync(pm.FenAfter, ms, false);
        int valueAfter = after is { BestMove.Length: > 0 } ? -after.Value : (pm.After.IsCheck ? Accuracy.MateValue : 0);
        book.Reviewed.Add(new ReviewedMove(pm.Mover == PieceColor.White ? "w" : "b", pm.Uci, pm.San, pm.FenBefore,
            before.Value, valueAfter, before.BestMove.Length > 0 ? before.BestMove : null));

        if (!IsCoached(pm.Mover, me) || !book.Rated.Contains(MoveKey(pm)) || ProfileFor(pm.Mover, me) is not { } prof) return;
        var review = Coach.Review(pm.Before.Board, pm.Move, pm.After.Board, pm.Mover, before, after);
        EngineResult? threatRes = null;
        if (Threats.NullMoveFen(pm.Before) is { } nf) threatRes = await _engine.AnalyzeAsync(nf, ms, false);
        var analysis = MoveAnalyzer.Analyze(new MoveAnalysisInput(review, before, after, threatRes, pm.MoveNumber));
        prof.Apply(analysis);
        prof.RecordHintUse(book.HintUsed.GetValueOrDefault(pm.FenBefore));
        if (analysis.Theme is { } th && _reviewStyleByFen.TryGetValue(pm.FenBefore, out var st)) _themeStyle[th.Raw()] = st;
        var puzzle = Puzzle.Make(review, before, analysis.MistakeType, analysis.Theme, prof.Level);
        if (puzzle != null && PuzzlesFor(prof.Id).Add(puzzle)) SavePuzzles(prof.Id);
        SaveProfile(prof);
        Notify();
    }

    private void FinishGame(GameEnd end)
    {
        var book = _book;
        _book = new GameBook();
        var pgn = _pgnCache;
        var opening = _openingName;
        if (end.Reason is GameEndReason.Overlay or GameEndReason.BoardGone or GameEndReason.Manual)
        {
            _record = new GameRecord(); _update = null; _stablePlacement = null; _pendingScreen = null;
            _cached = null; _hasLastFp = false; _pgnCache = ""; _openingName = null;
        }
        _gameEnd.Reset();
        if (book.Plies < 2) return;
        Log.Write($"Game end: {end.ReasonKey}, result {end.Result}, {book.Plies} plies");

        var me = _lastMe ?? PieceColor.White;
        var whiteProf = me == PieceColor.White ? Active : Partner;
        var blackProf = me == PieceColor.Black ? Active : Partner;
        var report = GameReport.Build(book.Reviewed, null, DateTime.UtcNow, whiteProf?.Id, blackProf?.Id, opening);
        report.Result = end.Result;
        report.EndReason = end.ReasonKey;
        SaveReport(report, pgn);
        LastReport = report;
        RecapSelection = report.Id;
        _progress[report.Id] = new RecapProgress(report.Id, book.Reviewed.Count, book.Plies);
        ReportChanged?.Invoke();
        if (Settings.OpenRecap && !end.Silent) OpenReport?.Invoke();
        _ = CompleteRecapAsync(report, book, me, whiteProf, blackProf);
    }

    private void SaveReport(GameReport report, string? pgn = null)
    {
        try { Storage.Save(report, pgn); } catch (Exception e) { Log.Write("Game not saved: " + e.Message); }
    }

    private async Task CompleteRecapAsync(GameReport report, GameBook book, PieceColor me, PlayerProfile? whiteProf, PlayerProfile? blackProf)
    {
        try
        {
            int total = book.Reviewed.Count + book.Queue.Count, sinceSave = 0;
            while (book.Queue.Count > 0)
            {
                _recapCts.Token.ThrowIfCancellationRequested();
                await AnalyzeMoveAsync(book, book.Queue.Dequeue(), me, 200);
                _progress[report.Id] = new RecapProgress(report.Id, book.Reviewed.Count, total);
                if (++sinceSave % 4 == 0 || book.Queue.Count == 0)
                {
                    report.Rebuild(book.Reviewed);
                    SaveReport(report);
                }
                ReportChanged?.Invoke();
            }
            report.Rebuild(book.Reviewed);
            _progress.Remove(report.Id);
            foreach (var (prof, c) in new[] { (whiteProf, "w"), (blackProf, "b") })
            {
                if (prof == null) continue;
                prof.CompleteGame(report.Side(c).Performance);
                prof.LearningSpeed = PuzzleScheduler.AdjustedLearningSpeed(prof.LearningSpeed, PuzzlesFor(prof.Id).Puzzles);
                SaveProfile(prof);
            }
            string color = me == PieceColor.White ? "w" : "b";
            var mine = report.Side(color);
            _reviewCard = new CoachCard("report|" + report.Id, CardKind.Info, Loc.T("gameCard.title"))
            {
                Lines = new[] { Loc.T("gameCard.line", $"{mine.Accuracy:0}", mine.Mistakes + mine.Blunders),
                                Active.PlacementText ?? Loc.T("gameCard.rating", $"{Active.Rating:0}") },
                Note = Loc.T("gameCard.note"),
            };
            UpdatePanel();
            Notify();
            await StreamSummaryAsync(report, color, me);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Log.Write("Recap failed: " + e.Message); _progress.Remove(report.Id); ReportChanged?.Invoke(); }
    }

    private Task StreamSummaryAsync(GameReport report, string color, PieceColor me)
    {
        var done = new TaskCompletionSource();
        string fallback = report.FallbackSummary(color);
        if (!LlmConfigured || report.Moves.Count == 0)
        {
            report.Summary = fallback;
            SaveReport(report);
            ReportChanged?.Invoke();
            return Task.CompletedTask;
        }
        var req = report.SummaryRequest(color, ContextFor(me));
        report.Summary = "";
        ReportChanged?.Invoke();
        Explain(req.System, req.User, req.MaxTokens, (text, finished) =>
        {
            report.Summary = text;
            if (finished) { SaveReport(report); done.TrySetResult(); }
            ReportChanged?.Invoke();
        }, msg =>
        {
            Log.Write("Summary failed: " + msg);
            report.Summary = fallback;
            SaveReport(report);
            ReportChanged?.Invoke();
            done.TrySetResult();
        });
        return done.Task;
    }

    public void WireTray(TrayController tray)
    {
        tray.OpenRecord = () => OpenRecord?.Invoke();
        tray.OpenTraining = () => OpenTraining?.Invoke();
        tray.EndGame = RequestEndGame;
        tray.OpenReport = () => ShowRecap();
        tray.CopyPgn = () =>
        {
            var pgn = CurrentPgn();
            if (pgn.Length > 0) System.Windows.Clipboard.SetText(pgn);
        };
        _panel.SetExtraActions(new[]
        {
            new PanelAction("record", Loc.T("panel.record"), () => OpenRecord?.Invoke()),
            new PanelAction("training", Loc.T("panel.training"), () => OpenTraining?.Invoke()),
            new PanelAction("report", Loc.T("panel.report"), () => ShowRecap()),
        });
    }
}
