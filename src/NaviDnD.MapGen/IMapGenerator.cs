using NaviDnD.Data.Models;
using NaviDnD.MapGen.Models;

namespace NaviDnD.MapGen;

public interface IMapGenerator
{
    MapConfig Generate(MapGenRequest request);
}
