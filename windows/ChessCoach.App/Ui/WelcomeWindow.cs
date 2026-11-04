using System.Windows;
using System.Windows.Controls;
using ChessCoach.App.Model;

namespace ChessCoach.App.Ui;

internal sealed class WelcomeWindow
{
    private readonly Window _window = Kit.Window(Loc.T("welcome.title"), 520, 460);

    public WelcomeWindow(AppModel model)
    {
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(Kit.Text(Loc.T("welcome.title"), 20, bold: true, bottom: 12));
        root.Children.Add(Kit.Text(Loc.T("welcome.intro"), bottom: 16));
        var steps = new[]
        {
            Loc.T("welcome.step1"), Loc.T("welcome.step2", Loc.T("menu.active")), Loc.T("welcome.step3"), Loc.T("welcome.step4"),
        };
        for (int i = 0; i < steps.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var number = Kit.Text($"{i + 1}.", bold: true);
            number.Width = 24;
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            row.Children.Add(Kit.Text(steps[i]));
            root.Children.Add(row);
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(Kit.Btn(Loc.T("welcome.gotIt"), _window.Hide));
        var on = Kit.Btn(Loc.T("welcome.switchOn"), () => { model.SetActive(true); _window.Hide(); });
        on.IsDefault = true;
        buttons.Children.Add(on);
        root.Children.Add(buttons);
        _window.Content = root;
        _window.SizeToContent = SizeToContent.Height;
    }

    public void Show() => Kit.Open(_window);
}
