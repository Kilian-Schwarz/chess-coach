using System.IO.Compression;
using ChessCoach.Setup;

namespace ChessCoach.App.Tests;

public class InstallLayoutTests
{
    private static string Dir(params string[] parts) => Path.Combine([Path.GetTempPath(), .. parts]);

    [Fact]
    public void DefaultDirectoryIsPerUserPrograms() =>
        Assert.Equal(Path.Combine("local", "Programs", "Chess Coach"), InstallLayout.DefaultDirectory("local"));

    [Fact]
    public void RegistryValuesDescribeTheInstallation()
    {
        var dir = Dir("Chess Coach");
        var values = InstallLayout.RegistryValues("1.2.3", dir, 2048 * 1024 + 1, new DateTime(2026, 11, 4));
        Assert.Equal("Chess Coach", values["DisplayName"]);
        Assert.Equal("1.2.3", values["DisplayVersion"]);
        Assert.Equal("Kilian Schwarz", values["Publisher"]);
        Assert.Equal(dir, values["InstallLocation"]);
        Assert.Equal(Path.Combine(dir, "ChessCoach.exe"), values["DisplayIcon"]);
        Assert.Equal($"\"{Path.Combine(dir, "Uninstall.exe")}\" /uninstall", values["UninstallString"]);
        Assert.Equal($"\"{Path.Combine(dir, "Uninstall.exe")}\" /uninstall /S", values["QuietUninstallString"]);
        Assert.Equal("20261104", values["InstallDate"]);
        Assert.Equal(2049, values["EstimatedSize"]);
        Assert.Equal(1, values["NoModify"]);
    }

    [Fact]
    public void EstimatedSizeIsCapped() =>
        Assert.Equal(int.MaxValue, InstallLayout.RegistryValues("1.0.0", Dir("x"), long.MaxValue, DateTime.Now)["EstimatedSize"]);

    [Theory]
    [InlineData("/S", true, false)]
    [InlineData("/silent", true, false)]
    [InlineData("-s", true, false)]
    [InlineData("/uninstall", false, true)]
    [InlineData("/UNINSTALL", false, true)]
    [InlineData("", false, false)]
    public void ParsesFlags(string arg, bool silent, bool uninstall)
    {
        var options = InstallLayout.Parse(arg.Length == 0 ? [] : [arg]);
        Assert.Equal(silent, options.Silent);
        Assert.Equal(uninstall, options.Uninstall);
    }

    [Fact]
    public void ParsesFromDirectoryAndExtras()
    {
        var options = InstallLayout.Parse(["/uninstall", "/from", "some dir", "/desktop", "/nostart", "/deletedata"]);
        Assert.Equal("some dir", options.From);
        Assert.True(options.Desktop && options.NoStart && options.DeleteData);
    }

    [Fact]
    public void ForwardedArgumentsRoundTrip()
    {
        var original = InstallLayout.Parse(["/uninstall", "/S", "/deletedata"]);
        var again = InstallLayout.Parse(InstallLayout.Forward(original, "target dir"));
        Assert.True(again.Uninstall && again.Silent && again.DeleteData);
        Assert.Equal("target dir", again.From);
    }

    [Fact]
    public void SameDirectoryIgnoresCaseAndTrailingSeparator()
    {
        var dir = Dir("Chess Coach");
        Assert.True(InstallLayout.SameDirectory(dir, dir.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.False(InstallLayout.SameDirectory(dir, Dir("Other")));
        Assert.False(InstallLayout.SameDirectory(dir, null));
        Assert.True(InstallLayout.IsInstalledCopy(dir, dir));
    }

    [Fact]
    public void IsInsideRejectsSiblingsAndTraversal()
    {
        var dir = Dir("Chess Coach");
        Assert.True(InstallLayout.IsInside(dir, Path.Combine(dir, "stockfish", "stockfish.exe")));
        Assert.False(InstallLayout.IsInside(dir, dir));
        Assert.False(InstallLayout.IsInside(dir, dir + " Other" + Path.DirectorySeparatorChar + "a.exe"));
        Assert.False(InstallLayout.IsInside(dir, Path.GetFullPath(Path.Combine(dir, "..", "evil.exe"))));
    }

    [Fact]
    public void PayloadIsFoundBehindTheStub()
    {
        var file = Path.GetTempFileName();
        try
        {
            var stub = new byte[1000];
            using var zipBytes = new MemoryStream();
            using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, true))
                using (var writer = new StreamWriter(zip.CreateEntry("ChessCoach.exe").Open())) writer.Write("payload");
            File.WriteAllBytes(file, [.. stub, .. zipBytes.ToArray(), .. Payload.Footer(zipBytes.Length)]);
            var payload = Payload.Find(file);
            Assert.NotNull(payload);
            Assert.Equal(1000, payload.StubLength);
            Assert.Equal(zipBytes.Length, payload.Length);
            using var archive = new ZipArchive(payload.Open());
            using var reader = new StreamReader(archive.GetEntry("ChessCoach.exe")!.Open());
            Assert.Equal("payload", reader.ReadToEnd());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void FileWithoutFooterHasNoPayload()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, new byte[100]);
            Assert.Null(Payload.Find(file));
        }
        finally { File.Delete(file); }
    }
}
