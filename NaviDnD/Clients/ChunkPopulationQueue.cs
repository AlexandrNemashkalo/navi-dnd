using NaviDnD.Data.Models;

namespace NaviDnD.Clients;

// One producer, one UI consumer. Fetching never applies state; abandoned locations discard the result.
public sealed class ChunkPopulationQueue
{
    public sealed record Result(MapConfig Map, MapChunk Chunk, string Json);
    private readonly object gate = new();
    private readonly HashSet<MapChunk> attempted = [];
    private Task<Result>? pending;
    private bool claimed;
    public bool HasPending { get { lock(gate) return pending != null; } }
    public bool IsReady { get { lock(gate) return pending?.IsCompleted == true && !claimed; } }

    public bool TryStart(MapConfig map, MapChunk chunk, Func<Task<string>> fetch)
    {
        lock(gate)
        {
            if(pending != null || chunk.Populated || !map.Chunks!.Contains(chunk) || !attempted.Add(chunk)) return false;
            pending=Fetch();
            return true;
            async Task<Result> Fetch() => new(map,chunk,await fetch());
        }
    }

    public async Task<Result?> TakeAsync(bool wait)
    {
        Task<Result> task;
        lock(gate)
        {
            if(pending==null || claimed || (!wait && !pending.IsCompleted)) return null;
            claimed=true;
            task=pending;
        }
        try { return await task; }
        finally { lock(gate) { pending=null; claimed=false; } }
    }
}
