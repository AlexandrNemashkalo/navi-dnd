using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

// Translation (L): each line from L.T/L.F/L.Plural and L.W/L.WF/L.WPlural in the source code has a Russian
// version in NaviDnD.Data/Localization/ru.json with the same substitutions {0}, {1}...
public class LocalizationTests
{
    private static readonly string[] SourceDirs = ["NaviDnD", "NaviDnD.Data", "NaviDnD.MapGen", "NaviDnD.Installer", "NaviDnD.McpServer"];
    private const string Literal = "\"((?:[^\"\\\\]|\\\\.)*)\"";

    [Fact]
    public void EveryWrappedStringHasRussianTranslation()
    {
        var missing = SourceKeys().Where(k => !L.RussianTable.ContainsKey(k.Key))
            .Select(k => $"{k.File}: {k.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Нет перевода в ru.json:\n" + string.Join("\n", missing));
    }

    // The installer has its own dictionary (NaviDnD.Installer/ru.json) - the same translations, only its strings.
    [Fact]
    public void InstallerDictionaryMatchesGame()
    {
        string path = Path.Combine(RepoRoot(), "NaviDnD.Installer", "ru.json");
        var installer = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        var wrong = SourceKeys().Where(k => k.Dir == "NaviDnD.Installer")
            .Where(k => !installer.TryGetValue(k.Key, out var v) || !L.RussianTable.TryGetValue(k.Key, out var game) || v != game)
            .Select(k => $"{k.File}: {k.Key}").Distinct().ToList();
        Assert.True(wrong.Count == 0, "Нет в NaviDnD.Installer/ru.json или расходится с игрой:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void TranslationsKeepPlaceholders()
    {
        var broken = L.RussianTable.Where(p => !Placeholders(p.Key).SetEquals(Placeholders(p.Value)))
            .Select(p => $"{p.Key} → {p.Value}").ToList();
        Assert.True(broken.Count == 0, "Подстановки не совпадают:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void KeysAreStaticLiterals()
    {
        // L.T($"…") and L.T(@"…") cannot be found in ru.json; the key is a regular string, the values are through L.F.
        var dynamic = SourceFiles().SelectMany(f => Regex.Matches(File.ReadAllText(f.File), @"L\.(T|F|W|WF)\(\s*[$@]")
            .Select(m => $"{Path.GetFileName(f.File)}: {m.Value}")).ToList();
        Assert.True(dynamic.Count == 0, string.Join("\n", dynamic));
    }

    // Names from directories are translated when displayed (L.T(name)) - everyone must have a translation.
    [Fact]
    public void CatalogNamesHaveRussianTranslation()
    {
        string[] places = ["capital", "city", "town", "village", "castle", "port", "ruins", "dungeon", "cave", "shrine"];
        var names = WorldBiomes.All.Values.Select(b => b.Name)
            .Concat(places.Select(WorldPlaceTypes.Label))
            .Concat(TerrainCatalog.Kinds.Values.Select(k => k.Name))
            .Concat(FurnitureCatalog.Kinds.Values.Select(k => k.Name))
            .Concat(["north", "south", "west", "east", "northwest", "northeast", "southwest", "southeast", "center of the world"])
            .Concat(["Night", "Morning", "Day", "Evening"]);
        var missing = names.Where(n => !L.RussianTable.ContainsKey(n)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Нет перевода в ru.json:\n" + string.Join("\n", missing));
    }

    // Options for the hero profile and the “Adventure” step - English keys (Prompts/Dnd5e/*.json): in the Russian game - translation.
    [Fact]
    public void HeroAndAdventureOptionsHaveRussianTranslation()
    {
        string dir = Path.Combine(RepoRoot(), "NaviDnD", "Prompts", "Dnd5e");
        var hero = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "heroOptions.json")))!;
        var values = new[] { "races", "classes", "alignments", "backgrounds" }
            .SelectMany(k => hero[k]!.AsArray().Select(o => o is System.Text.Json.Nodes.JsonValue ? o.ToString() : (string)o!["name"]!));
        var adventure = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "adventureOptions.json")))!;
        values = values.Concat(adventure["params"]!.AsArray()
            .SelectMany(p => p!["options"]!.AsArray().Select(o => o!.ToString()).Append((string)p["label"]!)));
        var missing = values.Where(v => !L.RussianTable.ContainsKey(v)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Нет перевода в ru.json:\n" + string.Join("\n", missing));
        Assert.NotEmpty(hero["namesEn"]!.AsArray());
    }

    [Fact]
    public void EnglishKeysTranslateToRussianAndFallBackToEnglish()
    {
        try
        {
            L.SetLanguage(L.English);
            Assert.Equal("SETTINGS", L.T("SETTINGS"));
            L.SetLanguage(L.Russian);
            Assert.Equal("НАСТРОЙКИ", L.T("SETTINGS"));
            Assert.Equal("Unknown key", L.T("Unknown key"));
            Assert.Equal("День 3", L.F("Day {0}", 3));
            Assert.Equal("НАЗВАНИЕ", L.T("TITLE"));
            Assert.Equal("ИМЯ", L.T("NAME"));
            Assert.Equal("север", L.T("north"));
            Assert.Equal("на север", L.T("to the north"));
        }
        finally { L.SetLanguage(L.Russian); }
    }

    [Theory]
    [InlineData(0, "дней", "days")]
    [InlineData(1, "день", "day")]
    [InlineData(2, "дня", "days")]
    [InlineData(5, "дней", "days")]
    [InlineData(11, "дней", "days")]
    [InlineData(12, "дней", "days")]
    [InlineData(21, "день", "days")]
    [InlineData(24, "дня", "days")]
    [InlineData(-1, "день", "day")]
    [InlineData(-22, "дня", "days")]
    [InlineData(long.MinValue, "дней", "days")]
    public void PluralsRespectLanguage(long n, string russian, string english)
    {
        try
        {
            L.SetLanguage(L.Russian);
            Assert.Equal(russian, L.Plural(n, "day", "days"));
            L.SetLanguage(L.English);
            Assert.Equal(english, L.Plural(n, "day", "days"));
        }
        finally { L.SetLanguage(L.Russian); }
    }

    [Fact]
    public void WorldLanguageIsIndependentFromInterface()
    {
        try
        {
            L.SetLanguage(L.English);
            L.SetWorld(L.Russian);
            Assert.Equal("Room 3", L.F("Room {0}", 3));
            Assert.Equal("Комната 3", L.WF("Room {0}", 3));
            Assert.Equal("дня", L.WPlural(3, "day", "days"));
            L.SetLanguage(L.Russian);
            L.SetWorld(L.English);
            Assert.Equal("Комната 3", L.F("Room {0}", 3));
            Assert.Equal("Room 3", L.WF("Room {0}", 3));
            Assert.Equal("days", L.WPlural(3, "day", "days"));
        }
        finally { L.SetLanguage(L.Russian); L.SetWorld(L.Russian); }
    }

    [Fact]
    public void TranslationKeysAreEnglishAndRussianPluralFormsAreComplete()
    {
        Assert.DoesNotContain(L.RussianTable.Keys, k => Regex.IsMatch(k, "[А-Яа-яЁё]"));
        Assert.DoesNotContain(SourceKeys(), k => Regex.IsMatch(k.Key, "[А-Яа-яЁё]"));
        Assert.All(L.RussianPluralTable.Values, forms => Assert.Equal(3, forms.Length));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).ToHashSet();

    private static IEnumerable<(string Dir, string File, string Key)> SourceKeys()
    {
        foreach (var (dir, file) in SourceFiles())
        {
            string code = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            foreach (Match m in Regex.Matches(code, @"L\.(?:T|F|W|WF)\(\s*" + Literal))
                yield return (dir, name, Unescape(m.Groups[1].Value));
            foreach (Match m in Regex.Matches(code, @"L\.W?Plural\([^,]+,\s*" + Literal + @"\s*,\s*" + Literal))
            {
                yield return (dir, name, Unescape(m.Groups[1].Value));
                yield return (dir, name, Unescape(m.Groups[2].Value));
            }
        }
    }

    private static string RepoRoot([CallerFilePath] string self = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(self)!, ".."));

    private static IEnumerable<(string Dir, string File)> SourceFiles()
    {
        string sep = Path.DirectorySeparatorChar.ToString();
        return SourceDirs.SelectMany(d => Directory.EnumerateFiles(Path.Combine(RepoRoot(), d), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(sep + "bin" + sep) && !f.Contains(sep + "obj" + sep)).Select(f => (d, f)));
    }

    private static string Unescape(string s) => Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch
    {
        "n" => "\n", "t" => "\t", "r" => "\r", var c => c,
    });
}
