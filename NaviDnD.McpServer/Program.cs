using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NaviDnD.McpServer;

string worldStatePath = args
    .SkipWhile(a => a != "--world-state")
    .Skip(1)
    .FirstOrDefault()
    ?? throw new ArgumentException("Required argument missing: --world-state <path>");

string? logPath = args
    .SkipWhile(a => a != "--log")
    .Skip(1)
    .FirstOrDefault();

// Tools use_reaction/roll_dice wait for the player's response through the dialog box (DialogDisplay). For
// actions without such UI (CreateNewGame, StartNewGame), the client sends --no-interactive, otherwise they
// simply hang until the timeout.
bool interactive = !args.Contains("--no-interactive");

// --tools a,b,c - give only these tools to the client (for Claude CLI --allowedTools does this; other clients,
// for example Codex CLI, may not have such a flag - then the filter is here on the server side).
string? toolsArg = args.SkipWhile(a => a != "--tools").Skip(1).FirstOrDefault();
HashSet<string>? allowedTools = toolsArg == null ? null
    : [.. toolsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

if (!File.Exists(worldStatePath))
    throw new FileNotFoundException($"worldState.json not found: {worldStatePath}");

// Game language (save field language): names from reference books and the generator - in it, as the GM writes.
// Picture pack of the game (iconPack) - find_icon searches in it.
try
{
    var savedState = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(worldStatePath));
    NaviDnD.L.SetWorld(savedState?["language"]?.GetValue<string>());
    NaviDnD.Helpers.CatalogSearch.IconPack = NaviDnD.Helpers.CatalogSearch.NormalizePack(savedState?["iconPack"]?.GetValue<string>());
}
catch { /*not read - Russian, game-icons*/ }

var builder = Host.CreateApplicationBuilder(Array.Empty<string>());

// All logs go to stderr — stdout must stay clean for JSON-RPC messages
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(new McpServerConfig(worldStatePath, logPath));
var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<WorldStateTools>()
    .WithTools<MapGenTools>()
    .WithTools<CalculateMovementTools>()
    .WithTools<GameActionTools>()
    .WithTools<IconTools>()
    .WithTools<SpellTools>()
    .WithTools<MonsterTools>()
    .WithTools<AssetSearchTools>()
    .WithTools<EquipmentTools>()
    .WithTools<WorldQueryTools>();

if (interactive)
{
    mcpBuilder
        .WithTools<UseReactionTools>()
        .WithTools<RollDiceTools>()
        .WithTools<SelectTargetTools>();
}

if (allowedTools != null)
    builder.Services.PostConfigure<McpServerOptions>(options =>
    {
        if (options.ToolCollection is not { } tools) return;
        foreach (var tool in tools.ToList())
            if (!allowedTools.Contains(tool.ProtocolTool.Name)) tools.Remove(tool);
    });

await builder.Build().RunAsync();
