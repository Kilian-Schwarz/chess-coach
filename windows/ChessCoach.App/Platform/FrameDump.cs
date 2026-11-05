using System.Text;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed class FrameDump(Func<bool> enabled, string? folder = null)
{
    public const int MaxFrames = 60;

    private int _sequence;

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
                string name = Path.Combine(Folder, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + (_sequence++ % 1000).ToString("D3"));
                File.WriteAllBytes(name + ".png", PngWriter.Encode(image.Pixels, image.Width, image.Height));
                File.WriteAllText(name + ".txt", reason + "\n", new UTF8Encoding(false));
                var stale = Directory.GetFiles(Folder).GroupBy(Path.GetFileNameWithoutExtension).OrderBy(g => g.Key, StringComparer.Ordinal).SkipLast(MaxFrames);
                foreach (var old in stale.SelectMany(g => g)) File.Delete(old);
            }
            catch (Exception e) { Log.Write("Frame dump failed: " + e.Message); }
        }
    }
}
