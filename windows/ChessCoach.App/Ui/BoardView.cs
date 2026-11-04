using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal sealed class BoardView : FrameworkElement
{
    private static readonly Brush Light = Frozen(Color.FromRgb(0xEE, 0xEE, 0xD2));
    private static readonly Brush Dark = Frozen(Color.FromRgb(0x76, 0x96, 0x56));
    private static readonly Brush SelectBrush = Frozen(Color.FromArgb(150, 255, 214, 10));
    private static readonly Brush MarkBrush = Frozen(Color.FromArgb(150, 10, 132, 255));
    private static readonly Typeface Face = new("Segoe UI Symbol");
    private static readonly string Glyphs = "KQRBNP";
    private static readonly string[] Symbols = { "♚", "♛", "♜", "♝", "♞", "♟" };

    private Placement _placement = new();
    private bool _whiteAtBottom = true;
    private (int File, int Rank)? _selected, _from, _to;

    public event Action<int, int>? SquareDown;
    public event Action<int, int>? SquareUp;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public void Set(Placement placement, bool whiteAtBottom)
    {
        _placement = placement;
        _whiteAtBottom = whiteAtBottom;
        _selected = _from = _to = null;
        InvalidateVisual();
    }

    public void Select((int, int)? square) { _selected = square; InvalidateVisual(); }

    public void Mark((int, int)? from, (int, int)? to) { _from = from; _to = to; InvalidateVisual(); }

    private (int File, int Rank)? Hit(MouseEventArgs e)
    {
        double s = Math.Min(ActualWidth, ActualHeight) / 8;
        var p = e.GetPosition(this);
        int col = (int)(p.X / s), row = (int)(p.Y / s);
        if (col is < 0 or > 7 || row is < 0 or > 7) return null;
        return Placement.ChessSquare(col, row, _whiteAtBottom);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (Hit(e) is { } sq) SquareDown?.Invoke(sq.File, sq.Rank);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (Hit(e) is { } sq) SquareUp?.Invoke(sq.File, sq.Rank);
    }

    protected override Size MeasureOverride(Size available)
    {
        double s = Math.Min(double.IsInfinity(available.Width) ? 400 : available.Width, double.IsInfinity(available.Height) ? 400 : available.Height);
        return new Size(s, s);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double s = Math.Min(ActualWidth, ActualHeight) / 8;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (int row = 0; row < 8; row++)
            for (int col = 0; col < 8; col++)
            {
                var rect = new Rect(col * s, row * s, s, s);
                dc.DrawRectangle((row + col) % 2 == 0 ? Light : Dark, null, rect);
                var (f, r) = Placement.ChessSquare(col, row, _whiteAtBottom);
                if (_selected == (f, r)) dc.DrawRectangle(SelectBrush, null, rect);
                if (_from == (f, r) || _to == (f, r)) dc.DrawRectangle(MarkBrush, null, rect);
                if (_placement.Squares[r][f] is not char p) continue;
                int idx = Glyphs.IndexOf(char.ToUpperInvariant(p));
                var ft = new FormattedText(Symbols[idx], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, s * 0.8, Brushes.Black, dpi);
                var geo = ft.BuildGeometry(new Point(col * s + (s - ft.Width) / 2, row * s + (s - ft.Height) / 2));
                bool white = char.IsUpper(p);
                dc.DrawGeometry(white ? Brushes.White : Brushes.Black, new Pen(white ? Brushes.Black : Brushes.White, s * 0.025), geo);
            }
    }
}
