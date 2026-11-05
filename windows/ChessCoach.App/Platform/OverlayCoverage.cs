using ChessCoach.App.Ui;

namespace ChessCoach.App.Platform;

internal static class OverlayCoverage
{
    public static bool[] Squares(OverlayState state)
    {
        var covered = new bool[64];
        void Mark(double col, double row)
        {
            int c = (int)Math.Floor(col), r = (int)Math.Floor(row);
            if (c is >= 0 and < 8 && r is >= 0 and < 8) covered[r * 8 + c] = true;
        }
        void Arrow(OverlayState.Arrow? arrow)
        {
            if (arrow is not { } a) return;
            for (int i = 0; i <= 32; i++)
            {
                double t = i / 32.0;
                Mark(a.From.Col + 0.5 + (a.To.Col - a.From.Col) * t, a.From.Row + 0.5 + (a.To.Row - a.From.Row) * t);
            }
        }
        Arrow(state.Move);
        Arrow(state.Threat);
        if (state.Highlight is { } h) Mark(h.Col, h.Row);
        if (state.Unsafe is { } marks) foreach (var m in marks) Mark(m.Col, m.Row);
        if (!string.IsNullOrEmpty(state.Label ?? state.Hint)) for (int c = 0; c < 8; c++) Mark(c, 0);
        return covered;
    }
}
