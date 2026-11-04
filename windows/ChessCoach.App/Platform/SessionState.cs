using Microsoft.Win32;

namespace ChessCoach.App.Platform;

internal sealed class SessionState : IDisposable
{
    private volatile bool _locked, _suspended;
    private TaskCompletionSource _resume = NewTcs();
    public event Action? Changed;

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Start()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPower;
    }

    public void Stop()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPower;
        _locked = false; _suspended = false;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect: _locked = true; break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect: _locked = false; break;
            default: return;
        }
        Notify();
    }

    private void OnPower(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) _suspended = true;
        else if (e.Mode == PowerModes.Resume) _suspended = false;
        else return;
        Notify();
    }

    private void Notify()
    {
        if (!IsHardPaused) { var t = _resume; _resume = NewTcs(); t.TrySetResult(); }
        Changed?.Invoke();
    }

    public bool IsHardPaused => _locked || _suspended;

    public Task WaitForResumeAsync(CancellationToken ct) => IsHardPaused ? _resume.Task.WaitAsync(ct) : Task.CompletedTask;

    public string Reason => Ui.Loc.T(_locked ? "reason.locked" : _suspended ? "reason.sleep" : "reason.screensaver");

    public bool IsPaused()
    {
        if (IsHardPaused) return true;
        int running = 0;
        if (Native.SystemParametersInfo(Native.SPI_GETSCREENSAVERRUNNING, 0, ref running, 0) && running != 0) return true;
        var desk = Native.OpenInputDesktop(0, false, Native.DESKTOP_SWITCHDESKTOP);
        if (desk == IntPtr.Zero) return true;
        Native.CloseDesktop(desk);
        return false;
    }

    public void Dispose() => Stop();
}
