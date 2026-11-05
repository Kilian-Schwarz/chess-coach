using System.Diagnostics;
using System.Runtime.CompilerServices;
using ChessCoach.Core;
using StbImageSharp;

namespace ChessCoach.Core.Tests;

public static class TestSupport
{
    [ModuleInitializer]
    public static void UseEnglish()
    {
        CoreLocale.Culture = CoreLocale.English;
        Environment.SetEnvironmentVariable("CHESSCOACH_LOG_DIR", Path.Combine(FindRoot(), "build", "tests", "logs"));
    }

    public static readonly string Root = FindRoot();
    public static string TestData => Path.Combine(Root, "Tools", "testdata");

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Tools", "testdata"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Project root (Tools/testdata) not found");
    }

    public static RgbImage Crop(RgbImage img, int x, int y, int w, int h)
    {
        var px = new byte[w * h * 4];
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                var (r, g, b) = img.Rgb(x + xx, y + yy);
                int i = (yy * w + xx) * 4;
                px[i] = (byte)r; px[i + 1] = (byte)g; px[i + 2] = (byte)b; px[i + 3] = 255;
            }
        return new RgbImage(w, h, px);
    }

    public static RgbImage LoadPng(string path)
    {
        using var s = File.OpenRead(path);
        var img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
        return new RgbImage(img.Width, img.Height, img.Data);
    }

    private static bool Complete(string dir) =>
        File.Exists(Path.Combine(dir, "mid_black.png")) && File.Exists(Path.Combine(dir, "lift_dots_only.png")) && File.Exists(Path.Combine(dir, "big_arrows.png"));

    public static string? SyntheticDir()
    {
        var dir = Path.Combine(Root, "build", "testboards");
        if (Complete(dir)) return dir;
        try
        {
            var psi = new ProcessStartInfo("python3", $"\"{Path.Combine(Root, "Tools", "make_board.py")}\" \"{dir}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            p.WaitForExit(60000);
            return Complete(dir) ? dir : null;
        }
        catch { return null; }
    }

    public static string? PrivateFixtures()
    {
        var dir = Path.Combine(Root, "intern", "fixtures");
        return File.Exists(Path.Combine(dir, "chesscom_gameover_popup_white_mated.png")) && File.Exists(Path.Combine(dir, "chesscom_start_white.png")) ? dir : null;
    }

    public static (PieceTemplates Templates, bool WhiteBottom) Learn(string startPath)
    {
        var img = LoadPng(startPath);
        var templates = new PieceTemplates();
        var board = BoardDetector.Detect(img) ?? throw new Exception("no board in " + startPath);
        return (templates, templates.LearnStart(BoardScanner.Analyze(img, board, templates).Features));
    }

    public static string? StockfishPath() => StockfishEngine.FindBinary();

    public static string PlacementOf(char?[][] screen, bool whiteAtBottom) =>
        new Placement(screen, whiteAtBottom).Fen(PieceColor.White).Split(' ')[0];

    public static (string Placement, bool WhiteBottom, PieceTemplates Templates) Recognize(RgbImage start, RgbImage mid)
    {
        var b1 = BoardDetector.Detect(start) ?? throw new Exception("no board in start image");
        var templates = new PieceTemplates();
        var s1 = BoardScanner.Analyze(start, b1, templates);
        Assert.True(s1.LooksLikeStart, "start image does not look like the start position");
        bool whiteBottom = templates.LearnStart(s1.Features);
        var res = BoardScanner.Scan(mid, templates) ?? throw new Exception("no board in image");
        var guess = BoardScanner.GuessWhiteAtBottom(res.Screen);
        return (PlacementOf(res.Screen, guess ?? whiteBottom), whiteBottom, templates);
    }

    public static string FakeEngine(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), "fake-sf-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
