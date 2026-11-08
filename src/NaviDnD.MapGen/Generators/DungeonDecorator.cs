using NaviDnD.Data.Models;

namespace NaviDnD.MapGen.Generators;

// Floor and details of the dungeon (TerrainCatalog, Indoor- views) - so that the rooms are not empty rectangles
// even before being filled with AI. Each room has its own floor style (slabs/rough stone/damp), plus cracks,
// bones, debris; in large halls there are columns, sometimes rubble. Obstacles are not placed at doors and do
// not break up the room (connectivity check after each).
public static class DungeonDecorator
{
    private const int W = MapChunk.Cols, H = MapChunk.Rows;

    public static List<string> Decorate(MapChunk chunk, IReadOnlyList<Room> rooms, IReadOnlyList<Door> doors)
    {
        var rng = new Random(unchecked(chunk.Seed * 31 + 17));
        var g = new char[W, H];
        for (int c = 0; c < W; c++) for (int r = 0; r < H; r++) g[c, r] = ' ';

        // Cells near doors/passages - no obstructions.
        var doorCells = new HashSet<(int, int)>();
        foreach (var d in doors)
            foreach (var p in new[] { d.From, d.To })
                if (p is { Count: >= 2 }) doorCells.Add((p[0], p[1]));

        foreach (var room in rooms)
        {
            var cells = (room.Positions ?? []).Where(p => p.Count >= 2 && chunk.Contains(p[0], p[1]))
                .Select(p => (c: p[0] - chunk.OriginCol - 1, r: p[1] - chunk.OriginRow - 1)).ToList();
            if (cells.Count == 0) continue;
            bool corridor = room.Passage == true;
            bool cave = chunk.Theme == MapChunk.Cave;

            // Room floor style (cave - always natural: stone, moss, puddles, scree).
            int style = cave ? 3 : corridor ? 1 : rng.Next(3);
            foreach (var (c, r) in cells)
            {
                double p = rng.NextDouble();
                g[c, r] = style switch
                {
                    0 => p < 0.07 ? 'K' : p < 0.09 ? 'b' : 'p',                                 // plates
                    1 => p < 0.05 ? 'r' : p < 0.08 ? 'k' : p < 0.095 ? 'b' : 'q',              // rough stone
                    2 => p < 0.14 ? 'm' : p < 0.19 ? 'u' : p < 0.23 ? 'k' : p < 0.25 ? 'b' : 'q', // dampness
                    _ => p < 0.10 ? 'm' : p < 0.16 ? 'u' : p < 0.24 ? 'r' : p < 0.26 ? 'b' : 'q', // cave
                };
            }
            if (corridor || cells.Count < 20) continue;

            var set = cells.ToHashSet();
            bool Interior((int c, int r) x)
            {
                for (int dc = -1; dc <= 1; dc++)
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        var n = (x.c + dc, x.r + dr);
                        if (!set.Contains(n) || doorCells.Contains((n.Item1 + chunk.OriginCol + 1, n.Item2 + chunk.OriginRow + 1))) return false;
                    }
                return true;
            }

            void TryBlock((int c, int r) x, char code)
            {
                if (!Interior(x)) return;
                char was = g[x.c, x.r];
                g[x.c, x.r] = code;
                if (!Connected(g, cells)) g[x.c, x.r] = was;
            }

            if (cave)
            {
                // Scattered stalagmites (1 per ~20 cells in the hall), sometimes a collapse.
                for (int i = 0; i < cells.Count / 20 + rng.Next(2); i++)
                    TryBlock(cells[rng.Next(cells.Count)], 'S');
                if (rng.Next(4) == 0) TryBlock(cells[rng.Next(cells.Count)], 'R');
                continue;
            }

            int roll = rng.Next(10);
            if (roll < 4 && cells.Count >= 30)
            {
                // Hall of Columns: columns in rows across the cell, moving away from the walls.
                int minC = cells.Min(x => x.c), minR = cells.Min(x => x.r);
                int phaseC = rng.Next(2), phaseR = rng.Next(2);
                foreach (var x in cells.Where(x => (x.c - minC) % 3 == 1 + phaseC % 2 && (x.r - minR) % 3 == 1 + phaseR % 2))
                    TryBlock(x, 'P');
            }
            else if (roll < 7)
            {
                // Collapse: blockage and scattering of debris around.
                var center = cells[rng.Next(cells.Count)];
                TryBlock(center, 'R');
                foreach (var x in cells.Where(x => Math.Abs(x.c - center.c) <= 2 && Math.Abs(x.r - center.r) <= 1 && g[x.c, x.r] != 'R'))
                    if (rng.Next(3) == 0) g[x.c, x.r] = 'r';
            }
        }

        var rows = new List<string>(H);
        for (int r = 0; r < H; r++)
        {
            var line = new char[W];
            for (int c = 0; c < W; c++) line[c] = g[c, r];
            rows.Add(new string(line));
        }
        return rows;
    }

    // All passable cells in the room are connected to each other (4 neighbors).
    private static bool Connected(char[,] g, List<(int c, int r)> cells)
    {
        var free = cells.Where(x => TerrainCatalog.Get(g[x.c, x.r]) is not { BlocksMove: true }).ToHashSet();
        if (free.Count == 0) return false;
        var start = free.First();
        var seen = new HashSet<(int, int)> { start };
        var queue = new Queue<(int c, int r)>([start]);
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            foreach (var n in new[] { (c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1) })
                if (free.Contains(n) && seen.Add(n)) queue.Enqueue(n);
        }
        return seen.Count == free.Count;
    }
}
