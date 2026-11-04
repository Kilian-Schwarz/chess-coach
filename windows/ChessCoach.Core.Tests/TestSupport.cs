using System.Diagnostics;
using System.Runtime.CompilerServices;
using ChessCoach.Core;
using StbImageSharp;

namespace ChessCoach.Core.Tests;

public static class TestSupport
{
    [ModuleInitializer]
    public static void UseEnglish() => CoreLocale.Culture = CoreLocale.English;

    public static readonly string Root = FindRoot();
    public static string TestData => Path.Combine(Root, "Tools", "testdata");

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Tools", "testdata"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Project root (Tools/testdata) not found");
    }

    public static RgbImage LoadPng(string path)
    {
        using var s = File.OpenRead(path);
        var img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
        return new RgbImage(img.Width, img.Height, img.Data);
    }

    public static string? SyntheticDir()
    {
        var dir = Path.Combine(Root, "build", "testboards");
        if (File.Exists(Path.Combine(dir, "mid_black.png"))) return dir;
        try
        {
            var psi = new ProcessStartInfo("python3", $"\"{Path.Combine(Root, "Tools", "make_board.py")}\" \"{dir}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi)!;
            p.WaitForExit(60000);
            return File.Exists(Path.Combine(dir, "mid_black.png")) ? dir : null;
        }
        catch { return null; }
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
