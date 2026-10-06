using System.Text;
using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Clients;

public class AiLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();
    private static readonly Encoding _utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public AiLogger()
    {
        string dir = Path.Combine(AppConfig.ProjectRoot, "logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, $"ai_{DateTime.Now:yyyy-MM-dd}.log");
    }

    public string LogPath => _logPath;

    public void LogRequest(string label, IReadOnlyList<string> systemBlocks, string userMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {DateTime.Now:HH:mm:ss.fff} | {label} ===");
        sb.AppendLine($"  INPUT:");
        sb.AppendLine(userMessage);
        Write(sb.ToString());
    }

    public void LogMcp(string direction, string content)
    {
        Write($"  MCP {direction} {content}\n");
    }

    public void LogResponse(string rawCliJson, string result, TimeSpan? duration = null)
    {
        var sb = new StringBuilder();
        if (duration.HasValue)
            sb.AppendLine($"  DURATION: {duration.Value.TotalSeconds:F2}s");
        try
        {
            using var doc = JsonDocument.Parse(rawCliJson);
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                sb.Append("  USAGE:");
                foreach (var prop in usage.EnumerateObject())
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                        sb.Append($"  {prop.Name}={prop.Value}");
                sb.AppendLine();

                if (doc.RootElement.TryGetProperty("total_cost_usd", out var cost))
                    sb.AppendLine($"  COST:    ${cost}");
                else if (doc.RootElement.TryGetProperty("cost_usd", out var costUsd))
                    sb.AppendLine($"  COST:    ${costUsd}");
            }
        }
        catch
        {
            sb.AppendLine($"  RAW CLI: {rawCliJson}");
        }

        sb.AppendLine($"  RESULT:  {result}");
        sb.AppendLine();
        Write(sb.ToString());
    }

    public void LogNote(string note)
    {
        Write($"  NOTE:    {note}\n");
    }

    public void LogTriggers(MapConfig map)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {DateTime.Now:HH:mm:ss.fff} | TRIGGERS SNAPSHOT ===");

        var entities = (map.Entities ?? []).Concat(map.Objects ?? []).ToList();
        if (entities.Any(e => e.Triggers != null))
        {
            sb.AppendLine("  ENTITIES/OBJECTS:");
            foreach (var e in entities.Where(e => e.Triggers != null))
            {
                sb.AppendLine($"    [{e.Symbol}] {e.Name}  hidden={e.Hidden == true}");
                AppendTriggerEffect(sb, "OnStep",    e.Triggers!.OnStep);
                AppendTriggerEffect(sb, "OnVisible", e.Triggers!.OnVisible);
            }
        }

        var doors = map.Doors ?? [];
        if (doors.Any(d => d.Triggers != null))
        {
            sb.AppendLine("  DOORS:");
            foreach (var d in doors.Where(d => d.Triggers != null))
            {
                var pos = $"[{d.From[0]},{d.From[1]}]->[{d.To[0]},{d.To[1]}]";
                sb.AppendLine($"    Door {pos}  hidden={d.Hidden == true}");
                AppendTriggerEffect(sb, "OnStep",    d.Triggers!.OnStep);
                AppendTriggerEffect(sb, "OnVisible", d.Triggers!.OnVisible);
            }
        }

        var areas = map.Area ?? [];
        if (areas.Any(a => a.Triggers != null))
        {
            sb.AppendLine("  AREAS:");
            foreach (var a in areas.Where(a => a.Triggers != null))
            {
                sb.AppendLine($"    Area \"{a.Name}\"");
                AppendTriggerEffect(sb, "OnExplored", a.Triggers!.OnExplored);
            }
        }

        var hiddenEntities = entities.Where(e => e.Hidden == true).ToList();
        var hiddenDoors    = doors.Where(d => d.Hidden == true).ToList();
        if (hiddenEntities.Count > 0 || hiddenDoors.Count > 0)
        {
            sb.AppendLine("  HIDDEN:");
            foreach (var e in hiddenEntities)
                sb.AppendLine($"    [{e.Symbol}] {e.Name}  pos=[{e.Position[0]},{e.Position[1]}]  triggers={e.Triggers != null}");
            foreach (var d in hiddenDoors)
            {
                var pos = $"[{d.From[0]},{d.From[1]}]->[{d.To[0]},{d.To[1]}]";
                sb.AppendLine($"    Door {pos}  triggers={d.Triggers != null}");
            }
        }

        sb.AppendLine();
        Write(sb.ToString());
    }

    private static void AppendTriggerEffect(StringBuilder sb, string type, TriggerEffect? effect)
    {
        if (effect == null) return;
        var once = effect.Once == true ? "once" : "repeatable";
        sb.AppendLine($"      {type}: [{once}] {effect.Effect}");
    }

    private void Write(string text)
    {
        lock (_lock)
            File.AppendAllText(_logPath, text, _utf8);
    }
}
