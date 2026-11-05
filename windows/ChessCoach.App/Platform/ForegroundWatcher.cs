namespace ChessCoach.App.Platform;

internal sealed class ForegroundWatcher : IDisposable
{
    private const uint EventSystemForeground = 3, OutOfContext = 0;

    private readonly Native.WinEventProc _proc;
    private IntPtr _hook;

    public ForegroundWatcher(Action changed)
    {
        _proc = (_, _, _, _, _, _, _) => changed();
        _hook = Native.SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _proc, 0, 0, OutOfContext);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        Native.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }
}
