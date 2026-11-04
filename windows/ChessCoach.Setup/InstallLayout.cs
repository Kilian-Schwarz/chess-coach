namespace ChessCoach.Setup;

internal sealed record SetupOptions(bool Silent, bool Uninstall, bool Desktop, bool NoStart, bool DeleteData, string? From);

internal static class InstallLayout
{
    public const string ProductName = "Chess Coach";
    public const string Publisher = "Kilian Schwarz";
    public const string ExeName = "ChessCoach.exe";
    public const string UninstallerName = "Uninstall.exe";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ChessCoach";
    public const string AboutUrl = "https://github.com/Kilian-Schwarz/chess-coach";

    public static string DefaultDirectory(string localAppData) => Path.Combine(localAppData, "Programs", ProductName);

    public static string ShortcutPath(string folder) => Path.Combine(folder, ProductName + ".lnk");

    public static string Quote(string path) => "\"" + path + "\"";

    public static long SizeKb(long bytes) => bytes / 1024 + (bytes % 1024 > 0 ? 1 : 0);

    public static bool SameDirectory(string? a, string? b) =>
        a != null && b != null && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalledCopy(string appDir, string? installLocation) => SameDirectory(appDir, installLocation);

    public static bool IsInside(string directory, string path)
    {
        var root = Normalize(directory) + Path.DirectorySeparatorChar;
        return Normalize(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, object> RegistryValues(string version, string directory, long sizeBytes, DateTime installed)
    {
        var exe = Path.Combine(directory, ExeName);
        var uninstaller = Quote(Path.Combine(directory, UninstallerName));
        return new Dictionary<string, object>
        {
            ["DisplayName"] = ProductName,
            ["DisplayVersion"] = version,
            ["Publisher"] = Publisher,
            ["DisplayIcon"] = exe,
            ["InstallLocation"] = directory,
            ["UninstallString"] = uninstaller + " /uninstall",
            ["QuietUninstallString"] = uninstaller + " /uninstall /S",
            ["URLInfoAbout"] = AboutUrl,
            ["InstallDate"] = installed.ToString("yyyyMMdd"),
            ["EstimatedSize"] = (int)Math.Min(int.MaxValue, SizeKb(sizeBytes)),
            ["NoModify"] = 1,
            ["NoRepair"] = 1,
        };
    }

    public static SetupOptions Parse(string[] args)
    {
        bool Has(string name) => args.Any(a => a.TrimStart('/', '-').Equals(name, StringComparison.OrdinalIgnoreCase));
        var from = Array.FindIndex(args, a => a.TrimStart('/', '-').Equals("from", StringComparison.OrdinalIgnoreCase));
        return new SetupOptions(Has("S") || Has("silent"), Has("uninstall"), Has("desktop"), Has("nostart"), Has("deletedata"),
            from >= 0 && from + 1 < args.Length ? args[from + 1] : null);
    }

    public static string[] Forward(SetupOptions o, string from)
    {
        var list = new List<string> { "/uninstall", "/from", from };
        if (o.Silent) list.Add("/S");
        if (o.DeleteData) list.Add("/deletedata");
        return list.ToArray();
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');
}
