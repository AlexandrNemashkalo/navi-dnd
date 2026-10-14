using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Helpers;

// Table of rules levels (GameData/DnD5e_levels.json): experience thresholds, proficiency bonus, class hit dice,
// stat improvement levels, experience for a monster by danger level. Read once per process (Lazy). This is a
// deterministic part of leveling up - without a AI; what a class receives at a level is decided by the GM
// (LevelUp).
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
        catch { return new LevelRules(); }   // no file - no levels (MaxLevel 1), the game runs as before
    });

    public static LevelRules Current => Loaded.Value;

    public int MaxLevel => Xp.Length;

    public static int LevelOf(Hero hero) => Math.Max(1, hero.Level ?? 1);

    // Experience for level (threshold); outside the table - the last one.
    public int XpFor(int level) => Xp[Math.Clamp(level, 1, MaxLevel) - 1];

    // Next level threshold; null - maximum level.
    public int? NextXp(int level) => level < MaxLevel ? Xp[level] : null;

    public int ProficiencyAt(int level) => Proficiency.Length == 0 ? 2 : Proficiency[Math.Clamp(level, 1, Proficiency.Length) - 1];

    // There is enough experience for the next level (outside of combat - the screen decides).
    public bool CanLevelUp(Hero hero) => NextXp(LevelOf(hero)) is int next && (hero.Xp ?? 0) >= next;

    // Hero class according to the “Class” stat (in any language; subclass in parentheses does not interfere); stranger - null.
    public ClassRule? ClassOf(Hero hero)
    {
        string cls = ClassLevel.Split(hero.Stat(StatKeys.Class)?.Value).cls.ToLowerInvariant();
        return Classes.FirstOrDefault(c => c.Names.Any(n => cls.StartsWith(n.ToLowerInvariant())));
    }

    // Class hit dice; unfamiliar class - d8 (average).
    public int HitDie(Hero hero) => ClassOf(hero)?.HitDie ?? 8;

    public bool IsAsiLevel(Hero hero, int level) => (ClassOf(hero)?.AsiLevels ?? AsiLevels).Contains(level);

    // Experience for a reference monster (monsterKey) according to its danger level; not found - 0.
    public int MonsterXp(string? monsterKey) =>
        MonsterDatabase.Find(monsterKey) is { } m && CrXp.TryGetValue(m.Cr.Trim(), out int xp) ? xp : 0;
}
