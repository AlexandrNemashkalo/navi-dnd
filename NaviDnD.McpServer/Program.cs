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

// Инструменты use_reaction/roll_dice ждут ответа игрока через диалоговое окно (DialogDisplay).
// Для действий без такого UI (CreateNewGame, StartNewGame) клиент передаёт --no-interactive,
// иначе они просто виснут до таймаута.
bool interactive = !args.Contains("--no-interactive");

// --tools a,b,c — отдавать клиенту только эти инструменты (у Claude CLI это делает --allowedTools; у других
// клиентов, например Codex CLI, такого флага может не быть — тогда фильтр здесь, на стороне сервера).
string? toolsArg = args.SkipWhile(a => a != "--tools").Skip(1).FirstOrDefault();
HashSet<string>? allowedTools = toolsArg == null ? null
    : [.. toolsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

if (!File.Exists(worldStatePath))
    throw new FileNotFoundException($"worldState.json not found: {worldStatePath}");

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
