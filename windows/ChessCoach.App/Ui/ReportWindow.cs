using System.Windows;
using System.Windows.Controls;
using ChessCoach.App.Model;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class ReportWindow
{
    private readonly AppModel _model;
    private readonly Window _window = Kit.Window(Loc.T("report.title"), 620, 740);
    private readonly ComboBox _games = new() { MinWidth = 320 };
    private readonly StackPanel _body = new() { Margin = new Thickness(14) };
    private List<GameReport> _list = new();
    private bool _loading, _refreshQueued;
    private string? _shownId;

    public ReportWindow(AppModel model)
    {
        _model = model;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 14, 14, 0) };
        top.Children.Add(_games);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _window.Content = root;
        _games.SelectionChanged += (_, _) =>
        {
            if (!_loading && _games.SelectedIndex >= 0) Render(_list[_games.SelectedIndex]);
        };
        model.ReportChanged += OnChanged;
    }

    public void Show()
    {
        Reload();
        Kit.Open(_window);
    }

    public void ShowWithoutFocus()
    {
        Reload();
        _window.ShowActivated = false;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Topmost = true;
        _window.Topmost = false;
        _window.ShowActivated = true;
    }

    public void Close() => _window.Hide();

    private void OnChanged()
    {
        if (!_window.IsVisible || _refreshQueued) return;
        _refreshQueued = true;
        _window.Dispatcher.BeginInvoke(() =>
        {
            _refreshQueued = false;
            if (_window.IsVisible) Reload(_shownId);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void Reload(string? select = null)
    {
        _list = _model.Storage.LoadGames(50);
        if (_model.LastReport is { } live)
        {
            int at = _list.FindIndex(g => g.Id == live.Id);
            if (at >= 0) _list[at] = live; else _list.Insert(0, live);
        }
        select ??= _model.RecapSelection;
        int index = Math.Max(0, _list.FindIndex(g => g.Id == select));
        _loading = true;
        _games.ItemsSource = _list.Select(g => $"{g.Date.ToLocalTime():dd.MM.yyyy HH:mm} · {g.Opening ?? Loc.T("report.noOpening")}").ToList();
        _games.SelectedIndex = _list.Count > 0 ? index : -1;
        _loading = false;
        _body.Children.Clear();
        _shownId = null;
        if (_list.Count == 0) _body.Children.Add(Kit.Text(Loc.T("report.empty"), muted: true));
        else Render(_list[index]);
    }

    private void Render(GameReport g)
    {
        _body.Children.Clear();
        _shownId = g.Id;
        _body.Children.Add(Kit.Text(g.Result is { } res && res != "*" ? Loc.T("report.resultLine", res) : Loc.T("report.resultUnknown"), 16, true, bottom: 2));
        if (g.EndReason != null) _body.Children.Add(Kit.Text(Loc.T("report.end." + g.EndReason), 12, muted: true, bottom: 8));
        if (_model.ProgressFor(g.Id) is { } progress)
        {
            _body.Children.Add(Kit.Text(Loc.T("report.progress", progress.Done, progress.Total), 12, muted: true));
            _body.Children.Add(Kit.Bar(progress.Done, Math.Max(1, progress.Total)));
        }
        if (g.EngineAssisted) _body.Children.Add(Kit.Text(Loc.T("report.notCounted"), 12, muted: true, bottom: 8));
        if (g.Opening != null) _body.Children.Add(Kit.Text(Loc.T("report.opening", g.Opening), 14, true, bottom: 8));
        foreach (var (color, s) in new[] { ("w", g.White), ("b", g.Black) })
        {
            var profile = _model.Profiles.FirstOrDefault(p => p.Id == (color == "w" ? g.WhiteProfileId : g.BlackProfileId));
            string name = profile?.Name ?? (color == "w" ? Loc.T("report.white") : Loc.T("report.black"));
            _body.Children.Add(Kit.Text(name, 15, true, bottom: 2));
            _body.Children.Add(Kit.Text(Loc.T("report.sideStats", $"{s.Accuracy:0.#}", $"{s.Acpl:0}", $"{s.Performance:0}"), 12));
            _body.Children.Add(Kit.Text(Loc.T("report.counts", s.Best, s.Good, s.Inaccuracies, s.Mistakes, s.Blunders), 12, muted: true, bottom: 10));
        }
        _body.Children.Add(Kit.Text(Loc.T("report.moments"), 14, true, bottom: 4));
        foreach (var k in g.KeyMoments)
        {
            _body.Children.Add(Kit.Text($"{GameReport.MoveLabel(k.Ply, k.Color)}: {k.MoveText}", 13, true));
            _body.Children.Add(Kit.Text($"{k.WinBefore:0} % → {k.WinAfter:0} %. {k.Facts}", 12, muted: true, bottom: 6));
        }
        if (g.Takeaways.Count > 0) RenderTakeaways(g);
        if (g.Summary != null)
        {
            _body.Children.Add(Kit.Text(Loc.T("report.summary"), 14, true, bottom: 4).WithMargin(top: 8));
            _body.Children.Add(g.Summary.Length == 0 ? Kit.Text(Loc.T("report.summaryLoading"), muted: true, bottom: 8) : Kit.Text(g.Summary, 13, bottom: 8));
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(Kit.Btn(Loc.T("report.copyPgn"), () =>
        {
            var path = Path.Combine(_model.Storage.Root, "games", $"{g.Date.ToUniversalTime():yyyy-MM-dd}-{g.Id}.pgn");
            if (File.Exists(path)) Clipboard.SetText(File.ReadAllText(path));
        }));
        _body.Children.Add(row);
    }

    private void RenderTakeaways(GameReport g)
    {
        _body.Children.Add(Kit.Text(Loc.T("report.takeaways"), 14, true, bottom: 4).WithMargin(top: 8));
        foreach (var t in g.Takeaways) _body.Children.Add(Kit.Text("• " + t.Text(), 13, bottom: 4));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        int puzzles = g.Takeaways.SelectMany(t => t.PuzzleIds).Distinct().Count();
        if (puzzles > 0) row.Children.Add(Kit.Btn(Loc.T("report.train", puzzles), () => _model.TrainTakeaways(g)));
        if (_model.TakeawayFocus(g) is { } skill)
            row.Children.Add(Kit.Btn(Loc.T("report.setFocus"), () =>
            {
                _model.SetFocus(skill);
                MessageBox.Show(_window, Loc.T("report.focusSet", skill.Label()), _window.Title);
            }));
        if (row.Children.Count > 0) _body.Children.Add(row);
    }
}
