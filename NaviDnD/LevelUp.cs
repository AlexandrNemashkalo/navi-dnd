using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;

namespace NaviDnD;

// Level up (New Level window). The player does not freely control the hero - he only chooses from what is
// offered: code - level, skill bonus, hits (average or hit dice roll), improvement of characteristics;   GM
// (LevelUpOptions) - what the class receives at this level and groups of choice (subclass, spells, traits...);
// GM (LevelUpApply) - patch of the hero according to the chosen one; the code skips only the hero’s sheets
// (stats, skills, abilities, resources, spells) and puts its numbers on top. Everything is on a copy of the
// hero: “Accept” replaces the entire hero.
public sealed class LevelUp
{
    public sealed class Option
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        // Option at the request of the player (input field) - the GM added him to the group; marked "★".
        public bool Custom { get; set; }
        // Resource growth (cell, Ki, use): the name is as in the hero sheet and the new “current/maximum” is not a skill.
        public string? Resource { get; set; }
        // Stat change (spells prepared, Speed...): name as in the hero sheet, new value - Value.
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

        // How many to choose: there are fewer options than expected - that's all.
        [System.Text.Json.Serialization.JsonIgnore] public int Needed => Math.Min(Count, Options.Count);
        [System.Text.Json.Serialization.JsonIgnore] public bool Done => Picked.Count == Needed;

        // Select/unselect option; As many as needed have already been selected - when you select one, it is replaced.
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

    // Improvement of characteristics: +2 to one, +1 to two or trait (GM group with id "feat").
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

    // Hits: null - not selected; Roll - the value of the die after the throw (one throw, no repeat).
    public bool? HpRoll { get; private set; }
    public int? HpDie { get; private set; }

    public AsiMode Asi { get; set; } = AsiMode.None;
    public List<string> AsiPicks { get; } = [];

    // The window opens: there is enough experience, the hero is alive and not in battle.
    public static bool Available(WorldState ws) =>
        ws.Hero is { Dead: not true } hero && ws.Combat?.Active != true && LevelRules.Current.CanLevelUp(hero);

    public LevelUp(Hero hero, LevelRules rules)
    {
        Hero = hero;
        Rules = rules;
        From = LevelRules.LevelOf(hero);
        HitDie = rules.HitDie(hero);
        IsAsi = rules.IsAsiLevel(hero, To);
        // An increase to the same level that was started earlier - options and a throw from there, without a new request.
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

    // Options and throw - at the hero (save with the game): after canceling or restarting, the window will continue with them.
    public void SaveDraft() => Hero.LevelUpDraft = new LevelUpDraft
    {
        Level = To,
        Plan = PlanLoaded ? JsonSerializer.Serialize(new { gains = Gains, choices = Choices }, DraftJson) : null,
        HpDie = HpDie,
    };

    public int Score(string ability) => AbilityNames.Score(AbilityNames.Find(Hero, ability)) ?? 10;

    // New stat value based on the selected enhancement.
    public int NewScore(string ability) =>
        Math.Min(Rules.MaxScore, Score(ability) + (Asi == AsiMode.Plus2 ? 2 : Asi == AsiMode.Plus1x2 ? 1 : 0) * AsiPicks.Count(a => a == ability));

    private int ConMod => AbilityNames.Modifier(Score(AbilityNames.Con));

    public int HpAverage => Math.Max(1, HitDie / 2 + 1 + ConMod);

    // Increase in hit points: dice (average or roll) + Constitution; The Constitution modifier went up -
    // another +difference for each level (5e rule).
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
        if (HpRoll == true) return;   // the throw is made - not replayed
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

    // Selection of characteristics for improvement: +2 - one (replacement), +1/+1 - two different ones; at maximum - it is impossible.
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

    // Selection groups currently visible: Traits - only if trait is selected instead of enhancement.
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

    // There is something to add to the hero sheet besides engine numbers: skills, choices, recalculation from the skill bonus or characteristics.
    public bool NeedsMaster => Gains.Count > 0 || VisibleChoices.Any() || AsiPicks.Count > 0
                               || Rules.ProficiencyAt(To) != Rules.ProficiencyAt(From);

    // ── Wizard answer with options ───────────────────── ─────────────────────

    // {"gains":[{name,description}], "choices":[{id,title,count,options:[{name,description}]}]}. Empty groups
    // and repetition of traits (the code itself gives their choice) are discarded. false - I don’t understand.
    public bool LoadPlan(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var root = JsonNode.Parse(json) as JsonObject;
            if (root == null) return false;
            // What the hero already has (a skill of the previous level) is not “received”; resource growth - received.
            var has = Hero.Abilities.Select(a => a.Name).Concat((Hero.Spells ?? []).Select(sp => sp.Name))
                .Select(n => n?.Trim().ToLowerInvariant()).ToHashSet();
            Gains = root["gains"]?.Deserialize<List<Option>>(options)?
                .Where(g => g.Name.Trim().Length > 0 && (g.Resource != null || g.Stat != null || !has.Contains(g.Name.Trim().ToLowerInvariant())
                    || !string.IsNullOrWhiteSpace(g.Value) && Hero.Abilities.Any(a => a.Deleted != true
                        && string.Equals(a.Name?.Trim(), g.Name.Trim(), StringComparison.OrdinalIgnoreCase))))
                .ToList() ?? [];
            Choices = root["choices"]?.Deserialize<List<Choice>>(options)?
                .Where(c => c.Options is { Count: > 0 } && c.Count > 0)
                .ToList() ?? [];
            // Traits - only at the stat enhancement level.
            if (!IsAsi) Choices.RemoveAll(c => c.Id == FeatId);
            for (int i = 0; i < Choices.Count; i++)
                if (string.IsNullOrWhiteSpace(Choices[i].Id)) Choices[i].Id = $"choice{i}";
            PlanLoaded = true;
            return true;
        }
        catch { return false; }
    }

    // ── Requests to the GM ────────────────────────── ───────────────────────────

    public string ClassName => ClassLevel.Split(Hero.Stat(StatKeys.Class)?.Value).cls;

    private string HeroSheet() => new AiContextBuilder(new WorldState { Hero = Hero })
        .Serialize("hero.stats", "hero.skills", "hero.resources", "hero.abilities", "hero.spells");

    public string OptionsRequest() =>
        $"Level up: {ClassName} {From} → {To}. HP, proficiency bonus ({AbilityNames.Signed(Rules.ProficiencyAt(To))})"
        + (IsAsi ? " and ability score improvements are handled by the engine; add a `feat` group with eligible feats as an alternative to the improvement." : " — handled by the engine.")
        + $"\nHero:\n{HeroSheet()}";

    // Selected by the player - to the GM for the patch.
    public string ApplyRequest()
    {
        var lines = new List<string>
        {
            $"Level up: {ClassName} {From} → {To}. Proficiency bonus: {AbilityNames.Signed(Rules.ProficiencyAt(From))} → {AbilityNames.Signed(Rules.ProficiencyAt(To))}.",
        };
        if (Gains.Count > 0) lines.Add("Gains: " + string.Join("; ", Gains.Select(OptionText)));
        foreach (var c in VisibleChoices)
            lines.Add($"Selected ({c.Title}): " + string.Join("; ", c.Picked.Order().Select(i => OptionText(c.Options[i]))));
        if (Asi is AsiMode.Plus2 or AsiMode.Plus1x2)
            lines.Add("Ability score improvement (engine sets values): " + string.Join(", ", AsiPicks.Distinct()
                .Select(a => $"{AbilityNames.Find(Hero, a)?.Name ?? a} {Score(a)} → {NewScore(a)}")));
        lines.Add($"Hero (before leveling):\n{HeroSheet()}");
        return string.Join("\n", lines);
    }

    private static string OptionText(Option option) => $"{option.Name} — {option.Description}"
        + (option.Value is { Length: > 0 } value ? $" (value: {value})" : "");

    // ── Summary ──────────────────────────────── ────────────────────────────────

    private static readonly string[] AllowedHeroKeys = ["stats", "skills", "abilities", "resources", "spells", "actions", "speedmax"];

    // New hero: copy of the current one + allowed part of the GM patch + engine numbers. The original hero does not change.
    public Hero Apply(string? masterPatchJson)
    {
        var scratch = new Storage();
        // Applying the patch sets the world language according to its state (L.SetWorld) - for the copy it is the same as for the game.
        scratch.WorldState.Language = L.World;
        scratch.WorldState.Hero =JsonSerializer.Deserialize<Hero>(JsonSerializer.Serialize(Hero, CopyJson), CopyJson);
        if (masterPatchJson != null && JsonNode.Parse(masterPatchJson) is JsonObject root
            && root.FirstOrDefault(p => p.Key.Equals("hero", StringComparison.OrdinalIgnoreCase)).Value is JsonObject patch)
        {
            var allowed = new JsonObject();
            foreach (var (key, value) in patch.ToList())
                if (AllowedHeroKeys.Contains(key.ToLowerInvariant())) allowed[key] = value?.DeepClone();
            scratch.ApplyUpdateWorldState(new JsonObject { ["hero"] = allowed }.ToJsonString());
        }

        var hero = scratch.WorldState.Hero!;
        // The color of the skill/spell, if the GM did not send it, is “utilitarian”, like the others (otherwise the line is missing).
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
        hero.LevelUpDraft = null;   // level received - no draft needed
        LimitPrepared(hero);
        return hero;
    }

    // Prepared spells (not spells) - no more than the class maximum: new ones (learned at this level) become
    // redundant; the player will prepare them himself. Classes “known” (without the “Prepared” stat) are not
    // affected.
    private void LimitPrepared(Hero hero)
    {
        if (!int.TryParse(hero.Stat(StatKeys.SpellsPrepared)?.Value?.Trim(), out int max)) return;
        var old = (Hero.Spells ?? []).Where(sp => sp.Deleted != true).Select(sp => sp.Name).ToHashSet();
        var prepared = (hero.Spells ?? []).Where(sp => sp.Deleted != true && sp.Level > 0 && sp.Prepared == true)
            .OrderBy(sp => old.Contains(sp.Name) ? 0 : 1).ToList();
        foreach (var sp in prepared.Skip(max)) sp.Prepared = false;
    }

    // Preview for the hero card before “Accept” - without a AI: engine numbers received and selected - by
    // skills, spells in the reference book - by spells (skills and saving throws will be recalculated by the GM
    // at "Accept").
    public Hero Preview()
    {
        var hero = Apply(null);
        var known = hero.Abilities.Select(a => a.Name).Concat((hero.Spells ?? []).Select(sp => sp.Name)).ToHashSet();
        foreach (var option in Gains.Concat(VisibleChoices.SelectMany(c => c.Picked.Order().Select(i => c.Options[i]))))
        {
            // Changing a stat - a new value in its line (by name or key), not a skill.
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
            // Existing ability growth uses Value for its complete new description.
            if (option.Resource == null && !string.IsNullOrWhiteSpace(option.Value)
                && hero.Abilities.FirstOrDefault(a => a.Deleted != true
                    && string.Equals(a.Name?.Trim(), option.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } ability)
            {
                ability.Description = option.Value;
                continue;
            }
            // Resource growth - new value in its line (new resource - new line), not a skill.
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

    // ── Your own version (player request) ───────────────────────────────────────

    public string CustomRequest(string request) => OptionsRequest()
        + $"\nPlayer request: {request.Trim()}\nChoices at this level: "
        + string.Join("; ", Choices.Select(c => $"{c.Id} — {c.Title} ({string.Join(", ", c.Options.Select(o => o.Name))})"));

    // The GM's response to the request: {"choices":[{id,title,count,options:[...]}]} - options are added to the
    // group with the same id (new group - entirely), or {"refusal":"..."}. Returns the refusal text; null -
    // option added.
    public string? MergeCustom(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject;
            if (root?["refusal"]?.GetValue<string>() is { Length: > 0 } refusal) return refusal;
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var groups = root?["choices"]?.Deserialize<List<Choice>>(options)?.Where(c => c.Options is { Count: > 0 }).ToList() ?? [];
            if (groups.Count == 0) return L.T("The DM did not offer an option.");
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
        catch { return L.T("The DM did not answer — try again."); }
    }

    private static readonly int[] UtilityColor = [220, 220, 220];

    private static readonly JsonSerializerOptions CopyJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
