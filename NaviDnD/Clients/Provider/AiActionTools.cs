namespace NaviDnD.Clients;

// Общее для всех CLI-провайдеров (Claude, Codex): какие MCP-инструменты нужны каждому действию и как запускать
// MCP-сервер игры (NaviDnD.McpServer) для запроса.
internal static class AiActionTools
{
    // Инструменты MCP-сервера, разрешённые действию (имя действия — папка промпта: SendAction, StartNewGame…).
    public static string[] ToolsFor(string action) => action switch
    {
        // Иконки, заклинания, предметы и монстры — ссылками в ответе ("icon"/"ref"/"monster", CatalogReferences), а не
        // вызовом инструмента на каждый: каждый вызов — лишний круг модели. find_item остался в игре — механика редкой
        // магической добычи, которую мастер описывает сам.
        // Нейронка задаёт план локации (plan_location), геометрию строит код — generate_map не нужен.
        "StartNewGame" => ["plan_location", "add_place"],
        // Герой, мир, описание, уровень — по данным в сообщении, инструменты не нужны.
        "CreateNewGame" or "CreateWorld" or "DescribeHero" or "RepairJson" or "LevelUpOptions" or "LevelUpApply" => [],
        "PopulateChunk" => ["get_world_state", "world_query"],
        // Правка героя перед игрой: данные героя — одним запросом.
        "FixHeroForNewGame" => ["get_world_state"],
        _ => ["get_world_state", "calculate_movement", "roll_dice", "use_reaction", "select_target",
              "advance_round", "long_rest", "short_rest", "door_action", "find_item", "world_query", "add_place", "plan_location"],
    };

    // Действия до создания мира/героя — MCP-сервер читает черновик (newGameState.json), а не сохранение игры.
    public static readonly HashSet<string> DraftActions = ["CreateNewGame", "FixHeroForNewGame", "StartNewGame", "DescribeHero"];

    // Действия без диалогового окна: ask_player/roll_dice там зависли бы до таймаута.
    public static readonly HashSet<string> NonInteractiveActions = ["CreateNewGame", "StartNewGame", "DescribeHero", "CreateWorld", "RepairJson", "LevelUpOptions", "LevelUpApply"];

    // Аргументы запуска MCP-сервера для действия; withToolFilter — сервер сам отдаёт только ToolsFor(action)
    // (для клиентов без своего списка разрешённых инструментов).
    public static List<string> McpArgs(string action, AiLogger? logger, bool withToolFilter)
    {
        string worldStatePath = DraftActions.Contains(action) ? Storage.DraftSavePath : Storage.SavePath;
        var args = new List<string> { "--world-state", worldStatePath };
        if (logger != null) args.AddRange(["--log", logger.LogPath]);
        args.AddRange(["--run-id", Guid.NewGuid().ToString("N")]);
        if (NonInteractiveActions.Contains(action)) args.Add("--no-interactive");
        if (withToolFilter) args.AddRange(["--tools", string.Join(",", ToolsFor(action).DefaultIfEmpty("none"))]);
        return args;
    }

    // Дополнительное окружение MCP-сервера; на Windows пустое. На macOS/Linux его запускатель должен найти среду .NET, а она
    // бывает не в папке по умолчанию (DOTNET_ROOT), и Codex запускает MCP-серверы с очищенным окружением.
    public static Dictionary<string, string> McpEnvironment()
    {
        var environment = new Dictionary<string, string>();
        if (OperatingSystem.IsWindows()) return environment;
        string? root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (string.IsNullOrEmpty(root) && Path.GetDirectoryName(typeof(object).Assembly.Location) is { Length: > 0 } runtime)
            root = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));   // <root>/shared/Microsoft.NETCore.App/<version>
        if (!string.IsNullOrEmpty(root) && Directory.Exists(Path.Combine(root, "shared"))) environment["DOTNET_ROOT"] = root;
        return environment;
    }
}
