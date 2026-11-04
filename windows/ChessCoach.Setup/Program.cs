using System.Diagnostics;
using System.Reflection;

namespace ChessCoach.Setup;

internal static class Program
{
    private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = InstallLayout.Parse(args);
        return options.Uninstall ? Uninstall(options) : Install(options);
    }

    private static int Install(SetupOptions options)
    {
        var payload = Environment.ProcessPath is { } self ? Payload.Find(self) : null;
        if (options.Silent)
        {
            if (payload == null) return 1;
            try
            {
                Installer.Install(payload, Version, options.Desktop, _ => { });
                if (!options.NoStart) Installer.Launch(Installer.TargetDirectory());
                return 0;
            }
            catch (Exception) { return 1; }
        }
        var upgrade = Installer.RegisteredVersion();
        var welcome = new DialogPage
        {
            Heading = Loc.T("installHeading", Version),
            Body = upgrade == null ? Loc.T("installBody") : Loc.T("upgradeBody", upgrade, Version),
            Icon = TaskDialog.InformationIcon,
            Buttons = [new DialogButton(100, Loc.T("installButton"))],
            CommonButtons = TaskDialog.CancelButton,
            Option = Loc.T("desktopOption"),
            Clicked = (dialog, id) =>
            {
                if (id != 100) return false;
                dialog.GoTo(Progress(dialog.OptionChecked, payload!));
                return true;
            },
        };
        if (payload == null) welcome = Failure(Loc.T("errorPayload"));
        new TaskDialog(Loc.T("title"), welcome).Show();
        return 0;
    }

    private static DialogPage Progress(bool desktop, Payload payload) =>
        new()
        {
            Heading = Loc.T("progressHeading"),
            Body = Loc.T("progressBody"),
            Icon = TaskDialog.InformationIcon,
            Progress = true,
            Clicked = (_, _) => true,
            Shown = dialog => Task.Run(() =>
            {
                try
                {
                    Installer.Install(payload, Version, desktop, dialog.SetProgress);
                    dialog.GoTo(Done());
                }
                catch (Exception e) { dialog.GoTo(Failure(e.Message)); }
            }),
        };

    private static DialogPage Done() => new()
    {
        Heading = Loc.T("doneHeading"),
        Body = Loc.T("doneBody"),
        Icon = TaskDialog.InformationIcon,
        Buttons = [new DialogButton(100, Loc.T("startButton"))],
        CommonButtons = TaskDialog.CloseButton,
        Clicked = (_, id) =>
        {
            if (id == 100) Installer.Launch(Installer.TargetDirectory());
            return false;
        },
    };

    private static DialogPage Failure(string message) => new()
    {
        Heading = Loc.T("errorHeading"),
        Body = message,
        Icon = TaskDialog.ErrorIcon,
        CommonButtons = TaskDialog.CloseButton,
    };

    private static int Uninstall(SetupOptions options)
    {
        var self = Environment.ProcessPath!;
        var directory = options.From ?? Installer.TargetDirectory();
        if (options.From == null && InstallLayout.SameDirectory(Path.GetDirectoryName(self), directory))
        {
            var copy = Path.Combine(Path.GetTempPath(), $"ChessCoachUninstall-{Guid.NewGuid():N}.exe");
            File.Copy(self, copy);
            var info = new ProcessStartInfo(copy) { UseShellExecute = false };
            foreach (var argument in InstallLayout.Forward(options, directory)) info.ArgumentList.Add(argument);
            Process.Start(info);
            return 0;
        }
        var ok = true;
        void Run(bool deleteData)
        {
            try { Installer.Uninstall(directory, deleteData); }
            catch (Exception) { ok = false; }
        }
        if (options.Silent) Run(options.DeleteData);
        else
        {
            var remove = new DialogPage
            {
                Heading = Loc.T("removeHeading"),
                Body = Loc.T("removeBody"),
                Icon = TaskDialog.InformationIcon,
                Buttons = [new DialogButton(100, Loc.T("removeButton"))],
                CommonButtons = TaskDialog.CancelButton,
                Option = Loc.T("deleteDataOption"),
                Clicked = (dialog, id) =>
                {
                    if (id != 100) return false;
                    var deleteData = dialog.OptionChecked;
                    dialog.GoTo(new DialogPage
                    {
                        Heading = Loc.T("removeProgressHeading"),
                        Icon = TaskDialog.InformationIcon,
                        Progress = true,
                        Clicked = (_, _) => true,
                        Shown = d => Task.Run(() =>
                        {
                            Run(deleteData);
                            d.GoTo(ok
                                ? new DialogPage { Heading = Loc.T("removeDoneHeading"), Icon = TaskDialog.InformationIcon, CommonButtons = TaskDialog.CloseButton }
                                : Failure(Loc.T("removeFailedHeading")));
                        }),
                    });
                    return true;
                },
            };
            new TaskDialog(Loc.T("titleUninstall"), remove).Show();
        }
        if (options.From != null) Installer.DeleteSelfLater(self);
        return ok ? 0 : 1;
    }
}
