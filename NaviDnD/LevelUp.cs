using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD;

// Повышение уровня (окно «Новый уровень»). Игрок не правит героя свободно — только выбирает из предложенного:
//   код — уровень, бонус мастерства, хиты (среднее или бросок кости хитов), улучшение характеристик;
//   мастер (LevelUpOptions) — что класс получает на этом уровне и группы выбора (подкласс, заклинания, черты…);
//   мастер (LevelUpApply) — патч героя по выбранному; код пропускает только листы героя (статы, навыки, умения,
//   ресурсы, заклинания) и поверх ставит свои числа. Всё — на копии героя: «Принять» заменяет героя целиком.
public sealed class LevelUp
{
    public sealed class Option
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        // Вариант по просьбе игрока (поле ввода) — мастер добавил его в группу; помечается «★».
        public bool Custom { get; set; }
        // Рост ресурса (ячейки, Ки, использования): название как в листе героя и новое «текущее/максимум» — не умение.
        public string? Resource { get; set; }
        // Изменение стата (Подготовлено заклинаний, Скорость…): название как в листе героя, новое значение — Value.
        public string? Stat { get; set; }
        public string? Value { get; set; }
    }

    public sealed class Choice
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int Count { get; set; } = 1;
        public List<Option> Options { get; set; } = [];
        [System.Text.Json.Serialization.JsonIgnore] public HashSet<int> Picked { get; } = [];

        // Сколько надо выбрать: вариантов меньше, чем положено, — все.
        [System.Text.Json.Serialization.JsonIgnore] public int Needed => Math.Min(Count, Options.Count);
        [System.Text.Json.Serialization.JsonIgnore] public bool Done => Picked.Count == Needed;

        // Выбор/снятие варианта; уже выбрано сколько нужно — при выборе одного заменяется он.
        public void Toggle(int index)
        {
            if (index < 0 || index >= Options.Count) return;
            if (Picked.Remove(index)) return;
            if (Picked.Count >= Needed)
            {
                if (Needed != 1) return;
                Picked.Clear();
            }
            Picked.Add(index);
        }
    }

    // Улучшение характеристик: +2 к одной, +1 к двум или черта (группа мастера с id "feat").
    public enum AsiMode { None, Plus2, Plus1x2, Feat }

    public const string FeatId = "feat";

    public Hero Hero { get; }
    public LevelRules Rules { get; }
    public int From { get; }
    public int To => From + 1;
    public int HitDie { get; }
    public bool IsAsi { get; }

    public List<Option> Gains { get; private set; } = [];
    public List<Choice> Choices { get; private set; } = [];
    public bool PlanLoaded { get; private set; }

    // Хиты: null — не выбрано; Roll — значение кости после броска (бросок один, повтора нет).
    public bool? HpRoll { get; private set; }
    public int? HpDie { get; private set; }

    public AsiMode Asi { get; set; } = AsiMode.None;
    public List<string> AsiPicks { get; } = [];

    // Окно открывается: опыта хватает, герой жив и не в бою.
    public static bool Available(WorldState ws) =>
        ws.Hero is { Dead: not true } hero && ws.Combat?.Active != true && LevelRules.Current.CanLevelUp(hero);

    public LevelUp(Hero hero, LevelRules rules)
    {
        Hero = hero;
        Rules = rules;
        From = LevelRules.LevelOf(hero);
        HitDie = rules.HitDie(hero);
        IsAsi = rules.IsAsiLevel(hero, To);
        // Начатое раньше повышение на этот же уровень — варианты и бросок оттуда, без нового запроса.
        if (hero.LevelUpDraft is { } draft && draft.Level == To)
        {
            if (draft.Plan != null) LoadPlan(draft.Plan);
            if (draft.HpDie is int die) { HpRoll = true; HpDie = Math.Clamp(die, 1, HitDie); }
        }
    }

    private static readonly JsonSerializerOptions DraftJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    // Варианты и бросок — в героя (сохранится с игрой): после отмены или перезапуска окно продолжит с них.
    public void SaveDraft() => Hero.LevelUpDraft = new LevelUpDraft
    {
        Level = To,
        Plan = PlanLoaded ? JsonSerializer.Serialize(new { gains = Gains, choices = Choices }, DraftJson) : null,
        HpDie = HpDie,
    };

    public int Score(string ability) => AbilityNames.Score(AbilityNames.Find(Hero, ability)) ?? 10;

    // Новое значение характеристики с учётом выбранного улучшения.
    public int NewScore(string ability) =>
        Math.Min(Rules.MaxScore, Score(ability) + (Asi == AsiMode.Plus2 ? 2 : Asi == AsiMode.Plus1x2 ? 1 : 0) * AsiPicks.Count(a => a == ability));

    private int ConMod => AbilityNames.Modifier(Score(AbilityNames.Con));

    public int HpAverage => Math.Max(1, HitDie / 2 + 1 + ConMod);

    // Прибавка хитов: кость (среднее или бросок) + Телосложение; поднялся модификатор Телосложения — ещё +разница
    // за каждый уровень (правило 5e).
    public int HpGain
    {
        get
        {
            int die = HpRoll == true ? Math.Max(1, (HpDie ?? 1) + ConMod) : HpAverage;
            int conDelta = AbilityNames.Modifier(NewScore(AbilityNames.Con)) - ConMod;
            return die + conDelta * To;
        }
    }

    public void ChooseAverage()
    {
        if (HpRoll == true) return;   // бросок сделан — не переигрывается
        HpRoll = false;
    }

    public int RollHp(Random random)
    {
        if (HpRoll == true) return HpDie!.Value;
        HpRoll = true;
        HpDie = random.Next(1, HitDie + 1);
        return HpDie.Value;
    }

    public void SetAsi(AsiMode mode)
    {
        if (mode == Asi) return;
        Asi = mode;
        AsiPicks.Clear();
        if (Choices.FirstOrDefault(c => c.Id == FeatId) is { } feat) feat.Picked.Clear();
    }

    // Выбор характеристики для улучшения: +2 — одна (замена), +1/+1 — две разные; на максимуме — нельзя.
    public void ToggleAsi(string ability)
    {
        if (AsiPicks.Remove(ability)) return;
        if (Score(ability) >= Rules.MaxScore) return;
        int needed = Asi == AsiMode.Plus2 ? 1 : Asi == AsiMode.Plus1x2 ? 2 : 0;
        if (needed == 0) return;
        if (AsiPicks.Count >= needed)
        {
            if (needed != 1) return;
            AsiPicks.Clear();
        }
        AsiPicks.Add(ability);
    }

    // Группы выбора, видимые сейчас: черты — только если вместо улучшения выбрана черта.
    public IEnumerable<Choice> VisibleChoices => Choices.Where(c => c.Id != FeatId || Asi == AsiMode.Feat);

    public bool HasFeats => Choices.Any(c => c.Id == FeatId && c.Options.Count > 0);

    public bool AsiDone => !IsAsi || Asi switch
    {
        AsiMode.Plus2 => AsiPicks.Count == 1,
        AsiMode.Plus1x2 => AsiPicks.Count == 2,
        AsiMode.Feat => true,
        _ => false,
    };

    public bool Ready => PlanLoaded && HpRoll != null && AsiDone && VisibleChoices.All(c => c.Done);

    // Есть что вносить в лист героя кроме чисел движка: умения, выбор, пересчёт от бонуса мастерства или характеристик.
    public bool NeedsMaster => Gains.Count > 0 || VisibleChoices.Any() || AsiPicks.Count > 0
                               || Rules.ProficiencyAt(To) != Rules.ProficiencyAt(From);

    // ── Ответ мастера с вариантами ──────────────────────────────────────────

    // {"gains":[{name,description}], "choices":[{id,title,count,options:[{name,description}]}]}. Пустые группы
    // и повтор черт (код даёт их выбор сам) отбрасываются. false — не разобрался.
    public bool LoadPlan(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null) return false;
            // Что у героя уже есть (умение прошлого уровня), — не «получено»; рост ресурса — получено.
            var has = Hero.Abilities.Select(a => a.Name).Concat((Hero.Spells ?? []).Select(sp => sp.Name))
                .Select(n => n?.Trim().ToLowerInvariant()).ToHashSet();
            Gains = root["gains"]?.Deserialize<List<Option>>(options)?
                .Where(g => g.Name.Trim().Length > 0 && (g.Resource != null || g.Stat != null || !has.Contains(g.Name.Trim().ToLowerInvariant())))
                .ToList() ?? [];
            Choices = root["choices"]?.Deserialize<List<Choice>>(options)?
                .Where(c => c.Options is { Count: > 0 } && c.Count > 0)
                .ToList() ?? [];
            // Черты — только на уровне улучшения характеристик.
            if (!IsAsi) Choices.RemoveAll(c => c.Id == FeatId);
            for (int i = 0; i < Choices.Count; i++)
                if (string.IsNullOrWhiteSpace(Choices[i].Id)) Choices[i].Id = $"choice{i}";
            PlanLoaded = true;
            return true;
        }
        catch { return false; }
    }

    // ── Запросы мастеру ─────────────────────────────────────────────────────

    public string ClassName => ClassLevel.Split(Hero.Stat(StatKeys.Class)?.Value).cls;

    private string HeroSheet() => new AiContextBuilder(new WorldState { Hero = Hero })
        .Serialize("hero.stats", "hero.skills", "hero.resources", "hero.abilities", "hero.spells");

    public string OptionsRequest() =>
        $"Повышение уровня: {ClassName} {From} → {To}. Хиты, бонус мастерства ({AbilityNames.Signed(Rules.ProficiencyAt(To))})"
        + (IsAsi ? " и улучшение характеристик — даёт движок; добавь группу `feat` с подходящими чертами (альтернатива улучшению)." : " — даёт движок.")
        + $"\nГерой:\n{HeroSheet()}";

    // Выбранное игроком — мастеру для патча.
    public string ApplyRequest()
    {
        var lines = new List<string>
        {
            $"Повышение уровня: {ClassName} {From} → {To}. Бонус мастерства: {AbilityNames.Signed(Rules.ProficiencyAt(From))} → {AbilityNames.Signed(Rules.ProficiencyAt(To))}.",
        };
        if (Gains.Count > 0) lines.Add("Получено: " + string.Join("; ", Gains.Select(g => $"{g.Name} — {g.Description}")));
        foreach (var c in VisibleChoices)
            lines.Add($"Выбрано ({c.Title}): " + string.Join("; ", c.Picked.Order().Select(i => $"{c.Options[i].Name} — {c.Options[i].Description}")));
        if (Asi is AsiMode.Plus2 or AsiMode.Plus1x2)
            lines.Add("Улучшение характеристик (значения ставит движок): " + string.Join(", ", AsiPicks.Distinct()
                .Select(a => $"{AbilityNames.Find(Hero, a)?.Name ?? a} {Score(a)} → {NewScore(a)}")));
        lines.Add($"Герой (до повышения):\n{HeroSheet()}");
        return string.Join("\n", lines);
    }

    // ── Итог ────────────────────────────────────────────────────────────────

    private static readonly string[] AllowedHeroKeys = ["stats", "skills", "abilities", "resources", "spells", "actions", "speedmax"];

    // Новый герой: копия текущего + разрешённая часть патча мастера + числа движка. Исходный герой не меняется.
    public Hero Apply(string? masterPatchJson)
    {
        var scratch = new Storage();
        scratch.WorldState.Hero = JsonSerializer.Deserialize<Hero>(JsonSerializer.Serialize(Hero, CopyJson), CopyJson);
        if (masterPatchJson != null && JsonNode.Parse(masterPatchJson) is JsonObject root
            && root.FirstOrDefault(p => p.Key.Equals("hero", StringComparison.OrdinalIgnoreCase)).Value is JsonObject patch)
        {
            var allowed = new JsonObject();
            foreach (var (key, value) in patch.ToList())
                if (AllowedHeroKeys.Contains(key.ToLowerInvariant())) allowed[key] = value?.DeepClone();
            scratch.ApplyUpdateWorldState(new JsonObject { ["hero"] = allowed }.ToJsonString());
        }

        var hero = scratch.WorldState.Hero!;
        // Цвет умения/заклинания, если мастер его не прислал, — «утилитарный», как у прочих (иначе строка выбивается).
        foreach (var ability in hero.Abilities.Where(x => x.Color is not { Count: 3 })) ability.Color = [.. UtilityColor];
        foreach (var spell in (hero.Spells ?? []).Where(x => x.Color is not { Count: 3 })) spell.Color = [.. UtilityColor];
        foreach (string ability in AbilityNames.All)
            if (NewScore(ability) != Score(ability) && AbilityNames.Find(hero, ability) is { } stat)
                stat.Value = AbilityNames.Format(NewScore(ability));
        if (AbilityNames.Proficiency(hero) is { } prof) prof.Value = AbilityNames.Signed(Rules.ProficiencyAt(To));
        int gain = HpGain;
        if (HpRoll != null && hero.Hp?.Split('/') is [var cur, var max] && int.TryParse(cur.Trim(), out int c) && int.TryParse(max.Trim(), out int m))
            hero.Hp = $"{c + gain}/{m + gain}";
        hero.Level = To;
        hero.Xp = Hero.Xp;
        hero.LevelUpDraft = null;   // уровень получен — черновик не нужен
        LimitPrepared(hero);
        return hero;
    }

    // Подготовленных заклинаний (не заговоров) — не больше максимума класса: лишними становятся новые (выученные на
    // этом уровне), подготовит их игрок сам. Классы «known» (без стата «Подготовлено») не трогаются.
    private void LimitPrepared(Hero hero)
    {
        if (!int.TryParse(hero.Stat(StatKeys.SpellsPrepared)?.Value?.Trim(), out int max)) return;
        var old = (Hero.Spells ?? []).Where(sp => sp.Deleted != true).Select(sp => sp.Name).ToHashSet();
        var prepared = (hero.Spells ?? []).Where(sp => sp.Deleted != true && sp.Level > 0 && sp.Prepared == true)
            .OrderBy(sp => old.Contains(sp.Name) ? 0 : 1).ToList();
        foreach (var sp in prepared.Skip(max)) sp.Prepared = false;
    }

    // Предпросмотр для карточки героя до «Принять» — без нейронки: числа движка, полученное и выбранное — умениями,
    // заклинания справочника — заклинаниями (навыки и спасброски пересчитает мастер при «Принять»).
    public Hero Preview()
    {
        var hero = Apply(null);
        var known = hero.Abilities.Select(a => a.Name).Concat((hero.Spells ?? []).Select(sp => sp.Name)).ToHashSet();
        foreach (var option in Gains.Concat(VisibleChoices.SelectMany(c => c.Picked.Order().Select(i => c.Options[i]))))
        {
            // Изменение стата — новое значение в его строке (по названию или ключу), не умение.
            if (option.Stat != null)
            {
                if (option.Value == null) continue;
                string? key = StatKeys.FromName(option.Stat);
                var stat = hero.Stats.FirstOrDefault(x => x.Deleted != true
                    && (string.Equals(x.Name?.Trim(), option.Stat.Trim(), StringComparison.OrdinalIgnoreCase) || key != null && x.Key == key));
                if (stat != null) stat.Value = option.Value;
                else hero.Stats.Add(new HeroStat { Name = option.Stat, Value = option.Value, Key = key });
                continue;
            }
            // Рост ресурса — новое значение в его строке (новый ресурс — новой строкой), не умение.
            string resName = option.Resource ?? option.Name;
            if (option.Resource != null || hero.Resources.Any(r => r.Deleted != true && r.Name == resName))
            {
                if (option.Value == null) continue;
                if (hero.Resources.FirstOrDefault(r => r.Deleted != true && r.Name == resName) is { } res) res.Value = option.Value;
                else hero.Resources.Add(new HeroResource { Name = resName, Value = option.Value,
                    Category = resName.Contains("ячейк", StringComparison.OrdinalIgnoreCase) || resName.Contains("slot", StringComparison.OrdinalIgnoreCase)
                        ? ResourceCategories.Spells : ResourceCategories.Abilities });
                continue;
            }
            if (!known.Add(option.Name)) continue;
            if (SpellDatabase.Find(option.Name) is { } spell)
                (hero.Spells ??= []).Add(new HeroSpell { Name = option.Name, Level = spell.Level, Prepared = true, Color = [.. UtilityColor] });
            else
                hero.Abilities.Add(new HeroAbility { Name = option.Name, Description = option.Description, Color = [.. UtilityColor] });
        }
        LimitPrepared(hero);
        return hero;
    }

    // ── Свой вариант (просьба игрока) ───────────────────────────────────────

    public string CustomRequest(string request) => OptionsRequest()
        + $"\nПросьба игрока: {request.Trim()}\nВыбор на этом уровне: "
        + string.Join("; ", Choices.Select(c => $"{c.Id} — {c.Title} ({string.Join(", ", c.Options.Select(o => o.Name))})"));

    // Ответ мастера на просьбу: {"choices":[{id,title,count,options:[…]}]} — варианты дописываются в группу с тем же id
    // (новая группа — целиком), или {"refusal":"…"}. Возвращает текст отказа; null — вариант добавлен.
    public string? MergeCustom(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root?["refusal"]?.GetValue<string>() is { Length: > 0 } refusal) return refusal;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var groups = root?["choices"]?.Deserialize<List<Choice>>(options)?.Where(c => c.Options is { Count: > 0 }).ToList() ?? [];
            if (groups.Count == 0) return L.T("Мастер не предложил вариант.");
            foreach (var g in groups)
            {
                foreach (var o in g.Options) o.Custom = true;
                if (Choices.FirstOrDefault(c => c.Id == g.Id) is { } existing)
                    existing.Options.AddRange(g.Options.Where(o => existing.Options.All(e => e.Name != o.Name)));
                else if (g.Id != FeatId || IsAsi)
                {
                    if (string.IsNullOrWhiteSpace(g.Id)) g.Id = $"custom{Choices.Count}";
                    if (g.Count <= 0) g.Count = 1;
                    Choices.Add(g);
                }
            }
            return null;
        }
        catch { return L.T("Мастер не ответил — попробуй ещё раз."); }
    }

    private static readonly int[] UtilityColor = [220, 220, 220];

    private static readonly JsonSerializerOptions CopyJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
