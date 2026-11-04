using Microsoft.Win32;
using ChessCoach.Setup;

namespace ChessCoach.App.Platform;

internal static class InstallRegistration
{
    public static void Refresh()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InstallLayout.UninstallKey, true);
            if (key == null || !InstallLayout.IsInstalledCopy(Model.AppPaths.AppDir, key.GetValue("InstallLocation") as string)) return;
            var version = Updater.CurrentVersion;
            if (key.GetValue("DisplayVersion") as string == version) return;
            var bytes = Directory.EnumerateFiles(Model.AppPaths.AppDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            key.SetValue("DisplayVersion", version);
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, InstallLayout.SizeKb(bytes)), RegistryValueKind.DWord);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
    }
}
