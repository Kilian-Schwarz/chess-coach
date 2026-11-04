using System.Globalization;
using System.Resources;

namespace ChessCoach.Setup;

internal static class Loc
{
    private static readonly ResourceManager Rm = new("ChessCoach.Setup.Strings", typeof(Loc).Assembly);

    public static string T(string key) => Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string T(string key, params object[] args) => string.Format(CultureInfo.CurrentUICulture, T(key), args);
}
