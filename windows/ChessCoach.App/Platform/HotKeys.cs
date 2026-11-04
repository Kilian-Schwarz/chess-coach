using System.Windows.Interop;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed class HotKeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotKeys()
    {
        _source = new HwndSource(new HwndSourceParameters("ChessCoachHotKeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _source.AddHook(Hook);
    }

    public bool Register(int modifiers, int vk, Action handler, string name)
    {
        int id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, modifiers | Native.MOD_NOREPEAT, vk))
        {
            Log.Write($"Hotkey {name} could not be registered (taken by another app?)");
            return false;
        }
        _handlers[id] = handler;
        return true;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            handled = true;
            try { h(); } catch (Exception e) { Log.Write("Hotkey error: " + e.Message); }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.Dispose();
    }
}
