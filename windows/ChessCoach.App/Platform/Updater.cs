using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text.Json;

namespace ChessCoach.App.Platform;

internal sealed record UpdateManifest(string Version, string Notes, string File, string Sha256, string Signature);

internal static partial class Updater
{
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEKtDuOT/afBlIb3eaZ6ZP9bh7+RuAQY1rDfINQoO/ci8ZZ+/aq0Ne50DxBTTHC9X1fmXeXHIAaUVxZwyW9b49zw==";
    public const string ReleasesUrl = "https://github.com/Kilian-Schwarz/chess-coach/releases";

    public static bool IsOfficialBuild =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Any(a => a.Key == "Official" && a.Value == "true");

    public static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static bool IsNewer(string candidate, string current)
    {
        var a = Numbers(candidate);
        var b = Numbers(current);
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^ChessCoach-\d+\.\d+\.\d+-windows-x64\.zip$")]
    private static partial Regex FilePattern();

    private static int[] Numbers(string version) =>
        version.TrimStart('v').Split('-')[0].Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

    public static UpdateManifest? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("windows", out var win)) return null;
            string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
            var manifest = new UpdateManifest(Text(root, "version"), Text(root, "notes"), Text(win, "file"), Text(win, "sha256"), Text(win, "signature"));
            return VersionPattern().IsMatch(manifest.Version) && FilePattern().IsMatch(manifest.File) ? manifest : null;
        }
        catch (JsonException) { return null; }
    }

    public static bool Verify(byte[] data, string sha256Hex, string signatureBase64, string publicKeyBase64)
    {
        try
        {
            if (!Convert.ToHexString(SHA256.HashData(data)).Equals(sha256Hex, StringComparison.OrdinalIgnoreCase)) return false;
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            return key.VerifyData(data, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception e) when (e is FormatException or CryptographicException) { return false; }
    }

    public static bool IsAcceptableUpdate(string stagedVersion, string feedVersion, string current) =>
        VersionPattern().IsMatch(stagedVersion) && stagedVersion == feedVersion && IsNewer(stagedVersion, current);

    public static string ReadVersion(string exe)
    {
        var info = FileVersionInfo.GetVersionInfo(exe);
        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
    }

    public static bool CanWriteAppDir()
    {
        try
        {
            var probe = Path.Combine(Model.AppPaths.AppDir, ".write-test-" + Environment.ProcessId);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return false; }
    }

    public static async Task<UpdateManifest?> FetchAsync(HttpClient http)
    {
        var json = await http.GetStringAsync(ReleasesUrl + "/latest/download/update.json");
        return Parse(json) is { } m && IsNewer(m.Version, CurrentVersion) ? m : null;
    }

    public static async Task<string> StageAsync(HttpClient http, UpdateManifest manifest)
    {
        var data = await http.GetByteArrayAsync($"{ReleasesUrl}/download/v{manifest.Version}/{manifest.File}");
        if (!Verify(data, manifest.Sha256, manifest.Signature, PublicKey)) throw new InvalidDataException("Update signature check failed.");
        var dir = Path.Combine(Model.AppPaths.DataDir, "update", manifest.Version);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        using (var zip = new ZipArchive(new MemoryStream(data))) zip.ExtractToDirectory(dir);
        var exe = Path.Combine(dir, "ChessCoach.exe");
        if (!File.Exists(exe) || !IsAcceptableUpdate(ReadVersion(exe), manifest.Version, CurrentVersion))
        {
            Directory.Delete(dir, true);
            throw new InvalidDataException("Update version check failed.");
        }
        return dir;
    }

    private static string Quote(string path) => "\"" + path.Replace("%", "%%") + "\"";

    public static void StartHelper(string stagedDir, bool restart)
    {
        var target = Model.AppPaths.AppDir.TrimEnd('\\', '/');
        var exe = Path.Combine(target, "ChessCoach.exe");
        var backup = exe + ".bak";
        var image = Path.GetFileName(Environment.ProcessPath) ?? "ChessCoach.exe";
        var script = Path.Combine(Path.GetDirectoryName(stagedDir)!, "apply.cmd");
        var lines = new List<string>
        {
            "@echo off",
            "set /a tries=0",
            ":wait",
            $"tasklist /FI \"PID eq {Environment.ProcessId}\" /FI \"IMAGENAME eq {image}\" /NH 2>nul | find /I \"{image}\" >nul",
            "if errorlevel 1 goto copy",
            "set /a tries+=1",
            "if %tries% geq 120 exit /b 1",
            "ping -n 2 127.0.0.1 >nul",
            "goto wait",
            ":copy",
            $"copy /y {Quote(exe)} {Quote(backup)} >nul",
            $"robocopy {Quote(stagedDir)} {Quote(target)} /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP >nul",
            "if errorlevel 8 goto fail",
            $"del {Quote(backup)}",
            $"rd /s /q {Quote(stagedDir)}",
        };
        if (restart) lines.Add($"start \"\" {Quote(exe)}");
        lines.AddRange(new[] { "exit /b 0", ":fail", $"copy /y {Quote(backup)} {Quote(exe)} >nul", "exit /b 1" });
        File.WriteAllLines(script, lines);
        Process.Start(new ProcessStartInfo(script) { CreateNoWindow = true, UseShellExecute = false });
    }
}
