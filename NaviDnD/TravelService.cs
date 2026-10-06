using System.Text;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD;

// Путешествие по карте мира (D&D 5e, «Путешествие»): маршрут пешком по клеткам мира (клетка ≈ четверть дня по дороге, WorldAtlas.CellDays),
// темп (быстрый — дальше, но хуже замечаешь; медленный — короче, но можно идти скрытно), привалы с долгим отдыхом,
// случайные встречи по опасности края (дорога в королевстве — спокойно, глушь — опаснее; враждебных — меньше
// мирных и находок), навигация вне дорог. Механику (путь, время, броски встреч) считает код; мастер одним вызовом
// описывает дорогу и разыгрывает встречу (GameAiClient.Travel).
public static class TravelService
{
    public enum Pace { Slow, Normal, Fast }

    public static string PaceName(Pace p) => p switch { Pace.Fast => "быстрый", Pace.Slow => "медленный", _ => "обычный" };

    // Сколько пути за то же время: быстрый — 30 миль в день вместо 24, медленный — 18.
    private static double Speed(Pace p) => p switch { Pace.Fast => 1.25, Pace.Slow => 0.75, _ => 1.0 };

    // Еда в пути: что из еды есть в инвентаре (id, количество) — мастер списывает её патчем; не хватает — фураж,
    // добытое сверх нужного — в инвентарь (иначе запас добытой еды терялся).
    private static readonly System.Text.RegularExpressions.Regex FoodName = new(
        "рацион|паёк|паек|провизи|еда|пищ|вялен|сухар|хлеб|сыр|мяс|рыб|ягод|орех|фрукт|яблок|похлёбк|похлебк|дичь",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string FoodLine(WorldState ws, int days)
    {
        var inv = ws.Hero?.Inventory ?? [];
        var food = inv.Select((it, i) => (it, i))
            .Where(t => t.it.Deleted != true && t.it.Name is { } n && FoodName.IsMatch(n))
            .Select(t => $"{t.it.Name} (id {t.i}) ×{t.it.Quantity ?? 1}").ToList();
        // Еда в составе набора («Набор путешественника: … рационы 10 …») — тоже еда: её выносят отдельным предметом.
        var packed = inv.Select((it, i) => (it, i))
            .Where(t => t.it.Deleted != true && !FoodName.IsMatch(t.it.Name ?? "") && t.it.Description is { } d && FoodName.IsMatch(d))
            .Select(t => $"в «{t.it.Name}» (id {t.i}): {t.it.Description}").ToList();
        string have = food.Count > 0 ? string.Join(", ", food) : "отдельной еды нет";
        string inKits = packed.Count > 0 ? $" Еда в составе наборов — {string.Join("; ", packed)}: вынеси её отдельным предметом с quantity (и убери из описания набора), спиши съеденное." : "";
        return $"Еда: в пути {days} {DayWord(days)} — по 1 дневной порции за день. В инвентаре: {have}.{inKits} "
               + "Спиши съеденное патчем hero.inventory ({\"id\":N,\"quantity\":остаток}, кончилось — deleted). Не хватило — фураж: ОДИН roll_dice Выживание СЛ 10 на всю нехватку "
               + "(успех — еды хватило, провал — половина недостающих дней впроголодь, истощение по правилам); добыто сверх нужного — излишек в инвентарь предметом с quantity («Добытая еда»), не теряй. "
               + "Еду, которая есть, не называй отсутствующей.";
    }

    public sealed record Plan((int x, int y) From, (int x, int y) To, string? TargetPlace, WorldAtlas.RouteInfo Route)
    {
        public double Days(Pace pace) => DaysAt(Route.Days, pace);
    }

    public static double DaysAt(double normalDays, Pace pace) => Math.Max(0.5, Math.Round(normalDays / Speed(pace) * 2) / 2);

    // Маршрут героя до клетки мира (или места на ней).
    public static Plan? PlanTo(WorldState ws, (int x, int y) target)
    {
        if (GameWorld.HeroTile(ws) is not { } from || GameWorld.ForMaster(ws) is not { } world) return null;
        if (from == target) return null;
        var route = WorldAtlas.Route(world, from, target);
        if (route == null) return null;
        var place = world.Places.FirstOrDefault(p => p.X == target.x && p.Y == target.y && (!p.Hidden));
        return new Plan(from, target, place?.Name, route);
    }

    // Пройти маршрут: герой идёт по дням, каждый день — бросок встречи; выпала — путь прерывается там.
    // Двигает героя и время, уводит из локации; возвращает сводку для мастера.
    public static string Go(WorldState ws, Plan plan, Pace pace, Random rng)
    {
        var world = GameWorld.ForMaster(ws)!;
        var link = ws.World!;
        string fromName = link.Place ?? WorldAtlas.TileName(world, plan.From.x, plan.From.y);
        GameWorld.LeaveLocation(ws);
        // События, назначенные на раунд сцены, которую герой покинул: счёт раундов в пути обнуляется, прошли
        // дни — без отмены они «сработали» бы потом не к месту.
        var dropped = (ws.ScheduledEvents ?? []).Where(e => e.Deleted != true).Select(e => e.Name).ToList();
        ws.ScheduledEvents = [];

        // Клетки пути по дням (четверть дня на клетку по дороге, вне дороги — дольше; темп меняет дневной переход).
        var path = plan.Route.Path;
        double perDay = 1.0 * Speed(pace);
        // День клетки — по накопленному времени пути (как в оценке маршрута): сколько дней показали при выборе
        // цели, столько герой и идёт.
        var days = new List<List<(int x, int y)>>();
        double acc = 0;
        for (int i = 1; i < path.Count; i++)
        {
            double step = Math.Min(WorldAtlas.StepDays(world, path[i].x, path[i].y), 6 * WorldAtlas.CellDays);
            if (i == path.Count - 1) step = Math.Min(step, 1.5 * WorldAtlas.CellDays);   // как в WorldAtlas.Route
            int day = Math.Max(0, (int)Math.Ceiling((acc + step) / perDay - 1e-9) - 1);
            acc += step;
            while (days.Count <= day) days.Add([]);
            days[day].Add(path[i]);
        }
        days.RemoveAll(d => d.Count == 0);

        // Встречи: шанс в день по опасности края, тип — чаще мирная встреча или находка, реже враги.
        int stopDay = -1;
        string? encounter = null;
        for (int d = 0; d < days.Count && stopDay < 0; d++)
        {
            double chance = days[d].Max(t => Danger(world, t.x, t.y));
            if (pace == Pace.Slow) chance *= 0.8;
            if (rng.NextDouble() >= chance) continue;
            double r = rng.NextDouble();
            double hostile = pace == Pace.Slow ? 0.18 : 0.25;
            encounter = r < hostile ? "враждебная (засада, хищник, разбойники — по силам героя, можно избежать или договориться)"
                : r < hostile + 0.45 ? "мирная (путники, торговец, патруль, отшельник — разговор, слухи, просьба)"
                : "находка (след, руины, тайник, знак, необычное место — зацепка, можно добавить место на карту)";
            stopDay = d;
        }

        int walked = stopDay >= 0 ? stopDay + 1 : days.Count;
        var walkedTiles = days.Take(walked).SelectMany(x => x).ToList();
        var end = walkedTiles.Count > 0 ? walkedTiles[^1] : plan.From;
        bool arrived = stopDay < 0;

        // Время: в путь — с утра (вечером/ночью — со следующего утра), день пути — до вечера, привал, ночь.
        int startDay = ws.Time.Day + (ws.Time.PartOfDay is "Вечер" or "Ночь" ? 1 : 0);
        ws.Time.Day = startDay + walked - 1;
        ws.Time.PartOfDay = "Вечер";
        ws.Time.TotalRounds = 1;

        link.X = end.x; link.Y = end.y;
        var arrivedPlace = arrived && plan.TargetPlace != null ? WorldAtlas.FindPlace(world, plan.TargetPlace) : null;
        link.Place = arrivedPlace?.Name;
        if (arrivedPlace != null && !link.Visited.Contains(arrivedPlace.Name)) link.Visited.Add(arrivedPlace.Name);
        link.Version++;

        // Сводка для мастера.
        var sb = new StringBuilder();
        sb.AppendLine("## ПУТЕШЕСТВИЕ (движок уже переместил героя по карте мира и сдвинул время — time и world.place не меняй)");
        string toName = plan.TargetPlace ?? WorldAtlas.TileName(world, plan.To.x, plan.To.y);
        sb.AppendLine($"Откуда: {fromName} → куда: {toName}, {WorldAtlas.Direction(plan.To.x - plan.From.x, plan.To.y - plan.From.y)} (направление пути — только это). Темп: {PaceName(pace)}{(pace == Pace.Fast ? " (−5 к пассивной Внимательности в пути)" : pace == Pace.Slow ? " (можно идти скрытно)" : "")}.");
        double roadShare = walkedTiles.Count == 0 ? 1 : walkedTiles.Count(t => world.RoadAt(t.x, t.y) > 0) / (double)walkedTiles.Count;
        sb.AppendLine($"В пути: {walked} {DayWord(walked)} из {days.Count}, по дороге {roadShare:P0}; местность: {Terrains(world, walkedTiles)}.");
        var rivers = Rivers(world, walkedTiles);
        if (rivers.Length > 0) sb.AppendLine($"Реки: {rivers}.");
        var kingdoms = walkedTiles.Select(t => world.OwnerAt(t.x, t.y)).Distinct().Select(o => o >= 0 && o < world.Kingdoms.Count ? world.Kingdoms[o].Name : "ничьи земли").ToList();
        sb.AppendLine($"Земли: {string.Join(" → ", kingdoms.Distinct())}.");
        var near = world.Places.Where(p => walkedTiles.Any(t => Math.Abs(t.x - p.X) <= 2 && Math.Abs(t.y - p.Y) <= 2)
                && p.Name != plan.TargetPlace && p.Name != fromName)
            .Select(p => $"{p.Name} ({WorldPlaceTypes.Label(p.Type)}{(p.Hidden ? ", герой не знает" : "")})").ToList();
        if (near.Count > 0) sb.AppendLine($"Рядом с дорогой: {string.Join(", ", near.Take(6))}.");
        int nights = Math.Max(walked - 1, 0);
        if (dropped.Count > 0)
            sb.AppendLine($"Отложенные события прежней сцены отменены движком (герой ушёл, прошли дни): {string.Join("; ", dropped)} — если важны, учти их исход в рассказе.");
        sb.AppendLine($"Привалы: ночёвок в пути — {nights}; по прибытии/на стоянке — долгий отдых (long_rest), если не помешали.");
        sb.AppendLine(FoodLine(ws, walked));
        if (roadShare < 0.5) sb.AppendLine("Вне дорог больше половины пути: проверка навигации — roll_dice Выживание СЛ 10 (лес/холмы) … 15 (горы/болото/пустыня); провал — сбился, +1 день (time.day+1), опиши блуждание.");
        if (encounter != null)
            sb.AppendLine($"ВСТРЕЧА на {walked}-й день, {WorldBiomes.Get(world.BiomeAt(end.x, end.y)).Name.ToLowerInvariant()} ({WorldAtlas.Surroundings(world, end.x, end.y, named: true)}): {encounter}. Путь прерван здесь — разыграй встречу по месту и королевству; нужна тактическая сцена (бой, обыск руин) — plan_location (местность клетки движок подставит сам), иначе — текстом. После неё герой может продолжить путь сам.");
        else if (arrivedPlace != null)
        {
            bool saved = GameWorld.Geo(ws) is { } geo && GameWorld.SavedLocationJson(geo, arrivedPlace.Name) != null;
            sb.AppendLine($"Прибытие: {arrivedPlace.Name} — {WorldPlaceTypes.Label(arrivedPlace.Type)}.{(arrivedPlace.Description is { Length: > 0 } ad ? " " + ad : "")}"
                          + (saved ? " Здесь уже бывали: движок открыл сохранённую локацию — герой снаружи у входа; ночлег или вход внутрь — опиши и переставь героя патчем hero.position." : " Тактической карты нет — веди сцену текстом; нужна (бой, подземелье, исследование) — plan_location."));
        }
        else sb.AppendLine($"Прибытие: герой на месте — {WorldBiomes.Get(world.BiomeAt(end.x, end.y)).Name.ToLowerInvariant()}, {WorldAtlas.Surroundings(world, end.x, end.y, named: true)}. Там, где нет места на карте, можно найти что-то (add_place) или просто разбить лагерь.");
        sb.AppendLine("Ответ — короткий рассказ о пути (2–4 записи history, без пересказа каждого дня), еда/отдых — патчем.");
        return sb.ToString();
    }

    // Опасность края: шанс встречи за день пути.
    private static double Danger(WorldMap w, int x, int y)
    {
        bool road = w.RoadAt(x, y) > 0, owned = w.OwnerAt(x, y) >= 0;
        double d = road && owned ? 0.05 : owned ? 0.09 : 0.14;
        if (w.BiomeAt(x, y) is WorldBiomes.Swamp or WorldBiomes.Mountains or WorldBiomes.DeepForest or WorldBiomes.Peaks) d += 0.03;
        return d;
    }

    private static string Terrains(WorldMap w, List<(int x, int y)> tiles) =>
        tiles.Count == 0 ? "—" : string.Join(", ", tiles.GroupBy(t => WorldBiomes.Get(w.BiomeAt(t.x, t.y)).Name.ToLowerInvariant())
            .OrderByDescending(g => g.Count()).Take(4).Select(g => g.Key));

    private static string Rivers(WorldMap w, List<(int x, int y)> tiles)
    {
        int bridges = tiles.Count(t => w.RiverAt(t.x, t.y) > 0 && w.RoadAt(t.x, t.y) > 0);
        int fords = tiles.Count(t => w.RiverAt(t.x, t.y) > 0 && w.RoadAt(t.x, t.y) == 0);
        var parts = new List<string>();
        if (bridges > 0) parts.Add($"мостов {bridges}");
        if (fords > 0) parts.Add($"бродов {fords} (переправа — Атлетика СЛ 10 или в обход)");
        return string.Join(", ", parts);
    }

    private static string DayWord(int d) =>
        d % 10 == 1 && d % 100 != 11 ? "день" : d % 10 is >= 2 and <= 4 && d % 100 is < 12 or > 14 ? "дня" : "дней";
}
