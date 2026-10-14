namespace NaviDnD.Clients;

// Common to all CLI providers (Claude, Codex): what MCP tools are needed for each action and how to run the
// MCP game server (NaviDnD.McpServer) for the request.
internal static class AiActionTools
{
    internal static readonly AsyncLocal<string?> StatePathOverride = new();
    // MCP-server tools allowed for the action (action name - prompt folder: SendAction, StartNewGame...).
    public static string[] ToolsFor(string action) => action switch
    {
        // Icons, spells, items and monsters - with links in the answer ("icon" / "ref" / "monster",
        // CatalogReferences)), and not by calling a tool for each: each call is an extra circle of the model.
        // find_item remained in the game - the mechanics of rare magical loot, which the GM describes himself.
        // The AI sets the location plan (plan_location), the code builds the geometry - generate_map is not
        // needed.
        "StartNewGame" => ["plan_location", "add_place"],
        // Hero, world, description, level - according to the message, no tools are needed.
        "CreateNewGame" or "CreateWorld" or "DescribeHero" or "RepairJson" or "LevelUpOptions" or "LevelUpApply" => [],
        "PopulateChunk" => ["get_world_state", "world_query"],
        // Editing the hero before the game: hero data - in one request.
        "FixHeroForNewGame" => ["get_world_state"],
        _ => ["get_world_state", "calculate_movement", "roll_dice", "use_reaction", "select_target",
              "advance_round", "long_rest", "short_rest", "door_action", "find_item", "world_query", "add_place", "plan_location"],
    };

    // Actions before world/hero creation - MCP-server reads draft (newGameState.json), not save game.
    public static readonly HashSet<string> DraftActions = ["CreateNewGame", "FixHeroForNewGame", "StartNewGame", "DescribeHero"];

    // Actions without a dialog: ask_player/roll_dice would hang there until it timed out.
    public static readonly HashSet<string> NonInteractiveActions = ["CreateNewGame", "StartNewGame", "DescribeHero", "CreateWorld", "RepairJson", "LevelUpOptions", "LevelUpApply"];

    // Arguments to start MCP-server for action; withToolFilter - the server itself gives only ToolsFor(action)
    // (for clients without their own list of allowed tools).
    public static List<string> McpArgs(string action, AiLogger? logger, bool withToolFilter)
    {
        string worldStatePath = StatePathOverride.Value ?? (DraftActions.Contains(action) ? Storage.DraftSavePath : Storage.SavePath);
        var args = new List<string> { "--world-state", worldStatePath };
        if (logger != null) args.AddRange(["--log", logger.LogPath]);
        args.AddRange(["--run-id", Guid.NewGuid().ToString("N")]);
        if (NonInteractiveActions.Contains(action)) args.Add("--no-interactive");
        if (withToolFilter) args.AddRange(["--tools", string.Join(",", ToolsFor(action).DefaultIfEmpty("none"))]);
        return args;
    }

    // Additional environment MCP-server; on Windows is empty. On macOS/Linux, its launcher must find the .NET
    // environment, but it is not in the default folder (DOTNET_ROOT), and Codex launches MCP- servers with a
    // cleaned environment.
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
