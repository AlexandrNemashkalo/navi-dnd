namespace NaviDnD.Clients;

// Общее для всех CLI-провайдеров (Claude, Codex): какие MCP-инструменты нужны каждому действию и как запускать
// MCP-сервер игры (NaviDnD.McpServer) для запроса.
internal static class AiActionTools
{
    // Инструменты MCP-сервера, разрешённые действию (имя действия — папка промпта: SendAction, StartNewGame…).
    public static string[] ToolsFor(string action) => action switch
    {
        "CreateNewGame" => ["find_icon", "find_spell", "find_item"],
        // Нейронка задаёт план локации (plan_location), геометрию строит код — generate_map не нужен.
        "StartNewGame" => ["plan_location", "find_assets", "find_icon", "find_monster", "add_place"],
        // Концепция мира и описание героя — по данным в сообщении, инструменты не нужны.
        "CreateWorld" or "DescribeHero" or "RepairJson" => [],
        "PopulateChunk" => ["get_world_state", "find_assets", "find_icon", "find_monster", "find_item", "world_query"],
        _ => ["get_world_state", "calculate_movement", "roll_dice", "use_reaction", "select_target",
              "advance_round", "long_rest", "short_rest", "door_action", "find_icon",
              "find_spell", "find_item", "world_query", "add_place", "plan_location"],
    };

    // Действия до создания мира/героя — MCP-сервер читает черновик (newGameState.json), а не сохранение игры.
    public static readonly HashSet<string> DraftActions = ["CreateNewGame", "FixHeroForNewGame", "StartNewGame", "DescribeHero"];

    // Действия без диалогового окна: ask_player/roll_dice там зависли бы до таймаута.
    public static readonly HashSet<string> NonInteractiveActions = ["CreateNewGame", "StartNewGame", "DescribeHero", "CreateWorld", "RepairJson"];

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
}
