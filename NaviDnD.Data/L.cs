using System.Text.Json;

namespace NaviDnD;

// Язык интерфейса. Ключ перевода — русская строка из кода (L.T("НАСТРОЙКИ")), английский — Localization/en.json
// (встроен в сборку). Нет перевода — русская строка. Файл подключается и в установщик (общий язык из settings.json).
public static class L
{
    public const string Russian = "ru", English = "en";

    public static string Language { get; private set; } = Russian;
    public static bool IsEnglish => Language == English;

    private static readonly Lazy<Dictionary<string, string>> En = new(Load);
    public static IReadOnlyDictionary<string, string> EnglishTable => En.Value;

    public static string Normalize(string? language) => language == English ? English : Russian;

    // Экраны берут строки при отрисовке: смена языка видна со следующей перерисовки.
    public static void SetLanguage(string? language) => Language = Normalize(language);

    public static string T(string ru) => IsEnglish && En.Value.TryGetValue(ru, out var en) ? en : ru;

    // Шаблон с {0}, {1}… — как string.Format; порядок аргументов в переводе может отличаться.
    public static string F(string ru, params object?[] args) => string.Format(T(ru), args);

    // Существительное к числу: русские формы для 1, 2–4 и 5+; в английском — перевод формы 1 или 5+.
    public static string Plural(long n, string one, string few, string many)
    {
        if (IsEnglish) return T(Math.Abs(n) == 1 ? one : many);
        long a = Math.Abs(n);
        return a % 10 == 1 && a % 100 != 11 ? one : a % 10 is >= 2 and <= 4 && a % 100 is < 12 or > 14 ? few : many;
    }

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("NaviDnD.Localization.en.json");
        if (stream == null) return [];
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
