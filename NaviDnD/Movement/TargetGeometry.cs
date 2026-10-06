using NaviDnD.Data.Models;

namespace NaviDnD;

// Геометрия select_target: какие клетки можно выбрать и какие клетки накрывает область.
// Дистанция — как у зрения героя (5-10-5 по диагонали), область-круг — по центрам клеток.
public static class TargetGeometry
{
    // Половина угла конуса: ширина конуса в любой точке равна расстоянию от вершины → atan(1/2).
    private static readonly double ConeHalfAngleCos = Math.Cos(Math.Atan(0.5)) - 1e-6;

    public static int DistanceFt(int c1, int r1, int c2, int r2)
    {
        int adx = Math.Abs(c1 - c2), ady = Math.Abs(r1 - r2);
        int diag = Math.Min(adx, ady);
        int straight = Math.Abs(adx - ady);
        return straight * 5 + (diag > 0 ? 5 + (diag - 1) * 10 : 0);
    }

    // Конус и линия идут от героя — выбранная клетка задаёт только направление.
    public static bool IsDirectional(string? shape) => shape is "cone" or "line";

    // isVisible — клетку герой видит сейчас; isExplored — клетка исследована.
    public static HashSet<(int col, int row)> SelectableCells(
        WorldState ws, TargetSelectionRequest req, Func<int, int, bool> isVisible, Func<int, int, bool> isExplored)
    {
        var result = new HashSet<(int, int)>();
        if (ws.Hero?.Position is not { Count: >= 2 } hp) return result;
        int hc = hp[0], hr = hp[1];

        bool InRange(int c, int r) => req.RangeFt is not int range || DistanceFt(hc, hr, c, r) <= range;

        if (req.Mode == "creature")
        {
            foreach (var e in LivingOnMap(ws))
            {
                int c = e.Position[0], r = e.Position[1];
                if (e == ws.Hero || (isVisible(c, r) && InRange(c, r) && MovementCalculator.HasLineOfSight(ws, hc, hr, c, r)))
                    result.Add((c, r));
            }
            return result;
        }

        // Поле локации до 100×100 — перебираем только рамку содержимого карты, не всё поле.
        var (c0, c1, r0, r1) = ScanBounds(ws);
        for (int c = c0; c <= c1; c++)
        for (int r = r0; r <= r1; r++)
        {
            // Конус/линия: клетка задаёт направление — допустимы клетки, которые фигура может накрыть
            // (в пределах её длины, по центрам клеток, как в AreaCells).
            bool directional = IsDirectional(req.Shape);
            if (directional && c == hc && r == hr) continue;
            if (directional && !WithinLength(c - hc, r - hr, req.SizeFt)) continue;
            if ((isVisible(c, r) || isExplored(c, r)) && (directional || InRange(c, r))
                && MovementCalculator.HasLineOfSight(ws, hc, hr, c, r))
                result.Add((c, r));
        }
        return result;
    }

    // Клетки области. Без формы — только сама точка. Стены режут область (нужна прямая видимость от её начала).
    public static HashSet<(int col, int row)> AreaCells(WorldState ws, int pointCol, int pointRow, string? shape, int? sizeFt)
    {
        var result = new HashSet<(int, int)>();
        int size = Math.Max(5, sizeFt ?? 5);
        double lengthCells = size / 5.0;

        if (shape is null or "")
        {
            result.Add((pointCol, pointRow));
            return result;
        }

        if (IsDirectional(shape))
        {
            if (ws.Hero?.Position is not { Count: >= 2 } hp) return result;
            int hc = hp[0], hr = hp[1];
            double vx = pointCol - hc, vy = pointRow - hr;
            double vlen = Math.Sqrt(vx * vx + vy * vy);
            if (vlen == 0) return result;
            vx /= vlen; vy /= vlen;

            int reach = (int)Math.Ceiling(lengthCells);
            for (int c = Math.Max(1, hc - reach); c <= Math.Min(ws.Map.Cols, hc + reach); c++)
            for (int r = Math.Max(1, hr - reach); r <= Math.Min(ws.Map.Rows, hr + reach); r++)
            {
                double dx = c - hc, dy = r - hr;
                double along = dx * vx + dy * vy;
                if (along <= 0 || along > lengthCells + 1e-6) continue;

                bool inside = shape == "line"
                    ? Math.Abs(dx * vy - dy * vx) <= 0.5 + 1e-6
                    : along / Math.Sqrt(dx * dx + dy * dy) >= ConeHalfAngleCos
                      && Math.Sqrt(dx * dx + dy * dy) <= lengthCells + 1e-6;
                if (inside && MovementCalculator.HasLineOfSight(ws, hc, hr, c, r))
                    result.Add((c, r));
            }
            return result;
        }

        if (shape == "cube")
        {
            int n = Math.Max(1, size / 5);
            int fromC = pointCol - (n - 1) / 2, fromR = pointRow - (n - 1) / 2;
            for (int c = fromC; c < fromC + n; c++)
            for (int r = fromR; r < fromR + n; r++)
                if (IsOnGrid(ws, c, r) && MovementCalculator.HasLineOfSight(ws, pointCol, pointRow, c, r))
                    result.Add((c, r));
            return result;
        }

        // sphere (и всё неизвестное) — круг по центрам клеток
        int radius = (int)Math.Ceiling(lengthCells);
        for (int c = pointCol - radius; c <= pointCol + radius; c++)
        for (int r = pointRow - radius; r <= pointRow + radius; r++)
        {
            double dx = c - pointCol, dy = r - pointRow;
            if (dx * dx + dy * dy <= lengthCells * lengthCells + 1e-6
                && IsOnGrid(ws, c, r) && MovementCalculator.HasLineOfSight(ws, pointCol, pointRow, c, r))
                result.Add((c, r));
        }
        return result;
    }

    public static SelectedTarget Describe(WorldState ws, int col, int row)
    {
        var hp = ws.Hero?.Position;
        var occupant = LivingOnMap(ws).FirstOrDefault(e => e.Position[0] == col && e.Position[1] == row);
        int entityIdx = occupant == null || occupant == ws.Hero ? -1 : ws.Map.Entities?.IndexOf(occupant) ?? -1;
        return new SelectedTarget
        {
            Id = entityIdx >= 0 ? entityIdx : null,
            Symbol = occupant?.Symbol,
            Name = occupant?.Name,
            Position = [col, row],
            DistanceFt = hp is { Count: >= 2 } ? DistanceFt(hp[0], hp[1], col, row) : 0
        };
    }

    public static List<SelectedTarget> CreaturesIn(WorldState ws, IEnumerable<(int col, int row)> cells)
    {
        var set = cells.ToHashSet();
        return LivingOnMap(ws)
            .Where(e => set.Contains((e.Position[0], e.Position[1])))
            .Select(e => Describe(ws, e.Position[0], e.Position[1]))
            .ToList();
    }

    public static List<SelectedTarget> ObjectsIn(WorldState ws, IEnumerable<(int col, int row)> cells)
    {
        var set = cells.ToHashSet();
        var hp = ws.Hero?.Position;
        return (ws.Map.Objects ?? [])
            .Select((o, i) => (o, i))
            .Where(x => x.o.Deleted != true && x.o.Hidden != true && x.o.Position is { Count: >= 2 }
                        && set.Contains((x.o.Position[0], x.o.Position[1])))
            .Select(x => new SelectedTarget
            {
                Id = x.i,
                Symbol = x.o.Symbol,
                Name = x.o.Name,
                Position = [x.o.Position[0], x.o.Position[1]],
                DistanceFt = hp is { Count: >= 2 } ? DistanceFt(hp[0], hp[1], x.o.Position[0], x.o.Position[1]) : 0
            })
            .ToList();
    }

    private static IEnumerable<LivingEntity> LivingOnMap(WorldState ws)
    {
        if (ws.Hero?.Position is { Count: >= 2 }) yield return ws.Hero;
        foreach (var e in ws.Map.Entities ?? [])
            if (e.Deleted != true && e.Hidden != true && e.Position is { Count: >= 2 })
                yield return e;
    }

    private static bool WithinLength(int dx, int dy, int? sizeFt)
    {
        double length = Math.Max(5, sizeFt ?? 5) / 5.0;
        return dx * dx + dy * dy <= length * length + 1e-6;
    }

    private static (int c0, int c1, int r0, int r1) ScanBounds(WorldState ws) =>
        ws.Map.ContentBounds() is { } b
            ? (Math.Max(1, b.minCol - 1), Math.Min(ws.Map.Cols, b.maxCol + 1), Math.Max(1, b.minRow - 1), Math.Min(ws.Map.Rows, b.maxRow + 1))
            : (1, ws.Map.Cols, 1, ws.Map.Rows);

    private static bool IsOnGrid(WorldState ws, int c, int r) => c >= 1 && c <= ws.Map.Cols && r >= 1 && r <= ws.Map.Rows;
}
