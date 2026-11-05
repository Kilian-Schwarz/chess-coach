namespace ChessCoach.Core;

public sealed class BoardFingerprint
{
    public const int PerSquare = 5;
    public byte[] Samples { get; } = new byte[64 * PerSquare];
    private readonly bool[] _mask = new bool[64];

    public void Compute(RgbImage img, BoardRect board, bool[]? mask = null)
    {
        if (mask is { Length: 64 }) Array.Copy(mask, _mask, 64); else Array.Clear(_mask);
        double s = board.Square, o = s * 0.18;
        int k = 0;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                double cx = board.X + (c + 0.5) * s, cy = board.Y + (r + 0.5) * s;
                Samples[k++] = Lum(img, cx, cy);
                Samples[k++] = Lum(img, cx - o, cy);
                Samples[k++] = Lum(img, cx + o, cy);
                Samples[k++] = Lum(img, cx, cy - o);
                Samples[k++] = Lum(img, cx, cy + o);
            }
    }

    public static BoardFingerprint Of(RgbImage img, BoardRect board)
    {
        var f = new BoardFingerprint(); f.Compute(img, board); return f;
    }

    private static byte Lum(RgbImage img, double x, double y)
    {
        var c = img.Rgb((int)x, (int)y);
        return (byte)((c.R * 299 + c.G * 587 + c.B * 114) / 1000);
    }

    public bool Differs(BoardFingerprint other, int tolerance = 12)
    {
        bool sameMask = _mask.AsSpan().SequenceEqual(other._mask);
        for (int i = 0; i < Samples.Length; i++)
        {
            if (!sameMask && (_mask[i / PerSquare] || other._mask[i / PerSquare])) continue;
            if (Math.Abs(Samples[i] - other.Samples[i]) > tolerance) return true;
        }
        return false;
    }

    public void CopyFrom(BoardFingerprint other)
    {
        Array.Copy(other.Samples, Samples, Samples.Length);
        Array.Copy(other._mask, _mask, 64);
    }
}
