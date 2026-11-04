using System.Text.Json;
using System.Text.Json.Serialization;

using ChessCoach.Core;

namespace ChessCoach.App.Model;

internal sealed class AppSettings
{
    public string Language { get; set; } = "system";
    public string Side { get; set; } = "auto";
    public int ThinkMs { get; set; } = 1000;
    public bool AlwaysMyTurn { get; set; }
    public bool DuoTraining { get; set; }
    public bool ExplainSuggestions { get; set; } = true;
    public bool ReviewMoves { get; set; } = true;
    public bool ShowThreats { get; set; } = true;
    public bool OpenRecap { get; set; } = true;
    public bool DragHints { get; set; } = true;
    public bool ExpertUnlocked { get; set; }
    public bool ShowBestImmediately { get; set; }
    public string LlmUrl { get; set; } = "";
    public string LlmBase { get; set; } = "";
    public string LlmModel { get; set; } = "";
    public string StockfishPath { get; set; } = "";
    public bool AutoUpdate { get; set; } = true;
    public bool FairPlayNoticeShown { get; set; }
    public bool FirstRunShown { get; set; }
    public bool PanelCollapsed { get; set; }
    public bool ResumeActive { get; set; }
    public string ActiveProfileId { get; set; } = "";
    public string PartnerProfileId { get; set; } = "";
    public Dictionary<string, double[]> PanelPositions { get; set; } = new();

    [JsonIgnore] public static string Path => AppPaths.SettingsFile;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path), Options) ?? new AppSettings();
        }
        catch (Exception e) { Log.Write("Settings unreadable: " + e.Message); }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
            File.Move(tmp, Path, true);
        }
        catch (Exception e) { Log.Write("Settings not saved: " + e.Message); }
    }
}
