using System.Runtime.InteropServices;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed record MonitorInfo(IntPtr Handle, int X, int Y, int Width, int Height, int WorkX, int WorkY, int WorkWidth, int WorkHeight,
    string Device, bool Primary, double Scale)
{
    public string Key => Device;
}

internal sealed class ScreenCapturer : IDisposable
{
    private IntPtr _screenDc, _memDc, _dib, _oldBitmap, _bits;
    private int _capW, _capH;
    private readonly RgbImage?[] _images = new RgbImage?[3];
    private int _nextSlot;

    public static List<MonitorInfo> Monitors()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) =>
        {
            var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(h, ref mi))
            {
                double scale = 1;
                try { if (Native.GetDpiForMonitor(h, Native.MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0) scale = dx / 96.0; } catch { }
                list.Add(new MonitorInfo(h, mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left,
                    mi.rcMonitor.Bottom - mi.rcMonitor.Top, mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left,
                    mi.rcWork.Bottom - mi.rcWork.Top, mi.szDevice, (mi.dwFlags & 1) != 0, scale));
            }
            return true;
        }, IntPtr.Zero);
        list.Sort((a, b) => b.Primary.CompareTo(a.Primary));
        return list;
    }

    private bool Ensure(int w, int h)
    {
        if (_screenDc == IntPtr.Zero) _screenDc = Native.GetDC(IntPtr.Zero);
        if (_screenDc == IntPtr.Zero) return false;
        if (_memDc == IntPtr.Zero) _memDc = Native.CreateCompatibleDC(_screenDc);
        if (_dib != IntPtr.Zero && w <= _capW && h <= _capH) return true;
        ReleaseBitmap();
        _capW = Math.Max(w, 64); _capH = Math.Max(h, 64);
        var bmi = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = _capW, biHeight = -_capH,
            biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        _dib = Native.CreateDIBSection(_screenDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
        if (_dib == IntPtr.Zero) return false;
        _oldBitmap = Native.SelectObject(_memDc, _dib);
        return true;
    }

    private void ReleaseBitmap()
    {
        if (_memDc != IntPtr.Zero && _oldBitmap != IntPtr.Zero) Native.SelectObject(_memDc, _oldBitmap);
        if (_dib != IntPtr.Zero) Native.DeleteObject(_dib);
        _dib = IntPtr.Zero; _oldBitmap = IntPtr.Zero; _bits = IntPtr.Zero; _capW = _capH = 0;
    }

    private RgbImage ImageFor(int w, int h)
    {
        foreach (var img in _images) if (img != null && img.Width == w && img.Height == h) return img;
        var created = new RgbImage(w, h, new byte[w * h * 4]);
        _images[_nextSlot] = created;
        _nextSlot = (_nextSlot + 1) % _images.Length;
        return created;
    }

    public unsafe RgbImage? Capture(int x, int y, int w, int h)
    {
        if (w < 16 || h < 16 || !Ensure(w, h)) return null;
        if (!Native.BitBlt(_memDc, 0, 0, w, h, _screenDc, x, y, Native.SRCCOPY | Native.CAPTUREBLT)) return null;
        var img = ImageFor(w, h);
        byte* src = (byte*)_bits;
        int stride = _capW * 4;
        fixed (byte* dstBase = img.Pixels)
        {
            for (int row = 0; row < h; row++)
            {
                byte* s = src + (long)row * stride;
                byte* d = dstBase + (long)row * w * 4;
                for (int col = 0; col < w; col++, s += 4, d += 4)
                {
                    d[0] = s[2]; d[1] = s[1]; d[2] = s[0]; d[3] = 255;
                }
            }
        }
        return img;
    }

    public void Dispose()
    {
        ReleaseBitmap();
        if (_memDc != IntPtr.Zero) { Native.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
        if (_screenDc != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
    }
}
