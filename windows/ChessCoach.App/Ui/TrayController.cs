using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using ChessCoach.App.Model;

namespace ChessCoach.App.Ui;

internal sealed class TrayController : IDisposable
{
    private readonly AppModel _model;
    private readonly NotifyIcon _icon = new();
    private readonly ContextMenuStrip _menu = new();
    private Icon? _current;
    public Action? OpenSettings, OpenRecord, OpenTraining, OpenReport, EndGame, CopyPgn, CheckUpdates, Quit;

    public TrayController(AppModel model)
    {
        _model = model;
        _icon.ContextMenuStrip = _menu;
        _menu.Opening += (_, _) => Rebuild();
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_icon, null);
        };
        _icon.DoubleClick += (_, _) => _model.SetActive(!_model.IsActive);
        ApplyIcon();
        _icon.Visible = true;
        SystemEvents.UserPreferenceChanged += OnPrefs;
        _model.StateChanged += () => UpdateTip();
        UpdateTip();
    }

    private void OnPrefs(object? s, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(ApplyIcon);
    }

    private static bool TaskbarIsLight()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    private void ApplyIcon()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "Assets", TaskbarIsLight() ? "tray-light.ico" : "tray-dark.ico");
        try
        {
            var next = File.Exists(file) ? new Icon(file) : SystemIcons.Application;
            _icon.Icon = next;
            _current?.Dispose();
            _current = next;
        }
        catch { }
    }

    public void Balloon(string text) =>
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => _icon.ShowBalloonTip(5000, "Chess Coach", text, ToolTipIcon.Info));

    private void UpdateTip()
    {
        string t = "Chess Coach · " + _model.Status;
        _icon.Text = t.Length > 63 ? t[..63] : t;
    }

    private static ToolStripMenuItem Item(string text, Action click, bool isChecked = false, string? shortcut = null)
    {
        var it = new ToolStripMenuItem(text) { Checked = isChecked };
        if (shortcut != null) it.ShortcutKeyDisplayString = shortcut;
        it.Click += (_, _) => click();
        return it;
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        var s = _model.Settings;
        _menu.Items.Add(Item(Loc.T("menu.active"), () => _model.SetActive(!_model.IsActive), _model.IsActive));
        _menu.Items.Add(new ToolStripMenuItem(_model.Status) { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Loc.T("menu.duo"), () => { s.DuoTraining = !s.DuoTraining; _model.SettingsChanged(); }, s.DuoTraining));
        _menu.Items.Add(Item(Loc.T("menu.explain"), () => { s.ExplainSuggestions = !s.ExplainSuggestions; _model.SettingsChanged(); }, s.ExplainSuggestions));
        _menu.Items.Add(Item(Loc.T("menu.review"), () => { s.ReviewMoves = !s.ReviewMoves; _model.SettingsChanged(); }, s.ReviewMoves));
        _menu.Items.Add(Item(Loc.T("menu.threats"), () => { s.ShowThreats = !s.ShowThreats; _model.SettingsChanged(); }, s.ShowThreats));

        var side = new ToolStripMenuItem(Loc.T("menu.side"));
        foreach (var (title, val) in new[] { (Loc.T("menu.sideAuto"), AppModel.SideChoice.Auto), (Loc.T("color.white"), AppModel.SideChoice.White), (Loc.T("color.black"), AppModel.SideChoice.Black) })
        {
            var v = val;
            side.DropDownItems.Add(Item(title, () => _model.Side = v, _model.Side == v));
        }
        _menu.Items.Add(side);

        if (s.ExpertUnlocked)
        {
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Item(Loc.T("menu.best"), () => { s.ShowBestImmediately = !s.ShowBestImmediately; _model.SettingsChanged(); }, s.ShowBestImmediately));
            _menu.Items.Add(Item(Loc.T("menu.always"), () => { s.AlwaysMyTurn = !s.AlwaysMyTurn; _model.SettingsChanged(); }, s.AlwaysMyTurn));
            var think = new ToolStripMenuItem(Loc.T("menu.think"));
            foreach (var ms in new[] { 300, 500, 1000, 2000, 4000 })
            {
                int v = ms;
                think.DropDownItems.Add(Item($"{ms / 1000.0:0.#} s", () => { s.ThinkMs = v; _model.SettingsChanged(); }, s.ThinkMs == v));
            }
            _menu.Items.Add(think);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Loc.T("menu.nextHint"), () => _model.NextHint(), shortcut: Loc.T("hint.keys")));
        _menu.Items.Add(Item(Loc.T("menu.toggleSide"), () => _model.ToggleSideToMove(), shortcut: Loc.T("menu.toggleKeys")));
        _menu.Items.Add(Item(Loc.T("menu.endGame"), () => EndGame?.Invoke()));
        _menu.Items.Add(Item(Loc.T("menu.copyPgn"), () => CopyPgn?.Invoke()));
        _menu.Items.Add(Item(Loc.T("menu.resetPanel"), () => _model.ResetPanelPosition()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Loc.T("menu.record"), () => OpenRecord?.Invoke()));
        _menu.Items.Add(Item(Loc.T("menu.training"), () => OpenTraining?.Invoke()));
        _menu.Items.Add(Item(Loc.T("menu.report"), () => OpenReport?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Loc.T("menu.relearn"), () => _model.RelearnPieces()));
        _menu.Items.Add(Item(Loc.T("menu.settings"), () => OpenSettings?.Invoke()));
        _menu.Items.Add(Item(Loc.T("menu.checkUpdates"), () => CheckUpdates?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item(Loc.T("menu.quit"), () => Quit?.Invoke()));
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnPrefs;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _current?.Dispose();
    }
}
