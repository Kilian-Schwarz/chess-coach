using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace ChessCoach.Setup;

internal static class Installer
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string AppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static string StartMenu => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs));
    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public static string? RegisteredDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InstallLayout.UninstallKey);
        return key?.GetValue("InstallLocation") as string;
    }

    public static string? RegisteredVersion()
    {
        using var key = Registry.CurrentUser.OpenSubKey(InstallLayout.UninstallKey);
        return key?.GetValue("DisplayVersion") as string;
    }

    public static string TargetDirectory() =>
        RegisteredDirectory() is { Length: > 0 } dir ? dir : InstallLayout.DefaultDirectory(LocalAppData);

    public static void Install(Payload payload, string version, bool desktop, Action<int> progress)
    {
        var dir = TargetDirectory();
        StopRunning(dir);
        Directory.CreateDirectory(dir);
        using var zip = new ZipArchive(payload.Open(), ZipArchiveMode.Read);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var total = Math.Max(1, files.Sum(e => e.Length));
        long done = 0;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(dir, InstallLayout.UninstallerName) };
        foreach (var entry in files)
        {
            var target = Path.GetFullPath(Path.Combine(dir, entry.FullName));
            if (!InstallLayout.IsInside(dir, target)) throw new InvalidDataException(entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Retry(() => entry.ExtractToFile(target, true));
            keep.Add(target);
            done += entry.Length;
            progress((int)(done * 90 / total));
        }
        RemoveStale(dir, keep);
        WriteUninstaller(payload, dir);
        progress(95);
        Shortcut.Create(InstallLayout.ShortcutPath(StartMenu), Path.Combine(dir, InstallLayout.ExeName), dir);
        if (desktop) Shortcut.Create(InstallLayout.ShortcutPath(Desktop), Path.Combine(dir, InstallLayout.ExeName), dir);
        Register(version, dir);
        progress(100);
    }

    public static void Uninstall(string dir, bool deleteData)
    {
        StopRunning(dir);
        DeleteFile(InstallLayout.ShortcutPath(StartMenu));
        DeleteFile(InstallLayout.ShortcutPath(Desktop));
        Registry.CurrentUser.DeleteSubKeyTree(InstallLayout.UninstallKey, false);
        Retry(() => { if (Directory.Exists(dir)) Directory.Delete(dir, true); });
        var data = Path.Combine(AppData, "ChessCoach");
        if (deleteData)
        {
            Retry(() => { if (Directory.Exists(data)) Directory.Delete(data, true); });
            var local = Path.Combine(LocalAppData, "ChessCoach");
            Retry(() => { if (Directory.Exists(local)) Directory.Delete(local, true); });
        }
        else
        {
            var staged = Path.Combine(data, "update");
            Retry(() => { if (Directory.Exists(staged)) Directory.Delete(staged, true); });
        }
    }

    public static void Launch(string dir) =>
        Process.Start(new ProcessStartInfo(Path.Combine(dir, InstallLayout.ExeName)) { WorkingDirectory = dir, UseShellExecute = true });

    public static void DeleteSelfLater(string file) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 >nul & del /f /q {InstallLayout.Quote(file)}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });

    private static void Register(string version, string dir)
    {
        var size = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        using var key = Registry.CurrentUser.CreateSubKey(InstallLayout.UninstallKey);
        foreach (var (name, value) in InstallLayout.RegistryValues(version, dir, size, DateTime.Now))
            key.SetValue(name, value, value is int ? RegistryValueKind.DWord : RegistryValueKind.String);
    }

    private static void WriteUninstaller(Payload payload, string dir)
    {
        var target = Path.Combine(dir, InstallLayout.UninstallerName);
        using var source = File.OpenRead(payload.File);
        using var output = File.Create(target);
        var remaining = payload.StubLength;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0) break;
            output.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void RemoveStale(string dir, HashSet<string> keep)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            if (!keep.Contains(Path.GetFullPath(file))) DeleteFile(file);
    }

    private static void StopRunning(string dir)
    {
        foreach (var name in new[] { "ChessCoach", "stockfish" })
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (path == null || !InstallLayout.IsInside(dir, path)) continue;
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { process.Dispose(); }
            }
    }

    private static void DeleteFile(string file)
    {
        try { File.Delete(file); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception e) when (attempt < 15 && e is IOException or UnauthorizedAccessException) { Thread.Sleep(300); }
        }
    }
}
