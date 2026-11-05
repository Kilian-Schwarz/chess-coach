using System.Drawing;

namespace ChessCoach.App.Platform;

internal static class CaptureMath
{
    public static Rectangle WindowCrop(Rectangle region, Rectangle windowRect, out Rectangle clipped)
    {
        clipped = Rectangle.Intersect(region, windowRect);
        if (clipped.IsEmpty) return Rectangle.Empty;
        return new Rectangle(clipped.X - windowRect.X, clipped.Y - windowRect.Y, clipped.Width, clipped.Height);
    }

    public static bool IsUniform(byte[] rgba, int pixels)
    {
        if (pixels <= 1) return true;
        uint first = BitConverter.ToUInt32(rgba, 0);
        for (int i = 1; i < pixels; i++)
            if (BitConverter.ToUInt32(rgba, i * 4) != first) return false;
        return true;
    }

    public static bool IsBlack(byte[] rgba, int pixels)
    {
        for (int i = 0; i < pixels; i += 11)
        {
            int o = i * 4;
            if (rgba[o] > 3 || rgba[o + 1] > 3 || rgba[o + 2] > 3) return false;
        }
        return true;
    }
}
