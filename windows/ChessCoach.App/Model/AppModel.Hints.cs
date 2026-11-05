using System.Drawing;
using ChessCoach.App.Platform;
using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    internal sealed record HintContext(Placement Placement, ChessPosition Position, PieceColor ToMove, PieceColor Me,
        bool WhiteAtBottom, EngineResult Result, UciMove Move, string Fen, Rectangle BoardRect, string Who, bool Partner,
        string Text, string Score);

    private HintContext? _hint;
    private string? _hintFen;
    private int _hintLevel;
    private CoachCard? _reviewCard, _suggestionCard, _answerCard;
    private CancellationTokenSource? _reviewCts, _suggestionCts, _questionCts, _threatCts;
    private Threats.Threat? _threat;
    private string? _threatFen;
    private Coach.ExplanationRequest? _lastReviewReq;
    private List<PlayedMove> _pendingMoves = new();
    private int _reviewSeq;
    private bool _confirmSoon;
    private int _gapCounter;
    private bool _panelDirty;
    private EngineResult? _threatResult;

    private static string MoveKey(PlayedMove pm) => $"{pm.FenBefore}|{pm.Uci}";

    private void OnPositionChanged(GameUpdate? previous, GameUpdate u, bool whiteAtBottom, PieceColor me)
    {
        _sideOverride = null;
        if (u.Change == GameChange.NewGame) { _reviewCard = null; _overlayEnd = null; }
        if (u.Change == GameChange.Moves && _overlayEnd is { } ended)
        {
            _overlayEnd = null;
            if (u.NewMoves[0].FenBefore == ended.Fen) RetractEnd(ended, u.NewMoves.Count);
        }
        if (u.Change == GameChange.Takeback) ReconcileBook();
        var moves = _record.Moves;
        if (_reviewCard is { ReviewPly: int ply } && !(ply >= 1 && ply <= moves.Count && MoveKey(moves[ply - 1]) == _reviewCard.Id))
        {
            _reviewSeq++;
            _reviewCts?.Cancel();
            _reviewCard = null;
        }
        if (u.Gap && Settings.ReviewMoves)
        {
            _reviewSeq++;
            _reviewCts?.Cancel();
            _reviewCard = new CoachCard("gap|" + ++_gapCounter, CardKind.Review, Loc.T("review.gap"));
        }
        LearningPositionChanged(previous, u, me);
    }

    private string ReviewTitle(string who, MoveReview review, string san)
    {
        string rating = $"{Loc.Rating(review.Rating)} ({san})";
        return Settings.DuoTraining ? Loc.T("review.titleWho", who, rating) : Loc.T("review.titleYou", rating);
    }

    private bool InRecord(PlayedMove pm) => _record.Moves.Any(m => MoveKey(m) == MoveKey(pm));

    private readonly HashSet<string> _reviewableKeys = new();

    private void ConfirmReviews(GameUpdate update, LastMoveMarking marking, PieceColor me, List<PlayedMove> carried)
    {
        ConfirmMoves(carried.Where(InRecord).ToList(), me);
        foreach (var pm in update.Reviewable) _reviewableKeys.Add(MoveKey(pm));
        var fresh = update.NewMoves.ToList();
        _pendingFrames = 0;
        if (fresh.Count > 0)
        {
            if (ReviewConfirmation.IsConfirmed(Native.PrimaryButtonDown(), AllMarked(fresh, marking), 0)) ConfirmMoves(fresh, me);
            else _pendingMoves = fresh;
        }
        if (_pendingMoves.Count == 0) _reviewableKeys.Clear();
    }

    private int _pendingFrames;

    private static bool AllMarked(List<PlayedMove> moves, LastMoveMarking marking)
    {
        var marked = marking.Marked.Select(m => UciMove.SquareName(m.File, m.Rank)).ToHashSet();
        return moves.All(pm => marked.Contains(pm.Uci[..2]) && marked.Contains(pm.Uci.Substring(2, 2)));
    }

    private void ConfirmPending(LastMoveMarking marking, PieceColor me)
    {
        var still = _pendingMoves.Where(InRecord).ToList();
        bool mouseDown = Native.PrimaryButtonDown();
        _pendingFrames = ReviewConfirmation.NextFrames(_pendingFrames, mouseDown);
        if (still.Count > 0 && !ReviewConfirmation.IsConfirmed(mouseDown, AllMarked(still, marking), _pendingFrames))
        {
            _pendingMoves = still;
            return;
        }
        _pendingMoves = new();
        ConfirmMoves(still, me);
        _reviewableKeys.Clear();
    }

    private void ConfirmMoves(List<PlayedMove> moves, PieceColor me)
    {
        foreach (var pm in moves)
        {
            if (_reviewableKeys.Contains(MoveKey(pm))) _book.Rated.Add(MoveKey(pm));
            EnqueueForLearning(pm);
        }
        var coached = moves.Where(pm => _reviewableKeys.Contains(MoveKey(pm)) && IsCoached(pm.Mover, me)).ToList();
        if (coached.Count > 0) StartReview(coached, me);
    }

    private void StartReview(List<PlayedMove> moves, PieceColor me)
    {
        int seq = ++_reviewSeq;
        _ = ReviewMovesAsync(moves, me, seq);
    }

    private async Task ReviewMovesAsync(List<PlayedMove> moves, PieceColor me, int seq)
    {
        try { foreach (var pm in moves) await ReviewOwnMoveAsync(pm, me, seq); }
        catch (Exception e) { Log.Write("Review failed: " + e.Message); }
    }

    private async Task ReviewOwnMoveAsync(PlayedMove pm, PieceColor me, int seq)
    {
        if (!Settings.ReviewMoves || pm.Before.Board.Problem(pm.Mover) != null) return;
        var started = DateTime.UtcNow;
        var before = await _engine.AnalyzeAsync(pm.FenBefore, 300);
        if (before == null) return;
        EngineResult? after = pm.After.Result == null && pm.After.Board.Problem(pm.Mover.Opposite()) == null
            ? await _engine.AnalyzeAsync(pm.FenAfter, 300) : null;
        if (seq != _reviewSeq || !IsActive || !InRecord(pm)) return;
        int ply = _record.Moves.ToList().FindLastIndex(m => MoveKey(m) == MoveKey(pm)) + 1;
        if (Coach.Review(pm.Before.Board, pm.Move, pm.After.Board, pm.Mover, before, after, pm.After.Result) is not { } review) return;
        string who = PlayerName(pm.Mover, me);
        var lines = new List<string> { Loc.T("review.played", review.PlayedText, review.PlayedScore) };
        if (review.Rating != MoveRating.Best && review.BestText != null) lines.Add(Loc.T("review.better", review.BestText, before.ScoreText));
        string id = MoveKey(pm);
        var card = new CoachCard(id, CardKind.Review, ReviewTitle(who, review, pm.SanDisplay))
        {
            Rating = review.Rating, Lines = lines, ReviewPly = ply,
            StaleTitle = Settings.DuoTraining ? null : $"{Loc.T("review.titleLast")}: {Loc.Rating(review.Rating)} ({pm.SanDisplay})",
        };
        Log.Write($"Review shown for {pm.San} {(int)(DateTime.UtcNow - started).TotalMilliseconds} ms after the move was confirmed");
        if (!LlmConfigured)
        {
            _reviewCard = card with { Note = Loc.T("note.needLlm") };
            UpdatePanel();
            return;
        }
        _reviewCard = card with { IsLoading = true };
        UpdatePanel();
        _reviewCts?.Cancel();
        var req = ReviewRequestFor(review, pm);
        _lastReviewReq = req;
        _lastReviewSource = (review, pm);
        _reviewCts = ExplainReview(id, req);
    }

    private CancellationTokenSource ExplainReview(string id, Coach.ExplanationRequest req) =>
        Explain(req.System, req.User, req.MaxTokens, (text, done) =>
        {
            if (_reviewCard?.Id != id) return;
            _reviewCard = _reviewCard with { Text = text, IsLoading = !done, AllowFeedback = done, StyleKey = CurrentStyleKey };
            UpdatePanel();
        }, msg =>
        {
            if (_reviewCard?.Id != id) return;
            _reviewCard = _reviewCard with { IsLoading = false, Note = msg };
            UpdatePanel();
        });

    private async Task ComputeThreatAsync(HintContext ctx, CancellationToken ct)
    {
        _threatFen = ctx.Fen;
        _threat = null;
        var nullFen = Threats.NullMoveFen(ctx.Position);
        if (nullFen == null) return;
        var opp = await _engine.AnalyzeAsync(nullFen, 300);
        ct.ThrowIfCancellationRequested();
        _threatResult = opp; _hintSetFen = null;
        if (opp == null || _hint?.Fen != ctx.Fen) return;
        _threat = Threats.Detect(ctx.Position, ctx.Result, opp);
        if (_threat != null) RenderHints(ctx);
    }

    private void ClearHints()
    {
        _hint = null; _hintFen = null; _hintLevel = 0;
        _panel.HintLevel = 0;
        _panel.CanHint = false;
        _panel.CanExplain = _reviewCard != null;
    }

    private string? _threatShownFen;

    private bool ThreatShown => Settings.ShowThreats && _hint is { } h
        && (EffectiveShowBest || _hintLevel >= Curriculum.FirstHintLevel || _threatShownFen == _hintFen
            || (ProfileFor(h.ToMove, h.Me) ?? Active).ShowsThreatAutomatically);

    private bool ThreatVisible => _threat != null && ThreatShown;

    private CoachCard? ThreatCard()
    {
        if (!ThreatShown) return null;
        if (_threat is { } th) return new CoachCard("threat|" + th.Text, CardKind.Info, th.Text);
        return _threatShownFen == _hintFen && _hintFen != null
            ? new CoachCard("nothreat|" + _hintFen, CardKind.Info, Loc.T("threat.none")) : null;
    }

    public void ShowThreat()
    {
        if (!IsActive || _hint is not { } ctx) return;
        _threatShownFen = _hintFen;
        RenderHints(ctx);
        UpdatePanel();
    }

    public void NextHint()
    {
        if (!IsActive || _hint is not { } ctx || EffectiveShowBest || _hintLevel >= PanelWindow.HintMax) return;
        double elapsed = (DateTime.UtcNow - _hintFirstSeen.GetValueOrDefault(ctx.Fen, DateTime.MinValue)).TotalSeconds;
        var prof = ProfileFor(ctx.ToMove, ctx.Me) ?? Active;
        if (Curriculum.NeedsThinkDelay(_hintLevel, elapsed, AdaptationFor(prof).ThinkSeconds, _hintNudged.Contains(ctx.Fen)))
        {
            _hintNudged.Add(ctx.Fen);
            if (_suggestionCard != null) _suggestionCard = _suggestionCard with { Note = Loc.T("hint.thinkFirst") };
            UpdatePanel();
            return;
        }
        _hintLevel++;
        _panel.HintLevel = _hintLevel;
        _lastChangeAt = DateTime.UtcNow;
        RecordHintUse(ctx, _hintLevel);
        RenderHints(ctx);
    }

    private readonly Dictionary<string, DateTime> _hintFirstSeen = new();
    private readonly HashSet<string> _hintNudged = new();

    private void RenderHints(HintContext ctx)
    {
        int level = EffectiveShowBest ? 4 : _hintLevel;
        if (!_hintFirstSeen.ContainsKey(ctx.Fen))
        {
            if (_hintFirstSeen.Count > 200) { _hintFirstSeen.Clear(); _hintNudged.Clear(); }
            _hintFirstSeen[ctx.Fen] = DateTime.UtcNow;
        }
        var from = Placement.ScreenSquare(ctx.Move.FromFile, ctx.Move.FromRank, ctx.WhiteAtBottom);
        var to = Placement.ScreenSquare(ctx.Move.ToFile, ctx.Move.ToRank, ctx.WhiteAtBottom);
        string prefix = Settings.DuoTraining ? ctx.Who + ": " : "";
        _panel.CanExplain = level >= 1 || _reviewCard != null;
        OverlayState.Arrow? threatArrow = null;
        if (_threat is { } th && ThreatVisible)
        {
            var tf = Placement.ScreenSquare(th.Move.FromFile, th.Move.FromRank, ctx.WhiteAtBottom);
            var tt = Placement.ScreenSquare(th.Move.ToFile, th.Move.ToRank, ctx.WhiteAtBottom);
            threatArrow = new OverlayState.Arrow(new(tf.Col, tf.Row), new(tt.Col, tt.Row));
        }
        OverlayState state;
        switch (level)
        {
            case 0:
                state = new OverlayState { Hint = Loc.T("hint.think", ctx.Who), Threat = threatArrow };
                SetHintCard($"{ctx.Fen}|h0", Loc.T("hint.turn", ctx.Who),
                    new[] { Loc.T("hint.thinkLine", Loc.T("hint.keys")) });
                break;
            case 1:
                state = new OverlayState { Hint = prefix + Loc.T("hint.l1"), Threat = threatArrow };
                SetHintCard($"{ctx.Fen}|h1", Loc.T("hint.l1"), Array.Empty<string>(), HintTopicText(ctx));
                break;
            case 2:
                state = new OverlayState
                {
                    Highlight = new(from.Col, from.Row), PartnerHighlight = ctx.Partner,
                    Hint = prefix + Loc.T("hint.l2overlay"), Threat = threatArrow,
                };
                SetHintCard($"{ctx.Fen}|h2", Loc.T("hint.l2"), new[] { HintStage2Text(ctx) });
                break;
            case 3:
                state = new OverlayState
                {
                    Highlight = new(from.Col, from.Row), PartnerHighlight = ctx.Partner,
                    Hint = prefix + Loc.T("hint.l3"), Threat = threatArrow,
                };
                RenderIdea(ctx);
                break;
            default:
                state = new OverlayState
                {
                    Move = new OverlayState.Arrow(new(from.Col, from.Row), new(to.Col, to.Row), ctx.Partner),
                    Label = $"{prefix}{ctx.Text}{(ctx.Score.Length == 0 ? "" : " · " + ctx.Score)}",
                    Threat = threatArrow,
                };
                RenderSolution(ctx);
                break;
        }
        ShowOverlay(ctx.BoardRect, state);
        UpdatePanel();
    }

    private void SetHintCard(string id, string title, IReadOnlyList<string> lines, string text = "")
    {
        if (_suggestionCard?.Id == id) return;
        _suggestionCts?.Cancel();
        _suggestionCard = new CoachCard(id, CardKind.Hint, title) { Lines = lines, Text = text };
    }

    private void RenderIdea(HintContext ctx)
    {
        string id = ctx.Fen + "|idea";
        if (_suggestionCard?.Id == id) return;
        var card = new CoachCard(id, CardKind.Hint, Loc.T("hint.l3"));
        var hintReq = HintRequestFor(ctx);
        if (!LlmConfigured || hintReq == null)
        {
            _suggestionCard = card with { Text = HintIdeaFallback(ctx), Note = LlmConfigured ? null : Loc.T("hint.noLlmIdea") };
            return;
        }
        _suggestionCard = card with { IsLoading = true };
        _suggestionCts?.Cancel();
        _suggestionCts = Explain(hintReq.System, hintReq.User, hintReq.MaxTokens, (text, done) =>
        {
            if (_suggestionCard?.Id != id) return;
            if (Hints.RevealsTarget(text, ctx.Move)) { _suggestionCts?.Cancel(); text = HintIdeaFallback(ctx); done = true; }
            _suggestionCard = _suggestionCard with { Text = text, IsLoading = !done, AllowFeedback = false };
            UpdatePanel();
        }, msg =>
        {
            if (_suggestionCard?.Id != id) return;
            _suggestionCard = _suggestionCard with { IsLoading = false, Text = HintIdeaFallback(ctx), Note = msg };
            UpdatePanel();
        });
    }

    private void RenderSolution(HintContext ctx)
    {
        string fen = ctx.Fen;
        if (_suggestionCard?.Id == fen) return;
        var card = new CoachCard(fen, CardKind.Suggestion, Settings.DuoTraining ? $"{ctx.Who}: {ctx.Text}" : Loc.T("suggest.title", ctx.Text))
        { Lines = Alternatives(ctx.Result, ctx.Placement) };
        var req = Settings.ExplainSuggestions ? SuggestionRequestFor(ctx) : null;
        if (req == null) { _suggestionCard = card; return; }
        if (!LlmConfigured)
        {
            _suggestionCard = card with { Note = Loc.T("note.needLlm") };
            return;
        }
        _suggestionCard = card with { IsLoading = true };
        _suggestionCts?.Cancel();
        _suggestionCts = ExplainSuggestion(fen, req);
    }

    private CancellationTokenSource ExplainSuggestion(string fen, Coach.ExplanationRequest req) =>
        Explain(req.System, req.User, req.MaxTokens, (text, done) =>
        {
            if (_suggestionCard?.Id != fen) return;
            _suggestionCard = _suggestionCard with { Text = text, IsLoading = !done, AllowFeedback = done, StyleKey = CurrentStyleKey };
            UpdatePanel();
        }, msg =>
        {
            if (_suggestionCard?.Id != fen) return;
            _suggestionCard = _suggestionCard with { IsLoading = false, Note = msg };
            UpdatePanel();
        });

    private (MoveReview Review, PlayedMove Move)? _lastReviewSource;

    private void More(CoachCard card)
    {
        if (!IsActive || !LlmConfigured || card.StyleKey == null || !_styles.TryGetValue(card.StyleKey, out var style)) return;
        if (style.Length.Longer() is not { } longer) return;
        var prof = _lastStyleProfile ?? Active;
        var length = (StyleLength)Math.Max((int)prof.Style.Length, (int)longer);
        prof.RecordFeedback(ExplanationFeedback.TooShort, style);
        SaveProfile(prof);
        if (card.Kind == CardKind.Review && _reviewCard?.Id == card.Id && _lastReviewSource is { } src)
        {
            var req = ReviewRequestFor(src.Review, src.Move, length);
            _lastReviewReq = req;
            _reviewCard = _reviewCard with { Text = "", Note = null, IsLoading = true, AllowFeedback = false };
            _reviewCts?.Cancel();
            UpdatePanel();
            _reviewCts = ExplainReview(card.Id, req);
        }
        else if (card.Kind == CardKind.Suggestion && _suggestionCard?.Id == card.Id && _hint is { } ctx)
        {
            if (SuggestionRequestFor(ctx, length) is not { } req) return;
            _suggestionCard = _suggestionCard with { Text = "", Note = null, IsLoading = true, AllowFeedback = false };
            _suggestionCts?.Cancel();
            UpdatePanel();
            _suggestionCts = ExplainSuggestion(card.Id, req);
        }
    }

    private static List<string> Alternatives(EngineResult r, Placement placement)
    {
        var alts = new List<string>();
        foreach (var line in r.Lines.Skip(1))
            if (UciMove.Parse(line.Move) is UciMove m) alts.Add($"{placement.DescribeMove(m)} ({line.ScoreText})");
        var outp = new List<string> { Loc.T("suggest.eval", r.ScoreText) };
        if (alts.Count > 0) outp.Add(Loc.T("suggest.also", string.Join(", ", alts)));
        return outp;
    }

    public void ExplainAgain()
    {
        if (!IsActive) return;
        RegisterExplainAgain();
        if (_hint is { } ctx && (EffectiveShowBest || _hintLevel >= 3))
        {
            _suggestionCts?.Cancel();
            _suggestionCard = null;
            RenderHints(ctx);
        }
        else if (_hint != null && _hintLevel >= 1 && _suggestionCard is { Kind: CardKind.Hint })
        {
            AskCore("Explain the hint on screen again and tell me why it makes sense.", Loc.T("ask.hintExplained"));
        }
        else if (_lastReviewReq is { } again && _reviewCard != null && LlmConfigured)
        {
            string id = _reviewCard.Id;
            _reviewCard = _reviewCard with { Text = "", Note = null, IsLoading = true, AllowFeedback = false };
            _reviewCts?.Cancel();
            UpdatePanel();
            _reviewCts = ExplainReview(id, again);
        }
    }

    private readonly Dictionary<string, Grounding> _groundingCache = new();
    private HashSet<string> _questionAllowed = new();

    private async Task<Grounding> GroundingAsync(string question, Placement placement, PieceColor color, HintContext? hint, int level, HintSet? hints,
        CancellationToken ct)
    {
        if (hint is not { } ctx)
        {
            var pos = new ChessPosition(placement, color, CastlingRights.None);
            var plain = await _engine.AnalyzeAsync(pos.Fen, 300);
            var g = PositionFacts.Build(pos, plain, null, null, false);
            g.Lines.Add("Hint rule: the player has not asked for a hint. Do not name or hint at the best move.");
            return g;
        }
        string key = ctx.Fen + "|" + (level >= 1 ? "1" : "0");
        if (!_groundingCache.TryGetValue(key, out var baseFacts))
        {
            EngineResult? nullResult = null;
            if (Threats.NullMoveFen(ctx.Position) is { } nullFen) nullResult = await _engine.AnalyzeAsync(nullFen, 300);
            ct.ThrowIfCancellationRequested();
            baseFacts = PositionFacts.Build(ctx.Position, ctx.Result, nullResult, hints?.Threat, level >= 1);
            if (_groundingCache.Count > 40) _groundingCache.Clear();
            _groundingCache[key] = baseFacts;
        }
        var grounding = new Grounding(new List<string>(baseFacts.Lines), new List<ChessPosition>(baseFacts.Positions), new List<IReadOnlyList<string>>(baseFacts.Pvs));
        grounding.Lines.AddRange(PositionFacts.HintLines(level, hints, ctx.Position));
        if (PositionFacts.MentionedMove(question, ctx.Position) is { } move && (level >= 1 || move != ctx.Move)
            && await _engine.AnalyzeAsync(ctx.Position.Applying(move).Fen, 300) is { } after)
        {
            ct.ThrowIfCancellationRequested();
            var mentioned = PositionFacts.MentionedLines(ctx.Position, move, after, ctx.Result.Lines.Count > 0 ? ctx.Result.Lines[0] : null);
            grounding.Lines.AddRange(mentioned.Lines);
            grounding.Positions.Add(mentioned.Position);
            grounding.Pvs.Add(mentioned.Pv);
        }
        return grounding;
    }

    public void Ask(string question) => AskCore(question, null);

    private void AskCore(string question, string? title)
    {
        string id = "ask|" + question;
        var placement = _hint?.Placement ?? _stablePlacement;
        if (placement == null)
        {
            _answerCard = new CoachCard(id, CardKind.Answer, Loc.T("ask.title")) { Note = Loc.T("ask.noPosition") };
            UpdatePanel();
            return;
        }
        if (!LlmConfigured)
        {
            _answerCard = new CoachCard(id, CardKind.Answer, Loc.T("ask.title")) { Note = Loc.T("ask.needLlm") };
            UpdatePanel();
            return;
        }
        var color = _hint?.ToMove ?? _sideToMove ?? _lastMe ?? PieceColor.White;
        var profile = ProfileFor(color) ?? Active;
        var context = profile.PromptContext;
        var style = profile.Style.Current;
        var hintCtx = _hint;
        int level = EffectiveShowBest ? 4 : _hintLevel;
        var hintSet = hintCtx != null ? HintSetFor(hintCtx) : null;
        _answerCard = new CoachCard(id, CardKind.Answer, title ?? Loc.T("ask.cardTitle", question)) { IsLoading = true };
        _panel.ClearQuestion();
        _panel.IsAsking = true;
        UpdatePanel();
        _questionCts?.Cancel();
        var gate = new CancellationTokenSource();
        _questionCts = gate;
        _ = Task.Run(async () =>
        {
            Coach.ExplanationRequest req;
            try
            {
                var grounding = await GroundingAsync(question, placement, color, hintCtx, level, hintSet, gate.Token);
                if (gate.IsCancellationRequested) return;
                _questionAllowed = FactCheck.Allowed(grounding.Positions, grounding.Pvs);
                req = Coach.QuestionRequest(grounding, question, context, style);
            }
            catch (OperationCanceledException) { return; }
            RunOnUi(() =>
            {
                if (gate.IsCancellationRequested) return;
                _questionCts = Explain(req.System, req.User, req.MaxTokens, (text, done) => AnswerUpdate(id, text, done), msg =>
                {
                    if (_answerCard?.Id != id) return;
                    _answerCard = _answerCard with { IsLoading = false, Note = msg };
                    _panel.IsAsking = false;
                    UpdatePanel();
                });
            });
        });
    }

    private void AnswerUpdate(string id, string text, bool done)
    {
        if (_answerCard?.Id != id) return;
        string shown = text;
        string? note = null;
        if (done)
        {
            var cleaned = FactCheck.Clean(text, _questionAllowed);
            if (cleaned.Removed > 0) Log.Write($"Answer: removed {cleaned.Removed} sentence(s) with invented moves");
            shown = cleaned.Text ?? "";
            if (cleaned.Text == null) note = Loc.T("explain.noReliable");
        }
        _answerCard = _answerCard with { Text = shown, IsLoading = !done, AllowFeedback = done && note == null, StyleKey = CurrentStyleKey, Note = note ?? _answerCard.Note };
        if (done) _panel.IsAsking = false;
        UpdatePanel();
    }

    private CancellationTokenSource Explain(string system, string prompt, int maxTokens, Action<string, bool> update, Action<string> failed)
    {
        var cts = new CancellationTokenSource();
        string b = Settings.LlmBase, token = _llmToken, model = Settings.LlmModel;
        _ = Task.Run(async () =>
        {
            try
            {
                var final = await LlmClient.ExplainAsync(b, token, model, system, prompt,
                    t => { if (!cts.IsCancellationRequested) RunOnUi(() => { if (!cts.IsCancellationRequested) update(t, false); }); },
                    cts.Token, maxTokens);
                RunOnUi(() => { if (!cts.IsCancellationRequested) update(final, true); });
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                RunOnUi(() => { if (!cts.IsCancellationRequested) failed(Loc.T("error.explain", e.Message)); });
            }
        });
        return cts;
    }

    private void UpdatePanel()
    {
        if (_panelDirty) return;
        _panelDirty = true;
        _ui.BeginInvoke(() =>
        {
            _panelDirty = false;
            if (!IsActive || !_boardPresent || _boardRect.Width == 0) return;
            var cards = new List<CoachCard>(5);
            if (_liftCard != null) cards.Add(_liftCard);
            var review = _reviewCard;
            bool reviewOld = review is { ReviewPly: int rp, StaleTitle: not null } && rp < _record.Moves.Count;
            if (reviewOld) review = review! with { Title = review.StaleTitle! };
            if (!reviewOld && review != null) cards.Add(review);
            if (reviewOld)
            {
                if (_suggestionCard != null) cards.Add(_suggestionCard);
                if (ThreatCard() is { } tc0) cards.Add(tc0);
                if (_answerCard != null) cards.Add(_answerCard);
                cards.Add(review!);
            }
            else
            {
                if (ThreatCard() is { } tc) cards.Add(tc);
                if (_suggestionCard != null) cards.Add(_suggestionCard);
                if (_answerCard != null) cards.Add(_answerCard);
            }
            _panel.CanFinish = CanFinish;
            _panel.CanShowThreat = _hint != null && Settings.ShowThreats && !ThreatShown;
            _panel.FocusText = PanelSubtitle();
            _panel.ShowNextTo(_boardRect, cards);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    public void ResetPanelPosition() => _panel.ResetPosition();
}
