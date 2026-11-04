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
    private static readonly Brush DotBrush = Frozen(Color.FromArgb(120, 20, 20, 20));
    private static readonly Typeface Face = new("Segoe UI Symbol");
    private static readonly string Glyphs = "KQRBNP";
    private static readonly string[] Symbols = { "♚", "♛", "♜", "♝", "♞", "♟" };

    private Placement _placement = new();
    private bool _whiteAtBottom = true;
    private (int File, int Rank)? _selected, _from, _to, _drag;
    private IReadOnlyCollection<(int File, int Rank)> _targets = Array.Empty<(int, int)>();
    private Point _cursor;

    public event Action<int, int>? SquareDown;
    public event Action<int, int>? SquareUp;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public void Set(Placement placement, bool whiteAtBottom)
    {
        _placement = placement;
        _whiteAtBottom = whiteAtBottom;
        _selected = _from = _to = _drag = null;
        _targets = Array.Empty<(int, int)>();
        InvalidateVisual();
    }

    public void Select((int, int)? square, IEnumerable<(int, int)>? targets = null)
    {
        _selected = square;
        _targets = targets?.ToList() ?? (IReadOnlyCollection<(int, int)>)Array.Empty<(int, int)>();
        InvalidateVisual();
    }

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
        if (Hit(e) is not { } sq) return;
        SquareDown?.Invoke(sq.File, sq.Rank);
        if (_selected == sq && _placement.Squares[sq.Rank][sq.File] != null)
        {
            _drag = sq;
            _cursor = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_drag == null) return;
        _cursor = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        bool dragging = _drag != null;
        _drag = null;
        if (dragging) { ReleaseMouseCapture(); InvalidateVisual(); }
        if (Hit(e) is { } sq) SquareUp?.Invoke(sq.File, sq.Rank);
    }

    protected override Size MeasureOverride(Size available)
    {
        double s = Math.Min(double.IsInfinity(available.Width) ? 400 : available.Width, double.IsInfinity(available.Height) ? 400 : available.Height);
        return new Size(s, s);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double s = Math.Min(finalSize.Width, finalSize.Height);
        return new Size(s, s);
    }

    private void DrawPiece(DrawingContext dc, char p, Point center, double s, double dpi)
    {
        int idx = Glyphs.IndexOf(char.ToUpperInvariant(p));
        var ft = new FormattedText(Symbols[idx], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, s * 0.8, Brushes.Black, dpi);
        var geo = ft.BuildGeometry(new Point(center.X - ft.Width / 2, center.Y - ft.Height / 2));
        bool white = char.IsUpper(p);
        dc.DrawGeometry(white ? Brushes.White : Brushes.Black, new Pen(white ? Brushes.Black : Brushes.White, s * 0.025), geo);
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
                if (_targets.Contains((f, r)))
                {
                    if (_placement.Squares[r][f] != null) dc.DrawEllipse(null, new Pen(DotBrush, s * 0.08), new Point(col * s + s / 2, row * s + s / 2), s * 0.44, s * 0.44);
                    else dc.DrawEllipse(DotBrush, null, new Point(col * s + s / 2, row * s + s / 2), s * 0.16, s * 0.16);
                }
                if (_placement.Squares[r][f] is not char p || _drag == (f, r)) continue;
                DrawPiece(dc, p, new Point(col * s + s / 2, row * s + s / 2), s, dpi);
            }
        if (_drag is { } d && _placement.Squares[d.Rank][d.File] is char dp) DrawPiece(dc, dp, _cursor, s, dpi);
    }
}
