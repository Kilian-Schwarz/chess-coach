namespace ChessCoach.Core;

public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }

    public byte[] Pixels { get; }

    public RgbImage(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Empty image");
        if (rgba.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small");
        Width = width; Height = height; Pixels = rgba;
    }

    public (int R, int G, int B) Rgb(int x, int y)
    {
        int xi = Math.Min(Math.Max(x, 0), Width - 1), yi = Math.Min(Math.Max(y, 0), Height - 1);
        int i = (yi * Width + xi) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
    }

    public static int ColorDistance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
