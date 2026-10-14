using System.Text.Json.Nodes;
using NaviDnD.Data.Models;

namespace NaviDnD.Clients;

// Validate against current geometry and occupied cells before one atomic Storage patch.
public static class ChunkPopulationPatch
{
    public static string? Prepare(WorldState world,ChunkPopulationQueue.Result result)
    {
        if(!ReferenceEquals(world.Map,result.Map) || world.Map.Chunks?.Contains(result.Chunk)!=true || result.Chunk.Populated) return null;
        var root=JsonNode.Parse(result.Json)?.AsObject() ?? throw new InvalidDataException("Population response must be an object.");
        var map=root["map"]?.AsObject() ?? throw new InvalidDataException("Population response must contain map.");
        if(map.Any(p=>p.Key is not ("rooms" or "entities" or "objects"))) throw new InvalidDataException("Population cannot change geometry.");
        var patch=new JsonObject();
        var symbols=(world.Map.Entities??[]).Cast<CellEntity>().Concat(world.Map.Objects??[]).Where(e=>e.Deleted!=true).Select(e=>e.Symbol).Append(world.Hero?.Symbol??"HRO").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var occupied=(world.Map.Entities??[]).Cast<CellEntity>().Concat(world.Map.Objects??[]).Where(e=>e.Deleted!=true && e.Position?.Count>=2).Select(e=>(e.Position[0],e.Position[1])).ToHashSet();
        if(world.Hero?.Position is {Count:>=2} hp) occupied.Add((hp[0],hp[1]));
        foreach(var key in new[]{"entities","objects"})
        {
            if(map[key] is not JsonArray items) continue;
            foreach(var node in items)
            {
                var item=node?.AsObject() ?? throw new InvalidDataException("Invalid population entity.");
                if(item.Any(p=>p.Key.Equals("id",StringComparison.OrdinalIgnoreCase) || p.Key.Equals("deleted",StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Population may only append entities.");
                var pos=item["position"]?.AsArray();
                if(pos?.Count!=2) throw new InvalidDataException("Missing population position.");
                int c=pos[0]!.GetValue<int>(),r=pos[1]!.GetValue<int>();
                if(!result.Chunk.Contains(c,r) || world.Map.TerrainAt(c,r)?.BlocksMove==true || !occupied.Add((c,r))) throw new InvalidDataException("Population cell is unavailable.");
                if(world.Map.TerrainAt(c,r)==null && world.Map.RoomAt(c,r)==null) throw new InvalidDataException("Population cell is outside rooms.");
                string symbol=item["symbol"]?.GetValue<string>()??"";
                if(symbol.Length!=3 || !symbols.Add(symbol)) throw new InvalidDataException("Population symbol is unavailable.");
            }
            patch[key]=items.DeepClone();
        }
        if(map["rooms"] is JsonArray rooms)
        {
            foreach(var node in rooms)
            {
                var room=node!.AsObject();
                if(room.Any(p=>p.Key is not ("id" or "name" or "aiInfo" or "triggers"))) throw new InvalidDataException("Population cannot change room geometry.");
                int id=room["id"]!.GetValue<int>();
                if(id<0 || id>=(world.Map.Rooms?.Count??0) || world.Map.Rooms![id].Positions.Any(p=>!result.Chunk.Contains(p[0],p[1]))) throw new InvalidDataException("Population room is outside requested chunk.");
            }
            patch["rooms"]=rooms.DeepClone();
        }
        // Background population never changes the hero, time, combat, existing narrative or visible history.
        return new JsonObject{["map"]=patch}.ToJsonString();
    }
}
