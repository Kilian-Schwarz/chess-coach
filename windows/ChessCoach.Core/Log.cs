using System.Globalization;
using System.Text;

namespace ChessCoach.Core;

public static class Log
{
    private static readonly object Gate = new();
    private static string? _directory;

    public static string Directory
    {
        get => _directory ??= DefaultDirectory(Environment.GetEnvironmentVariable("CHESSCOACH_LOG_DIR"));
        set => _directory = value;
    }

    public static string DefaultDirectory(string? overrideDirectory) => overrideDirectory is { Length: > 0 }
        ? overrideDirectory
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChessCoach", "Logs");

    public static string FilePath => System.IO.Path.Combine(Directory, "chesscoach.log");

    public static void Write(string message)
    {
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {message}\n";
        lock (Gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var path = FilePath;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = System.IO.Path.Combine(Directory, "chesscoach.1.log");
                    File.Delete(old);
                    File.Move(path, old);
                }
                File.AppendAllText(path, line, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
