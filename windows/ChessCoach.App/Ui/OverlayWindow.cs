using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ChessCoach.App.Platform;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class OverlayWindow : Window
{
    private readonly OverlayVisual _visual = new();
    private IntPtr _hwnd;
    private (int x, int y, int w, int h) _bounds;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        Width = 10; Height = 10; Left = -32000; Top = -32000;
        Content = _visual;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            Native.SetExStyle(_hwnd, Native.GetExStyle(_hwnd) | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST);
            if (!Native.ExcludeFromCapture(_hwnd)) Log.Write("Overlay: SetWindowDisplayAffinity unavailable (needs Windows 10 2004 or later)");
        };
    }

    public void Show(System.Drawing.Rectangle boardPx, OverlayState state)
    {
        const int pad = 4;
        var b = (boardPx.X - pad, boardPx.Y - pad, boardPx.Width + 2 * pad, boardPx.Height + 2 * pad);
        if (!IsVisible) { Show(); }
        if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).Handle;
        if (b != _bounds)
        {
            _bounds = b;
            Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, b.Item1, b.Item2, b.Item3, b.Item4,
                Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW | Native.SWP_NOOWNERZORDER);
        }
        _visual.SetState(state);
    }

    public void HideOverlay()
    {
        if (IsVisible) Hide();
        _bounds = default;
    }
}

internal sealed class OverlayVisual : FrameworkElement
{
    private OverlayState _state = new();

    public void SetState(OverlayState s)
    {
        if (_state == s) return;
        _state = s;
        InvalidateVisual();
    }

    private static readonly Brush Frame = Freeze(new SolidColorBrush(Color.FromArgb(140, 52, 199, 89)));
    private static readonly Pen FramePen = FreezePen(new Pen(Frame, 2));
    private static readonly Brush LabelBg = Freeze(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)));
    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static Pen FreezePen(Pen p) { p.Freeze(); return p; }
    private static readonly Typeface LabelFace = new("Segoe UI Semibold");

    protected override void OnRender(DrawingContext dc)
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double inset = 4 / dpi;
        double size = Math.Min(ActualWidth, ActualHeight) - 2 * inset;
        if (size <= 0) return;
        double sq = size / 8;
        dc.DrawRoundedRectangle(null, FramePen, new Rect(inset, inset, size, size), 3, 3);

        if (_state.Highlight is { } h)
        {
            var c = _state.PartnerHighlight ? Color.FromRgb(10, 132, 255) : Color.FromRgb(255, 214, 10);
            var fill = new SolidColorBrush(Color.FromArgb(72, c.R, c.G, c.B));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(242, c.R, c.G, c.B)), Math.Max(2, sq * 0.06));
            dc.DrawRectangle(fill, pen, new Rect(inset + h.Col * sq, inset + h.Row * sq, sq, sq));
        }
        if (_state.Threat is { } t) DrawArrow(dc, t, sq, inset, Color.FromRgb(255, 59, 48));
        if (_state.Move is { } a) DrawArrow(dc, a, sq, inset, a.Partner ? Color.FromRgb(10, 132, 255) : Color.FromRgb(52, 199, 89));

        string? text = _state.Label ?? _state.Hint;
        if (!string.IsNullOrEmpty(text))
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                LabelFace, 14, Brushes.White, dpi);
            var r = new Rect(inset + 8, inset + 8, ft.Width + 20, ft.Height + 8);
            dc.DrawRoundedRectangle(LabelBg, null, r, r.Height / 2, r.Height / 2);
            dc.DrawText(ft, new Point(r.X + 10, r.Y + 4));
        }
    }

    private static void DrawArrow(DrawingContext dc, OverlayState.Arrow a, double sq, double inset, Color c)
    {
        var from = new Point(inset + (a.From.Col + 0.5) * sq, inset + (a.From.Row + 0.5) * sq);
        var to = new Point(inset + (a.To.Col + 0.5) * sq, inset + (a.To.Row + 0.5) * sq);
        double width = sq * 0.2;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1);
        double ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
        double head = Math.Min(width * 2.4, len * 0.6);
        var start = new Point(from.X + ux * width * 1.2, from.Y + uy * width * 1.2);
        var neck = new Point(to.X - ux * head, to.Y - uy * head);
        double w = width / 2;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(start.X + nx * w, start.Y + ny * w), true, true);
            ctx.LineTo(new Point(neck.X + nx * w, neck.Y + ny * w), true, false);
            ctx.LineTo(new Point(neck.X + nx * w * 2.6, neck.Y + ny * w * 2.6), true, false);
            ctx.LineTo(to, true, false);
            ctx.LineTo(new Point(neck.X - nx * w * 2.6, neck.Y - ny * w * 2.6), true, false);
            ctx.LineTo(new Point(neck.X - nx * w, neck.Y - ny * w), true, false);
            ctx.LineTo(new Point(start.X - nx * w, start.Y - ny * w), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(191, c.R, c.G, c.B)), null, g);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(230, c.R, c.G, c.B)), Math.Max(2, sq * 0.06)),
            from, sq * 0.45, sq * 0.45);
    }
}
