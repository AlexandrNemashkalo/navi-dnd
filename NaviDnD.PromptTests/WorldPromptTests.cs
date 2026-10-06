using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using Xunit;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// Карта мира с реальной нейронкой (дорого — запускать осознанно, как и остальные тесты проекта):
// StartNewGame на свежем мире (концепция мира/королевств, стартовое место через add_place, локация по клетке
// мира), путешествие (TravelService + GameAiClient.Travel), вопрос о руинах (add_place по ходу игры).
// Миры — в NaviDnD.PromptTests/Storage/Worlds (AppConfig.ProjectRoot этого проекта), боевые не трогаются.
public class WorldPromptTests(ITestOutputHelper output)
{
    // GameAiClient читает промпты от AppConfig.ProjectRoot (у этого проекта — NaviDnD.PromptTests) — на время
    // теста кладём туда копию боевых промптов.
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

    // Мастер новой игры, шаг «Мир» → «ПРИДУМАТЬ»: концепция нового мира (название, суть, все королевства со столицами).
    [Fact]
    public async Task CreateWorld_GivesConceptForEveryKingdom()
    {
        string prompts = CopyPrompts();
        try
        {
            var world = WorldGenerator.Generate(new WorldGenerator.Options(5, WorldSizes.Large));
            var storage = new Storage();
            var config = PromptScenario.TestConfig();
            var client = new GameAiClient(storage.WorldState, storage, new SwitchableAiProvider(config), config);
            bool ok = await client.CreateWorld(world, "суровый север, викинги и древние руины на юге");
            var chron = world.Chronicle;
            output.WriteLine($"{chron.Name}: {chron.Description} | {chron.DmNotes}");
            foreach (var k in chron.Kingdoms)
                output.WriteLine($"  K{k.Id} {k.Name} (столица {chron.Places.FirstOrDefault(p => p.Slot == world.Kingdoms[k.Id].Capital)?.Name}): {k.Description} | {k.DmNotes}");
            Assert.True(ok, "CreateWorld не применился");
            Assert.False(string.IsNullOrEmpty(chron.Name));
            Assert.Equal(world.Kingdoms.Count, chron.Kingdoms.Count);
            Assert.Equal(world.Kingdoms.Count, chron.Places.Count(p => p.Type == WorldPlaceTypes.Capital));
        }
        finally { try { Directory.Delete(prompts, recursive: true); } catch { } }
    }

    // Новая игра героем из библиотеки: уровень/статы/заклинания те же, ХП полные, эффекты сняты, снаряжение — новое.
    [Fact]
    public async Task ReuseHero_KeepsLevelAndSkills_RefitsGear()
    {
        string prompts = CopyPrompts();
        try
        {
            var source = PromptScenario.LoadUiTestFixture("full_game.json").WorldState.Hero!;
            string heroJson = System.Text.Json.JsonSerializer.Serialize(source, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            var storage = new Storage();
            var config = PromptScenario.TestConfig();
            var client = new GameAiClient(storage.WorldState, storage, new SwitchableAiProvider(config), config);
            await client.ReuseHero(new NewGameData { Name = source.Name, Symbol = source.Symbol ?? "HRO", Description = "", Setting = "DnD 5e", SettingWish = "" }, heroJson);
            var hero = storage.WorldState.Hero!;
            string Stat(Data.Models.Hero h, string n) => h.Stats?.FirstOrDefault(s => s.Name == n)?.Value?.ToString() ?? "";
            output.WriteLine($"было: {Stat(source, "Класс")}, HP {source.Hp}, предметы: {string.Join(", ", source.Inventory?.Select(i => i.Name) ?? [])}");
            output.WriteLine($"стало: {Stat(hero, "Класс")}, HP {hero.Hp}, предметы: {string.Join(", ", hero.Inventory?.Where(i => i.Deleted != true).Select(i => i.Name) ?? [])}");
            Assert.Equal(Stat(source, "Класс"), Stat(hero, "Класс"));
            Assert.Equal(Stat(source, "Сила"), Stat(hero, "Сила"));
            Assert.Equal(source.Spells?.Count ?? 0, hero.Spells?.Count ?? 0);
            Assert.Empty(hero.Effects ?? []);
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
        var client = new GameAiClient(storage.WorldState, storage, new SwitchableAiProvider(config), config);
        var ws = storage.WorldState;

        // 1. Начало игры.
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

        // 2. Путешествие в столицу.
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

        // 3. Слух о месте — мастер добавляет его на карту.
        int placesBefore = geo.Chronicle.Places.Count;
        historyBefore = ws.History!.Count;
        await client.SendAction("Спрашиваю у первого встречного, где ближайшая деревня или городок, куда он сам держит путь, — как она называется и далеко ли.");
        foreach (var m in ws.History.Skip(historyBefore)) output.WriteLine($"  > {m.Author}: {m.Text}");
        foreach (var p in geo.Chronicle.Places.Skip(placesBefore)) output.WriteLine($"  новое место {p.Name} ({p.Type}) [{p.X},{p.Y}] trail={p.Trail?.Count / 2}: {p.Description} | {p.DmNotes}");
        output.WriteLine($"Знает: {string.Join(", ", ws.World!.Known)}");
        Assert.True(geo.Chronicle.Places.Count > placesBefore, "мастер не добавил место на карту (add_place)");
    }
}
