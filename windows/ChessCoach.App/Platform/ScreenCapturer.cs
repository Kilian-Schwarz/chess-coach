using System.Drawing;
using System.Runtime.InteropServices;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed record MonitorInfo(IntPtr Handle, int X, int Y, int Width, int Height, int WorkX, int WorkY, int WorkWidth, int WorkHeight,
    string Device, bool Primary, double Scale)
{
    public string Key => Device;
}

internal sealed class ScreenCapturer : IScreenCapture, IDisposable
{
    private IntPtr _screenDc, _memDc, _dib, _oldBitmap, _bits;
    private int _capW, _capH;
    private readonly RgbImage?[] _images = new RgbImage?[3];
    private int _nextSlot;
    private IntPtr _target;
    private Rectangle _targetBounds;
    private int _pointX, _pointY;
    private readonly object _sync = new();
    private bool _disposed;
    private IntPtr _failedTarget;
    private DateTime _failedAt;
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly Native.WindowEnumProc _enumProc;

    public ScreenCapturer() { _enumProc = VisitWindow; }

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
        if (list.Count == 0)
        {
            var (w, h) = Native.PrimarySize();
            if (w > 0 && h > 0) list.Add(new MonitorInfo(IntPtr.Zero, 0, 0, w, h, 0, 0, w, h, "primary", true, 1));
        }
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
        int slot = _nextSlot;
        _nextSlot = (_nextSlot + 1) % _images.Length;
        if (_images[slot] is { } existing && existing.Width == w && existing.Height == h) return existing;
        return _images[slot] = new RgbImage(w, h, new byte[w * h * 4]);
    }

    private readonly Dictionary<string, string?> _notes = new();

    private RgbImage? Problem(string api, string? reason)
    {
        _notes.TryGetValue(api, out var previous);
        if (previous == reason) return null;
        _notes[api] = reason;
        Log.Write(reason == null ? $"{api} capture recovered" : $"{api} capture problem: {reason}");
        return null;
    }

    private void ResetScreenDc()
    {
        ReleaseBitmap();
        if (_memDc != IntPtr.Zero) { Native.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
        if (_screenDc != IntPtr.Zero) { Native.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
    }

    public RgbImage? Capture(int x, int y, int w, int h)
    {
        lock (_sync)
        {
            if (_disposed || w < 16 || h < 16) return null;
            string reason = "";
            foreach (uint rop in new[] { Native.SRCCOPY | Native.CAPTUREBLT, Native.SRCCOPY })
            {
                if (!Ensure(w, h)) { reason = $"cannot create a {w}x{h} bitmap (error {Marshal.GetLastWin32Error()})"; break; }
                if (!Native.BitBlt(_memDc, 0, 0, w, h, _screenDc, x, y, rop))
                {
                    reason = $"BitBlt failed (error {Marshal.GetLastWin32Error()}, rop {rop:X}, {w}x{h} at {x},{y})";
                    continue;
                }
                var img = ToImage(0, 0, w, h);
                if (CaptureMath.IsBlack(img.Pixels, w * h)) { reason = $"BitBlt returned a black image (rop {rop:X}, {w}x{h} at {x},{y})"; continue; }
                Problem("BitBlt", null);
                return img;
            }
            ResetScreenDc();
            return Problem("BitBlt", reason);
        }
    }

    public RgbImage? CaptureWindow(ref Rectangle region)
    {
        lock (_sync) return _disposed ? null : CaptureWindowLocked(ref region);
    }

    private RgbImage? CaptureWindowLocked(ref Rectangle region)
    {
        if (!ResolveTarget(region)) return null;
        if (_target == _failedTarget && DateTime.UtcNow - _failedAt < RetryAfterFailure) return null;
        Native.GetWindowRect(_target, out var wr);
        var windowRect = Rectangle.FromLTRB(wr.Left, wr.Top, wr.Right, wr.Bottom);
        var crop = CaptureMath.WindowCrop(region, windowRect, out var clipped);
        if (crop.Width < 16 || crop.Height < 16 || !Ensure(windowRect.Width, windowRect.Height)) return null;
        if (!Native.PrintWindow(_target, _memDc, Native.PW_RENDERFULLCONTENT)) return Fail($"PrintWindow failed (error {Marshal.GetLastWin32Error()})", windowRect);
        var img = ToImage(crop.X, crop.Y, crop.Width, crop.Height);
        if (CaptureMath.IsUniform(img.Pixels, crop.Width * crop.Height)) return Fail("PrintWindow returned a blank image", windowRect);
        _failedTarget = IntPtr.Zero;
        Problem("PrintWindow", null);
        region = clipped;
        return img;
    }

    private RgbImage? Fail(string reason, Rectangle windowRect)
    {
        Problem("PrintWindow", $"{reason}, window class {Native.ClassName(_target)}, {windowRect.Width}x{windowRect.Height}, using screen capture for 30 s");
        _failedTarget = _target;
        _failedAt = DateTime.UtcNow;
        return null;
    }

    private bool ResolveTarget(Rectangle region)
    {
        int cx = region.X + region.Width / 2, cy = region.Y + region.Height / 2;
        if (_target != IntPtr.Zero && Native.IsWindow(_target) && Native.IsWindowVisible(_target) && !Native.IsIconic(_target) &&
            FrameBounds(_target) is { } fb && fb == _targetBounds && fb.Contains(cx, cy)) return true;
        _target = IntPtr.Zero;
        _pointX = cx; _pointY = cy;
        Native.EnumWindows(_enumProc, IntPtr.Zero);
        return _target != IntPtr.Zero;
    }

    private bool VisitWindow(IntPtr hwnd, IntPtr data)
    {
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownPid) return true;
        if ((Native.GetExStyle(hwnd) & (Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW)) != 0) return true;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
        if (FrameBounds(hwnd) is not { } fb || !fb.Contains(_pointX, _pointY)) return true;
        _target = hwnd; _targetBounds = fb;
        return false;
    }

    private static Rectangle? FrameBounds(IntPtr hwnd)
    {
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out Native.RECT r, Marshal.SizeOf<Native.RECT>()) != 0) return null;
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    private unsafe RgbImage ToImage(int sx, int sy, int w, int h)
    {
        var img = ImageFor(w, h);
        byte* src = (byte*)_bits;
        int stride = _capW * 4;
        fixed (byte* dstBase = img.Pixels)
        {
            for (int row = 0; row < h; row++)
            {
                byte* s = src + (long)(sy + row) * stride + sx * 4;
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
        lock (_sync)
        {
            _disposed = true;
            ResetScreenDc();
        }
    }
}
