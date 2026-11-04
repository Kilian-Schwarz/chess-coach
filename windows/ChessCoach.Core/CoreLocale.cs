using System.Globalization;
using System.Resources;

namespace ChessCoach.Core;

public static class CoreLocale
{
    private static readonly ResourceManager Resources = new("ChessCoach.Core.CoreStrings", typeof(CoreLocale).Assembly);

    public static CultureInfo English { get; } = new("en");
    public static CultureInfo Culture { get; set; } = CultureInfo.CurrentUICulture;

    public static string Get(string key, CultureInfo? culture = null) => Resources.GetString(key, culture ?? Culture) ?? key;

    public static string Format(string key, CultureInfo? culture, params object[] args) =>
        string.Format(culture ?? Culture, Get(key, culture), args);

    public static string AnswerLanguage(CultureInfo? culture = null) => Get("language.english", culture);
}
