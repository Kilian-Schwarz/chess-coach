namespace ChessCoach.App.Model;

internal static class AppPaths
{
    public static string DataDir
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChessCoach");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string TokenFile => Path.Combine(DataDir, "token.dat");
    public static string TemplatesFile => Path.Combine(DataDir, "pieces.json");
    public static string AppDir => AppContext.BaseDirectory;
}
