using System.Globalization;
using System.Resources;
using ChessCoach.Core;

namespace ChessCoach.App.Ui;

internal static class Loc
{
    private static readonly ResourceManager Rm = new("ChessCoach.App.Strings", typeof(Loc).Assembly);

    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"), ("it", "Italiano"),
        ("pt-BR", "Português (Brasil)"), ("nl", "Nederlands"), ("pl", "Polski"), ("tr", "Türkçe"),
        ("ru", "Русский"), ("uk", "Українська"), ("zh-Hans", "简体中文"), ("ja", "日本語"), ("ko", "한국어"),
    };

    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    public static void Apply(string setting)
    {
        Culture = new CultureInfo(Resolve(setting));
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CoreLocale.Culture = Culture;
    }

    private static string Resolve(string setting)
    {
        if (Languages.Any(l => l.Code == setting)) return setting;
        var system = CultureInfo.InstalledUICulture;
        var exact = Languages.FirstOrDefault(l => l.Code.Equals(system.Name, StringComparison.OrdinalIgnoreCase));
        if (exact.Code != null) return exact.Code;
        var match = Languages.FirstOrDefault(l => l.Code.Split('-')[0] == system.TwoLetterISOLanguageName);
        return match.Code ?? "en";
    }

    public static string T(string key) => Rm.GetString(key, Culture) ?? key;

    public static string T(string key, params object[] args) => string.Format(Culture, T(key), args);

    public static string Color(PieceColor c) => T(c == PieceColor.White ? "color.white" : "color.black");

    public static string Rating(MoveRating r) => T("rating." + r.ToString().ToLowerInvariant());
}
