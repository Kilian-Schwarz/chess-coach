using System.Diagnostics;
using System.Windows;
using ChessCoach.App.Model;
using ChessCoach.App.Platform;
using ChessCoach.App.Ui;
using ChessCoach.Core;

namespace ChessCoach.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(false, "ChessCoach.SingleInstance");
        if (!mutex.WaitOne(args.Contains("--restarted") ? 5000 : 0)) return;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log.Write("Unhandled error: " + e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("Crash: " + e.ExceptionObject);
        InstallRegistration.Refresh();
        Loc.Apply(AppSettings.Load().Language);
        var model = new AppModel();
        var tray = new TrayController(model);
        UpdateService? updates = null;
        SettingsWindow? settings = null;
        RecordWindow? record = null;
        TrainingWindow? training = null;
        ReportWindow? report = null;
        tray.OpenSettings = () => (settings ??= new SettingsWindow(model)).ShowAndActivate();
        model.OpenRecord = () => (record ??= new RecordWindow(model)).Show();
        model.OpenTraining = () => (training ??= new TrainingWindow(model)).Show();
        model.OpenReport = () => (report ??= new ReportWindow(model)).Show();
        tray.OpenRecord = model.OpenRecord;
        tray.OpenTraining = model.OpenTraining;
        tray.OpenReport = model.OpenReport;
        void Exit(bool applyUpdate)
        {
            if (applyUpdate) updates?.ApplyPending();
            updates?.Dispose();
            tray.Dispose();
            model.Shutdown();
            app.Shutdown();
        }
        updates = new UpdateService(model.Settings, tray.Balloon, () => app.Dispatcher.Invoke(() => Exit(false)));
        tray.CheckUpdates = () => _ = updates.CheckNowAsync();
        tray.Quit = () => Exit(true);
        model.Restart = () =>
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restarted") { UseShellExecute = true });
            Exit(false);
        };
        model.WireTray(tray);
        updates.Start();
        if (!model.Settings.FirstRunShown)
        {
            model.Settings.FirstRunShown = true;
            model.Settings.Save();
            tray.Balloon(Loc.T("firstRun.text"));
        }
        app.Run();
    }
}
