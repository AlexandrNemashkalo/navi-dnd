using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

// Перевод (L): у каждой строки из L.T/L.F/L.Plural и L.W/L.WF/L.WPlural в исходниках есть английский вариант
// в NaviDnD.Data/Localization/en.json с теми же подстановками {0}, {1}…
public class LocalizationTests
{
    private static readonly string[] SourceDirs = ["NaviDnD", "NaviDnD.Data", "NaviDnD.MapGen", "NaviDnD.Installer", "NaviDnD.McpServer"];
    private const string Literal = "\"((?:[^\"\\\\]|\\\\.)*)\"";

    [Fact]
    public void EveryWrappedStringHasEnglishTranslation()
    {
        var missing = SourceKeys().Where(k => !L.EnglishTable.ContainsKey(k.Key))
            .Select(k => $"{k.File}: {k.Key}").Distinct().ToList();
        Assert.True(missing.Count == 0, "Нет перевода в en.json:\n" + string.Join("\n", missing));
    }

    // У установщика свой словарь (NaviDnD.Installer/en.json) — те же переводы, только его строки.
    [Fact]
    public void InstallerDictionaryMatchesGame()
    {
        string path = Path.Combine(RepoRoot(), "NaviDnD.Installer", "en.json");
        var installer = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
        var wrong = SourceKeys().Where(k => k.Dir == "NaviDnD.Installer")
            .Where(k => !installer.TryGetValue(k.Key, out var v) || !L.EnglishTable.TryGetValue(k.Key, out var game) || v != game)
            .Select(k => $"{k.File}: {k.Key}").Distinct().ToList();
        Assert.True(wrong.Count == 0, "Нет в NaviDnD.Installer/en.json или расходится с игрой:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void TranslationsKeepPlaceholders()
    {
        var broken = L.EnglishTable.Where(p => !Placeholders(p.Key).SetEquals(Placeholders(p.Value)))
            .Select(p => $"{p.Key} → {p.Value}").ToList();
        Assert.True(broken.Count == 0, "Подстановки не совпадают:\n" + string.Join("\n", broken));
    }

    [Fact]
    public void KeysAreStaticLiterals()
    {
        // L.T($"…") и L.T(@"…") не найти в en.json: ключ — обычная строка, значения — через L.F.
        var dynamic = SourceFiles().SelectMany(f => Regex.Matches(File.ReadAllText(f.File), @"L\.(T|F|W|WF)\(\s*[$@]")
            .Select(m => $"{Path.GetFileName(f.File)}: {m.Value}")).ToList();
        Assert.True(dynamic.Count == 0, string.Join("\n", dynamic));
    }

    // Названия из справочников переводятся при показе (L.T(name)) — у каждого должен быть перевод.
    [Fact]
    public void CatalogNamesHaveEnglishTranslation()
    {
        string[] places = ["capital", "city", "town", "village", "castle", "port", "ruins", "dungeon", "cave", "shrine"];
        var names = WorldBiomes.All.Values.Select(b => b.Name)
            .Concat(places.Select(WorldPlaceTypes.Label))
            .Concat(TerrainCatalog.Kinds.Values.Select(k => k.Name))
            .Concat(FurnitureCatalog.Kinds.Values.Select(k => k.Name))
            .Concat(["север", "юг", "запад", "восток", "северо-запад", "северо-восток", "юго-запад", "юго-восток", "центр мира"])
            .Concat(["Ночь", "Утро", "День", "Вечер"]);
        var missing = names.Where(n => !L.EnglishTable.ContainsKey(n)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Нет перевода в en.json:\n" + string.Join("\n", missing));
    }

    // Варианты анкеты героя и шага «Приключение» — русские ключи (Prompts/Dnd5e/*.json): в английской игре — перевод.
    [Fact]
    public void HeroAndAdventureOptionsHaveEnglishTranslation()
    {
        string dir = Path.Combine(RepoRoot(), "NaviDnD", "Prompts", "Dnd5e");
        var hero = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "heroOptions.json")))!;
        var values = new[] { "races", "classes", "alignments", "backgrounds" }
            .SelectMany(k => hero[k]!.AsArray().Select(o => o is System.Text.Json.Nodes.JsonValue ? o.ToString() : (string)o!["name"]!));
        var adventure = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "adventureOptions.json")))!;
        values = values.Concat(adventure["params"]!.AsArray()
            .SelectMany(p => p!["options"]!.AsArray().Select(o => o!.ToString()).Append((string)p["label"]!)));
        var missing = values.Where(v => !L.EnglishTable.ContainsKey(v)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Нет перевода в en.json:\n" + string.Join("\n", missing));
        Assert.NotEmpty(hero["namesEn"]!.AsArray());
    }

    [Fact]
    public void EnglishSwitchesAndFallsBack()
    {
        try
        {
            L.SetLanguage(L.English);
            Assert.Equal("SETTINGS", L.T("НАСТРОЙКИ"));
            Assert.Equal("нет такого ключа", L.T("нет такого ключа"));
            Assert.Equal(L.T("день"), L.Plural(1, "день", "дня", "дней"));
            Assert.Equal(L.T("дней"), L.Plural(21, "день", "дня", "дней"));
            L.SetLanguage(L.Russian);
            Assert.Equal("НАСТРОЙКИ", L.T("НАСТРОЙКИ"));
            Assert.Equal("день", L.Plural(21, "день", "дня", "дней"));
            Assert.Equal("дня", L.Plural(3, "день", "дня", "дней"));
            Assert.Equal("дней", L.Plural(12, "день", "дня", "дней"));
        }
        finally { L.SetLanguage(L.Russian); }
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
            foreach (Match m in Regex.Matches(code, @"L\.W?Plural\([^,]+,\s*" + Literal + @"\s*,\s*" + Literal + @"\s*,\s*" + Literal))
            {
                yield return (dir, name, Unescape(m.Groups[1].Value));
                yield return (dir, name, Unescape(m.Groups[3].Value));
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
