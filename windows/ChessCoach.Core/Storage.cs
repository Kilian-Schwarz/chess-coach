using System.Text;
using System.Text.Json;

namespace ChessCoach.Core;

public sealed class Storage
{
    public string Root { get; }

    public static string DefaultRoot
    {
        get
        {
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "ChessCoach");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChessCoach");
        }
    }

    public Storage(string? root = null) => Root = root ?? DefaultRoot;

    private string Dir(string name)
    {
        var d = Path.Combine(Root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Write<T>(T value, string path)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ChessCoachJson.Serialize(value), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    private static T? Read<T>(string path) => ChessCoachJson.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));

    public void Save(PlayerProfile profile) => Write(profile, Path.Combine(Dir("profiles"), profile.Id + ".json"));

    public PlayerProfile LoadProfile(string id) => ReadProfile(Path.Combine(Root, "profiles", id + ".json"))
        ?? throw new InvalidDataException("Unreadable profile: " + id);

    private static PlayerProfile? ReadProfile(string path)
    {
        try
        {
            var p = Read<PlayerProfile>(path);
            if (p != null && !string.IsNullOrEmpty(p.Id) && p.Name != null) return p.Normalize();
        }
        catch (Exception e) when (e is JsonException or IOException or InvalidOperationException) { }
        Quarantine(path);
        return null;
    }

    private static void Quarantine(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt", overwrite: true);
            Log.Write("Unreadable profile moved to " + path + ".corrupt");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public List<PlayerProfile> LoadProfiles()
    {
        var folder = Path.Combine(Root, "profiles");
        if (!Directory.Exists(folder)) return new();
        return Directory.EnumerateFiles(folder, "*.json").Select(ReadProfile).Where(p => p != null).Select(p => p!)
            .OrderBy(p => p.CreatedAt).ToList();
    }

    public void Save(PuzzleSet puzzles, string profileId) => Write(puzzles, Path.Combine(Dir("puzzles"), profileId + ".json"));

    public PuzzleSet LoadPuzzles(string profileId)
    {
        try { return Read<PuzzleSet>(Path.Combine(Root, "puzzles", profileId + ".json")) ?? new PuzzleSet(); }
        catch (Exception e) when (e is JsonException or IOException) { return new PuzzleSet(); }
    }

    private static string DayString(DateTime d) => (d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d).ToString("yyyy-MM-dd");

    public string Save(GameReport game, string? pgn = null)
    {
        var baseName = $"{DayString(game.Date)}-{game.Id}";
        var folder = Dir("games");
        var path = Path.Combine(folder, baseName + ".json");
        Write(game, path);
        if (pgn != null) File.WriteAllText(Path.Combine(folder, baseName + ".pgn"), pgn, new UTF8Encoding(false));
        return path;
    }

    public List<GameReport> LoadGames(int limit = 50)
    {
        var folder = Path.Combine(Root, "games");
        if (!Directory.Exists(folder)) return new();
        var list = new List<GameReport>();
        foreach (var f in Directory.EnumerateFiles(folder, "*.json"))
        {
            try { if (Read<GameReport>(f) is { } g) list.Add(g); }
            catch (Exception e) when (e is JsonException or IOException) { }
        }
        return list.OrderByDescending(g => g.Date).Take(limit).ToList();
    }
}
