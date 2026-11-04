using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using ChessCoach.App.Model;
using ChessCoach.App.Platform;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class SettingsWindow : Window
{
    private readonly AppModel _model;
    private readonly TextBox _url = new(), _sfPath = new();
    private readonly PasswordBox _token = new();
    private readonly ComboBox _models = new();
    private readonly TextBlock _llmStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray };
    private readonly TextBlock _httpWarning = new() { Text = Loc.T("settings.httpWarning"), TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DarkOrange, Visibility = Visibility.Collapsed };
    private readonly TextBlock _sfStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Gray };
    private readonly GroupBox _expert = new() { Header = Loc.T("settings.expert"), Visibility = Visibility.Collapsed };
    private readonly CheckBox _showBest = new() { Content = Loc.T("menu.best") };
    private readonly CheckBox _always = new() { Content = Loc.T("menu.always") };
    private readonly ComboBox _think = new() { Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
    private bool _loading;

    public SettingsWindow(AppModel model)
    {
        _model = model;
        Title = Loc.T("settings.title");
        Width = 520; SizeToContent = SizeToContent.Height; MaxHeight = 760;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        var root = new StackPanel { Margin = new Thickness(14) };

        var llm = new StackPanel { Margin = new Thickness(8) };
        llm.Children.Add(Row(Loc.T("settings.server"), _url));
        llm.Children.Add(Row(Loc.T("settings.token"), _token));
        llm.Children.Add(_httpWarning);
        var load = new Button { Content = Loc.T("settings.loadModels"), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) };
        load.Click += async (_, _) => { _model.LlmToken = _token.Password; await _model.LoadModelsAsync(_url.Text); RefreshModels(); };
        llm.Children.Add(load);
        llm.Children.Add(Row(Loc.T("settings.model"), _models));
        llm.Children.Add(_llmStatus);
        root.Children.Add(new GroupBox { Header = Loc.T("settings.llm"), Content = llm, Margin = new Thickness(0, 0, 0, 8) });
        _models.SelectionChanged += (_, _) =>
        {
            if (!_loading && _models.SelectedItem is LlmModel m) _model.SetModel(m.Id);
        };
        _url.LostFocus += (_, _) => { _model.Settings.LlmUrl = _url.Text; _model.Settings.Save(); };
        _token.LostFocus += (_, _) => _model.LlmToken = _token.Password;
        _url.TextChanged += (_, _) => UpdateHttpWarning();
        _token.PasswordChanged += (_, _) => UpdateHttpWarning();

        var sf = new StackPanel { Margin = new Thickness(8) };
        var browse = new Button { Content = Loc.T("settings.choose"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) =>
        {
            var d = new OpenFileDialog { Filter = "Stockfish (*.exe)|*.exe", Title = Loc.T("settings.chooseTitle") };
            if (d.ShowDialog(this) == true) { _sfPath.Text = d.FileName; SavePath(); }
        };
        var sfRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        sfRow.Children.Add(browse);
        sfRow.Children.Add(_sfPath);
        sf.Children.Add(Row(Loc.T("settings.path"), sfRow));
        sf.Children.Add(_sfStatus);
        root.Children.Add(new GroupBox { Header = "Stockfish", Content = sf, Margin = new Thickness(0, 0, 0, 8) });
        _sfPath.LostFocus += (_, _) => SavePath();

        var gen = new StackPanel { Margin = new Thickness(8) };
        var autostart = new CheckBox { Content = Loc.T("settings.autostart"), IsChecked = Autostart.IsEnabled() };
        autostart.Click += (_, _) => Autostart.Set(autostart.IsChecked == true);
        gen.Children.Add(autostart);
        var resetPanel = new Button { Content = Loc.T("menu.resetPanel"), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        resetPanel.Click += (_, _) => _model.ResetPanelPosition();
        gen.Children.Add(resetPanel);
        gen.Children.Add(new TextBlock { Text = Loc.T("settings.hotkeys"), Margin = new Thickness(0, 8, 0, 0), Foreground = System.Windows.Media.Brushes.Gray, TextWrapping = TextWrapping.Wrap });
        var autoUpdate = new CheckBox { Content = Loc.T("settings.autoUpdate"), IsChecked = _model.Settings.AutoUpdate, Margin = new Thickness(0, 6, 0, 0) };
        autoUpdate.Click += (_, _) => { _model.Settings.AutoUpdate = autoUpdate.IsChecked == true; _model.Settings.Save(); };
        gen.Children.Add(autoUpdate);
        var names = new List<string> { Loc.T("settings.langSystem") };
        names.AddRange(Loc.Languages.Select(l => l.Name));
        var current = Array.FindIndex(Loc.Languages, l => l.Code == _model.Settings.Language) + 1;
        var lang = new ComboBox { ItemsSource = names, SelectedIndex = current, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        lang.SelectionChanged += (_, _) => ChangeLanguage(lang.SelectedIndex);
        gen.Children.Add(Row(Loc.T("settings.language"), lang));
        root.Children.Add(new GroupBox { Header = Loc.T("settings.general"), Content = gen, Margin = new Thickness(0, 0, 0, 8) });

        var ex = new StackPanel { Margin = new Thickness(8) };
        ex.Children.Add(_showBest);
        ex.Children.Add(_always);
        foreach (var ms in new[] { 300, 500, 1000, 2000, 4000 }) _think.Items.Add(ms);
        ex.Children.Add(Row(Loc.T("settings.thinkMs"), _think));
        var lockBtn = new Button { Content = Loc.T("settings.lock"), Padding = new Thickness(10, 3, 10, 3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        lockBtn.Click += (_, _) => { _model.ExpertUnlocked = false; ShowExpert(); };
        ex.Children.Add(lockBtn);
        _expert.Content = ex;
        root.Children.Add(_expert);
        _showBest.Click += (_, _) => { _model.Settings.ShowBestImmediately = _showBest.IsChecked == true; _model.SettingsChanged(); };
        _always.Click += (_, _) => { _model.Settings.AlwaysMyTurn = _always.IsChecked == true; _model.SettingsChanged(); };
        _think.SelectionChanged += (_, _) =>
        {
            if (!_loading && _think.SelectedItem is int ms) { _model.Settings.ThinkMs = ms; _model.SettingsChanged(); }
        };

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        PreviewKeyDown += OnKey;
        Closing += (_, e) => { e.Cancel = true; Hide(); };
        model.StateChanged += () => Dispatcher.BeginInvoke(RefreshStatus);
        Reload();
    }

    private void ChangeLanguage(int index)
    {
        string code = index == 0 ? "system" : Loc.Languages[index - 1].Code;
        if (code == _model.Settings.Language) return;
        _model.Settings.Language = code;
        _model.Settings.Save();
        if (MessageBox.Show(this, Loc.T("settings.restartPrompt"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _model.Restart?.Invoke();
    }

    private static UIElement Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var need = ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift;
        if (key == Key.E && (Keyboard.Modifiers & need) == need)
        {
            _model.ExpertUnlocked = !_model.ExpertUnlocked;
            ShowExpert();
            e.Handled = true;
        }
    }

    private void SavePath()
    {
        _model.Settings.StockfishPath = _sfPath.Text.Trim();
        _model.Settings.Save();
        RefreshStatus();
    }

    private void ShowExpert()
    {
        _expert.Visibility = _model.Settings.ExpertUnlocked ? Visibility.Visible : Visibility.Collapsed;
        _loading = true;
        _showBest.IsChecked = _model.Settings.ShowBestImmediately;
        _always.IsChecked = _model.Settings.AlwaysMyTurn;
        _think.SelectedItem = _model.Settings.ThinkMs;
        _loading = false;
    }

    private void UpdateHttpWarning() =>
        _httpWarning.Visibility = _token.Password.Length > 0 && _url.Text.Trim().StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;

    private void RefreshModels()
    {
        _loading = true;
        _models.ItemsSource = _model.LlmModels;
        _models.DisplayMemberPath = "Name";
        _models.SelectedItem = _model.LlmModels.FirstOrDefault(m => m.Id == _model.Settings.LlmModel);
        _loading = false;
    }

    private void RefreshStatus()
    {
        _llmStatus.Text = _model.LlmStatus;
        var found = StockfishEngine.FindBinary(_model.Settings.StockfishPath, AppPaths.AppDir);
        _sfStatus.Text = found != null ? Loc.T("settings.sfFound", found) : Loc.T("settings.sfMissing");
    }

    public void Reload()
    {
        _url.Text = _model.Settings.LlmUrl;
        _token.Password = _model.LlmToken;
        _sfPath.Text = _model.Settings.StockfishPath;
        RefreshModels();
        ShowExpert();
        RefreshStatus();
    }

    public void ShowAndActivate()
    {
        Reload();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}

internal static class Autostart
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key);
        return k?.GetValue("ChessCoach") != null;
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Key, true);
        if (k == null) return;
        if (on) k.SetValue("ChessCoach", "\"" + Environment.ProcessPath + "\"");
        else k.DeleteValue("ChessCoach", false);
    }
}
