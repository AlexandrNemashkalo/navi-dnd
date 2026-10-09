using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Helpers;

// Таблица уровней правил (GameData/DnD5e_levels.json): пороги опыта, бонус мастерства, кости хитов классов, уровни
// улучшения характеристик, опыт за монстра по уровню опасности. Читается один раз за процесс (Lazy). Это
// детерминированная часть повышения уровня — без нейронки; что получает класс на уровне, решает мастер (LevelUp).
public sealed class LevelRules
{
    public sealed class ClassRule
    {
        public string[] Names { get; set; } = [];
        public int HitDie { get; set; } = 8;
        public int[]? AsiLevels { get; set; }
    }

    public int[] Xp { get; set; } = [0];
    public int[] Proficiency { get; set; } = [2];
    public int[] AsiLevels { get; set; } = [];
    public int MaxScore { get; set; } = 20;
    public Dictionary<string, int> CrXp { get; set; } = [];
    public List<ClassRule> Classes { get; set; } = [];

    private static readonly string FilePath = Path.Combine(AppConfig.AssetDirectory("GameData"), "DnD5e_levels.json");

    private static readonly Lazy<LevelRules> Loaded = new(() =>
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<LevelRules>(File.ReadAllText(FilePath), options) ?? new LevelRules();
        }
        catch { return new LevelRules(); }   // нет файла — уровней нет (MaxLevel 1), игра идёт как раньше
    });

    public static LevelRules Current => Loaded.Value;

    public int MaxLevel => Xp.Length;

    public static int LevelOf(Hero hero) => Math.Max(1, hero.Level ?? 1);

    // Опыт для уровня (порог); за пределами таблицы — последний.
    public int XpFor(int level) => Xp[Math.Clamp(level, 1, MaxLevel) - 1];

    // Порог следующего уровня; null — уровень максимальный.
    public int? NextXp(int level) => level < MaxLevel ? Xp[level] : null;

    public int ProficiencyAt(int level) => Proficiency.Length == 0 ? 2 : Proficiency[Math.Clamp(level, 1, Proficiency.Length) - 1];

    // Опыта хватает на следующий уровень (вне боя — решает экран).
    public bool CanLevelUp(Hero hero) => NextXp(LevelOf(hero)) is int next && (hero.Xp ?? 0) >= next;

    // Класс героя по стату «Класс» (на любом языке; подкласс в скобках не мешает); незнакомый — null.
    public ClassRule? ClassOf(Hero hero)
    {
        string cls = ClassLevel.Split(hero.Stat(StatKeys.Class)?.Value).cls.ToLowerInvariant();
        return Classes.FirstOrDefault(c => c.Names.Any(n => cls.StartsWith(n.ToLowerInvariant())));
    }

    // Кость хитов класса; незнакомый класс — d8 (средняя).
    public int HitDie(Hero hero) => ClassOf(hero)?.HitDie ?? 8;

    public bool IsAsiLevel(Hero hero, int level) => (ClassOf(hero)?.AsiLevels ?? AsiLevels).Contains(level);

    // Опыт за монстра справочника (monsterKey) по его уровню опасности; не нашёлся — 0.
    public int MonsterXp(string? monsterKey) =>
        MonsterDatabase.Find(monsterKey) is { } m && CrXp.TryGetValue(m.Cr.Trim(), out int xp) ? xp : 0;
}
