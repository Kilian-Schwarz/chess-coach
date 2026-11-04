using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ChessCoach.App.Model;
using ChessCoach.App.Platform;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class PanelWindow : Window
{
    public const double WidthDip = 340, CollapsedHeightDip = 44;
    public const int HintMax = 4;

    private readonly AppSettings _settings;
    private readonly StackPanel _cardsHost = new() { Margin = new Thickness(12) };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title = Label("Chess Coach", 15, true, Theme.Text);
    private readonly TextBlock _focus = Label("", 11, false, Theme.Muted);
    private readonly TextBlock _hintInfo = Label("", 11, false, Theme.Muted);
    private readonly Button _collapse;
    private readonly Button _next, _again, _send, _finish;
    private readonly TextBox _question;
    private readonly StackPanel _extraHost = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
    private readonly UIElement _body;
    private readonly Dictionary<string, string> _votes = new();
    private IReadOnlyList<CoachCard> _cards = Array.Empty<CoachCard>();
    private IntPtr _hwnd;
    private bool _applying;
    private System.Drawing.Rectangle _board;
    private int _hintLevel;
    private bool _canHint, _canExplain, _isAsking, _canFinish;

    public event Action? NextHintClicked, ExplainAgainClicked, FinishClicked;
    public event Action<string>? QuestionAsked;
    public event Action<CoachCard, string>? FeedbackGiven;
    public event Action<bool>? CollapsedChanged;

    public PanelWindow(AppSettings settings)
    {
        _settings = settings;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = WidthDip; Height = 400; Left = -32000; Top = -32000;
        FontFamily = new FontFamily("Segoe UI");

        _collapse = new Button { Content = "⌃", Style = Theme.SmallButton, Width = 28, Height = 24, ToolTip = Loc.T("panel.collapse") };
        _collapse.Click += (_, _) => SetCollapsed(!_settings.PanelCollapsed, true);

        var header = new Grid { Height = CollapsedHeightDip, Margin = new Thickness(12, 0, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(_title);
        _focus.Visibility = Visibility.Collapsed;
        titles.Children.Add(_focus);
        header.Children.Add(titles);
        Grid.SetColumn(_hintInfo, 1);
        _hintInfo.VerticalAlignment = VerticalAlignment.Center;
        _hintInfo.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(_hintInfo);
        Grid.SetColumn(_collapse, 2);
        header.Children.Add(_collapse);

        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _cardsHost };

        _next = new Button { Content = Loc.T("panel.nextHint"), Style = Theme.PrimaryButton, Margin = new Thickness(0, 0, 6, 0) };
        _next.Click += (_, _) => NextHintClicked?.Invoke();
        _again = new Button { Content = Loc.T("panel.again"), Style = Theme.PanelButton };
        _again.Click += (_, _) => ExplainAgainClicked?.Invoke();
        _finish = new Button { Content = Loc.T("panel.finish"), Style = Theme.SmallButton, Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        _finish.Click += (_, _) => FinishClicked?.Invoke();
        var row1 = new StackPanel { Orientation = Orientation.Horizontal };
        row1.Children.Add(_next); row1.Children.Add(_again);

        _question = new TextBox
        {
            Background = Theme.FieldBg, Foreground = Theme.Text, CaretBrush = Theme.Text, BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 5, 8, 5), FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 300,
        };
        _question.PreviewMouseLeftButtonDown += (_, _) => ActivateForTyping();
        _question.KeyDown += (_, e) => { if (e.Key == Key.Enter) SendQuestion(); };
        _question.TextChanged += (_, _) => UpdateButtons();
        var hint = new TextBlock { Text = Loc.T("panel.ask"), Foreground = Theme.Muted, Margin = new Thickness(10, 0, 0, 0),
            IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var questionGrid = new Grid();
        questionGrid.Children.Add(_question);
        questionGrid.Children.Add(hint);
        _question.TextChanged += (_, _) => hint.Visibility = _question.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _send = new Button { Content = "➤", Style = Theme.PrimaryButton, Margin = new Thickness(6, 0, 0, 0), Width = 34 };
        _send.Click += (_, _) => SendQuestion();
        var row2 = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row2.Children.Add(questionGrid);
        Grid.SetColumn(_send, 1);
        row2.Children.Add(_send);

        var controls = new StackPanel { Margin = new Thickness(12) };
        controls.Children.Add(row1);
        controls.Children.Add(_finish);
        controls.Children.Add(_extraHost);
        controls.Children.Add(row2);

        var bodyGrid = new Grid();
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sep1 = new Border { Height = 1, Background = Theme.PanelBorder };
        bodyGrid.Children.Add(sep1);
        Grid.SetRow(_scroll, 1); bodyGrid.Children.Add(_scroll);
        var ctl = new Border { BorderBrush = Theme.PanelBorder, BorderThickness = new Thickness(0, 1, 0, 0), Child = controls };
        Grid.SetRow(ctl, 2); bodyGrid.Children.Add(ctl);
        _body = bodyGrid;

        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(bodyGrid);
        Content = new Border
        {
            Background = Theme.PanelBg, BorderBrush = Theme.PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Child = root, SnapsToDevicePixels = true,
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); } catch (InvalidOperationException) { return; }
            RememberPosition();
        };
        Deactivated += (_, _) => SetNoActivate(true);
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, Native.GetExStyle(_hwnd) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        };
        ApplyCollapsedVisuals();
        UpdateButtons();
    }

    private static TextBlock Label(string text, double size, bool bold, Brush brush) => new()
    {
        Text = text, FontSize = size, Foreground = brush, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    public int HintLevel { set { if (_hintLevel != value) { _hintLevel = value; _hintInfo.Text = value > 0 ? Loc.T("panel.hintOf", value, HintMax) : ""; UpdateButtons(); } } }
    public bool CanHint { set { if (_canHint != value) { _canHint = value; UpdateButtons(); } } }
    public bool CanExplain { set { if (_canExplain != value) { _canExplain = value; UpdateButtons(); } } }
    public bool CanFinish { set { if (_canFinish != value) { _canFinish = value; UpdateButtons(); } } }
    public bool IsAsking { set { if (_isAsking != value) { _isAsking = value; UpdateButtons(); } } }

    public string? FocusText
    {
        set
        {
            _focus.Text = value ?? "";
            _focus.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public void ClearQuestion() => _question.Text = "";

    public void SetExtraActions(IEnumerable<PanelAction> actions)
    {
        _extraHost.Children.Clear();
        foreach (var a in actions)
        {
            var b = new Button { Content = a.Title, Style = Theme.SmallButton, Margin = new Thickness(0, 0, 6, 0) };
            var h = a.Handler;
            b.Click += (_, _) => h();
            _extraHost.Children.Add(b);
        }
    }

    private void UpdateButtons()
    {
        _next.IsEnabled = _canHint && _hintLevel < HintMax;
        _again.IsEnabled = _canExplain;
        _finish.IsEnabled = _canFinish;
        _send.IsEnabled = !_isAsking && _question.Text.Trim().Length > 0;
    }

    public void ShowNextTo(System.Drawing.Rectangle boardPx, IReadOnlyList<CoachCard> cards, bool force = false)
    {
        if (!SameCards(cards)) { _cards = cards; RebuildCards(); }
        bool moved = boardPx != _board;
        _board = boardPx;
        if (!IsVisible) { Show(); force = true; }
        if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).Handle;
        if (moved || force) Place();
    }

    public void HidePanel() { if (IsVisible) Hide(); }

    private bool SameCards(IReadOnlyList<CoachCard> cards)
    {
        if (cards.Count != _cards.Count) return false;
        for (int i = 0; i < cards.Count; i++) if (!cards[i].Equals(_cards[i])) return false;
        return true;
    }

    private MonitorInfo? MonitorAt(int x, int y)
    {
        var all = ScreenCapturer.Monitors();
        return all.FirstOrDefault(m => x >= m.X && x < m.X + m.Width && y >= m.Y && y < m.Y + m.Height) ?? all.FirstOrDefault();
    }

    public void Place(bool ignoreSaved = false)
    {
        if (_hwnd == IntPtr.Zero || _board.Width == 0) return;
        var mon = MonitorAt(_board.X + _board.Width / 2, _board.Y + _board.Height / 2);
        if (mon == null) return;
        double s = mon.Scale;
        int w = (int)Math.Round(WidthDip * s);
        double hDip = _settings.PanelCollapsed ? CollapsedHeightDip : Math.Min(Math.Max(_board.Height / s, 320), 640);
        int h = (int)Math.Round(hDip * s);
        int x, y;
        if (!ignoreSaved && _settings.PanelPositions.TryGetValue(mon.Key, out var off) && off.Length == 2)
        {
            x = mon.WorkX + (int)off[0]; y = mon.WorkY + (int)off[1];
        }
        else
        {
            int gap = (int)(14 * s);
            x = _board.Right + gap;
            if (x + w > mon.WorkX + mon.WorkWidth)
                x = _board.Left - gap - w >= mon.WorkX ? _board.Left - gap - w : mon.WorkX + mon.WorkWidth - w - 8;
            y = _board.Top;
        }
        x = Math.Min(Math.Max(x, mon.WorkX), Math.Max(mon.WorkX, mon.WorkX + mon.WorkWidth - w));
        y = Math.Min(Math.Max(y, mon.WorkY), Math.Max(mon.WorkY, mon.WorkY + mon.WorkHeight - h));
        _applying = true;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);
        _applying = false;
    }

    private void RememberPosition()
    {
        if (_applying || _hwnd == IntPtr.Zero || !Native.GetWindowRect(_hwnd, out var r)) return;
        var mon = MonitorAt((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
        if (mon == null) return;
        _settings.PanelPositions[mon.Key] = new double[] { r.Left - mon.WorkX, r.Top - mon.WorkY };
        _settings.Save();
    }

    public void ResetPosition()
    {
        _settings.PanelPositions.Clear();
        _settings.Save();
        Place(true);
    }

    private void SetCollapsed(bool value, bool notify)
    {
        _settings.PanelCollapsed = value;
        _settings.Save();
        ApplyCollapsedVisuals();
        Place();
        if (notify) CollapsedChanged?.Invoke(value);
    }

    private void ApplyCollapsedVisuals()
    {
        bool c = _settings.PanelCollapsed;
        _body.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
        _collapse.Content = c ? "⌄" : "⌃";
        _collapse.ToolTip = Loc.T(c ? "panel.expand" : "panel.collapse");
    }

    private void SetNoActivate(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = Native.GetExStyle(_hwnd);
        Native.SetExStyle(_hwnd, on ? ex | Native.WS_EX_NOACTIVATE : ex & ~Native.WS_EX_NOACTIVATE);
    }

    private void ActivateForTyping()
    {
        SetNoActivate(false);
        Activate();
        _question.Focus();
        Keyboard.Focus(_question);
    }

    private void SendQuestion()
    {
        var q = _question.Text.Trim();
        if (q.Length == 0 || _isAsking) return;
        QuestionAsked?.Invoke(q);
    }

    private void RebuildCards()
    {
        _cardsHost.Children.Clear();
        foreach (var card in _cards) _cardsHost.Children.Add(BuildCard(card));
    }

    private UIElement BuildCard(CoachCard card)
    {
        var stack = new StackPanel();
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        if (card.Rating is { } r)
        {
            titleRow.Children.Add(new TextBlock
            {
                Text = Theme.RatingSymbol(r), Foreground = new SolidColorBrush(Theme.RatingColor(r)), FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 6, 0), FontSize = 14,
            });
        }
        var title = Label(card.Title, 14, true, Theme.Text);
        titleRow.Children.Add(title);
        stack.Children.Add(titleRow);
        foreach (var line in card.Lines) stack.Children.Add(Label(line, 12.5, false, Theme.Muted));
        if (card.Text.Length > 0)
        {
            var t = Label(card.Text, 13.5, false, Theme.Text);
            t.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(t);
        }
        else if (card.IsLoading)
        {
            var t = Label(Loc.T("card.loading"), 12.5, false, Theme.Muted);
            t.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(t);
        }
        if (card.Note != null)
        {
            var n = Label(card.Note, 11.5, false, Theme.Muted);
            n.Margin = new Thickness(0, 4, 0, 0);
            stack.Children.Add(n);
        }
        if (card.AllowFeedback && !card.IsLoading && card.Text.Length > 0) stack.Children.Add(BuildFeedback(card));
        return new Border
        {
            Background = Theme.CardBg, CornerRadius = new CornerRadius(10), Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10), Child = stack,
        };
    }

    private UIElement BuildFeedback(CoachCard card)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        if (_votes.TryGetValue(card.Id, out var given))
        {
            row.Children.Add(Label(Loc.T("card.thanks", given), 11, false, Theme.Muted));
            return row;
        }
        void Add(string text, string key)
        {
            var b = new Button { Content = text, Style = Theme.SmallButton, Margin = new Thickness(0, 0, 6, 0) };
            b.Click += (_, _) =>
            {
                _votes[card.Id] = text;
                if (_votes.Count > 60) _votes.Remove(_votes.Keys.First());
                FeedbackGiven?.Invoke(card, key);
                RebuildCards();
            };
            row.Children.Add(b);
        }
        Add(Loc.T("feedback.long"), "long"); Add(Loc.T("feedback.short"), "short"); Add(Loc.T("feedback.helpful"), "helpful");
        return row;
    }
}
