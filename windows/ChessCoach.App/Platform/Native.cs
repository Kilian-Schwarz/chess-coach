using System.Runtime.InteropServices;

namespace ChessCoach.App.Platform;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
        WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const uint PW_RENDERFULLCONTENT = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOOWNERZORDER = 0x200;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public const uint MDT_EFFECTIVE_DPI = 0;
    public const uint SPI_GETSCREENSAVERRUNNING = 0x72;
    public const uint DESKTOP_SWITCHDESKTOP = 0x100;

    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    public delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool EnumWindows(WindowEnumProc proc, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hwnd, int id, int modifiers, int vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint winIni);
    [DllImport("user32.dll")] public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] public static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int max);
    private const int SM_SWAPBUTTON = 23, SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    public static (int Width, int Height) PrimarySize() => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    public static string ClassName(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(128);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "?";
    }
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr monitor, uint type, out uint dpiX, out uint dpiY);


    public static bool PrimaryButtonDown() => (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? 0x02 : 0x01) & 0x8000) != 0;

    public static int GetExStyle(IntPtr hwnd) =>
        IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);

    public static void SetExStyle(IntPtr hwnd, int style)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_EXSTYLE, new IntPtr(style));
        else SetWindowLong32(hwnd, GWL_EXSTYLE, style);
    }
}
