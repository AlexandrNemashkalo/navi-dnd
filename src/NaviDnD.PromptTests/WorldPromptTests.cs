using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Xunit;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// World map with a real AI (expensive - run consciously, like other tests of the project): StartNewGame on a
// test world (world/kingdom concept, starting place through add_place, location by world cell), travel
// (TravelService + GameAiClient.Travel), question about ruins (add_place during the game). Worlds - in
// NaviDnD.PromptTests/Storage/Worlds (AppConfig.ProjectRoot of this project), the combat ones are not touched.
public class WorldPromptTests(ITestOutputHelper output)
{
    // GameAiClient reads prompts from AppConfig.ProjectRoot (for this project - NaviDnD.PromptTests) - for the
    // duration of the test we put a copy of the combat prompts there.
    private static string CopyPrompts()
    {
        string src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NaviDnD", "Prompts"));
        string dst = Path.Combine(AppConfig.ProjectRoot, "Prompts");
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(dst, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
        return dst;
    }

    [Fact]
    public async Task StartTravelAndAddPlace_WorldGrowsWithTheGame()
    {
        string prompts = CopyPrompts();
        try { await Scenario(); }
        finally { try { Directory.Delete(prompts, recursive: true); } catch { } }
    }

    // New Game GM, step “World” → “IMAGE”: concept of a new world (name, essence, all kingdoms with capitals).
    [Theory]
    [InlineData("суровый север, викинги и древние руины на юге")]
    [InlineData("Индустриальный прибрежный регион без магии: пароходы, железные дороги, портовые города. Приключение об исследовании, торговле и местных тайнах; без конца света и обязательного злодея. Политические регионы могут быть республиками или союзами городов.")]
    public async Task CreateWorld_GivesConceptForEveryKingdom(string settingWish)
    {
        string prompts = CopyPrompts();
        try
        {
            var world = WorldGenerator.Generate(new WorldGenerator.Options(5, WorldSizes.Large));
            var storage = new Storage();
            var config = PromptScenario.TestConfig();
            var client = new GameAiClient(storage.WorldState, storage, new SwitchableAiProvider(config, new AiLogger()), config);
            bool ok = await client.CreateWorld(world, settingWish);
            var chron = world.Chronicle;
            output.WriteLine($"{chron.Name}: {chron.Description} | {chron.DmNotes}");
            foreach (var k in chron.Kingdoms)
                output.WriteLine($"  K{k.Id} {k.Name} (столица {chron.Places.FirstOrDefault(p => p.Slot == world.Kingdoms[k.Id].Capital)?.Name}): {k.Description} | {k.DmNotes}");
            Assert.True(ok, "CreateWorld не применился");
            Assert.False(string.IsNullOrEmpty(chron.Name));
            Assert.False(string.IsNullOrWhiteSpace(chron.DmNotes));
            Assert.All(chron.Kingdoms, k =>
            {
                Assert.False(string.IsNullOrWhiteSpace(k.Description));
                Assert.False(string.IsNullOrWhiteSpace(k.DmNotes));
            });
            Assert.Equal(world.Kingdoms.Count, chron.Kingdoms.Count);
            Assert.Equal(world.Kingdoms.Count, chron.Places.Count(p => p.Type == WorldPlaceTypes.Capital));
        }
        finally { try { Directory.Delete(prompts, recursive: true); } catch { } }
    }

    // New game with a hero from the library: the same level/stats/spells, full HP, effects removed, new equipment.
    [Fact]
    public async Task ReuseHero_KeepsLevelAndSkills_RefitsGear()
    {
        string prompts = CopyPrompts();
        try
        {
            var source = PromptScenario.LoadUiTestFixture("full_game.json").WorldState.Hero!;
            source.Level = 5;
            source.Xp = 6500;
            source.Hp = "3/44";
            source.Stats.Add(new() { Name = "Класс", Key = StatKeys.Class, Value = "Воин (Чемпион)" });
            source.Effects = [new() { Name = "Схвачен" }];
            source.SpeedMax = 0;
            source.SpeedLeft = 0;
            source.Dead = true;
            source.NoOpportunityAttacksRound = 500;
            string heroJson = System.Text.Json.JsonSerializer.Serialize(source, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            var storage = new Storage();
            var config = PromptScenario.TestConfig();
            var client = new GameAiClient(storage.WorldState, storage, new SwitchableAiProvider(config, new AiLogger()), config);
            await client.ReuseHero(new NewGameData { Name = source.Name, Symbol = source.Symbol ?? "HRO", Description = "", Setting = "DnD 5e", SettingWish = "" }, heroJson);
            var hero = storage.WorldState.Hero!;
            string Stat(Data.Models.Hero h, string n) => h.Stats?.FirstOrDefault(s => s.Name == n)?.Value?.ToString() ?? "";
            output.WriteLine($"было: {Stat(source, "Класс")}, HP {source.Hp}, предметы: {string.Join(", ", source.Inventory?.Select(i => i.Name) ?? [])}");
            output.WriteLine($"стало: {Stat(hero, "Класс")}, HP {hero.Hp}, предметы: {string.Join(", ", hero.Inventory?.Where(i => i.Deleted != true).Select(i => i.Name) ?? [])}");
            Assert.Equal(Stat(source, "Класс"), Stat(hero, "Класс"));
            Assert.Equal(Stat(source, "Сила"), Stat(hero, "Сила"));
            Assert.Equal(source.Spells?.Count ?? 0, hero.Spells?.Count ?? 0);
            Assert.Empty(hero.Effects ?? []);
            Assert.Equal(5, hero.Level);
            Assert.Equal(6500, hero.Xp);
            Assert.Equal("44/44", hero.Hp);
            Assert.NotEqual(true, hero.Dead);
            Assert.Null(hero.NoOpportunityAttacksRound);
            Assert.Equal(30, hero.SpeedMax);
            Assert.Equal(hero.SpeedMax, hero.SpeedLeft);
        }
        finally { try { Directory.Delete(prompts, recursive: true); } catch { } }
    }

    private async Task Scenario()
    {
        var storage = PromptScenario.LoadUiTestFixture("full_game.json");
        storage.NewGameData = new NewGameData
        {
            Name = storage.WorldState.Hero!.Name, Symbol = storage.WorldState.Hero.Symbol ?? "HRO", Description = "",
            Setting = "DnD 5e", SettingWish = "Небольшое приключение: деревня у леса, рядом старые руины, мрачноватый тон.",
            WorldSize = WorldSizes.Small,
        };
        var config = PromptScenario.TestConfig();
        var client = new GameAiClient(storage.WorldState, storage, new SwitchableAiProvider(config, new AiLogger()), config);
        var ws = storage.WorldState;

        // 1. Start of the game.
        await client.StartNewGame("");
        var geo = GameWorld.Geo(ws)!;
        output.WriteLine($"Мир: {geo.Chronicle.Name} — {geo.Chronicle.Description}");
        foreach (var k in geo.Chronicle.Kingdoms) output.WriteLine($"  K{k.Id} {k.Name}: {k.Description}");
        foreach (var p in geo.Chronicle.Places) output.WriteLine($"  место {p.Name} ({p.Type}) [{p.X},{p.Y}] slot {p.Slot}: {p.Description} | {p.DmNotes}");
        output.WriteLine($"Герой: place={ws.World?.Place} tile={GameWorld.HeroTile(ws)} known={string.Join(",", ws.World?.Known ?? [])}");
        output.WriteLine($"Локация: {(GameWorld.HasLocation(ws) ? string.Join(",", ws.Map.Chunks?.Select(c => c.Theme) ?? []) : "нет")}");
        if (GameWorld.HeroTile(ws) is { } ht) output.WriteLine($"Клетка мира: {WorldBiomes.Get(geo.BiomeAt(ht.x, ht.y)).Name}, сайт {GameWorld.SiteAt(geo, ht.x, ht.y)}");
        foreach (var m in ws.History ?? []) output.WriteLine($"  > {m.Author}: {m.Text}");
        Assert.False(string.IsNullOrEmpty(geo.Chronicle.Description), "мир без концепции");
        Assert.NotEmpty(geo.Chronicle.Kingdoms);
        Assert.NotNull(ws.World?.Place);
        Assert.NotNull(GameWorld.HeroTile(ws));

        // 2. Travel to the capital.
        var world = GameWorld.ForMaster(ws)!;
        var capital = world.Places.First(p => p.Type == WorldPlaceTypes.Capital);
        var plan = TravelService.PlanTo(ws, (capital.X, capital.Y));
        Assert.NotNull(plan);
        int historyBefore = ws.History?.Count ?? 0;
        string report = TravelService.Go(ws, plan!, TravelService.Pace.Normal, new Random(11));
        output.WriteLine(report);
        await client.Travel(report);
        output.WriteLine($"После пути: place={ws.World?.Place} tile={GameWorld.HeroTile(ws)} day={ws.Time.Day} {ws.Time.PartOfDay} loc={GameWorld.HasLocation(ws)}");
        foreach (var m in (ws.History ?? []).Skip(historyBefore)) output.WriteLine($"  > {m.Author}: {m.Text}");
        Assert.True((ws.History?.Count ?? 0) > historyBefore, "мастер не описал путь");
        Assert.DoesNotContain(ws.History!.Skip(historyBefore), m => m.Text?.StartsWith("Ошибка") == true);

        // 3. Rumor about a place - the GM adds it to the map.
        int placesBefore = geo.Chronicle.Places.Count;
        var knownBefore = (ws.World?.Known ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string familiarPlaces = string.Join(", ", GameWorld.ForDisplay(ws).Places.Select(p => p.Name));
        historyBefore = ws.History!.Count;
        await client.SendAction($"Спрашиваю у первого встречного о небольшой деревне или городке, которого я ещё не знаю. Мне уже известны: {familiarPlaces}. Хочу узнать название другого места, что там интересного и как туда добраться; никаких поручений пока не принимаю.");
        foreach (var m in ws.History.Skip(historyBefore)) output.WriteLine($"  > {m.Author}: {m.Text}");
        foreach (var p in geo.Chronicle.Places.Skip(placesBefore)) output.WriteLine($"  новое место {p.Name} ({p.Type}) [{p.X},{p.Y}] trail={p.Trail?.Count / 2}: {p.Description} | {p.DmNotes}");
        output.WriteLine($"Знает: {string.Join(", ", ws.World!.Known)}");
        var discovered = (ws.World?.Known ?? []).Where(name => !knownBefore.Contains(name)).ToList();
        Assert.True(geo.Chronicle.Places.Count > placesBefore || discovered.Count > 0,
            "Мастер должен добавить место или раскрыть существующее неизвестное герою место.");
        var afterWorld = GameWorld.ForMaster(ws)!;
        Assert.All(discovered, name => Assert.NotNull(WorldAtlas.FindPlace(afterWorld, name)));
    }
}
