using System.Text.Json;

namespace NaviDnD;

// Языки. Ключ перевода — русская строка из кода, английский — Localization/en.json (встроен в сборку); нет перевода —
// русская строка. Два языка:
// · интерфейса (L.T/L.F/L.Plural) — меню и экраны, настройка игрока;
// · мира (L.W/L.WF/L.WPlural) — что сохраняется и уходит мастеру: история, сводки, названия генератора. Язык игры,
//   выбран при создании мира (WorldState.Language), задаётся при загрузке игры.
// Файл подключается и в установщик (язык из settings.json).
public static class L
{
    public const string Russian = "ru", English = "en";

    public static string Language { get; private set; } = Russian;
    public static bool IsEnglish => Language == English;

    public static string World { get; private set; } = Russian;
    public static bool WorldIsEnglish => World == English;

    private static readonly Lazy<Dictionary<string, string>> En = new(Load);
    public static IReadOnlyDictionary<string, string> EnglishTable => En.Value;

    public static string Normalize(string? language) => language == English ? English : Russian;

    // Экраны берут строки при отрисовке: смена языка видна со следующей перерисовки.
    public static void SetLanguage(string? language) => Language = Normalize(language);
    public static void SetWorld(string? language) => World = Normalize(language);

    public static string T(string ru) => In(Language, ru);
    public static string W(string ru) => In(World, ru);

    // Шаблон с {0}, {1}… — как string.Format; порядок аргументов в переводе может отличаться.
    public static string F(string ru, params object?[] args) => string.Format(T(ru), args);
    public static string WF(string ru, params object?[] args) => string.Format(W(ru), args);

    // Существительное к числу: русские формы для 1, 2–4 и 5+; в английском — перевод формы 1 или 5+.
    public static string Plural(long n, string one, string few, string many) => PluralIn(Language, n, one, few, many);
    public static string WPlural(long n, string one, string few, string many) => PluralIn(World, n, one, few, many);

    private static string In(string language, string ru) =>
        language == English && En.Value.TryGetValue(ru, out var en) ? en : ru;

    private static string PluralIn(string language, long n, string one, string few, string many)
    {
        long a = Math.Abs(n);
        if (language == English) return In(English, a == 1 ? one : many);
        return a % 10 == 1 && a % 100 != 11 ? one : a % 10 is >= 2 and <= 4 && a % 100 is < 12 or > 14 ? few : many;
    }

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("NaviDnD.Localization.en.json");
        if (stream == null) return [];
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
