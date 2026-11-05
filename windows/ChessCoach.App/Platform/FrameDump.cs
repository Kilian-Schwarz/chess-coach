using System.Text;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed class FrameDump(Func<bool> enabled, string? folder = null)
{
    public const int MaxFiles = 60;

    private readonly object _gate = new();

    public string Folder => folder ?? Path.Combine(Log.Directory, "frames");

    public void Save(string reason, RgbImage? image)
    {
        if (image == null || !enabled()) return;
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string name = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                File.WriteAllBytes(name + ".png", PngWriter.Encode(image.Pixels, image.Width, image.Height));
                File.WriteAllText(name + ".txt", reason + "\n", new UTF8Encoding(false));
                foreach (var old in Directory.GetFiles(Folder).OrderBy(f => f, StringComparer.Ordinal).SkipLast(MaxFiles)) File.Delete(old);
            }
            catch (Exception e) { Log.Write("Frame dump failed: " + e.Message); }
        }
    }
}
