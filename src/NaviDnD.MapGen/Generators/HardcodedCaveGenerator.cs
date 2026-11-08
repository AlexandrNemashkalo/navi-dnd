using NaviDnD.Data.Models;
using NaviDnD.MapGen.Models;

namespace NaviDnD.MapGen.Generators;

// Temporary Muffler: Brings back a fixed 3-zone cave. Replace with a procedural algorithm in the next step.
public class HardcodedCaveGenerator : IMapGenerator
{
    public MapConfig Generate(MapGenRequest request)
    {
        return new MapConfig
        {
            Cols = 20,
            Rows = 15,
            Rooms =
            [
                new Room
                {
                    Name = "Entrance Cave",
                    Color = [101, 67, 33],   // dark brown
                    Positions =
                    [
                        [2, 2], [3, 2], [4, 2],
                        [2, 3], [3, 3], [4, 3], [5, 3],
                        [2, 4], [3, 4], [4, 4], [5, 4], [6, 4],
                        [3, 5], [4, 5], [5, 5],
                        [4, 6],
                    ]
                },
                new Room
                {
                    Name = "Narrow Passage",
                    Color = [70, 70, 80],    // gray-blue
                    Positions =
                    [
                        [7, 4], [8, 4],
                        [7, 5], [8, 5], [9, 5],
                        [9, 6], [10, 6],
                    ]
                },
                new Room
                {
                    Name = "Inner Chamber",
                    Color = [60, 30, 80],    // dark purple
                    Positions =
                    [
                        [11, 5], [12, 5], [13, 5],
                        [10, 6], [11, 6], [12, 6], [13, 6], [14, 6],
                        [10, 7], [11, 7], [12, 7], [13, 7], [14, 7], [15, 7],
                        [11, 8], [12, 8], [13, 8], [14, 8],
                        [12, 9], [13, 9],
                    ]
                },
            ],
            Doors =
            [
                // Passage from Entrance Cave to Narrow Passage (open, without door)
                new Door { From = [6, 4], To = [7, 4], Color = null },
                // Passage from Narrow Passage to Inner Chamber
                new Door { From = [10, 6], To = [11, 6], Color = null },
            ],
            Area = [],
        };
    }
}
