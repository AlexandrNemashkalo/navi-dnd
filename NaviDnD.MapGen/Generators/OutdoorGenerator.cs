using NaviDnD.Data.Models;

namespace NaviDnD.MapGen.Generators;

// Блок открытой местности (лес, равнина, холмы/горы, болото): без комнат и стен, клетки — рельеф
// (TerrainCatalog). Рисунок — по шуму от координат поля (соседние блоки стыкуются без шва), края без
// соседа-местности закрыты естественной преградой (чаща, скалы, вода), от каждого выхода к центру —
// тропа; недостижимые карманы соединяются бродами/просеками.
public static class OutdoorGenerator
{
    public sealed record Result(List<string> Terrain, List<Door> Doors);

    // Река через локацию (по карте мира): поперёк поля по оси, центр — строка/столбец поля, ширина глубокой
    // воды в клетках (по краям — мелководье); петляет по шуму. Тропы пересекают её бродом.
    public sealed record RiverSpec(bool Horizontal, int Center, int Width, int Seed);

    private const int W = MapChunk.Cols, H = MapChunk.Rows;

    // bands: сторона → код преграды по краю (нет стороны — край открыт: там соседний блок местности).
    public static Result Generate(MapChunk chunk, IReadOnlyList<ChunkExit> exits, IReadOnlyDictionary<ChunkSide, char> bands, int noiseSeed,
        RiverSpec? river = null)
    {
        var rng = new Random(chunk.Seed);
        var g = new char[W, H];
        string theme = chunk.Theme;
        char ground = theme switch { "mountain" => 's', "swamp" => ',', "desert" => 'a', "snow" or "taiga" => 'n', _ => '.' };

        for (int c = 0; c < W; c++)
            for (int r = 0; r < H; r++)
            {
                int fc = chunk.OriginCol + c + 1, fr = chunk.OriginRow + r + 1;
                double n = Fbm(fc, fr, noiseSeed), m = Fbm(fc, fr, noiseSeed + 101);
                double p = rng.NextDouble();
                g[c, r] = theme switch
                {
                    "forest" => n > 0.68 ? 'F' : n > 0.52 ? (p < 0.7 ? 'T' : '"')
                        : p < 0.10 ? 'T' : p < 0.18 ? '"' : p < 0.40 ? ',' : m > 0.82 ? 'o' : '.',
                    "plains" => n > 0.78 ? (p < 0.6 ? 'T' : '"') : m > 0.80 ? '"'
                        : p < 0.025 ? 'T' : p < 0.04 ? 'o' : p < 0.40 ? ',' : '.',
                    "hills" or "mountain" => n > 0.64 ? '^' : n > 0.54 ? 'g'
                        : p < 0.05 ? 'o' : p < 0.10 ? 'T' : p < 0.25 ? (theme == "hills" ? ',' : 'g') : ground,
                    "swamp" => n > 0.64 ? 'W' : n > 0.52 ? '~' : m > 0.58 ? ':'
                        : p < 0.08 ? 'T' : p < 0.16 ? '"' : p < 0.5 ? ',' : '.',
                    "desert" => n > 0.62 ? 'd' : m > 0.84 ? 'g' : p < 0.03 ? 'c' : p < 0.05 ? 'o' : 'a',
                    "snow" => n > 0.72 ? 'e' : m > 0.8 ? 'o' : p < 0.03 ? 'e' : p < 0.06 ? 'g' : 'n',
                    "taiga" => n > 0.62 ? 'e' : n > 0.5 ? (p < 0.6 ? 'e' : 'n') : p < 0.12 ? 'e' : p < 0.16 ? '"' : 'n',
                    _ => ground,
                };
            }

        // Река поперёк поля: глубокая вода посередине, мелководье по краям.
        if (river != null)
            for (int i = 0; i < (river.Horizontal ? W : H); i++)
            {
                int along = river.Horizontal ? chunk.OriginCol + i + 1 : chunk.OriginRow + i + 1;
                int center = river.Center + (int)Math.Round(3 * Math.Sin(along / 6.0 + river.Seed % 7) + 3 * (Fbm(along, river.Seed % 997, noiseSeed + 33) - 0.5));
                for (int k = -river.Width / 2 - 1; k <= (river.Width - 1) / 2 + 1; k++)
                {
                    int across = center + k;
                    int local = across - (river.Horizontal ? chunk.OriginRow : chunk.OriginCol) - 1;
                    if (local < 0 || local >= (river.Horizontal ? H : W)) continue;
                    bool edge = k == -river.Width / 2 - 1 || k == (river.Width - 1) / 2 + 1;
                    if (river.Horizontal) g[i, local] = edge ? '~' : 'W';
                    else g[local, i] = edge ? '~' : 'W';
                }
            }

        // Края без соседа-местности: полоса преграды 1–3 клетки.
        foreach (var (side, code) in bands)
        {
            int len = side is ChunkSide.Left or ChunkSide.Right ? H : W;
            for (int i = 0; i < len; i++)
            {
                var (bc, br) = SideCell(side, i, 0);
                double n = Fbm(chunk.OriginCol + bc + 1, chunk.OriginRow + br + 1, noiseSeed + 7);
                int depth = 1 + (n > 0.45 ? 1 : 0) + (n > 0.7 ? 1 : 0);
                for (int d = 0; d < depth; d++)
                {
                    var (c, r) = SideCell(side, i, d);
                    g[c, r] = code;
                }
            }
        }

        // Поляна в центре и тропы от выходов к ней.
        const int hubC = W / 2, hubR = H / 2;
        for (int c = hubC - 2; c <= hubC + 2; c++)
            for (int r = hubR - 1; r <= hubR + 1; r++)
                if (Kind(g[c, r]).BlocksMove) g[c, r] = ground;
        char trail = theme is "mountain" or "hills" ? 's' : theme == "swamp" ? '.' : theme == "desert" ? 'a' : '=';
        foreach (var exit in exits)
        {
            var (ac, ar) = exit.AnchorCell(chunk);
            Carve(g, ac - chunk.OriginCol - 1, ar - chunk.OriginRow - 1, hubC, hubR, trail, rng);
        }

        ConnectPockets(g, hubC, hubR, ground);

        // Вход снаружи — дверь-выход на карту мира на краю (LocationGrower.MarkWorldExit делает её фиолетовой).
        var doors = new List<Door>();
        foreach (var exit in exits.Where(e => e.External))
        {
            var (ac, ar) = exit.AnchorCell(chunk);
            var (dx, dy) = ChunkExit.Step(exit.Side);
            doors.Add(new Door { From = [ac, ar], To = [ac + dx, ar + dy], IsEntry = true, IsDoor = false });
        }

        var rows = new List<string>(H);
        for (int r = 0; r < H; r++)
        {
            var line = new char[W];
            for (int c = 0; c < W; c++) line[c] = g[c, r];
            rows.Add(new string(line));
        }
        return new Result(rows, doors);
    }

    private static TerrainKind Kind(char code) => TerrainCatalog.Get(code) ?? TerrainCatalog.Kinds['.'];

    // Клетка на расстоянии depth внутрь от стороны, i — вдоль стороны (локальные координаты 0-based).
    private static (int c, int r) SideCell(ChunkSide side, int i, int depth) => side switch
    {
        ChunkSide.Left => (depth, i),
        ChunkSide.Right => (W - 1 - depth, i),
        ChunkSide.Bottom => (i, depth),
        _ => (i, H - 1 - depth),
    };

    // Тропа от (c, r) к (tc, tr): шаг к цели с редкими отклонениями; преграды на пути убираются.
    private static void Carve(char[,] g, int c, int r, int tc, int tr, char trail, Random rng)
    {
        for (int guard = 0; guard < 200; guard++)
        {
            if (c >= 0 && c < W && r >= 0 && r < H && (Kind(g[c, r]).BlocksMove || g[c, r] is '.' or ',' or 's'))
                g[c, r] = Kind(g[c, r]).Code == 'W' ? '~' : trail;
            if (c == tc && r == tr) return;
            bool horizontal = Math.Abs(tc - c) > Math.Abs(tr - r) ? rng.Next(5) > 0 : rng.Next(5) == 0;
            if (horizontal && c != tc) c += Math.Sign(tc - c);
            else if (r != tr) r += Math.Sign(tr - r);
            else c += Math.Sign(tc - c);
        }
    }

    // Недостижимые от центра проходимые клетки соединяются просекой/бродом.
    private static void ConnectPockets(char[,] g, int hubC, int hubR, char ground)
    {
        for (int iteration = 0; iteration < 30; iteration++)
        {
            var reached = Flood(g, hubC, hubR);
            (int c, int r)? pocket = null;
            for (int c = 0; c < W && pocket == null; c++)
                for (int r = 0; r < H; r++)
                    if (!Kind(g[c, r]).BlocksMove && !reached[c, r]) { pocket = (c, r); break; }
            if (pocket is not var (pc, pr)) return;

            // От кармана к центру, пока не упрёмся в достижимую клетку.
            while (!reached[pc, pr])
            {
                if (Kind(g[pc, pr]).BlocksMove) g[pc, pr] = g[pc, pr] == 'W' ? '~' : ground;
                if (pc == hubC && pr == hubR) break;
                if (Math.Abs(hubC - pc) >= Math.Abs(hubR - pr)) pc += Math.Sign(hubC - pc);
                else pr += Math.Sign(hubR - pr);
            }
        }
    }

    private static bool[,] Flood(char[,] g, int sc, int sr)
    {
        var seen = new bool[W, H];
        if (Kind(g[sc, sr]).BlocksMove) return seen;
        var queue = new Queue<(int, int)>([(sc, sr)]);
        seen[sc, sr] = true;
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            foreach (var (nc, nr) in new[] { (c + 1, r), (c - 1, r), (c, r + 1), (c, r - 1) })
            {
                if (nc < 0 || nr < 0 || nc >= W || nr >= H || seen[nc, nr] || Kind(g[nc, nr]).BlocksMove) continue;
                seen[nc, nr] = true;
                queue.Enqueue((nc, nr));
            }
        }
        return seen;
    }

    // Шум по координатам поля: две октавы сглаженного значения решётки.
    private static double Fbm(int x, int y, int seed) => 0.65 * Noise(x / 5.0, y / 5.0, seed) + 0.35 * Noise(x / 2.3, y / 2.3, seed + 1);

    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed);
        double c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    private static double Lattice(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + seed * 982451653));
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }
}
