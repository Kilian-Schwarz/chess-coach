using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ChessCoach.App.Platform;

namespace ChessCoach.App.Ui;

internal static class Kit
{
    public static TextBlock Text(string text, double size = 13, bool bold = false, bool muted = false, double bottom = 0) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
        Foreground = muted ? System.Windows.Media.Brushes.Gray : System.Windows.SystemColors.WindowTextBrush,
        Margin = new Thickness(0, 0, 0, bottom),
    };

    public static Button Btn(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
        b.Click += (_, _) => click();
        return b;
    }

    public static Window Window(string title, double width, double height)
    {
        var w = new Window
        {
            Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
        };
        w.SourceInitialized += (_, _) => Native.ExcludeFromCapture(new WindowInteropHelper(w).Handle);
        w.Closing += (_, e) => { e.Cancel = true; w.Hide(); };
        return w;
    }

    public static void Open(Window w)
    {
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    public static ProgressBar Bar(double value, double max = 1) => new()
    {
        Minimum = 0, Maximum = max, Value = value, Height = 10, Margin = new Thickness(0, 2, 0, 6),
        Foreground = System.Windows.Media.Brushes.SteelBlue,
    };
}
