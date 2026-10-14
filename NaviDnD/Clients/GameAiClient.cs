using NaviDnD.Data.Models;
using NaviDnD.Helpers;
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
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ChunkPopulationQueue _population = new();
    private readonly string _ruleSet;
    private readonly AppConfig _appConfig;
    public DialogDisplay? ActiveDialog { get; set; }

    private static readonly string _rollRequestPath =
        Path.Combine(AppConfig.DataRoot, "Storage", "roll_request.json");

    private static readonly string _askPlayerRequestPath =
        Path.Combine(AppConfig.DataRoot, "Storage", "ask_player_request.json");

    private static readonly string _selectTargetRequestPath =
        Path.Combine(AppConfig.DataRoot, "Storage", "select_target_request.json");

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

    // Game rules (folder of prompts and rules data - Prompts/{RuleSet}).
    public string RuleSet => _ruleSet;

    public async Task CreateNewGame(NewGameData newGameData)
    {
        _storage.ResetWorldState();
        // MCP- server requires an existing state file at startup (see Program.cs) - before that CreateNewGame
        // went without MCP at all (allowedTools:null), now it needs find_icon.
        _storage.SaveDraft();

        string input = JsonSerializer.Serialize(newGameData, _serializeOptions);
        string userMessage = $"Player preferences for the character and rules:\n{input}";
        await CallAction(userMessage, nameof(CreateNewGame), _appConfig.ClaudeCreateNewGameModel);

        // The player’s choice in the questionnaire is as is, on top of the AI’s response.
        if (_settings.Hero is { } hero)
        {
            if (int.TryParse(newGameData.Level, out int level) && level > 0)
            {
                hero.Level = level;
                hero.Xp = LevelRules.Current.XpFor(level);
            }
            hero.VisionFt = null;   // hero overview - by light (engine); AI set “Vision 5 ft”
            if (!string.IsNullOrWhiteSpace(newGameData.Image)) hero.Image = newGameData.Image;
            if (newGameData.Color is { Count: 3 }) hero.Color = [.. newGameData.Color];
            if (!string.IsNullOrWhiteSpace(newGameData.Symbol)) hero.Symbol = newGameData.Symbol.Trim();
        }
    }

    // Questionnaire for a new game: the AI selects portraits of the hero by name and description (find_icon) -
    // they are at the beginning of the gallery. Error/empty answer - empty list (the gallery remains from the
    // ready set). New game with a hero from the library: the hero as he was (level, stats, skills, spells,
    // abilities), personality - from the profile; the state of the previous game resets the code (full HP,
    // effects, inspiration, location), equipment and money for a new one the adventure is revealed by the AI
    // with one short call (FixHeroForNewGame).
    public async Task ReuseHero(NewGameData data, string heroJson)
    {
        _storage.ResetWorldState();
        _storage.ApplySavedState("{\"hero\":" + heroJson + "}");   // level and experience - as they were
        if (_settings.Hero is not { } hero) return;
        if (hero.Hp is { } hp && hp.Split('/') is [_, var max] && max.Trim().Length > 0) hero.Hp = $"{max.Trim()}/{max.Trim()}";
        hero.Effects = [];
        hero.Dead = null;
        hero.Hidden = null;
        hero.NoOpportunityAttacksRound = null;
        hero.Light = null;
        hero.SpeedLeft = hero.SpeedMax;
        foreach (var action in hero.Actions ?? [])
            if (action.Deleted != true) action.Value = action.MaxValue;
        hero.Inspiration = null;
        hero.Stealth = null;
        hero.Position = null;
        hero.VisionFt = null;   // hero review - by light (engine)
        if (!string.IsNullOrWhiteSpace(data.Name)) hero.Name = data.Name.Trim();
        if (!string.IsNullOrWhiteSpace(data.Symbol)) hero.Symbol = data.Symbol.Trim();
        if (!string.IsNullOrWhiteSpace(data.Image)) hero.Image = data.Image;
        if (data.Color is { Count: 3 }) hero.Color = [.. data.Color];
        await FixHeroForNewGame(
            "This hero from a previous game is starting a new adventure. Preserve level, ability scores, skills, spells, " +
            "features, race, and class. Replace equipment and money with normal starting equipment for this class and " +
            "level (remove unique/quest items from the previous game); refill resources and spell slots; restore normal speed without conditions." +
            (string.IsNullOrWhiteSpace(data.Description) ? "" : $" Player's revised hero description: {data.Description}"));
    }

    // Hero profile: description (appearance, character, background - not a character sheet) - create by
    // name/race/class or modify the current one at the request of the player. null - it didn’t work out.
    public async Task<string?> DescribeHero(string name, string race, string cls, string? current, string? request, string? extra = null)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(DescribeHero));
        try
        {
            string userMessage = $"Name: {name}\nRace: {race}\nClass: {cls}" + (string.IsNullOrWhiteSpace(extra) ? "" : $"\n{extra.Trim()}")
                                 + (string.IsNullOrWhiteSpace(current) ? "" : $"\nCurrent description (from the player):\n{current.Trim()}")
                                 + (string.IsNullOrWhiteSpace(request) ? "" : $"\nPlayer request: {request.Trim()}");
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

    // “New Level” window: what the class receives at the level and selection groups - JSON for LevelUp.LoadPlan
    // (parses the window in its own thread). null - it didn’t work out.
    public async Task<string?> LevelUpOptions(LevelUp levelUp)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(LevelUpOptions));
        string response = "";
        try
        {
            response = await CompleteConfigured([ReadPrompt(actionPath)], levelUp.OptionsRequest(), actionPath, _appConfig.ClaudeModel);
            return await EnsureValidJson(ExtractJson(response));
        }
        catch (AiSetupException) { throw; }
        catch (Exception ex)
        {
            Logger?.LogNote($"LevelUpOptions: {ex}\nResponse: {response}");
            return null;
        }
    }

    // The player’s request for his own upgrade option (input field) - the same option prompt; JSON for LevelUp.MergeCustom.
    public async Task<string?> LevelUpCustom(LevelUp levelUp, string request)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(LevelUpOptions));
        string response = "";
        try
        {
            response = await CompleteConfigured([ReadPrompt(actionPath)], levelUp.CustomRequest(request), actionPath, _appConfig.ClaudeModel);
            return await EnsureValidJson(ExtractJson(response));
        }
        catch (AiSetupException) { throw; }
        catch (Exception ex)
        {
            Logger?.LogNote($"LevelUpCustom: {ex}\nResponse: {response}");
            return null;
        }
    }

    // The choice is made: a new hero (a copy of the current one) with the received and selected one; null - the
    // GM did not answer. There is nothing to add to the sheet (no skills, no choices, no new numbers) - without
    // calling the AI.
    public async Task<Hero?> LevelUpApply(LevelUp levelUp)
    {
        if (!levelUp.NeedsMaster) return levelUp.Apply(null);
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(LevelUpApply));
        string response = "";
        try
        {
            response = await CompleteConfigured([ReadPrompt(actionPath)], levelUp.ApplyRequest(), actionPath, _appConfig.ClaudeModel);
            return levelUp.Apply(await EnsureValidJson(ExtractJson(response)));
        }
        catch (AiSetupException) { throw; }
        catch (Exception ex)
        {
            Logger?.LogNote($"LevelUpApply: {ex}\nResponse: {response}");
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

        // How SendAction: history is printed character by character (with sound) through
        // SequentialHistoryPlayer, and does not appear entirely as one ApplyUpdateWorldState, as in CallAction.
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

    // Questionnaire for the new game, step “World”: the concept of the new world (name, essence, kingdoms with
    // capitals) - in its chronicle, so that the player sees the world before the start. It does not create
    // places - the GM will add them as the game progresses. description - current description (the player
    // wrote/corrected): the AI refines it upon request, and does not write it again.
    public async Task<bool> CreateWorld(WorldMap world, string? wish, string? name = null, string? description = null)
    {
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(CreateWorld));
        L.SetWorld(world.Language);   // description of the world - in its language
        try
        {
            string userMessage = WorldAtlas.TerrainSummary(world)
                                 + (string.IsNullOrWhiteSpace(name) ? "" : $"\nThe player chose the world name: «{name.Trim()}» — preserve it (name).")
                                 + (string.IsNullOrWhiteSpace(description) ? "" : $"\nCurrent world description (from the player): revise as requested and preserve everything not requested to change:\n{description.Trim()}")
                                 + (string.IsNullOrWhiteSpace(wish) ? "" : $"\nPlayer request: {wish}");
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
        // According to the plot, AI plans the location using the plan_location tool (how many blocks, where,
        // what is in each); the geometry according to the plan is built by the code
        // (LocationGrower.CreatePlanned → ChunkGenerator), the tool gives the starting block - the AI fills it
        // in the same response, the rest - PopulateChunk when approaching.
        _settings.Map.Chunks = null;

        // Game world: the code gave only land (terrain, kingdoms, roads); the concept of the world and
        // kingdoms, the starting place and places known to the hero are specified by the AI in the same answer
        // (world, add_place). World from the GM of the new game (selected or created at the “World” step; the
        // chronicle continues).
        if (_storage.NewGameData?.WorldId is { } wid) WorldLibrary.SetActive(wid);
        // Who the hero is (the first phrase of the description from the questionnaire: “thin drow...”) - in the
        // context of each move (hero.aiInfo): without her, the GM addressed the heroine in the masculine
        // gender. The entire description does not go into the save.
        if (_settings.Hero is { AiInfo: null or { Count: 0 } } h && _storage.NewGameData?.Description is { Length: > 0 } desc)
        {
            string first = desc.Trim();
            int dot = first.IndexOfAny(['.', '!', '?']);
            if (dot > 0) first = first[..(dot + 1)];
            h.AiInfo = [first.Length > 220 ? first[..220] + "…" : first];
        }
        var world = WorldLibrary.Current;
        _settings.World = new GameWorldLink { Id = world.Id };
        // The game is in the language of the world (story, history, names); it is also in every request (lang:).
        _settings.Language = world.Language;
        L.SetWorld(world.Language);
        // The world has already been described in previous games (chronicle) - the same concept and places; the
        // new one is only earth. The kingdoms have already been described (past games or “IMAGE” in the
        // questionnaire) - do not change the concept; there is only a name/description from the player - take
        // them and invent kingdoms.
        bool known = world.Chronicle.Kingdoms.Count > 0;
        string playerConcept = world.Chronicle.Name is { Length: > 0 } || world.Chronicle.Description is { Length: > 0 }
            ? $"\nThe player defined the world — name «{world.Chronicle.Name ?? world.Name}»{(world.Chronicle.Description is { Length: > 0 } pd ? $", description: {pd}" : "")}. Use these unchanged (name/description); develop kingdoms consistent with the description."
            : "";
        string worldBrief = known
            ? "\n\nThe world is already described: preserve its concept, kingdoms, and places; use add_place for additions. Chronicle:\n"
              + WorldAtlas.Overview(GameWorld.Compose(world, _settings.World, includeHidden: true), withDmNotes: true)
              + "\nGeography:\n" + WorldAtlas.TerrainSummary(world)
            : "\n\nWorld geography (concept in world, places through add_place):\n" + WorldAtlas.TerrainSummary(world) + playerConcept;
        // Adventure parameters (Adventure step): genre, emphasis, length, difficulty, tempo.
        if (_storage.NewGameData?.AdventureStyle is { Length: > 0 } style)
            worldBrief = $"\nAdventure parameters: {style}" + worldBrief;
        worldBrief += $"\nCurrent time: day {_settings.Time.Day}, {PartsOfDay.Normalize(_settings.Time.PartOfDay)} — describe this time of day or explicitly change time.";
        // The starting location chosen by the player on the map (Adventure step).
        if (_storage.NewGameData?.StartPlace is { Length: > 0 } sp)
            worldBrief += $"\nThe player chose the starting place: «{sp}» — start there (world.place).";
        else if (_storage.NewGameData is { StartX: int sx, StartY: int sy })
            worldBrief += $"\nThe player chose the starting map cell: [{sx},{sy}] — {WorldBiomes.Get(world.BiomeAt(sx, sy)).Name.ToLowerInvariant()}, {WorldAtlas.Surroundings(world, sx, sy, named: true)}; add_place at or near this cell (near is unnecessary: specify kingdom and terrain from this cell).";

        // Save the hero and map to a draft file before running AI - MCP tools read this file. We do not touch
        // the combat worldState.json until the world is created successfully.
        _storage.SaveDraft();

        string userMessage;
        if (_ruleSet == "Dnd5e")
        {
            userMessage = $"Hero data: symbol: {_settings.Hero?.Symbol}, name: {_settings.Hero?.Name}, passive Perception: {GetPassivePerception()}" +
                          $"\n\nGame preferences: {_storage.NewGameData?.SettingWish}" + worldBrief;
        }
        else
        {
            userMessage = $"Hero data: symbol: {_settings.Hero?.Symbol}, name: {_settings.Hero?.Name}" +
                          $"\n\nGame preferences: {_storage.NewGameData?.SettingWish}" + worldBrief;
        }

        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(StartNewGame));
        string systemPrompt = ReadPrompt(actionPath);
        string response = "";
        try
        {
            response = await CompleteConfigured([systemPrompt], userMessage, actionPath, _appConfig.ClaudeStartNewGameModel);

            // MCP-tools could have added a draft - we re-read it so as not to lose anything.
            _storage.LoadDraft();
            // The plan_location tag is only needed in the middle of the game - here the map has already been taken from the draft.
            try { File.Delete(Path.Combine(Path.GetDirectoryName(Storage.DraftSavePath) ?? ".", Storage.LocationPlannedFlag)); } catch { }

            string json = ExtractJson(response);
            // Old format answer (full map with cols/rows/rooms - mocks of UI tests): applied as before, on top
            // of an empty map, without block generation.
            if (JsonNode.Parse(json)?["map"]?["cols"] != null)
            {
                var map = _settings.Map;
                map.Rooms = []; map.Doors = []; map.Area = []; map.Entities = []; map.Objects = [];
                map.Chunks = null;
            }
            // plan_location not called - fallback option: one block so that there is somewhere to play.
            else if (_settings.Map.Chunks is not { Count: > 0 })
                LocationGrower.CreateStart(_settings);
            if (_settings.Map.Chunks?.FirstOrDefault(c => c.Exits.Any(e => e.External)) is { } startChunk)
                startChunk.Populated = true;
            // The wizard's introduction (history) is not displayed immediately: it is printed character by
            // character, with sound, by the game screen (PlayIntro - as a normal response from the wizard); the
            // rest is displayed immediately.
            if (JsonNode.Parse(json) is JsonObject startRoot && startRoot["history"] is JsonArray intro && intro.Count > 0)
            {
                startRoot.Remove("history");
                _pendingIntro = intro.ToJsonString(_serializeOptions);
                json = startRoot.ToJsonString(_serializeOptions);
            }
            _settings.History = [];   // conversation in the hero editor is not part of the game
            _storage.ApplyUpdateWorldState(json);

            // The world was created successfully - we record it in the combat save file
            _storage.Save();
            Storage.DeleteDraft();
        }
        catch (Exception ex)
        {
            RecordError(ex, response);
        }
    }

    // The location block that the hero approached (LocationGrower.UnpopulatedNearHero) - the AI names the rooms
    // and places creatures/objects. The geometry is already generated by the code and is in the map.
    public async Task PopulateChunk(
        MapChunk chunk,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        // If prefetch is already running, consume the same request rather than issuing a second one.
        await FlushPopulationAsync(true, onIdle);
        if (chunk.Populated) return;
        TryPrefetchChunk(chunk);
        await FlushPopulationAsync(true, onIdle);
        onRedraw?.Invoke();
    }

    public bool TryPrefetchChunk(MapChunk chunk)
    {
        if (_requestGate.CurrentCount == 0 || !_population.CanStart(_settings.Map, chunk)) return false;
        var ctx = new AiContextBuilder(_settings, _storage.VisibleCells);
        string message = "## Current state\n" + ctx.PopulationState() + "\n" + ctx.LocationPlan(chunk) + "\n" + ctx.ChunkState(chunk);
        string snapshot = JsonSerializer.Serialize(_settings, _serializeOptions);
        string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, nameof(PopulateChunk));
        return _population.TryStart(_settings.Map, chunk, async () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "NaviDnD-population-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "worldState.json");
            try
            {
                await File.WriteAllTextAsync(path, snapshot);
                AiActionTools.StatePathOverride.Value = path;
                return ExtractJson(await CompleteConfigured([ReadPrompt(actionPath)], message, actionPath, _appConfig.ClaudeModel));
            }
            finally
            {
                AiActionTools.StatePathOverride.Value = null;
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        });
    }

    public async Task<bool> FlushPopulationAsync(bool wait = false, Action? onIdle = null)
    {
        if (wait)
            while (_population.HasPending && !_population.IsReady)
            {
                onIdle?.Invoke();
                await Task.Delay(16);
            }
        try
        {
            var result = await _population.TakeAsync(wait);
            if (result == null) return false;
            var json = ChunkPopulationPatch.Prepare(_settings, result);
            if (json == null) return false;
            _storage.ApplyUpdateWorldState(json);
            result.Chunk.Populated = true;
            _storage.Save();
            return true;
        }
        catch (Exception ex) { RecordError(ex, ""); return false; }
    }

    public async Task SendAction(
        string playerAction,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        await FlushPopulationAsync(true, onIdle);
        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        Helpers.Speech.Stop();
        string response = "";
        try
        {
            _storage.Save(); // MCP (plan_location, add_place, world_query) reads the save The GM did not describe the past
            // journey (the server did not respond) - first it, then the player’s action.
            string pending = _settings.PendingTravel is { Length: > 0 } pt ? pt + "\n\n## Player action after travel\n" : "";
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

    // Traveling on the world map (TravelService has already moved the hero and shifted time): one challenge to
    // the GM - describe the road, rest stops, supplies, meeting (if it happened) and arrival; when necessary,
    // he builds a stage (plan_location).
    public async Task Travel(
        string travelReport,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        await FlushPopulationAsync(true, onIdle);
        var (actionPath, systemBlocks, ctx) = BuildSendActionContext();
        string response = "";
        try
        {
            _storage.Save();
            // The past path not described by the GM is along with this (its result is also needed).
            if (_settings.PendingTravel is { Length: > 0 } earlier) travelReport = earlier + "\n\n" + travelReport;
            _settings.PendingTravel = travelReport;   // before the wizard's response, the path is “not described”
            response = await CompleteWithSpinner(systemBlocks, ctx.MinimalState(travelReport), actionPath, _appConfig.ClaudeModel, onIdle);
            ReloadPlannedLocation();
            _settings.PendingTravel = null;
            await PlayResponse(ExtractJson(response), onChunk, onNewMessage, onRedraw);
            _storage.Save();
        }
        catch (Exception ex)
        {
            RecordError(ex, response, L.T("Travel error"));
            _settings.History.Add(new DialogMessage { Text = L.T("The DM didn't respond, but the journey is done: the road, food and encounters will be described with your next action.") });
            _storage.Save();
        }
    }

    // The GM built a location in the middle of the game (MCP plan_location recorded it in the save and left a
    // mark) - re-read the map and the hero’s place, otherwise the next Save would have erased its state from
    // memory.
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
        catch { /*the map was not re-read - we play on the old one*/ }
    }

    public async Task FireEnemyTurn(
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        await FlushPopulationAsync(true, onIdle);
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
            RecordError(ex, response, L.T("Enemy turn error"));
        }
    }

    public async Task FireRoundTransition(
        RoundBundle bundle,
        Action<string>? onChunk = null,
        Action<string>? onNewMessage = null,
        Action? onIdle = null,
        Action? onRedraw = null)
    {
        await FlushPopulationAsync(true, onIdle);
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
            RecordError(ex, response, L.T("Round error"));
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

        await FlushPopulationAsync(true, onIdle);
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
            RecordError(ex, response, L.T("Trigger error"));
        }
    }

    // Only the triggered sub-key is removed (Storage.MergeTriggers) - we do not touch the other triggers of the
    // same object. spotted/opportunity_attack_on_hero - not one-time, they do not go here.
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
            AddEngineNotices();
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

    // The entry of a new game - waits for the game screen (MapScreenLoop), it is printed there character by character.
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
        AddEngineNotices();
    }

    // Engine messages (experience accrued, level available) - after the GM's notes.
    private void AddEngineNotices()
    {
        foreach (string notice in _storage.TakeNotices())
            (_settings.History ??= []).Add(new DialogMessage { Text = notice });
    }

    // Reply from the GM with the broken JSON (extra/lost quote, comma, break) - disassembled in its entirety
    // before anything is applied to the game, so it can be repaired rather than losing a move. Not repeating
    // the entire request: it would ask the player for dice rolls and answers again. A short call without tools
    // fixes only the syntax. In UI tests with mocks, there is no repair (where the broken answer is checked
    // intentionally, but the real AI cannot be called).
    private async Task<string> EnsureValidJson(string json)
    {
        if (IsValidJson(json) || _appConfig.MockedActions.Length > 0) return json;
        try
        {
            string actionPath = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ruleSet, "RepairJson");
            const string repairPrompt = "Repair JSON syntax. The input is malformed JSON (extra or missing quotes, " +
                "commas, brackets, unescaped string characters, or truncation). Return the same JSON with corrected " +
                "syntax: preserve all keys, values, and text without semantic additions or deletions. If truncated, close the " +
                "unfinished string/array/object. Return JSON only, without explanations or markdown fences.";
            string fixedJson = ExtractJson(await CompleteConfigured([repairPrompt], json, actionPath, _appConfig.RepairJsonModel));
            if (IsValidJson(fixedJson)) return fixedJson;
        }
        catch { /*could not be fixed - parsing error below, same as before*/ }
        return json;
    }

    private static bool IsValidJson(string json)
    {
        try { using var _ = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
    }

    // AI log (Program): AI errors are written there in their entirety - briefly in the player’s dialogue.
    public AiLogger? Logger { get; set; }

    // Details - only when setting "AI ERRORS: DETAILS"; otherwise the player is given a short summary of what
    // to do. AI setup error (CLI not found) - always with its own text: repeating will not help. Completely in
    // the AI log.
    private void RecordError(Exception ex, string response, string? prefix = null)
    {
        prefix ??= L.T("Error");
        Logger?.LogNote($"{prefix}: {ex}" + (response.Length > 0 ? $"\nResponse: {response}" : ""));
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
                ? $"{prefix}: {ex.Message} " + L.T("Response: ") + preview
                : $"{prefix}: " + L.T("the DM stumbled and couldn't respond — repeat the action."),
        });
    }

    private string GetPassivePerception()
    {
        var skill = _settings.Hero?.Skills?.FirstOrDefault(s => SkillNames.IsPerception(s.Name));

        if (skill?.Value == null) return "10";
        return $"10 {skill.Value}";
    }

    private static string ReadPrompt(string actionPath) =>
        File.ReadAllText(Path.Combine(actionPath, "systemPrompt.md"), Encoding.UTF8);

    private async Task<string> CompleteConfigured(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model)
    {
        await _requestGate.WaitAsync();
        try
        {
            return await _provider.Complete(systemBlocks,
                $"lang:{L.World}\nspeechEnabled:{(_appConfig.SpeechEnabled ? "true" : "false")}\n{userMessage}", actionPath, model);
        }
        finally { _requestGate.Release(); WindowControls.NotifyIfMinimized(); }
    }

    // JSON from the answer, with links to directories already allowed (CatalogReferences: icon/ref/monster).
    private static string ExtractJson(string text) => CatalogReferences.Resolve(ExtractRawJson(text));

    private static string ExtractRawJson(string text)
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
