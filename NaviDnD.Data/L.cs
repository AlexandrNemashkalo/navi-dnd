using System.Text.Json;

namespace NaviDnD;

// English source strings are translation keys. Russian UI/world text lives in embedded ru.json resources.
// UI language and world language are independent; existing saves without a language remain Russian.
public static class L
{
    public const string Russian = "ru", English = "en";

    public static string Language { get; private set; } = Russian;
    public static bool IsEnglish => Language == English;
    public static string World { get; private set; } = Russian;
    public static bool WorldIsEnglish => World == English;

    private static readonly Lazy<Dictionary<string, string>> Ru = new(() => Load<Dictionary<string, string>>("ru.json") ?? []);
    private static readonly Lazy<Dictionary<string, string[]>> RuPlurals = new(() => Load<Dictionary<string, string[]>>("ru.plurals.json") ?? []);
    public static IReadOnlyDictionary<string, string> RussianTable => Ru.Value;
    public static IReadOnlyDictionary<string, string[]> RussianPluralTable => RuPlurals.Value;

    public static string Normalize(string? language) => language == English ? English : Russian;
    public static void SetLanguage(string? language) => Language = Normalize(language);
    public static void SetWorld(string? language) => World = Normalize(language);

    public static string T(string english) => In(Language, english);
    public static string W(string english) => In(World, english);
    public static string F(string english, params object?[] args) => string.Format(T(english), args);
    public static string WF(string english, params object?[] args) => string.Format(W(english), args);

    // English uses singular/plural; Russian selects one/few/many from its plural resource by the singular key.
    public static string Plural(long n, string one, string many) => PluralIn(Language, n, one, many);
    public static string WPlural(long n, string one, string many) => PluralIn(World, n, one, many);

    private static string In(string language, string english) =>
        language == Russian && Ru.Value.TryGetValue(english, out var russian) ? russian : english;

    private static string PluralIn(string language, long n, string one, string many)
    {
        if (language == Russian && RuPlurals.Value.TryGetValue(one, out var forms) && forms.Length == 3)
        {
            // Taking the remainder first also supports long.MinValue without overflow.
            long last = Math.Abs(n % 10), lastTwo = Math.Abs(n % 100);
            int index = last == 1 && lastTwo != 11 ? 0 : last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? 1 : 2;
            return forms[index];
        }
        return In(language, n is 1 or -1 ? one : many);
    }

    private static T? Load<T>(string file)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("NaviDnD.Localization." + file);
        return stream == null ? default : JsonSerializer.Deserialize<T>(stream);
    }
}
