using System.Drawing;
using System.Windows.Threading;
using ChessCoach.App.Platform;
using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed partial class AppModel
{
    public enum SideChoice { Auto, White, Black }

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    public AppSettings Settings { get; }
    public ITokenStore Tokens { get; } = new DpapiTokenStore();
    private readonly OverlayWindow _overlay = new();
    private readonly PanelWindow _panel;
    private readonly HotKeys _hotKeys = new();
    private readonly SessionState _session = new();
    private readonly ScreenCapturer _capturer = new();
    private readonly EngineSession _engine;
    private readonly Openings? _openings = Openings.FromAppDirectory(AppPaths.AppDir);

    private CancellationTokenSource? _loop;
    private PieceTemplates _templates = new();
    private string _llmToken;

    public event Action? StateChanged;
    public Action? Restart;

    public bool IsActive { get; private set; }
    public string Status { get; private set; } = Loc.T("status.off");
    public string LlmToken { get => _llmToken; set { _llmToken = value; Tokens.Save(value); } }
    public bool LlmConfigured => Settings.LlmBase.Length > 0 && Settings.LlmModel.Length > 0;
    public string LlmStatus { get; set; } = "";
    public List<LlmModel> LlmModels { get; private set; } = new();

    public bool ExpertUnlocked
    {
        get => Settings.ExpertUnlocked;
        set { Settings.ExpertUnlocked = value; Settings.Save(); ClearAnalysisState(); }
    }
    public bool EffectiveShowBest => Settings.ExpertUnlocked && Settings.ShowBestImmediately;
    public bool EffectiveAlwaysMyTurn => Settings.ExpertUnlocked && Settings.AlwaysMyTurn;
    public int EffectiveThinkMs => Settings.ExpertUnlocked ? Math.Clamp(Settings.ThinkMs, 200, 10000) : 1000;
    public SideChoice Side
    {
        get => Settings.Side switch { "white" => SideChoice.White, "black" => SideChoice.Black, _ => SideChoice.Auto };
        set { Settings.Side = value.ToString().ToLowerInvariant(); Settings.Save(); ResetTracking(); }
    }

    public AppModel()
    {
        Settings = AppSettings.Load();
        _llmToken = Tokens.Load();
        _engine = new EngineSession(() => StockfishEngine.FindBinary(Settings.StockfishPath, AppPaths.AppDir));
        _panel = new PanelWindow(Settings);
        _templates = LoadTemplates() ?? new PieceTemplates();
        if (Settings.LlmModel.Length > 0) LlmModels = new() { new LlmModel(Settings.LlmModel, Settings.LlmModel) };
        _panel.NextHintClicked += NextHint;
        _panel.ThreatClicked += ShowThreat;
        _panel.ExplainAgainClicked += ExplainAgain;
        _panel.FinishClicked += RequestEndGame;
        _panel.QuestionAsked += Ask;
        _panel.FeedbackGiven += OnFeedback;
        _panel.CollapsedChanged += OnPanelCollapsed;
        _session.Changed += () => _ui.BeginInvoke(Invalidate);
        InitLearning();
        Log.Write("Chess Coach started, version " + typeof(AppModel).Assembly.GetName().Version);
        if (Settings.ResumeActive) _ui.BeginInvoke(() => SetActive(true));
    }

    public void Notify() => StateChanged?.Invoke();

    private void SetStatus(string s)
    {
        if (Status == s) return;
        Status = s;
        Notify();
    }

    public void SettingsChanged()
    {
        Settings.Save();
        if (!Settings.DragHints) ClearLift();
        ClearAnalysisState();
    }

    public void SetActive(bool on)
    {
        if (on == IsActive) return;
        IsActive = on;
        Settings.ResumeActive = on;
        Settings.Save();
        if (on) { ShowFairPlayNoticeOnce(); RegisterHotKeys(); Start(); } else { _hotKeys.Clear(); Stop(); }
        Notify();
    }

    private void RegisterHotKeys()
    {
        const int mods = Native.MOD_CONTROL | Native.MOD_SHIFT;
        _hotKeys.Register(mods, 0x77, () => _ui.BeginInvoke(NextHint), "Ctrl+Shift+F8");
        _hotKeys.Register(mods, 0x78, () => _ui.BeginInvoke(ToggleSideToMove), "Ctrl+Shift+F9");
    }

    private void Start()
    {
        SetStatus(Loc.T("status.searching"));
        _session.Start();
        Invalidate();
        _loop?.Cancel();
        var cts = _loop = new CancellationTokenSource();
        _ = RunLoopAsync(cts.Token);
    }

    private void Stop()
    {
        _loop?.Cancel();
        _loop = null;
        if (_book.Plies >= 2) FinishGame(new GameEnd(GameEndReason.Manual, "*", true));
        _session.Stop();
        _tracked = null;
        _cached = null;
        _hasLastFp = false;
        HideAll();
        _engine.Shutdown("app off");
        ResetTracking();
        SetStatus(Loc.T("status.off"));
    }

    public void Shutdown()
    {
        if (IsActive) { SetActive(false); }
        _recapCts.Cancel();
        _hotKeys.Dispose();
        _capturer.Dispose();
        _engine.Dispose();
        _overlay.Close();
        _panel.Close();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_session.IsHardPaused)
                {
                    EnterPause();
                    await _session.WaitForResumeAsync(ct);
                    Invalidate();
                    continue;
                }
                await TickAsync(ct);
                await Task.Delay(NextDelay(), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                Log.Write("Loop error: " + e);
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private int NextDelay()
    {
        if (_pausedShown) return 2000;
        if (_view == BoardSight.Covered && _book.Plies >= GameEndDetector.MinPlies) return 500;
        if (_confirmSoon && _tracked != null) return 200;
        if (_tracked == null)
        {
            double lost = (DateTime.UtcNow - (_lostSince ?? DateTime.UtcNow)).TotalSeconds;
            return OnBattery() ? 10000 : lost > 300 ? 8000 : lost > 30 ? 2000 : 1000;
        }
        double calm = (DateTime.UtcNow - _lastChangeAt).TotalSeconds;
        if (calm < 3) return 400;
        int delay = calm > 120 ? 4000 : (int)Math.Min(1500, 400 + (calm - 3) / 12 * 1100);
        return OnBattery() ? delay * 2 : delay;
    }

    private static bool OnBattery() =>
        System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;

    private void Invalidate() { _settled = false; _lastChangeAt = DateTime.UtcNow; }

    private void ClearAnalysisState()
    {
        _suggestionCard = null;
        Invalidate();
        Notify();
    }

    public async Task LoadModelsAsync(string url)
    {
        Settings.LlmUrl = url;
        LlmStatus = Loc.T("llm.connecting");
        Notify();
        try
        {
            var (b, models) = await LlmClient.DiscoverAsync(url, _llmToken);
            Settings.LlmBase = b;
            LlmModels = models;
            if (!models.Any(m => m.Id == Settings.LlmModel)) Settings.LlmModel = models.FirstOrDefault()?.Id ?? "";
            Settings.Save();
            LlmStatus = Loc.T("llm.found", models.Count, b);
        }
        catch (Exception e) { LlmStatus = e.Message; }
        Notify();
    }

    public async Task<int?> MoveLossAsync(string fen, string uci)
    {
        if (ChessPosition.FromFen(fen) is not { } pos || UciMove.Parse(uci) is not { } move) return null;
        var before = await _engine.AnalyzeAsync(fen, 300, false);
        if (before == null) return null;
        var next = pos.Applying(move);
        if (next.Result is { Kind: GameResultKind.Checkmate }) return 0;
        var after = await _engine.AnalyzeAsync(next.Fen, 300, false);
        return after == null ? null : Math.Max(0, before.Value + after.Value);
    }

    public void SetModel(string id)
    {
        Settings.LlmModel = id;
        Settings.Save();
        _suggestionCard = null;
        Invalidate();
    }

    private static PieceTemplates? LoadTemplates()
    {
        try
        {
            if (File.Exists(AppPaths.TemplatesFile))
                return System.Text.Json.JsonSerializer.Deserialize<PieceTemplates>(File.ReadAllText(AppPaths.TemplatesFile));
        }
        catch (Exception e) { Log.Write("pieces.json unreadable: " + e.Message); }
        return null;
    }

    private static void SaveTemplates(PieceTemplates t)
    {
        try { File.WriteAllText(AppPaths.TemplatesFile, System.Text.Json.JsonSerializer.Serialize(t)); }
        catch (Exception e) { Log.Write("pieces.json not saved: " + e.Message); }
    }

    public void RelearnPieces()
    {
        _templates = new PieceTemplates();
        SaveTemplates(_templates);
        ResetTracking();
        Notify();
    }

    private void HideAll()
    {
        ClearLift();
        _overlay.HideOverlay();
        _panel.HidePanel();
        _hint = null;
        _panel.CanHint = false;
    }

    private void RunOnUi(Action a)
    {
        if (_ui.CheckAccess()) a(); else _ui.BeginInvoke(a);
    }

    private Rectangle Inflate(Rectangle r, double factor, Rectangle bounds)
    {
        int mx = Math.Max(40, (int)(r.Width * factor)), my = Math.Max(40, (int)(r.Height * factor));
        var x = Math.Max(bounds.Left, r.Left - mx);
        var y = Math.Max(bounds.Top, r.Top - my);
        var right = Math.Min(bounds.Right, r.Right + mx);
        var bottom = Math.Min(bounds.Bottom, r.Bottom + my);
        return new Rectangle(x, y, right - x, bottom - y);
    }
}
