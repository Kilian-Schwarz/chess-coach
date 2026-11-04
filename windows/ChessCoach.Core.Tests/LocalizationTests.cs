using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Xml.Linq;

namespace ChessCoach.Core.Tests;

public class LocalizationTests
{
    static readonly string[] Languages = { "de", "fr", "es", "it", "pt-BR", "nl", "pl", "tr", "ru", "uk", "zh-Hans", "ja", "ko" };

    static string CoreDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "ChessCoach.Core", "CoreStrings.resx"))) d = d.Parent;
        return Path.Combine(d!.FullName, "ChessCoach.Core");
    }

    static Dictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(CoreDir(), file)).Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    static string Placeholders(string s) =>
        string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, @"\{\d\}").Select(m => m.Value).OrderBy(x => x));

    [Fact]
    public void EveryLanguageHasAllKeysAndPlaceholders()
    {
        var english = Load("CoreStrings.resx");
        foreach (var lang in Languages)
        {
            var map = Load($"CoreStrings.{lang}.resx");
            Assert.Equal(english.Keys.OrderBy(k => k), map.Keys.OrderBy(k => k));
            foreach (var (key, value) in map)
            {
                Assert.False(string.IsNullOrWhiteSpace(value), $"{lang}:{key}");
                Assert.Equal(Placeholders(english[key]), Placeholders(value));
            }
        }
    }

    [Fact]
    public void EveryLanguageIsLoadedFromTheAssembly()
    {
        var rm = new ResourceManager("ChessCoach.Core.CoreStrings", typeof(CoreLocale).Assembly);
        Assert.Equal("White", rm.GetString("color.white", CultureInfo.InvariantCulture));
        foreach (var lang in Languages.Append("en"))
            Assert.False(string.IsNullOrEmpty(CoreLocale.AnswerLanguage(new CultureInfo(lang))));
        Assert.Equal("Weiß", CoreLocale.Get("color.white", new CultureInfo("de")));
        Assert.Equal("German", CoreLocale.AnswerLanguage(new CultureInfo("de-AT")));
    }

    [Fact]
    public void CodeKeysExistInEnglish()
    {
        var english = Load("CoreStrings.resx");
        var pattern = new System.Text.RegularExpressions.Regex("CoreLocale\\.(?:Get|Format)\\(\"([A-Za-z_.]+)\"[,)]");
        foreach (var file in Directory.GetFiles(CoreDir(), "*.cs"))
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(File.ReadAllText(file)))
                Assert.True(english.ContainsKey(m.Groups[1].Value), $"{Path.GetFileName(file)}: {m.Groups[1].Value}");
    }
}
