using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace NaviDnD.Clients;

public class GameAiClient
{
    private readonly WorldState _settings;
    private readonly Storage _storage;
    private readonly IAiProvider _provider;
    private readonly string _ruleSet;
    private readonly AppConfig _appConfig;
    public DialogDisplay? ActiveDialog { get; set; }

    private static readonly string _rollRequestPath =
        Path.Combine(AppConfig.ProjectRoot, "Storage", "roll_request.json");

    private static readonly string _askPlayerRequestPath =
        Path.Combine(AppConfig.ProjectRoot, "Storage", "ask_player_request.json");

    private static readonly string _selectTargetRequestPath =
        Path.Combine(AppConfig.ProjectRoot, "Storage", "select_target_request.json");

    private static readonly JsonSerializerOptions _rollJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly JsonSerializerOptions _serializeOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public GameAiClient(WorldState settings, Storage storage, IAiProvider provider, AppConfig config)
    {
        _settings = settings;
        _storage = storage;
        _provider = provider;
        _ruleSet = config.RuleSet;
        _appConfig = config;
    }

    // Правила игры (папка промптов и данных правил — Prompts/{RuleSet}).
    public string RuleSet => _ruleSet;

    public async Task CreateNewGame(NewGameData newGameData)
    {
        _storage.ResetWorldState();
        // MCP-сервер требует существующий файл состояния при старте (см. Program.cs) — до этого
        // CreateNewGame шёл вообще без MCP (allowedTools:null), теперь ей нужен find_icon.
        _storage.SaveDraft();

        string input = JsonSerializer.Serialize(newGameData, _serializeOptions);
        string userMessage = $"Пользовательские пожелания к своему персонажу и правилам:\n{input}";
        await CallAction(userMessage, nameof(CreateNewGame), _appConfig.ClaudeCreateNewGameModel);

        // Выбранное игроком в анкете — как есть, поверх ответа нейронки.
        if (_settings.Hero is { } hero)
        {
            hero.VisionFt = null;   // обзор героя — по свету (движок); нейронка ставила «Зрение 5 фт»
            if (!string.IsNullOrWhiteSpace(newGameData.Image)) hero.Image = newGameData.Image;
            if (newGameData.Color is { Count: 3 }) hero.Color = [.. newGameData.Color];
            if (!string.IsNullOrWhiteSpace(newGameData.Symbol)) hero.Symbol = newGameData.Symbol.Trim();
        }
    }

    // Анкета новой игры: нейронка подбирает портреты героя по имени и описанию (find_icon) — они идут
    // в начале галереи. Ошибка/пустой ответ — пустой список (галерея остаётся из готового набора).
    // Новая игра героем из библиотеки: герой как был (уровень, статы, навыки, заклинания, способности), личность —
    // из анкеты; состояние прошлой игры сбрасывает код (ХП полные, эффекты, вдохновение, место), снаряжение и
    // деньги под новое приключение выдаёт нейронка одним коротким вызовом (FixHeroForNewGame).
    public async Task ReuseHero(NewGameData data, string heroJson)
    {
        _storage.ResetWorldState();
        _storage.ApplyUpdateWorldState("{\"hero\":" + heroJson + "}");
        if (_settings.Hero is not { } hero) return;
        if (hero.Hp is { } hp && hp.Split('/') is [_, var max] && max.Trim().Length > 0) hero.Hp = $"{max.Trim()}/{max.Trim()}";
        hero.Effects = [];
        hero.Inspiration = null;
        hero.Stealth = null;
        hero.Position = null;
        hero.VisionFt = null;   // обзор героя — по свету (движок)
        if (!string.IsNullOrWhiteSpace(data.Name)) hero.Name = data.Name.Trim();
        if (!string.IsNullOrWhiteSpace(data.Symbol)) hero.Symbol = data.Symbol.Trim();
        if (!string.IsNullOrWhiteSpace(data.Image)) hero.Image = data.Image;
        if (data.Color is { Count: 3 }) hero.Color = [.. data.Color];
        await FixHeroForNewGame(
            "Этот герой из прошлой игры начинает новое приключение. Уровень, характеристики, навыки, заклинания, " +
            "способности, раса и класс — НЕ меняй. Замени снаряжение и деньги на обычные стартовые для его класса и " +
            "уровня (убери уникальные/сюжетные предметы прошлой игры), ресурсы и ячейки заклинаний — полные." +
            (string.IsNullOrWhiteSpace(data.Description) ? "" : $" Описание героя игрок уточнил: {data.Description}"));
    }

    // Анкета героя: описание (внешность, характер, предыстория — не лист персонажа) — придумать по имени/расе/классу
    // или доработать текущее по запросу игрока. null — не вышло.
    public async Task<string?> DescribeHero(string name, string race, string cls, string? current, string? request, string? extra = null)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(DescribeHero));
        try
        {
            string userMessage = $"Имя: {name}\nРаса: {race}\nКласс: {cls}" + (string.IsNullOrWhiteSpace(extra) ? "" : $"\n{extra.Trim()}")
                                 + (string.IsNullOrWhiteSpace(current) ? "" : $"\nТекущее описание (от игрока):\n{current.Trim()}")
                                 + (string.IsNullOrWhiteSpace(request) ? "" : $"\nЗапрос игрока: {request.Trim()}");
            string response = await CompleteConfigured([ReadPrompt(actionPath)], userMessage, actionPath, _appConfig.ClaudeCreateNewGameModel);
            string? text = JsonNode.Parse(ExtractJson(response))?["description"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (AiSetupException) { throw; }
        catch
        {
            return null;
        }
    }

    public async Task FixHeroForNewGame(string request, DialogDisplay? dialog = null)
    {
        _storage.SaveDraft();
        var ctx = new AiContextBuilder(_settings);
        string userMessage = ctx.HeroMinimalState(request);
        if (dialog == null)
        {
            await CallAction(userMessage, nameof(FixHeroForNewGame), _appConfig.ClaudeModel);
            return;
        }

        // Как SendAction: history печатается посимвольно (со звуком) через SequentialHistoryPlayer,
        // а не появляется целиком одним ApplyUpdateWorldState, как в CallAction.
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(FixHeroForNewGame));
        string response = "";
        try
        {
            response = await CompleteWithSpinner([ReadPrompt(actionPath)], userMessage, actionPath, _appConfig.ClaudeModel, dialog.TickSpinner);
            await PlayResponse(ExtractJson(response), dialog.AppendStreamChunk, dialog.NewStreamingMessage, onRedraw: null);
        }
        catch (Exception ex)
        {
            RecordError(ex, response);
        }
    }

    // Анкета новой игры, шаг «Мир»: концепция нового мира (название, суть, королевства со столицами) — в его
    // хронику, чтобы игрок увидел мир до начала. Мест не создаёт — их мастер добавит по ходу игры.
    // description — текущее описание (игрок написал/поправил): нейронка дорабатывает его по запросу, а не пишет заново.
    public async Task<bool> CreateWorld(WorldMap world, string? wish, string? name = null, string? description = null)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(CreateWorld));
        try
        {
            string userMessage = WorldAtlas.TerrainSummary(world)
                                 + (string.IsNullOrWhiteSpace(name) ? "" : $"\nНазвание мира задано игроком: «{name.Trim()}» — оставь его (name).")
                                 + (string.IsNullOrWhiteSpace(description) ? "" : $"\nТекущее описание мира (от игрока) — доработай по запросу, сохрани то, что не просят менять:\n{description.Trim()}")
                                 + (string.IsNullOrWhiteSpace(wish) ? "" : $"\nЗапрос игрока: {wish}");
            string response = await CompleteConfigured([ReadPrompt(actionPath)], userMessage, actionPath, _appConfig.ClaudeStartNewGameModel);
            using var doc = JsonDocument.Parse(ExtractJson(response));
            bool ok = GameWorld.ApplyChronicle(world, doc.RootElement);
            if (ok && !string.IsNullOrWhiteSpace(name)) world.Chronicle.Name = name.Trim();
            return ok;
        }
        catch (AiSetupException) { throw; }
        catch
        {
            return false;
        }
    }

    public async Task StartNewGame(string _)
    {
        // Нейронка по сюжету планирует локацию инструментом plan_location (сколько блоков, где, что в каждом);
        // геометрию по плану строит код (LocationGrower.CreatePlanned → ChunkGenerator), инструмент отдаёт
        // стартовый блок — его нейронка наполняет в этом же ответе, остальные — PopulateChunk при подходе.
        _settings.Map.Chunks = null;

        // Мир игры: код дал только землю (рельеф, королевства, тракты); концепцию мира и королевств, стартовое
        // место и известные герою места нейронка задаёт в этом же ответе (world, add_place).
        // Мир из мастера новой игры (выбран или создан на шаге «Мир»; хроника продолжается).
        if (_storage.NewGameData?.WorldId is { } wid) WorldLibrary.SetActive(wid);
        // Кто герой (первая фраза описания из анкеты: «худощавая дроу…») — в контекст каждого хода (hero.aiInfo):
        // без неё мастер обращался к героине в мужском роде. Описание целиком в сохранение не попадает.
        if (_settings.Hero is { AiInfo: null or { Count: 0 } } h && _storage.NewGameData?.Description is { Length: > 0 } desc)
        {
            string first = desc.Trim();
            int dot = first.IndexOfAny(['.', '!', '?']);
            if (dot > 0) first = first[..(dot + 1)];
            h.AiInfo = [first.Length > 220 ? first[..220] + "…" : first];
        }
        var world = WorldLibrary.Current;
        _settings.World = new GameWorldLink { Id = world.Id };
        // Мир уже описан в прошлых играх (хроника) — та же концепция и места; новый — только земля.
        // Королевства уже описаны (прошлые игры или «ПРИДУМАТЬ» в анкете) — концепцию не менять; есть только
        // название/описание от игрока — взять их и придумать королевства.
        bool known = world.Chronicle.Kingdoms.Count > 0;
        string playerConcept = world.Chronicle.Name is { Length: > 0 } || world.Chronicle.Description is { Length: > 0 }
            ? $"\nМир задал игрок — название «{world.Chronicle.Name ?? world.Name}»{(world.Chronicle.Description is { Length: > 0 } pd ? $", описание: {pd}" : "")}. Используй их (name/description не меняй), королевства придумай в духе описания."
            : "";
        string worldBrief = known
            ? "\n\nМир уже описан — НЕ меняй концепцию, королевства и места; новое — add_place. Хроника:\n"
              + WorldAtlas.Overview(GameWorld.Compose(world, _settings.World, includeHidden: true), withDmNotes: true)
              + "\nЗемля:\n" + WorldAtlas.TerrainSummary(world)
            : "\n\nЗемля мира (концепция — в world, места — add_place):\n" + WorldAtlas.TerrainSummary(world) + playerConcept;
        // Параметры приключения (шаг «Приключение»): жанр, упор, длина, сложность, темп.
        if (_storage.NewGameData?.AdventureStyle is { Length: > 0 } style)
            worldBrief = $"\nПараметры приключения: {style}" + worldBrief;
        worldBrief += $"\nСейчас: день {_settings.Time.Day}, {_settings.Time.PartOfDay.ToLowerInvariant()} — описывай сцену в это время суток или задай другое в time.";
        // Место старта, выбранное игроком на карте (шаг «Приключение»).
        if (_storage.NewGameData?.StartPlace is { Length: > 0 } sp)
            worldBrief += $"\nИгрок выбрал стартовое место: «{sp}» — начни там (world.place).";
        else if (_storage.NewGameData is { StartX: int sx, StartY: int sy })
            worldBrief += $"\nИгрок выбрал старт на карте: клетка [{sx},{sy}] — {WorldBiomes.Get(world.BiomeAt(sx, sy)).Name.ToLowerInvariant()}, {WorldAtlas.Surroundings(world, sx, sy, named: true)}; стартовое место — add_place рядом (near не нужен: укажи kingdom и terrain по этой клетке) или ровно там.";

        // Сохраняем героя и карту в файл-черновик до запуска AI — MCP-инструменты читают этот файл.
        // Боевой worldState.json не трогаем, пока мир не создан успешно.
        _storage.SaveDraft();

        string userMessage;
        if (_ruleSet == "Dnd5e")
        {
            userMessage = $"Данные героя: символ: {_settings.Hero?.Symbol}, имя: {_settings.Hero?.Name}, пассивное восприятие: {GetPassivePerception()}" +
                          $"\n\nПожелания к игре: {_storage.NewGameData?.SettingWish}" + worldBrief;
        }
        else
        {
            userMessage = $"Данные героя: символ: {_settings.Hero?.Symbol}, имя: {_settings.Hero?.Name}" +
                          $"\n\nПожелания к игре: {_storage.NewGameData?.SettingWish}" + worldBrief;
        }

        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(StartNewGame));
        string systemPrompt = ReadPrompt(actionPath);
        string response = "";
        try
        {
            response = await CompleteConfigured([systemPrompt], userMessage, actionPath, _appConfig.ClaudeStartNewGameModel);

            // MCP-инструменты могли дописать черновик — перечитываем, чтобы ничего не потерять.
            _storage.LoadDraft();
            // Метка plan_location нужна только посреди игры — здесь карта уже взята из черновика.
            try { File.Delete(Path.Combine(Path.GetDirectoryName(Storage.DraftSavePath) ?? ".", Storage.LocationPlannedFlag)); } catch { }

            string json = ExtractJson(response);
            // Ответ старого формата (полная карта с cols/rows/rooms — моки UI-тестов): применяется как
            // раньше, поверх пустой карты, без блочной генерации.
            if (JsonNode.Parse(json)?["map"]?["cols"] != null)
            {
                var map = _settings.Map;
                map.Rooms = []; map.Doors = []; map.Area = []; map.Entities = []; map.Objects = [];
                map.Chunks = null;
            }
            // plan_location не вызван — запасной вариант: один блок, чтобы было где играть.
            else if (_settings.Map.Chunks is not { Count: > 0 })
                LocationGrower.CreateStart(_settings);
            if (_settings.Map.Chunks?.FirstOrDefault(c => c.Exits.Any(e => e.External)) is { } startChunk)
                startChunk.Populated = true;
            // Вступление мастера (history) не выводится сразу: его печатает посимвольно, со звуком, экран игры
            // (PlayIntro — как обычный ответ мастера); остальное — в состояние сразу.
            if (JsonNode.Parse(json) is JsonObject startRoot && startRoot["history"] is JsonArray intro && intro.Count > 0)
            {
                startRoot.Remove("history");
                _pendingIntro = intro.ToJsonString(_serializeOptions);
                json = startRoot.ToJsonString(_serializeOptions);
            }
            _settings.History = [];   // разговор в редакторе героя — не часть игры
            _storage.ApplyUpdateWorldState(json);

            // Мир создан успешно — фиксируем в боевой файл сохранения
            _storage.Save();
            Storage.DeleteDraft();
        }
        catch (Exception ex)
        {
            RecordError(ex, response);
        }
    }

    // Блок локации, к которому подошёл герой (LocationGrower.UnpopulatedNearHero) — нейронка называет комнаты и расставляет
    // существ/объекты. Геометрия уже сгенерирована кодом и лежит в карте.
    public async Task PopulateChunk(
        MapChunk chunk,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(PopulateChunk));
        var ctx = new AiContextBuilder(_settings, _storage.VisibleCells);
        string userMessage = ctx.MinimalState("(новый блок локации — наполни его)") + "\n\n" + ctx.LocationPlan(chunk) + "\n" + ctx.ChunkState(chunk);
        string response = "";
        try
        {
            _storage.Save(); // MCP get_world_state читает сохранение
            response = await CompleteWithSpinner([ReadPrompt(actionPath)], userMessage, actionPath, _appConfig.ClaudeModel, onIdle);
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            chunk.Populated = true;
            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response);
        }
    }

    public async Task SendAction(
        string playerAction,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        Helpers.Speech.Stop();
        string response = "";
        try
        {
            _storage.Save(); // MCP (plan_location, add_place, world_query) читает сохранение
            // Прошлое путешествие мастер не описал (сервер не ответил) — сначала оно, потом действие игрока.
            string pending = _settings.PendingTravel is { Length: > 0 } pt ? pt + "\n\n## Действие игрока после пути\n" : "";
            response = await CompleteWithSpinner(systemBlocks, ctx.MinimalState(pending + playerAction), actionPath, _appConfig.ClaudeModel, onIdle);
            ReloadPlannedLocation();
            _settings.PendingTravel = null;
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response);
        }
    }

    // Путешествие по карте мира (TravelService уже переместил героя и сдвинул время): один вызов мастера —
    // описать дорогу, привалы, припасы, встречу (если выпала) и прибытие; при нужде он строит сцену (plan_location).
    public async Task Travel(
        string travelReport,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        string response = "";
        try
        {
            _storage.Save();
            // Не описанный мастером прошлый путь — вместе с этим (его итог тоже нужен).
            if (_settings.PendingTravel is { Length: > 0 } earlier) travelReport = earlier + "\n\n" + travelReport;
            _settings.PendingTravel = travelReport;   // до ответа мастера путь «не описан»
            response = await CompleteWithSpinner(systemBlocks, ctx.MinimalState(travelReport), actionPath, _appConfig.ClaudeModel, onIdle);
            ReloadPlannedLocation();
            _settings.PendingTravel = null;
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response, "Ошибка в пути");
            _settings.History.Add(new DialogMessage { Text = "Мастер не ответил, но путь пройден: дорогу, еду и встречи он опишет со следующим твоим действием." });
            _storage.Save();
        }
    }

    // Мастер построил локацию посреди игры (MCP plan_location записал её в сохранение и оставил метку) —
    // перечитать карту и место героя, иначе следующий Save затёр бы её состоянием из памяти.
    private void ReloadPlannedLocation()
    {
        string flag = Path.Combine(Path.GetDirectoryName(Storage.SavePath) ?? ".", Storage.LocationPlannedFlag);
        if (!File.Exists(flag)) return;
        try { File.Delete(flag); } catch { }
        try
        {
            _storage.ReloadMapFrom(Storage.SavePath);
            if (_settings.Map.Chunks?.FirstOrDefault(c => c.Exits.Any(e => e.External)) is { } start) start.Populated = true;
        }
        catch { /* карта не перечиталась — играем на прежней */ }
    }

    public async Task FireEnemyTurn(
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        string response = "";
        try
        {
            response = await CompleteWithSpinner(systemBlocks, ctx.CombatTurnState(), actionPath, _appConfig.ClaudeModel, onIdle);
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response, "Ошибка хода врагов");
        }
    }

    public async Task FireRoundTransition(
        RoundBundle bundle,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        string response = "";
        try
        {
            response = await CompleteWithSpinner(systemBlocks, ctx.RoundTransitionState(bundle), actionPath, _appConfig.ClaudeModel, onIdle);
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response, "Ошибка раунда");
        }
    }

    public async Task FireTrigger(
        List<(CellEntity entity, string triggerType)> triggers,
        List<Area> areaTriggers,
        List<(Door door, string triggerType)> doorTriggers,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        var oneShotPatches = BuildOneShotPatches(triggers);
        oneShotPatches.AddRange(BuildAreaOneShotPatches(areaTriggers));
        oneShotPatches.AddRange(BuildDoorOneShotPatches(doorTriggers));

        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        string response = "";
        try
        {
            bool wasInCombat = _settings.Combat?.Active == true;
            response = await CompleteWithSpinner(systemBlocks, ctx.TriggerState(triggers, areaTriggers, doorTriggers), actionPath, _appConfig.ClaudeModel, onIdle);
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            if (!wasInCombat && _settings.Combat?.Active == true && _settings.Hero != null)
            {
                _settings.Hero.SpeedLeft = _settings.Hero.SpeedMax ?? 30;
                _storage.ResetDiagonalCount();
            }

            foreach (var patch in oneShotPatches)
                _storage.ApplyUpdateWorldState(patch);

            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response, "Ошибка триггера");
        }
    }

    // Снимается только сработавший под-ключ (Storage.MergeTriggers) — остальные триггеры того же
    // объекта не трогаем. spotted/opportunity_attack_on_hero — не одноразовые, сюда не попадают.
    private static TriggerEffect? OneShotEffect(CellTriggers? t, string triggerType) => triggerType switch
    {
        "onStep" => t?.OnStep,
        "onVisible" => t?.OnVisible,
        _ => null,
    };

    private static string RemoveTriggerPatch(string listKey, int id, string triggerKey) =>
        $"{{\"map\":{{\"{listKey}\":[{{\"id\":{id},\"triggers\":{{\"{triggerKey}\":null}}}}]}}}}";

    private List<string> BuildOneShotPatches(List<(CellEntity entity, string triggerType)> triggers)
    {
        var patches = new List<string>();
        foreach (var (entity, triggerType) in triggers)
        {
            if (OneShotEffect(entity.Triggers, triggerType)?.Once != true) continue;

            var inEntities = entity is LivingEntity le ? (_settings.Map.Entities?.IndexOf(le) ?? -1) : -1;
            var inObjects = inEntities < 0 ? (_settings.Map.Objects?.IndexOf(entity) ?? -1) : -1;
            int id = inEntities >= 0 ? inEntities : inObjects;
            if (id < 0) continue;

            patches.Add(RemoveTriggerPatch(inEntities >= 0 ? "entities" : "objects", id, triggerType));
        }
        return patches;
    }

    private List<string> BuildAreaOneShotPatches(List<Area> areas)
    {
        var patches = new List<string>();
        foreach (var area in areas)
        {
            bool isRoom = area is Room room && (_settings.Map.Rooms?.Contains(room) == true);
            int id = isRoom
                ? _settings.Map.Rooms!.IndexOf((Room)area)
                : (_settings.Map.Area?.IndexOf(area) ?? -1);
            if (id < 0) continue;

            patches.Add(RemoveTriggerPatch(isRoom ? "rooms" : "area", id, "onExplored"));
        }
        return patches;
    }

    private List<string> BuildDoorOneShotPatches(List<(Door door, string triggerType)> doorTriggers)
    {
        var patches = new List<string>();
        foreach (var (door, triggerType) in doorTriggers)
        {
            if (OneShotEffect(door.Triggers, triggerType)?.Once != true) continue;

            int id = _settings.Map.Doors?.IndexOf(door) ?? -1;
            if (id < 0) continue;

            patches.Add(RemoveTriggerPatch("doors", id, triggerType));
        }
        return patches;
    }

    private async Task CallAction(string userMessage, string actionName, string model, Action? onIdle = null)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, actionName);
        string systemPrompt = ReadPrompt(actionPath);

        string response = "";
        try
        {
            response = await CompleteWithSpinner([systemPrompt], userMessage, actionPath, model, onIdle);
            _storage.ApplyUpdateWorldState(await EnsureValidJson(ExtractJson(response)));
            if (actionName is nameof(StartNewGame) or nameof(SendAction))
                _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response);
        }
    }

    private (string actionPath, IReadOnlyList<string> systemBlocks, AiContextBuilder ctx) BuildSendActionContext()
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(SendAction));
        string instructions = ReadPrompt(actionPath);
        var ctx = new AiContextBuilder(_settings, _storage.VisibleCells);
        return (actionPath, [ctx.Instructions(instructions)], ctx);
    }

    private async Task<string> CompleteWithSpinner(
        IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model, Action? onIdle)
    {
        if (onIdle == null)
            return await CompleteConfigured(systemBlocks, userMessage, actionPath, model);

        using var cts = new CancellationTokenSource();
        var spinTask = SpinAsync(onIdle, this, _rollRequestPath, _askPlayerRequestPath, cts.Token);
        try
        {
            return await CompleteConfigured(systemBlocks, userMessage, actionPath, model);
        }
        finally
        {
            cts.Cancel();
            await spinTask;
        }
    }

    private static async Task SpinAsync(
        Action onIdle,
        GameAiClient client, string rollRequestPath,
        string askPlayerRequestPath,
        CancellationToken ct)
    {
        const int IdleIntervalMs = 100;
        const int PollIntervalMs = 16;
        var nextIdleTick = DateTime.UtcNow;
        try
        {
            while (true)
            {
                var now = DateTime.UtcNow;
                if (now >= nextIdleTick)
                {
                    nextIdleTick = now.AddMilliseconds(IdleIntervalMs);
                    onIdle();

                    if (File.Exists(rollRequestPath))
                    {
                        try
                        {
                            string json = await File.ReadAllTextAsync(rollRequestPath, ct);
                            var req = JsonSerializer.Deserialize<RollRequest>(json, _rollJsonOptions);
                            if (req != null && !req.Answered)
                            {
                                int answer = client.ActiveDialog != null
                                    ? await client.ActiveDialog.PlayDiceRollRequest(req)
                                    : 1;
                                req.Answered = true;
                                req.Answer = answer;
                                await File.WriteAllTextAsync(rollRequestPath,
                                    JsonSerializer.Serialize(req, _rollJsonOptions), ct);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }

                    if (File.Exists(askPlayerRequestPath))
                    {
                        try
                        {
                            string json = await File.ReadAllTextAsync(askPlayerRequestPath, ct);
                            var req = JsonSerializer.Deserialize<AskPlayerRequest>(json, _rollJsonOptions);
                            if (req != null && !req.Answered)
                            {
                                string? answer = client.ActiveDialog != null
                                    ? await client.ActiveDialog.PlayAskPlayerRequest(req)
                                    : null;
                                req.Answered = true;
                                req.Answer = answer;
                                await File.WriteAllTextAsync(askPlayerRequestPath,
                                    JsonSerializer.Serialize(req, _rollJsonOptions), ct);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }

                    if (File.Exists(_selectTargetRequestPath))
                    {
                        try
                        {
                            string json = await File.ReadAllTextAsync(_selectTargetRequestPath, ct);
                            var req = JsonSerializer.Deserialize<TargetSelectionRequest>(json, _rollJsonOptions);
                            if (req != null && !req.Answered)
                            {
                                req.Result = client.ActiveDialog != null
                                    ? await client.ActiveDialog.PlayTargetSelectionRequest(req)
                                    : new TargetSelectionResult { Cancelled = true };
                                req.Answered = true;
                                await File.WriteAllTextAsync(_selectTargetRequestPath,
                                    JsonSerializer.Serialize(req, _rollJsonOptions), ct);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                }
                else
                {
                    // Between spinner ticks: fast mouse/keyboard poll without dialog rerender.
                    client.ActiveDialog?.PollOnly();
                }

                await Task.Delay(PollIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Вступление новой игры — ждёт экрана игры (MapScreenLoop), там печатается посимвольно.
    private string? _pendingIntro;
    public bool HasPendingIntro => _pendingIntro != null;

    public async Task PlayIntro(Action<string>? onChunk, Action<string>? onNewMessage, Action? onRedraw)
    {
        if (_pendingIntro is not { } intro) return;
        _pendingIntro = null;
        try
        {
            await PlayResponse("{\"history\":" + intro + "}", onChunk, onNewMessage, onRedraw);
        }
        catch (Exception ex)
        {
            RecordError(ex, intro);
        }
        _storage.Save();
    }

    private async Task PlayResponse(
        string json,
        Action<string>? onChunk,
        Action<string>? onNewMessage,
        Action? onRedraw)
    {
        json = await EnsureValidJson(json);
        var player = new SequentialHistoryPlayer(
            _settings, _storage, _serializeOptions, onChunk, onNewMessage, onRedraw,
            onPoll: ActiveDialog != null ? ActiveDialog.PollOnly : null,
            animations: ActiveDialog?.Animations);
        var history = await player.PlayAsync(json);
        (_settings.History ??= []).AddRange(history);
    }

    // Ответ мастера с битым JSON (лишняя/потерянная кавычка, запятая, обрыв) — разбирается целиком до того, как
    // что-то применено к игре, поэтому его можно починить, а не терять ход. Не повтор всего запроса: он заново
    // попросил бы у игрока броски кубиков и ответы. Короткий вызов без инструментов чинит только синтаксис.
    // В UI-тестах с моками — без починки (там битый ответ проверяется намеренно, а настоящую нейронку звать нельзя).
    private async Task<string> EnsureValidJson(string json)
    {
        if (IsValidJson(json) || _appConfig.MockedActions.Length > 0) return json;
        try
        {
            string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, "RepairJson");
            const string repairPrompt = "Ты чинишь синтаксис JSON. Тебе дают сломанный JSON (лишние или пропущенные кавычки, " +
                "запятые, скобки, неэкранированные символы в строках, обрыв в конце). Верни тот же JSON с исправленным " +
                "синтаксисом: те же ключи, значения и тексты, ничего не добавляй и не убирай по смыслу. Обрыв — закрой " +
                "незаконченную строку/массив/объект. Ответ — только JSON, без пояснений и без ```.";
            string fixedJson = ExtractJson(await _provider.Complete([repairPrompt], json, actionPath, _appConfig.RepairJsonModel));
            if (IsValidJson(fixedJson)) return fixedJson;
        }
        catch { /* починить не вышло — ошибка разбора ниже, как раньше */ }
        return json;
    }

    private static bool IsValidJson(string json)
    {
        try { using var _ = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
    }

    // Лог нейронки (Program): ошибки ИИ пишутся туда целиком — в диалоге игроку коротко.
    public AiLogger? Logger { get; set; }

    // Подробности — только при настройке «ОШИБКИ ИИ: ПОДРОБНО»; иначе игроку — коротко, что делать. Ошибка настройки
    // нейронки (CLI не найден) — всегда своим текстом: повтор не поможет. Полностью — в логе нейронки.
    private void RecordError(Exception ex, string response, string prefix = "Ошибка")
    {
        Logger?.LogNote($"{prefix}: {ex}" + (response.Length > 0 ? $"\nОтвет: {response}" : ""));
        if ((ex as AiSetupException ?? ex.InnerException as AiSetupException) is { } setup)
        {
            _settings.History.Add(new DialogMessage { Text = $"{prefix}: {setup.Message}" });
            return;
        }
        string preview = response.Length > 200 ? response[..200] : response;
        preview = preview.Replace("\r", "").Replace("\n", " ");
        _settings.History.Add(new DialogMessage
        {
            Text = _appConfig.ShowAiErrors || _appConfig.MockedActions.Length > 0
                ? $"{prefix}: {ex.Message} Ответ: {preview}"
                : $"{prefix}: мастер запнулся и не смог ответить — повтори действие.",
        });
    }

    private string GetPassivePerception()
    {
        var skill = _settings.Hero?.Skills?.FirstOrDefault(s =>
            s.Name?.Contains("Восприятие", StringComparison.OrdinalIgnoreCase) == true ||
            s.Name?.Contains("Perception", StringComparison.OrdinalIgnoreCase) == true);

        if (skill?.Value == null) return "10";
        return $"10 {skill.Value}";
    }

    private static string ReadPrompt(string actionPath) =>
        File.ReadAllText(Path.Combine(actionPath, "systemPrompt.md"), Encoding.UTF8);

    private Task<string> CompleteConfigured(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model) =>
        _provider.Complete(systemBlocks, $"speechEnabled:{(_appConfig.SpeechEnabled ? "true" : "false")}\n{userMessage}", actionPath, model);

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0) return text;

        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return text[start..(i + 1)]; }
        }

        // Truncated JSON — close remaining open braces
        return text[start..] + new string('}', depth);
    }
}
