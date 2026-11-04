using System.Security.Cryptography;
using System.Text;
using ChessCoach.App.Model;
using ChessCoach.Core;

namespace ChessCoach.App.Platform;

internal sealed class DpapiTokenStore : ITokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ChessCoach.Token.v1");

    public string Load()
    {
        try
        {
            if (!File.Exists(AppPaths.TokenFile)) return "";
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(AppPaths.TokenFile), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) { Log.Write("Token unreadable: " + e.GetType().Name); return ""; }
    }

    public void Save(string token)
    {
        try
        {
            if (string.IsNullOrEmpty(token)) { if (File.Exists(AppPaths.TokenFile)) File.Delete(AppPaths.TokenFile); return; }
            var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(AppPaths.TokenFile, cipher);
        }
        catch (Exception e) { Log.Write("Token not saved: " + e.GetType().Name); }
    }
}
