using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ChessCoach.App.Model;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class RecordWindow
{
    private readonly AppModel _model;
    private readonly Window _window = Kit.Window(Loc.T("record.title"), 560, 720);
    private readonly ComboBox _profiles = new() { DisplayMemberPath = "Name", MinWidth = 180 };
    private readonly StackPanel _body = new() { Margin = new Thickness(14) };
    private bool _loading;

    public RecordWindow(AppModel model)
    {
        _model = model;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 14, 14, 0) };
        top.Children.Add(_profiles);
        var add = Kit.Btn(Loc.T("record.new"), AddProfile);
        add.Margin = new Thickness(8, 0, 6, 0);
        top.Children.Add(add);
        var asPartner = Kit.Btn(Loc.T("record.partner"), () => { if (_profiles.SelectedItem is PlayerProfile p) _model.SelectProfile(p.Id, true); });
        top.Children.Add(asPartner);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _window.Content = root;
        _profiles.SelectionChanged += (_, _) =>
        {
            if (_loading || _profiles.SelectedItem is not PlayerProfile p) return;
            _model.SelectProfile(p.Id, false);
            Render(p);
        };
    }

    public void Show()
    {
        _loading = true;
        _profiles.ItemsSource = null;
        _profiles.ItemsSource = _model.Profiles;
        _profiles.SelectedItem = _model.Active;
        _loading = false;
        Render(_model.Active);
        Kit.Open(_window);
    }

    private void AddProfile()
    {
        var name = Prompt.Ask(_window, Loc.T("record.newName"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var p = _model.AddProfile(name.Trim());
        _model.SelectProfile(p.Id, false);
        Show();
    }

    private void Render(PlayerProfile p)
    {
        _body.Children.Clear();
        _body.Children.Add(Kit.Text($"{p.Name} · {p.Level.Label()} · {Loc.T("record.rating")} {p.Rating:0}", 18, true, bottom: 2));
        _body.Children.Add(Kit.Text(p.PlacementText ?? Loc.T("record.games", p.GamesPlayed), muted: true, bottom: 10));
        _body.Children.Add(Kit.Text(Curriculum.FocusLabel(p.FocusSkill), 14, true, bottom: 10));

        _body.Children.Add(Kit.Text(Loc.T("record.history"), 14, true, bottom: 4));
        _body.Children.Add(HistoryChart(p));

        _body.Children.Add(Kit.Text(Loc.T("record.skills"), 14, true, bottom: 4));
        foreach (var s in SkillExt.All)
        {
            double v = Curriculum.Value(p.Skills, s);
            _body.Children.Add(Kit.Text($"{s.Label()} ({v:P0})", 12));
            _body.Children.Add(Kit.Bar(v));
        }

        _body.Children.Add(Kit.Text(Loc.T("record.mistakes"), 14, true, bottom: 4));
        var top = p.TopMistakes(5);
        if (top.Count == 0) _body.Children.Add(Kit.Text(Loc.T("record.none"), muted: true, bottom: 8));
        foreach (var (t, n) in top) _body.Children.Add(Kit.Text($"{t.Label()}: {n}", 12));

        var set = _model.PuzzlesFor(p.Id).Puzzles;
        var st = PuzzleScheduler.Stats(set);
        _body.Children.Add(Kit.Text(Loc.T("record.puzzles"), 14, true, bottom: 4).WithMargin(top: 10));
        _body.Children.Add(Kit.Text(Loc.T("record.puzzleStats", st.Total, st.Due + st.New, st.Mastered, st.Attempts,
            st.RecentSuccessRate is double q ? $"{q:P0}" : "-", p.LearningSpeed.ToString("0.0")), 12));

        _body.Children.Add(Kit.Text(Loc.T("record.hints"), 14, true, bottom: 4).WithMargin(top: 10));
        _body.Children.Add(Kit.Text(string.Join(" · ", p.HintStats.LevelUses.Select((n, i) => $"{(i == 0 ? Loc.T("record.noHint") : i.ToString())}: {n}")), 12));

        _body.Children.Add(Kit.Text(Loc.T("record.style"), 14, true, bottom: 4).WithMargin(top: 10));
        _body.Children.Add(Kit.Text($"{p.Style.Length.Raw()} · {p.Style.Depth.Raw()}", 12));

        _body.Children.Add(Kit.Text(Loc.T("record.lastGames"), 14, true, bottom: 4).WithMargin(top: 10));
        foreach (var g in _model.Storage.LoadGames(30).Where(g => g.WhiteProfileId == p.Id || g.BlackProfileId == p.Id).Take(8))
        {
            var side = g.WhiteProfileId == p.Id ? g.White : g.Black;
            _body.Children.Add(Kit.Text($"{g.Date.ToLocalTime():g} · {side.Accuracy:0} % · {g.Opening ?? "-"}", 12));
        }
    }

    private static FrameworkElement HistoryChart(PlayerProfile p)
    {
        var canvas = new Canvas { Height = 90, Margin = new Thickness(0, 0, 0, 10), ClipToBounds = true };
        var h = p.RatingHistory;
        if (h.Count < 2) return new Border { Height = 24, Child = Kit.Text(Loc.T("record.noHistory"), muted: true) };
        double min = h.Min(e => e.Rating) - 20, max = h.Max(e => e.Rating) + 20;
        var line = new Polyline { Stroke = Brushes.SteelBlue, StrokeThickness = 2 };
        canvas.SizeChanged += (_, _) =>
        {
            line.Points.Clear();
            for (int i = 0; i < h.Count; i++)
                line.Points.Add(new Point(i * canvas.ActualWidth / (h.Count - 1), canvas.Height - (h[i].Rating - min) / (max - min) * canvas.Height));
        };
        canvas.Children.Add(line);
        return canvas;
    }
}

internal static class Ext
{
    public static T WithMargin<T>(this T e, double top = 0) where T : FrameworkElement
    {
        e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
        return e;
    }
}

internal static class Prompt
{
    public static string? Ask(Window owner, string label)
    {
        var box = new TextBox { Margin = new Thickness(0, 6, 0, 10), MinWidth = 240 };
        var ok = new Button { Content = Loc.T("common.ok"), IsDefault = true, Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = Loc.T("common.cancel"), IsCancel = true, Padding = new Thickness(14, 3, 14, 3) };
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(ok); row.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(box); panel.Children.Add(row);
        var win = new Window
        {
            Content = panel, SizeToContent = SizeToContent.WidthAndHeight, Owner = owner, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = label, ShowInTaskbar = false,
        };
        ok.Click += (_, _) => win.DialogResult = true;
        box.Loaded += (_, _) => box.Focus();
        return win.ShowDialog() == true ? box.Text : null;
    }
}
