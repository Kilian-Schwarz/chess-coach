using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ChessCoach.App.Model;
using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed class UpdateService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly AppSettings _settings;
    private readonly Action<string> _notify;
    private readonly Action _quit;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly HttpClient _http = new();
    private readonly SemaphoreSlim _busy = new(1, 1);
    private Timer? _timer;
    private string? _staged;
    private string? _stagedVersion;
    private string? _offered;

    public UpdateService(AppSettings settings, Action<string> notify, Action quit)
    {
        _settings = settings;
        _notify = notify;
        _quit = quit;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ChessCoach");
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public void Start() => _timer = new Timer(_ => _ = CheckAsync(false), null, TimeSpan.FromSeconds(20), Interval);

    public Task CheckNowAsync() => CheckAsync(true);

    public void ApplyPending()
    {
        if (_staged == null) return;
        try { Updater.StartHelper(_staged, false); }
        catch (Exception e) { Log.Write("Update helper failed: " + e.Message); }
    }

    private async Task CheckAsync(bool manual)
    {
        if (!await _busy.WaitAsync(0)) return;
        try
        {
            var manifest = await Updater.FetchAsync(_http);
            if (manifest == null)
            {
                if (manual) _notify(Loc.T("update.current"));
                return;
            }
            if (!Updater.IsOfficialBuild || !Updater.CanWriteAppDir()) { await OfferPageAsync(manifest.Version, manual); return; }
            bool install = _settings.AutoUpdate || await _ui.InvokeAsync(() => Ask("update.prompt", manifest.Version));
            if (!install) return;
            if (_stagedVersion != manifest.Version)
            {
                _staged = await Updater.StageAsync(_http, manifest);
                _stagedVersion = manifest.Version;
            }
            if (_settings.AutoUpdate) _notify(Loc.T("update.ready", manifest.Version));
            else { Updater.StartHelper(_staged!, true); _staged = null; _stagedVersion = null; _quit(); }
        }
        catch (Exception e)
        {
            Log.Write("Update check failed: " + e.Message);
            if (manual) _notify(Loc.T("update.failed", e.Message));
        }
        finally { _busy.Release(); }
    }

    private async Task OfferPageAsync(string version, bool manual)
    {
        if (!manual && _offered == version) return;
        _offered = version;
        if (await _ui.InvokeAsync(() => Ask("update.openPage", version)))
            Process.Start(new ProcessStartInfo(Updater.ReleasesUrl + "/latest") { UseShellExecute = true });
    }

    private static bool Ask(string key, string version) =>
        MessageBox.Show(Loc.T(key, version), "Chess Coach", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }
}
