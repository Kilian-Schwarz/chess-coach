using ChessCoach.App.Platform;
using ChessCoach.Core;

namespace ChessCoach.App.Tests;

public class FrameDumpTests
{
    [Fact]
    public void PngHasSignatureAndDimensions()
    {
        var png = PngWriter.Encode(new byte[4 * 3 * 4], 4, 3);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal(4, png[19]);
        Assert.Equal(3, png[23]);
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    [Fact]
    public void BlackDetectionIgnoresRealContent()
    {
        var black = new byte[100 * 4];
        Assert.True(CaptureMath.IsBlack(black, 100));
        black[11 * 4] = 120;
        Assert.False(CaptureMath.IsBlack(black, 100));
    }

    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "chesscoach-frames-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DumpWritesPngAndReasonTextAndKeepsSixtyFiles()
    {
        var folder = TempFolder();
        try
        {
            var dump = new FrameDump(() => true, folder);
            var image = new RgbImage(8, 8, new byte[8 * 8 * 4]);
            for (int i = 0; i < 35; i++) { dump.Save("Region miss: test " + i, image); Thread.Sleep(2); }
            var files = Directory.GetFiles(folder);
            Assert.Equal(FrameDump.MaxFiles, files.Length);
            var last = files.Where(f => f.EndsWith(".txt")).OrderBy(f => f, StringComparer.Ordinal).Last();
            Assert.Equal("Region miss: test 34\n", File.ReadAllText(last));
            Assert.True(File.Exists(Path.ChangeExtension(last, ".png")));
            Assert.Matches(@"^\d{8}-\d{6}-\d{3}$", Path.GetFileNameWithoutExtension(last));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void DisabledDumpWritesNothing()
    {
        var folder = TempFolder();
        new FrameDump(() => false, folder).Save("x", new RgbImage(8, 8, new byte[8 * 8 * 4]));
        Assert.False(Directory.Exists(folder));
    }
}
