using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Display;

// Новая игра по шагам, в стиле анкеты героя (рамки по центру, поля слева, превью справа): 1 МИР — новый (карта
// сразу, концепция от нейронки по «ПРИДУМАТЬ») или мир, где уже играли; 2 ГЕРОЙ — анкета (NewGameDisplay: новый
// или из библиотеки героев); 3 ПРИКЛЮЧЕНИЕ — после редактора: пожелания к сюжету и, по желанию, место старта
// кликом по карте. Блоки: «МИР»/«ПРИКЛЮЧЕНИЕ» (выбор и поля), «КАРТА» (превью), «О МИРЕ» (концепция).
public class NewGameWizard
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly GameAiClient _ai;
    private readonly BorderDrawer _border;
    public int StartTop { get; }

    // Заголовок экрана (шаги «[F1]МИР [F2]ГЕРОЙ …», «[Esc]МЕНЮ») — клики по нему; CanGo — на какой шаг уже можно.
    public string Title = "";
    public Func<int, bool> CanGo = _ => false;
    // Мир, с которым перешли дальше (шаг 1).
    public string? ChosenWorldId { get; private set; }
    // Состояние шага «Мир» между заходами на него (вернулся с другого шага — всё как было).
    private sealed class WorldPick
    {
        public List<WorldEntry> Entries = [];
        public int Sel, Seed;
        public int[] Param = [];
        public string Name = "", DraftKey = "", NewDesc = "", Prompt = "";
        public bool NameEdited;
        public WorldMap? Draft;
        public string? RegisteredId;   // мир этого создания игры, уже записанный в библиотеку
    }
    private WorldPick? _pick;
    private string? _titleHover;
    private int _clickCol;   // колонка холста последнего клика (курсор в тексте по клику)
    // Карта с выбором старта: клетка под курсором, перетаскивание (начало — запомнить камеру, сдвиг в символах).
    private (int col, int row)? _mapHover;
    private Action? MapPanBegin;
    private Action<int, int>? MapPan;

    private int _width, _height, _x0, _bodyTop;
    private char[,] _chars = new char[0, 0];
    private List<int>?[,] _fg = new List<int>?[0, 0];
    private List<int>?[,] _bgc = new List<int>?[0, 0];
    private readonly List<(int row, int x, int w, string id)> _hits = [];
    private bool _frozen;
    private (int left, int top, int w, int h, WorldMapView view, WorldMap world)? _map;
    private string? _hovered;
    // Что сейчас нарисовано на месте карты: та же карта (мир, концепция, отметка) — не перерисовывается.
    private string? _mapDrawn;
    private (int left, int top, int w, int h)? _mapRect;

    private List<int> Fg => _display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(_display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(_display.MainForeground, 0.5);

    // Раскладка: форма 46, превью 68 (карта до 64×24 — весь мир), промежуток 3.
    private const int FormWidth = 46, MapBoxWidth = 68, Gap = 3, MapW = 64, MapH = 24;
    private const int LabelCol = 3, ValueCol = 16;
    private const int TopBoxHeight = MapH + 2, InfoHeight = 9;

    public NewGameWizard(WorldState settings, DisplayConfig display, GameAiClient ai)
    {
        _settings = settings;
        _display = display;
        _ai = ai;
        _border = new BorderDrawer(settings, display);
        StartTop = Console.CursorTop;
    }

    // ── Шаг 1: мир ───────────────────────────────────────────────────────────

    // Мир в листалке: из библиотеки (Id), вариант этого создания игры (Draft — готов, в файл только если выбран)
    // или новый (оба null).
    private sealed record WorldEntry(string? Id, string Label, WorldMap? Draft = null);

    // Параметры нового мира: ←→ на поле — следующий вариант; карта сразу перестраивается (тот же seed).
    // Сколько чего-то в мире; индекс 3 («обычно») — 0 у генератора, «нет» — -3.
    private static readonly string[] Amount = ["нет", "очень мало", "мало", "обычно", "много", "очень много"];

    // def — вариант по умолчанию; для шкал он же «ноль» генератора (значение = индекс - def).
    private static readonly (string label, string[] options, int def)[] Params =
    [
        ("РАЗМЕР", ["малый", "большой"], 0),
        ("КОНТИНЕНТЫ", ["авто", "1", "2", "3", "4", "5"], 0),
        ("ВОДА", ["очень мало", "мало", "обычно", "много", "очень много"], 2),
        ("КЛИМАТ", ["ледяной", "холодный", "умеренный", "тёплый", "жаркий"], 2),
        ("ГОРЫ", Amount, 3),
        ("ЛЕСА", Amount, 3),
        ("ПУСТЫНИ", Amount, 3),
        ("РЕКИ", Amount, 3),
        ("КОРОЛЕВСТВ", ["авто", "1", "2", "3", "4", "5", "6", "7", "8"], 0),
    ];

    private const int DescHeight = 13, DescLines = 9;

    // Смысловые группы параметров (земля / природа / народы) — с каких параметров начинаются; между группами пустая строка.
    private static readonly int[] ParamGroups = [3, 8];

    private static int GroupHeading(int p, int y, int left, int lx) => ParamGroups.Contains(p) ? y + 1 : y;

    // Мир для новой игры: id мира библиотеки (новый — сохранён и сделан активным) или null — отмена (в меню).
    // Слева «МИР» — рельеф и карта (параметры, «ДРУГАЯ КАРТА», «РАНДОМ»); справа «КАРТА»; снизу «ОПИСАНИЕ МИРА» —
    // свой текст или правка, запрос к нейронке и «ПРИДУМАТЬ» (описание + имена королевств и столиц; до этого —
    // имена от генератора).
    // Возвращает шаг, на который перейти (2.. — мир выбран, ChosenWorldId), или 0 — выход в меню.
    public async Task<int> PickWorld()
    {
        Begin();
        var rnd = new Random();
        if (_pick == null)
        {
            _pick = new WorldPick { Seed = rnd.Next(), Param = [.. Params.Select(p => p.def)] };
            _pick.Name = WorldGenerator.DefaultName(_pick.Seed);
            _pick.Entries.Add(new(null, "новый мир"));
            string? active = null;
            try { active = WorldLibrary.Current.Id; } catch { }
            foreach (var w in WorldLibrary.All().OrderBy(w => w.id == active ? 0 : 1))
                _pick.Entries.Add(new(w.id, w.name));
        }
        var ps = _pick;
        var entries = ps.Entries;
        int sel = Math.Clamp(ps.Sel, 0, entries.Count - 1);
        int seed = ps.Seed;
        int[] param = ps.Param;   // индексы вариантов Params
        string name = ps.Name;
        bool nameEdited = ps.NameEdited;
        WorldMap? draft = ps.Draft;
        string draftKey = ps.DraftKey;
        // Описание мира (своё или от нейронки) и курсор в нём; запрос к нейронке.
        string? descFor = null;
        string newDesc = ps.NewDesc;   // описание нового мира — хранится, пока листаешь готовые
        bool descLocked = false;       // готовый мир с описанием — описание только читать
        string desc = "";
        int descCur = 0;
        int descScroll = 0, descW = 1;   // прокрутка описания (первая видимая строка), ширина текста
        bool descFollow = true;          // прокрутка — за курсором (после правки); колесо — как прокрутил игрок
        int lx0 = 0;                     // левый край блоков (для клика по тексту)
        string prompt = ps.Prompt;
        // Фокус: 0 — мир, 1 — название, 2.. — параметры, FB — кнопки карты, FD — описание, FQ — запрос,
        // FA — ПРИДУМАТЬ, FN — ДАЛЕЕ.
        int FB = 2 + Params.Length, FD = FB + 1, FQ = FD + 1, FA = FQ + 1, FN = FA + 1;
        int focus = 0;
        int mapAction = 0;
        Task<bool>? aiTask = null;
        WorldMap? aiWorld = null;
        string message = "";
        bool confirmDelete = false;   // ждём [Enter] — удалить выбранный мир
        int frame = 0;

        WorldMap Draft()
        {
            string key = $"{seed}|{string.Join(",", param)}";
            if (draft == null || draftKey != key)
            {
                draft = WorldGenerator.Generate(new WorldGenerator.Options(seed, param[0] == 1 ? WorldSizes.Large : WorldSizes.Small,
                    Continents: param[1], Water: V(2), Climate: V(3), Mountains: V(4), Forests: V(5),
                    Deserts: V(6), Rivers: V(7), Kingdoms: param[8]));
                draftKey = key;
            }
            return draft;
        }

        int V(int p) => param[p] - Params[p].def;

        while (true)
        {
            var entry = entries[sel];
            bool isNew = entry.Id == null && entry.Draft == null;
            var world = entry.Draft ?? (isNew ? Draft() : WorldLibrary.Get(entry.Id)!);
            // Описание — своё у каждого выбранного мира (готовый — из хроники).
            string worldKey = isNew ? "new" : entry.Id ?? "d:" + entry.Draft!.Id;
            if (descFor != worldKey)
            {
                if (descFor == "new") newDesc = desc;
                descScroll = 0;
                descFor = worldKey;
                desc = isNew ? newDesc : world.Chronicle.Description ?? "";
                descCur = desc.Length;
                descLocked = !isNew && desc.Trim().Length > 0;
            }
            if (aiTask is { IsCompleted: true })
            {
                if (aiTask.Result && aiWorld == world)
                {
                    desc = world.Chronicle.Description ?? desc;
                    descCur = desc.Length;
                    descScroll = 0;
                    descFollow = false;   // новый текст — с начала
                    if (!isNew) WorldLibrary.Save(world);   // мир из библиотеки — сразу в файл
                    message = "";
                    prompt = "";   // запрос выполнен — строка ввода снова чистая
                }
                else if (!aiTask.Result) message = "Нейронка не ответила — попробуй ещё раз.";
                aiTask = null;
            }
            if (!Visible(focus)) focus = 0;

            // ── Отрисовка ──
            _frozen = aiTask != null;   // нейронка думает — ничего не нажимается
            Clear();
            int total = FormWidth + Gap + MapBoxWidth;
            int left = Math.Max(0, (_width - total) / 2);
            lx0 = left;
            int blockH = TopBoxHeight + 1 + DescHeight + 1 + 3;
            int top = Math.Max(1, (_height - blockH) / 2);

            // Блок «МИР»: рельеф и карта.
            DrawBox(top, left, FormWidth, TopBoxHeight, "МИР");
            int vx = left + ValueCol, lx = left + LabelCol, valueW = FormWidth - ValueCol - 3;
            int y = top + 2;
            Label(y, lx, "МИР", focus == 0);
            ChoiceValue(y, vx, valueW, entry.Label, focus == 0, "world", "f:0", $"{sel + 1}/{entries.Count}");
            Hit(y, lx - 2, ValueCol - LabelCol + 2, "f:0");
            y += 2;
            if (isNew)
            {
                Label(y, lx, "НАЗВАНИЕ", focus == 1);
                Put(y, vx, Fit(name + (focus == 1 ? "▌" : ""), valueW), focus == 1 ? Bright : Fg);
                Hit(y, lx - 2, FormWidth - LabelCol, "f:1");
                y += 2;
                for (int p = 0; p < Params.Length; p++, y++)
                {
                    y = GroupHeading(p, y, left, lx);
                    Label(y, lx, Params[p].label, focus == 2 + p);
                    ChoiceValue(y, vx, valueW, Params[p].options[param[p]], focus == 2 + p, $"p{p}", $"f:{2 + p}");
                    Hit(y, lx - 2, ValueCol - LabelCol + 2, $"f:{2 + p}");
                }
                // Кнопки карты — внизу блока, плашками.
                int bx = lx;
                bx = Button(top + TopBoxHeight - 3, bx, "ДРУГАЯ КАРТА", focus == FB && mapAction == 0, "regen:0") + 2;
                Button(top + TopBoxHeight - 3, bx, "РАНДОМ", focus == FB && mapAction == 1, "regen:1");
            }
            else
            {
                // Готовый мир: те же поля, но только для чтения — без стрелок.
                string Level(int p, int v) => Params[p].options[Math.Clamp(v + Params[p].def, 0, Params[p].options.Length - 1)];
                string[] values =
                [
                    world.Size == WorldSizes.Large ? "большой" : "малый",
                    world.Continents > 0 ? world.Continents.ToString() : "авто",
                    Level(2, world.Water), Level(3, world.Climate),
                    Level(4, world.Mountains), Level(5, world.Forests), Level(6, world.Deserts), Level(7, world.RiverAmount),
                    world.Kingdoms.Count.ToString(),
                ];
                ReadOnlyRow(y, lx, vx, valueW, "НАЗВАНИЕ", world.Chronicle.Name is { Length: > 0 } cn ? cn : world.Name, center: false);
                y += 2;
                for (int p = 0; p < Params.Length; p++, y++)
                {
                    y = GroupHeading(p, y, left, lx);
                    ReadOnlyRow(y, lx, vx, valueW, Params[p].label, values[p], center: true);
                }
                if (world.Chronicle.Places.Count > 0) ReadOnlyRow(y++, lx, vx, valueW, "МЕСТ", world.Chronicle.Places.Count.ToString(), center: true);
            }

            // Блок «КАРТА».
            int mapBoxLeft = left + FormWidth + Gap;
            DrawBox(top, mapBoxLeft, MapBoxWidth, TopBoxHeight, "КАРТА");
            var (mw, mh) = MapSize(world, MapW, MapH);
            int mapLeft = mapBoxLeft + (MapBoxWidth - mw) / 2, mapTop = top + 1 + (MapH - mh) / 2;

            // Блок «ОПИСАНИЕ МИРА»: сверху текст (свой или правка, королевства — в нём же), под разделителем — ввод
            // запроса к нейронке и ПРИДУМАТЬ/ИЗМЕНИТЬ.
            int dTop = top + TopBoxHeight + 1;
            string shownName = isNew ? name : world.Chronicle.Name is { Length: > 0 } wn ? wn : world.Name;
            DrawBox(dTop, left, total, DescHeight, $"ОПИСАНИЕ МИРА · {shownName.ToUpperInvariant()}");
            int dW = total - 6;
            if (descLocked)
            {
                // Готовый мир: описание на весь блок, без ввода и кнопок.
                int rowsL = DescHeight - 2;
                var all = TextArea.View(desc, 0, ref descScroll, dW, rowsL, false, out _, out _, out int totalL);
                for (int i = 0; i < rowsL; i++)
                {
                    if (i < all.Count) Put(dTop + 1 + i, lx, all[i], Fg);
                    Hit(dTop + 1 + i, left + 1, total - 2, $"descview:{i}");
                }
                DrawBar(dTop + 1, left + total - 2, rowsL, totalL, descScroll);
            }
            else
            {
            // Текст: перенос по словам, курсор (подсвеченная клетка) и прокрутка — колесом, ↑↓, PgUp/PgDn.
            descW = dW;
            bool descEditing = focus == FD;
            if (desc.Length == 0 && !descEditing) Put(dTop + 1, lx, Fit("свой текст о мире — или пусть придумает нейронка", dW), Dim);
            else
            {
                var view = TextArea.View(desc, descCur, ref descScroll, dW, DescLines, descEditing && descFollow,
                    out int curRow, out int curCol, out int totalLines);
                for (int i = 0; i < view.Count; i++) Put(dTop + 1 + i, lx, view[i], descEditing ? Bright : Fg);
                if (descEditing && curRow >= 0)
                {
                    string line = view[curRow];
                    Put(dTop + 1 + curRow, lx + curCol, curCol < line.Length ? line[curCol].ToString() : " ", _display.MainBackground, Bright);
                }
                DrawBar(dTop + 1, left + total - 2, DescLines, totalLines, descScroll);
            }
            for (int i = 0; i < DescLines; i++) Hit(dTop + 1 + i, left + 1, total - 3, $"desc:{i}");
            int sepY = dTop + DescHeight - 3;
            Put(sepY, left, "├" + new string('─', total - 2) + "┤", Dim);
            // Ввод запроса и кнопка отправки справа.
            int iy = sepY + 1;
            string goText = desc.Length > 0 ? "ИЗМЕНИТЬ" : "ПРИДУМАТЬ";
            int gx = left + total - 3 - (goText.Length + 4);
            // Нейронка думает — вся строка (без «Ввод:» и кнопки) под статус.
            if (aiTask != null)
            {
                Put(iy, lx, Fit($"{Spinner.Frames[frame++ % Spinner.Frames.Length]} Нейронка думает: описание, королевства и столицы…", total - 6), Bright);
            }
            else
            {
            Put(iy, lx, "Ввод: ", focus == FQ ? Bright : Fg);
            int px = lx + 6, promptW = gx - 2 - px;
            // Есть итог (готово / ошибка) и поле пустое — вместо подсказки в строке ввода он.
            string status = message;
            bool showStatus = status.Length > 0 && prompt.Length == 0 && focus != FQ;
            string promptShown = showStatus ? status
                : prompt.Length == 0 && focus != FQ ? "что придумать или как изменить описание (необязательно)" : TailFit(prompt + (focus == FQ ? "▌" : ""), promptW);
            Put(iy, px, Fit(promptShown, promptW), showStatus ? Bright : prompt.Length == 0 && focus != FQ ? Dim : focus == FQ ? Bright : Fg);
            Hit(iy, left + 1, gx - 2 - left, $"f:{FQ}");
            Button(iy, gx, goText, focus == FA, "ai");
            }
            }

            // ДАЛЕЕ и подсказка.
            int by = dTop + DescHeight + 1;
            Button(by, left + (total - "ДАЛЕЕ: ГЕРОЙ".Length - 4) / 2, "ДАЛЕЕ: ГЕРОЙ", focus == FN, "next", disabled: desc.Trim().Length == 0);
            // Подсказка; вместо неё — подтверждение удаления или сообщение (у готового мира строки ввода нет).
            if (confirmDelete) CenterIn(by + 2, left, total, $"Удалить мир «{entry.Label}» со всеми локациями?   [Enter]ДА   [Esc]НЕТ", Bright);
            else if (message.Length > 0 && descLocked) CenterIn(by + 2, left, total, message, Bright);
            else CenterIn(by + 2, left, total, "[↑↓]ПОЛЕ   [←→]ВЫБОР/КУРСОР   [Enter]ДАЛЕЕ" + (isNew ? "" : "   [Del]УДАЛИТЬ МИР") + "   [Esc]МЕНЮ", Dim);
            // Карта: колесо — масштаб, перетаскивание — сдвиг (клик ничего не делает); другой мир — камера с начала.
            if (_worldCam.For != world.Id) _worldCam.Reset(world.Id);
            Flush(mapLeft, mapTop, mw, mh);
            DrawMapAt(world, mapLeft, mapTop, mw, mh, null, _worldCam);
            SetMapPan(_worldCam, () => DrawMapAt(world, mapLeft, mapTop, mw, mh, null, _worldCam));

            // ── Ввод ──
            var input = await WaitInput(aiTask != null ? 200 : 0, MapMode.Pan);
            if (input == null) continue;
            var (key, click) = input.Value;
            if (confirmDelete && (key != null || click != null))
            {
                // Подтверждение удаления: Enter — да, Esc/любая другая клавиша или клик — нет.
                confirmDelete = false;
                message = "";
                if (key is { Key: ConsoleKey.Enter }) DeleteWorld();
                continue;
            }
            if (key is { Key: ConsoleKey.Escape } || click == "esc") return Leave(0);
            if (click is { } mwh && mwh.StartsWith("mapwheel:") && _map is { } zm)
            {
                var parts = mwh[9..].Split(':');
                var cell = parts[1].Split(',');
                _worldCam.ZoomAt(zm.view, int.Parse(parts[0]), int.Parse(cell[0]), int.Parse(cell[1]), zm.w, zm.h);
                continue;
            }
            // Колесо над описанием — прокрутка текста.
            if (click is { } wh && wh.StartsWith("wheel:"))
            {
                var parts = wh.Split(':', 3);
                if (parts[2].StartsWith("desc")) { descScroll -= int.Parse(parts[1]); descFollow = false; }
                continue;
            }
            if (aiTask != null) continue;   // нейронка думает — ввод ждёт
            // Шаги: дальше — только с выбранным миром (как «ДАЛЕЕ»).
            if (click is { } sc && sc.StartsWith("step:"))
            {
                int to = int.Parse(sc[5..]);
                if (to >= 2 && (to == 2 || CanGo(to)) && Proceed() != null) return Leave(to);
                continue;
            }
            if (click is { } c)
            {
                Sound.PlayClick();
                if (c == "world:-1") CycleWorld(-1);
                else if (c == "world:1") CycleWorld(1);
                else if (c.StartsWith("p") && c.Contains(':')) Step(int.Parse(c[1..c.IndexOf(':')]), int.Parse(c[(c.IndexOf(':') + 1)..]));
                else if (c.StartsWith("regen:")) { mapAction = int.Parse(c[6..]); focus = FB; RunMap(); }
                else if (c == "ai") { focus = FA; RunAi(); }
                else if (c.StartsWith("desc:"))
                {
                    // Клик по тексту — курсор в это место.
                    focus = FD;
                    descCur = TextArea.CursorAt(desc, descScroll, descW, int.Parse(c[5..]), _clickCol - (lx0 + LabelCol));
                    descFollow = true;
                }
                else if (c.StartsWith("f:")) { focus = int.Parse(c[2..]); if (focus == FD) descCur = desc.Length; }
                else if (c == "next" && Proceed() != null) return Leave(2);
                continue;
            }
            if (key is not { } k) continue;
            if (k.Key == ConsoleKey.Delete && focus is not 1 && focus != FD && focus != FQ)
            {
                AskDelete();
                continue;
            }
            if (focus == FD && TextArea.Edit(ref desc, ref descCur, k, 1200, descW, DescLines)) { descFollow = true; continue; }
            if (focus == FQ && EditText(ref prompt, k, 300)) continue;
            if (focus == 1)
            {
                string before = name;
                if (EditText(ref name, k, 40)) { if (name != before) nameEdited = true; continue; }
            }
            switch (k.Key)
            {
                case ConsoleKey.UpArrow: focus = Move(focus, -1); Sound.PlayClick(); break;
                case ConsoleKey.DownArrow or ConsoleKey.Tab: focus = Move(focus, 1); if (focus == FD) descCur = desc.Length; Sound.PlayClick(); break;
                case ConsoleKey.LeftArrow or ConsoleKey.RightArrow:
                    int dir = k.Key == ConsoleKey.LeftArrow ? -1 : 1;
                    if (focus == 0) CycleWorld(dir);
                    else if (focus >= 2 && focus < FB) Step(focus - 2, dir);
                    else if (focus == FB) mapAction = dir < 0 ? 0 : 1;
                    break;
                case ConsoleKey.Enter:
                    if (focus == 1 || focus == FD) focus = Move(focus, 1);
                    else if (focus == FQ) { if (prompt.Trim().Length > 0) { focus = FA; RunAi(); } }   // пустой ввод — не отправлять
                    else if (focus == FB) RunMap();
                    else if (focus == FA) RunAi();
                    else if (Proceed() != null) return Leave(2);
                    break;
            }

            bool Visible(int f) => f == 0 || f == FN || (f >= FD ? !descLocked : isNew);

            int Move(int f, int dir)
            {
                for (int n = f + dir; n >= 0 && n <= FN; n += dir)
                    if (Visible(n)) return n;
                return f;
            }

            bool Busy()
            {
                if (aiTask == null) return false;
                message = "Подожди — нейронка думает.";
                return true;
            }

            void CycleWorld(int dir)
            {
                if (Busy()) return;
                sel = (sel + dir + entries.Count) % entries.Count;
                message = "";
                Sound.PlayClick();
            }

            // Параметр нового мира: другой вариант — новая карта, как «ДРУГАЯ КАРТА» (новый рельеф; название от
            // генератора — новое, если его не правили; имена королевств — снова от генератора).
            void Step(int p, int dir)
            {
                if (Busy()) return;
                Archive();
                param[p] = (param[p] + dir + Params[p].options.Length) % Params[p].options.Length;
                seed = rnd.Next();
                if (!nameEdited) name = WorldGenerator.DefaultName(seed);
                message = "";
                Sound.PlayClick();
            }

            // «ДРУГАЯ КАРТА» — новый рельеф с теми же параметрами; «РАНДОМ» — случайные параметры и название.
            void RunMap()
            {
                Sound.PlayClick();
                if (Busy()) return;
                Archive();
                seed = rnd.Next();
                if (mapAction == 1)
                {
                    for (int p = 0; p < param.Length; p++) param[p] = rnd.Next(Params[p].options.Length);
                    nameEdited = false;
                }
                if (!nameEdited) name = WorldGenerator.DefaultName(seed);
                message = "";
            }

            // Новый мир уже с описанием — перед новой картой он становится готовым вариантом в листалке (только на
            // время этого создания игры; в библиотеку попадёт, если с него перейти дальше), описание — с чистого листа.
            void Archive()
            {
                if (!isNew || draft == null || desc.Trim().Length == 0) return;
                draft.Chronicle.Description = desc.Trim();
                if (name.Trim().Length > 0) { draft.Name = name.Trim(); draft.Chronicle.Name = name.Trim(); }
                entries.Insert(1, new(null, draft.Chronicle.Name ?? draft.Name, draft));
                sel = 0;
                draft = null;
                desc = newDesc = prompt = "";
                descCur = 0;
                nameEdited = false;
                message = "Прежний вариант — в списке миров (◄ ►), пока создаётся эта игра.";
            }

            // [Del] — удалить выбранный мир (с подтверждением). Мир, в котором есть игра, не удаляется.
            void AskDelete()
            {
                Sound.PlayClick();
                if (Busy()) return;
                if (isNew) { message = "Новый мир ещё не сохранён — удалять нечего."; return; }
                if (entry.Id != null && WorldLibrary.GamesUsing(entry.Id) is { Count: > 0 } games)
                {
                    message = $"Нельзя удалить: в этом мире {(games.Count == 1 ? "игра" : "игры")} " +
                              string.Join(", ", games.Select(g => $"«{g.hero}»")) + " — сначала удали их в «Мои игры».";
                    return;
                }
                confirmDelete = true;
            }

            void DeleteWorld()
            {
                Sound.PlayClick();
                if (entry.Id != null && !WorldLibrary.Delete(entry.Id)) { message = "Не удалось удалить мир."; return; }
                entries.RemoveAt(sel);
                sel = Math.Min(sel, entries.Count - 1);
                descFor = null;
                message = $"Мир «{entry.Label}» удалён.";
            }

            void RunAi()
            {
                Sound.PlayClick();
                if (Busy()) return;
                // Без запроса нейронка только придумывает с нуля; переписывать готовое описание «ни о чём» — нет.
                if (prompt.Trim().Length == 0 && desc.Trim().Length > 0)
                {
                    message = "Напиши в «Ввод», что изменить в описании";   // видно в строке ввода
                    return;
                }
                aiWorld = world;
                aiTask = _ai.CreateWorld(world, prompt, isNew ? name : null, desc);
            }

            // Уйти с шага: запомнить всё заполненное (вернёмся — как было).
            int Leave(int step)
            {
                ps.Sel = sel; ps.Seed = seed; ps.Param = param; ps.Name = name; ps.NameEdited = nameEdited;
                ps.Draft = draft; ps.DraftKey = draftKey; ps.Prompt = prompt;
                ps.NewDesc = descFor == "new" ? desc : newDesc;
                return step;
            }

            // Мир выбран: в библиотеку/активным. Мир, записанный раньше в этом же создании игры и теперь не выбранный,
            // удаляется (если в нём нет игр): в библиотеке остаётся только тот, с которым перешли дальше.
            string? Proceed()
            {
                string? id = ProceedCore();
                if (id == null) return null;
                if (ps.RegisteredId != null && ps.RegisteredId != id && WorldLibrary.GamesUsing(ps.RegisteredId).Count == 0)
                    WorldLibrary.Delete(ps.RegisteredId);
                ps.RegisteredId = entry.Id == null ? id : null;
                ChosenWorldId = id;
                return id;
            }

            string? ProceedCore()
            {
                Sound.PlayClick();
                if (Busy()) return null;
                // Готовый мир обязан иметь описание.
                if (desc.Trim().Length == 0)
                {
                    message = "Сначала описание мира: напиши своё или нажми «ПРИДУМАТЬ».";
                    focus = FQ;
                    return null;
                }
                world.Chronicle.Description = desc.Trim();
                if (entry.Draft != null)
                {
                    WorldLibrary.Register(entry.Draft);
                    return entry.Draft.Id;
                }
                if (entry.Id != null)
                {
                    WorldLibrary.Save(world);
                    WorldLibrary.SetActive(entry.Id);
                    return entry.Id;
                }
                if (name.Trim().Length > 0) { world.Name = name.Trim(); world.Chronicle.Name = name.Trim(); }
                WorldLibrary.Register(world);
                return world.Id;
            }
        }
    }

    private static string TailFit(string text, int width) =>
        text.Length <= width ? text : "…" + text[^(width - 1)..];

    // ── Шаг 3: приключение ───────────────────────────────────────────────────

    // Приключение: пожелания, старт (место или клетка), параметры (индексы вариантов) и они же строкой для мастера.
    public sealed record Adventure(string Wish, string? StartPlace, (int x, int y)? StartTile, int[]? Params = null, string Style = "");

    // Пожелания к сюжету и место старта (по желанию). null — назад в редактор героя.
    // Выход из создания игры в меню: предупреждение, что заполненное не сохранится. true — выйти.
    public async Task<bool> ConfirmExit()
    {
        Begin();
        while (true)
        {
            Clear();
            const int w = 64, h = 7;
            int left = Math.Max(0, (_width - w) / 2), top = Math.Max(1, (_height - h) / 2 - 2);
            DrawBox(top, left, w, h, "ВЫЙТИ В МЕНЮ?");
            CenterIn(top + 2, left, w, "Прогресс создания новой игры не сохранится.", Bright);
            int bx = left + (w - "ВЫЙТИ".Length - "ОСТАТЬСЯ".Length - 10) / 2;
            bx = Button(top + 4, bx, "ВЫЙТИ", false, "yes") + 2;
            Button(top + 4, bx, "ОСТАТЬСЯ", false, "no");
            CenterIn(top + h + 1, left, w, "[Enter]ВЫЙТИ   [Esc]ОСТАТЬСЯ", Dim);
            Flush();
            var input = await WaitInput(0);
            if (input == null) continue;
            var (key, click) = input.Value;
            if (click?.StartsWith("wheel:") == true) continue;
            if (click == "yes" || key is { Key: ConsoleKey.Enter }) { Sound.PlayClick(); return true; }
            if (click != null || key != null) { Sound.PlayClick(); return false; }
        }
    }

    // Создание игры брошено: мир, записанный в библиотеку на этом создании, удаляется (если в нём нет игр).
    public void DiscardWorld()
    {
        if (_pick?.RegisteredId is { } id && WorldLibrary.GamesUsing(id).Count == 0) WorldLibrary.Delete(id);
    }

    // Шаг «Приключение»: возвращает заполненное и куда дальше — StartStep (начать игру), шаг 1–3 или 0 (выход в меню).
    public const int StartStep = 5;

    // Параметры приключения правил (Prompts/{RuleSet}/adventureOptions.json): группа, подпись, варианты, по умолчанию.
    private List<(string group, string label, string[] options, int def)>? _advOptions;

    private List<(string group, string label, string[] options, int def)> AdventureOptions()
    {
        if (_advOptions != null) return _advOptions;
        _advOptions = [];
        try
        {
            string path = Path.Combine(AppConfig.ProjectRoot, "Prompts", _ai.RuleSet, "adventureOptions.json");
            foreach (var p in System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))?["params"]?.AsArray() ?? [])
            {
                string[] opts = [.. (p?["options"]?.AsArray() ?? []).Select(o => o?.ToString() ?? "").Where(o => o.Length > 0)];
                if (opts.Length == 0) continue;
                _advOptions.Add(((string?)p?["group"] ?? "", (string?)p?["label"] ?? "", opts, Math.Clamp((int?)p?["default"] ?? 0, 0, opts.Length - 1)));
            }
        }
        catch { /* нет файла — только пожелания и старт */ }
        return _advOptions;
    }

    // Параметры приключения строкой для мастера: «Жанр: тёмное фэнтези; Сложность: сложная; …».
    private string StyleText(int[] values)
    {
        var opts = AdventureOptions();
        return string.Join("; ", opts.Select((o, i) =>
            $"{char.ToUpperInvariant(o.label[0])}{o.label[1..].ToLowerInvariant()}: {o.options[Math.Clamp(values[i], 0, o.options.Length - 1)]}"));
    }

    // Камера карты (масштаб: -1 — весь мир; центр: NaN — середина мира): колесо — масштаб у курсора, перетаскивание —
    // сдвиг; дальше «мир во всё окно» не отдаляется, за край мира не уходит. Своя у шагов «Мир» и «Приключение».
    private sealed class MapCam
    {
        public int Zoom = -1;
        public double Cx = double.NaN, Cy = double.NaN;
        public string? For;   // мир, для которого камера (другой мир — с начала)

        public void Reset(string id) { For = id; Zoom = -1; Cx = Cy = double.NaN; }

        public void Clamp(WorldMapView v, WorldMap world, int w, int h)
        {
            Zoom = Math.Clamp(Zoom < 0 ? v.MaxZoom : Zoom, 0, v.MaxZoom);
            double s = v.Scale(Zoom), halfW = w / 2.0 * s, halfH = h / 2.0 * 2 * s;
            if (double.IsNaN(Cx)) { Cx = world.Width / 2.0; Cy = world.Height / 2.0; }
            Cx = halfW * 2 >= world.Width ? world.Width / 2.0 : Math.Clamp(Cx, halfW, world.Width - halfW);
            Cy = halfH * 2 >= world.Height ? world.Height / 2.0 : Math.Clamp(Cy, halfH, world.Height - halfH);
        }

        // Колесо: точка мира под курсором (символ col,row области w×h) остаётся под курсором.
        public void ZoomAt(WorldMapView v, int notches, int col, int row, int w, int h)
        {
            double sx = col - w / 2.0, sy = row - h / 2.0, s0 = v.Scale(Zoom);
            double wx = Cx + sx * s0, wy = Cy + sy * 2 * s0;
            Zoom = Math.Clamp(Zoom - notches, 0, v.MaxZoom);
            double s1 = v.Scale(Zoom);
            Cx = wx - sx * s1;
            Cy = wy - sy * 2 * s1;
        }
    }

    private readonly MapCam _advCam = new(), _worldCam = new();

    // Перетаскивание карты (зовёт WaitInput, пока зажата кнопка): запомнить центр, сдвинуть на (dx, dy) символов.
    private void SetMapPan(MapCam cam, Action redraw)
    {
        double px = 0, py = 0;
        MapPanBegin = () => { px = cam.Cx; py = cam.Cy; };
        MapPan = (dx, dy) =>
        {
            if (_map is not { } m) return;
            double s = m.view.Scale(cam.Zoom);
            cam.Cx = px - dx * s;
            cam.Cy = py - dy * 2 * s;
            redraw();
        };
    }

    // Отметка старта на карте — сплошной кружок цвета, которого на карте нет.
    private static readonly List<int> StartMarkColor = WorldMapView.MarkerColor;

    public async Task<(int step, Adventure adventure)> PickAdventure(string worldId, Adventure? filled)
    {
        Begin();
        var world = WorldLibrary.Get(worldId) ?? WorldLibrary.Current;
        var opts = AdventureOptions();
        int[] values = filled?.Params is { } fp && fp.Length == opts.Count ? [.. fp] : [.. opts.Select(o => o.def)];
        string wish = filled?.Wish ?? "";
        string? startPlace = filled?.StartPlace;
        (int x, int y)? startTile = filled?.StartTile;
        int wishCur = wish.Length, wishScroll = 0, wishW = 1;
        bool wishFollow = true;
        // Фокус: 0.. — параметры, FB — кнопки старта (СБРОСИТЬ/РАНДОМ), FW — пожелания, FS — НАЧАТЬ ИГРУ.
        int FB = opts.Count, FW = FB + 1, FS = FW + 1;
        int focus = 0, action = 0;   // action: 0 — СБРОСИТЬ, 1 — РАНДОМ
        const int WishRows = DescHeight - 2;
        int mw = MapW, mh = MapH;   // что под курсором — в разрыве нижней линии рамки
        (int, Adventure) Leave(int step) => (step, new Adventure(wish.Trim(), startPlace, startTile, [.. values], StyleText(values)));

        if (_advCam.For != world.Id) _advCam.Reset(world.Id);

        while (true)
        {
            bool hasStart = startPlace != null || startTile != null;
            Clear();
            int total = FormWidth + Gap + MapBoxWidth;
            int left = Math.Max(0, (_width - total) / 2);
            int blockH = TopBoxHeight + 1 + DescHeight + 1 + 3;
            int top = Math.Max(1, (_height - blockH) / 2);
            int vx = left + ValueCol, lx = left + LabelCol, valueW = FormWidth - ValueCol - 3;

            // Блок «ПРИКЛЮЧЕНИЕ»: параметры группами, старт и кнопки.
            DrawBox(top, left, FormWidth, TopBoxHeight, "ПРИКЛЮЧЕНИЕ");
            int y = top + 2;
            for (int p = 0; p < opts.Count; p++, y++)
            {
                if (p > 0 && opts[p].group != opts[p - 1].group) y++;
                Label(y, lx, opts[p].label, focus == p);
                ChoiceValue(y, vx, valueW, opts[p].options[values[p]], focus == p, $"p{p}", $"f:{p}");
                Hit(y, lx - 2, ValueCol - LabelCol + 2, $"f:{p}");
            }
            y++;
            Label(y, lx, "СТАРТ", false);
            string start = startPlace ?? (startTile is { } st ? TileText(world, st) : "решит мастер");
            var startLines = Wrap(start, valueW);
            for (int i = 0; i < startLines.Count && i < 2; i++) Put(y + i, vx, startLines[i], hasStart ? StartMarkColor : Dim);
            if (!hasStart) Put(y + 1, vx, Fit("или клик по карте", valueW), Dim);
            int bx = lx;
            if (hasStart) bx = Button(top + TopBoxHeight - 3, bx, "СБРОСИТЬ", focus == FB && action == 0, "reset") + 2;
            Button(top + TopBoxHeight - 3, bx, "РАНДОМ СТАРТ", focus == FB && (action == 1 || !hasStart), "random");

            // Блок карты: колесо — масштаб, перетаскивание — сдвиг, клик — старт.
            int mapBoxLeft = left + FormWidth + Gap;
            DrawBox(top, mapBoxLeft, MapBoxWidth, TopBoxHeight, (world.Chronicle.Name is { Length: > 0 } wn ? wn : "КАРТА").ToUpperInvariant());
            int mapLeft = mapBoxLeft + (MapBoxWidth - mw) / 2, mapTop = top + 1;
            string under = "";   // что под курсором
            var underColor = Dim;
            if (_map is { } mp && _mapHover is { } hc)
            {
                int place = mp.view.PlaceAtCell(hc.col, hc.row);
                var t = mp.view.TileAt(hc.col, hc.row, mw, mh, _advCam.Cx, _advCam.Cy, _advCam.Zoom);
                if (place >= 0) { under = $"{mp.world.Places[place].Name} — {WorldPlaceTypes.Label(mp.world.Places[place].Type)}"; underColor = Fg; }
                else if (mp.world.InBounds(t.x, t.y))
                {
                    under = WorldBiomes.IsWater(mp.world.BiomeAt(t.x, t.y)) ? "вода — сюда нельзя" : TileText(mp.world, t);
                    underColor = Fg;
                }
            }
            if (under.Length > 0) CenterIn(top + TopBoxHeight - 1, mapBoxLeft, MapBoxWidth, $" {Fit(under, MapBoxWidth - 6)} ", underColor);

            // Блок «ПОЖЕЛАНИЯ К ИСТОРИИ» (необязательно): поле с курсором и прокруткой, как описание мира.
            int dTop = top + TopBoxHeight + 1;
            DrawBox(dTop, left, total, DescHeight, "ПОЖЕЛАНИЯ К ИСТОРИИ");
            wishW = total - 6;
            bool wishEditing = focus == FW;
            if (wish.Length == 0 && !wishEditing)
                Put(dTop + 1, lx, Fit("что хочешь от истории: сюжет, враги, настроение — необязательно, хватит параметров выше", wishW), Dim);
            else
            {
                var view = TextArea.View(wish, wishCur, ref wishScroll, wishW, WishRows, wishEditing && wishFollow,
                    out int curRow, out int curCol, out int totalLines);
                for (int i = 0; i < view.Count; i++) Put(dTop + 1 + i, lx, view[i], wishEditing ? Bright : Fg);
                if (wishEditing && curRow >= 0)
                {
                    string line = view[curRow];
                    Put(dTop + 1 + curRow, lx + curCol, curCol < line.Length ? line[curCol].ToString() : " ", _display.MainBackground, Bright);
                }
                DrawBar(dTop + 1, left + total - 2, WishRows, totalLines, wishScroll);
            }
            for (int i = 0; i < WishRows; i++) Hit(dTop + 1 + i, left + 1, total - 3, $"wish:{i}");

            int by = dTop + DescHeight + 1;
            Button(by, left + (total - "НАЧАТЬ ИГРУ".Length - 4) / 2, "НАЧАТЬ ИГРУ", focus == FS, "next");
            CenterIn(by + 2, left, total, "[↑↓]ПОЛЕ   [←→]ВЫБОР   [Enter]НАЧАТЬ   [Esc]МЕНЮ", Dim);

            // Карта: сначала холст (место карты не затирается), потом сама карта с камерой и отметкой старта.
            var marker = startTile ?? (startPlace != null && WorldAtlas.FindPlace(Preview(world), startPlace) is { } sp ? (sp.X, sp.Y) : null);
            Flush(mapLeft, mapTop, mw, mh);
            DrawMapAt(world, mapLeft, mapTop, mw, mh, marker, _advCam);
            SetMapPan(_advCam, () => DrawMapAt(world, mapLeft, mapTop, mw, mh, marker, _advCam));

            var input = await WaitInput(0, MapMode.Pick);
            if (input == null) continue;
            var (key, click) = input.Value;
            if (key is { Key: ConsoleKey.Escape } || click == "esc") return Leave(0);
            if (click is { } c)
            {
                if (c.StartsWith("wheel:"))
                {
                    var parts = c.Split(':', 3);
                    if (parts[2].StartsWith("wish")) { wishScroll -= int.Parse(parts[1]); wishFollow = false; }
                    continue;
                }
                if (c.StartsWith("mapwheel:") && _map is { } zm)
                {
                    var parts = c[9..].Split(':');
                    var cell = parts[1].Split(',');
                    _advCam.ZoomAt(zm.view, int.Parse(parts[0]), int.Parse(cell[0]), int.Parse(cell[1]), mw, mh);
                    continue;
                }
                if (c.StartsWith("step:")) { int to = int.Parse(c[5..]); if (to < 4 && CanGo(to)) return Leave(to); continue; }
                Sound.PlayClick();
                if (c.StartsWith("p") && c.Contains(':'))
                {
                    int p = int.Parse(c[1..c.IndexOf(':')]), dir = int.Parse(c[(c.IndexOf(':') + 1)..]);
                    focus = p;
                    values[p] = (values[p] + dir + opts[p].options.Length) % opts[p].options.Length;
                }
                else if (c.StartsWith("f:")) focus = int.Parse(c[2..]);
                else if (c.StartsWith("wish:"))
                {
                    focus = FW;
                    wishCur = TextArea.CursorAt(wish, wishScroll, wishW, int.Parse(c[5..]), _clickCol - (left + LabelCol));
                    wishFollow = true;
                }
                else if (c == "reset") { startPlace = null; startTile = null; focus = FB; action = 1; }
                else if (c == "random") { Randomize(); focus = FB; action = 1; }
                else if (c == "next") return Leave(StartStep);
                else if (c.StartsWith("map:") && _map is { } m)
                {
                    var parts = c[4..].Split(',');
                    int mc = int.Parse(parts[0]), mr = int.Parse(parts[1]);
                    int place = m.view.PlaceAtCell(mc, mr);
                    var (tx, ty) = m.view.TileAt(mc, mr, mw, mh, _advCam.Cx, _advCam.Cy, _advCam.Zoom);
                    if (place >= 0) { startPlace = m.world.Places[place].Name; startTile = null; }
                    else if (m.world.InBounds(tx, ty) && !WorldBiomes.IsWater(m.world.BiomeAt(tx, ty))) { startTile = (tx, ty); startPlace = null; }
                }
                continue;
            }
            if (key is not { } k) continue;
            if (focus == FW && k.Key is not (ConsoleKey.Enter or ConsoleKey.Tab))
            {
                if (TextArea.Edit(ref wish, ref wishCur, k, 1500, wishW, WishRows)) { wishFollow = true; continue; }
                if (k.Key == ConsoleKey.UpArrow) focus = FB;
                else if (k.Key == ConsoleKey.DownArrow) focus = FS;
                continue;
            }
            switch (k.Key)
            {
                case ConsoleKey.UpArrow: focus = Math.Max(0, focus - 1); Sound.PlayClick(); break;
                case ConsoleKey.DownArrow or ConsoleKey.Tab:
                    focus = Math.Min(FS, focus + 1);
                    if (focus == FW) { wishCur = wish.Length; wishFollow = true; }
                    Sound.PlayClick();
                    break;
                case ConsoleKey.LeftArrow or ConsoleKey.RightArrow:
                    int dir = k.Key == ConsoleKey.LeftArrow ? -1 : 1;
                    if (focus < opts.Count) values[focus] = (values[focus] + dir + opts[focus].options.Length) % opts[focus].options.Length;
                    else if (focus == FB && hasStart) action = dir < 0 ? 0 : 1;
                    Sound.PlayClick();
                    break;
                case ConsoleKey.Enter:
                    Sound.PlayClick();
                    if (focus == FB)
                    {
                        if (hasStart && action == 0) { startPlace = null; startTile = null; action = 1; }
                        else Randomize();
                    }
                    else if (focus == FS) return Leave(StartStep);
                    else focus = focus == FW ? FS : Math.Min(FS, focus + 1);
                    break;
            }
        }

        // «РАНДОМ СТАРТ» — случайная клетка суши (не вода и не пики); карта при приближении — к ней.
        void Randomize()
        {
            for (int tries = 0; tries < 2000; tries++)
            {
                int x = _rnd.Next(world.Width), y = _rnd.Next(world.Height);
                char b = world.BiomeAt(x, y);
                if (WorldBiomes.IsWater(b) || b == WorldBiomes.Peaks) continue;
                startTile = (x, y);
                startPlace = null;
                _advCam.Cx = x + 0.5;
                _advCam.Cy = y + 0.5;
                return;
            }
        }
    }

    private static readonly Random _rnd = new();

    // Карта с камерой (масштаб и сдвиг), отметка старта — кружок.
    private void DrawMapAt(WorldMap geo, int left, int top, int w, int h, (int x, int y)? marker, MapCam cam)
    {
        var world = GameWorld.Compose(geo, KnownAll(geo), includeHidden: false);
        var key = (geo.Id + "|" + world.Places.Count + "|" + geo.Chronicle.Kingdoms.Count + "|" + geo.Chronicle.Name, w, h);
        if (!_views.TryGetValue(key, out var view))
            _views[key] = view = new WorldMapView(world, w, h);
        cam.Clamp(view, geo, w, h);
        view.Hero = marker;
        view.HeroColor = StartMarkColor;
        _map = (left, top, w, h, view, world);
        string drawn = $"{key}|{left},{top}|{marker}|{cam.Zoom}|{cam.Cx:0.##},{cam.Cy:0.##}";
        if (_mapDrawn == drawn) return;
        _mapDrawn = drawn;
        bool vis = Console.CursorVisible;
        Console.CursorVisible = false;
        for (int r = 0; r < h; r++)
        {
            Console.SetCursorPosition(_x0 + left, _bodyTop + top + r);
            Console.Write(view.RenderRow(r, w, h, cam.Cx, cam.Cy, cam.Zoom, -1));
        }
        ColorHelper.SetBackgroundColor(_display.MainBackground);
        ColorHelper.SetForegroundColor(_display.MainForeground);
        Console.CursorVisible = vis;
    }

    private static string TileText(WorldMap w, (int x, int y) t)
    {
        int own = w.OwnerAt(t.x, t.y);
        string land = own >= 0 && own < w.Kingdoms.Count ? (w.Chronicle.Kingdoms.FirstOrDefault(k => k.Id == own)?.Name ?? w.Kingdoms[own].Name) : "ничьи земли";
        return $"{WorldBiomes.Get(w.BiomeAt(t.x, t.y)).Name.ToLowerInvariant()}, {land} ({WorldAtlas.Compass(w, t.x, t.y)})";
    }

    // ── Карта ────────────────────────────────────────────────────────────────

    private readonly Dictionary<(string id, int w, int h), WorldMapView> _views = [];

    // Хроника видна целиком (игрок выбирает мир/старт — показываем всё, что о мире известно).
    private static GameWorldLink KnownAll(WorldMap w) => new() { Id = w.Id, Known = w.Chronicle.Places.Select(p => p.Name).ToList() };

    private WorldMap Preview(WorldMap geo) => GameWorld.Compose(geo, KnownAll(geo), includeHidden: false);

    // Превью — весь мир в рамке: w символов × h строк (строка — 2 пикселя) под пропорции мира.
    private static (int w, int h) MapSize(WorldMap world, int maxW, int maxH)
    {
        int w = maxW, h = (int)Math.Round(w * world.Height / (double)world.Width / 2);
        if (h > maxH) { h = maxH; w = (int)Math.Round(h * 2.0 * world.Width / world.Height); }
        return (Math.Max(8, w), Math.Max(4, h));
    }

    // ── Ввод ─────────────────────────────────────────────────────────────────

    // Ждёт нажатие или клик (клик — id зоны, «map:c,r» — клетка карты). Смена наведения — перерисовка (null).
    // timeoutMs > 0 — вернуть null по таймауту (анимация ожидания нейронки). mapClicks — карта кликабельна (выбор старта).
    // Карта в WaitInput: нет / сдвиг и масштаб (Pan) / ещё и клик — выбор клетки, что под курсором (Pick).
    private enum MapMode { None, Pan, Pick }

    private async Task<(ConsoleKeyInfo? key, string? click)?> WaitInput(int timeoutMs, MapMode mapMode = MapMode.None)
    {
        bool mapClicks = mapMode == MapMode.Pick, mapPan = mapMode != MapMode.None;
        var started = DateTime.UtcNow;
        while (true)
        {
            var (move, click, _) = ConsoleMouseReader.DrainMouseEvents();
            if (move is { } m)
            {
                string? h = HitAt(m.x, m.y);
                var tabs = MouseUiHelper.ComputeTitleTabs(Title);
                // Нейронка думает — ничего не нажимается: обычный указатель, вкладки заголовка не подсвечиваются.
                string? tab = _frozen ? null : MouseUiHelper.GetHoveredTabKey(m.x, m.y, tabs);
                if (tab != _titleHover)
                {
                    if (_titleHover != null) MouseUiHelper.SetTabHighlight(Title, tabs, _titleHover, false, _display);
                    if (tab != null) MouseUiHelper.SetTabHighlight(Title, tabs, tab, true, _display);
                    _titleHover = tab;
                }
                ConsoleMouseReader.SetCursorShape(h != null || tab != null || mapPan && MapCell(m.x, m.y) != null);
                // Смена наведения — перерисовка; но если в той же пачке есть нажатие — сначала оно (иначе
                // нажатие с лёгким сдвигом мыши терялось и карта не начинала перетаскиваться).
                var mc = mapClicks ? MapCell(m.x, m.y) : null;
                bool hoverChanged = h != _hovered || mc != _mapHover;
                _hovered = h;
                _mapHover = mc;
                if (hoverChanged && click == null) return null;
            }
            if (click is { } c)
            {
                if (c.y < _bodyTop)
                {
                    // Заголовок: шаг «[Fn]…» или «[Esc]МЕНЮ»; остальное — перетаскивание окна.
                    if (!_frozen && MouseUiHelper.GetHoveredTabKey(c.x, c.y, MouseUiHelper.ComputeTitleTabs(Title)) is { } tk)
                    {
                        Sound.PlayClick();
                        return (null, tk == "Esc" ? "esc" : "step:" + tk[1..]);
                    }
                    ConsoleMouseReader.StartWindowDrag();
                    continue;
                }
                _clickCol = c.x - _x0;
                if (HitAt(c.x, c.y) is { } id) return (null, id);
                if (mapPan && MapCell(c.x, c.y) is { } cell)
                {
                    // Зажал и тянешь — карта едет за курсором; отпустил, не сдвинув, — клик (выбор старта, если можно).
                    bool panned = false;
                    if (MapPan != null)
                    {
                        MapPanBegin?.Invoke();
                        ConsoleMouseReader.TrackDrag(c, (dx, dy) => { if (dx != 0 || dy != 0) { panned = true; MapPan(dx, dy); } });
                    }
                    if (panned || !mapClicks) { _mapHover = null; return null; }
                    return (null, $"map:{cell.col},{cell.row}");
                }
            }
            // Колесо: «wheel:щелчки:зона под курсором» (прокрутка текста; карта колесо не берёт).
            if (ConsoleMouseReader.TakeWheel(out var wp) is int notches and not 0)
            {
                if (mapPan && MapCell(wp.x, wp.y) is { } wc) return (null, $"mapwheel:{notches}:{wc.col},{wc.row}");
                if (HitAt(wp.x, wp.y) is { } wid) return (null, $"wheel:{notches}:{wid}");
            }
            if (ConsoleMouseReader.TryReadKey() is { } key)
            {
                if (key.Key is >= ConsoleKey.F1 and <= ConsoleKey.F4) return (null, "step:" + (key.Key - ConsoleKey.F1 + 1));
                return (key, null);
            }
            if (timeoutMs > 0 && (DateTime.UtcNow - started).TotalMilliseconds >= timeoutMs) return null;
            await Task.Delay(15);
        }
    }

    private (int col, int row)? MapCell(short x, short y)
    {
        if (_map is not { } m) return null;
        int col = x - _x0 - m.left, row = y - _bodyTop - m.top;
        return col >= 0 && col < m.w && row >= 0 && row < m.h ? (col, row) : null;
    }

    // Текстовое поле: печать, Backspace, вставка. true — клавиша обработана.
    private static bool EditText(ref string value, ConsoleKeyInfo key, int max)
    {
        if (ClipboardText.IsPasteKey(key))
        {
            string paste = ClipboardText.Get().Replace("\r", " ").Replace("\n", " ");
            value += paste[..Math.Min(paste.Length, Math.Max(0, max - value.Length))];
            return true;
        }
        if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value = value[..^1]; return true; }
        if (!char.IsControl(key.KeyChar) && value.Length < max) { value += key.KeyChar; return true; }
        return false;
    }

    // ── Холст ────────────────────────────────────────────────────────────────

    private void Begin()
    {
        Console.CursorVisible = false;
        Console.SetCursorPosition(0, StartTop);
        _border.DrawSeparator();
        _bodyTop = StartTop + 1;
        _width = _display.InnerWidth(_display.ViewCols(_settings.Map));
        _height = Math.Max(TopBoxHeight + InfoHeight + 8, Console.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        Console.SetCursorPosition(0, _bodyTop);
        for (int r = 0; r < _height; r++) _border.DrawContentLine(() => { });
        _border.DrawBottomBorder();
        _map = null;
        _hovered = null;
        _frozen = false;
        _mapDrawn = null;
        _mapRect = null;
        _mapHover = null;
        MapPan = null;
        MapPanBegin = null;
    }

    private void Clear()
    {
        _chars = new char[_height, _width];
        _fg = new List<int>?[_height, _width];
        _bgc = new List<int>?[_height, _width];
        for (int r = 0; r < _height; r++) for (int c = 0; c < _width; c++) _chars[r, c] = ' ';
        _hits.Clear();
        _map = null;
    }

    // Кнопка-плашка «  ТЕКСТ  » на заливке фона (видно, что нажимается); под мышью или в фокусе — заливка ярче,
    // недоступна — приглушена. Возвращает x правого края.
    private int Button(int y, int x, string text, bool focused, string id, bool disabled = false)
    {
        disabled |= _frozen;
        bool on = !disabled && (focused || _hovered == id);
        var bg = ColorHelper.MixWith(_display.MainBackground, _display.MainForeground, on ? 0.42 : 0.16);
        string t = $"  {text}  ";
        Put(y, x, t, disabled ? Dim : on ? ColorHelper.Pale(_display.MainForeground, 0.85) : Fg, bg);
        Hit(y, x, t.Length, id);
        return x + t.Length;
    }

    // Поле только для чтения (параметры готового мира): подпись и значение приглушены, без стрелок.
    private void ReadOnlyRow(int y, int lx, int vx, int width, string label, string value, bool center)
    {
        Put(y, lx, label, Fg);
        string text = Fit(value, width - 4);
        Put(y, center ? vx + 2 + (width - 4 - text.Length) / 2 : vx, text, Fg);
    }

    // Подпись поля как в анкете: «→ ИМЯ» у поля в фокусе.
    // Полоса прокрутки текста справа: ползунок «┃», дорожка «│»; текст влезает — ничего.
    private void DrawBar(int top, int x, int rows, int total, int scroll)
    {
        if (TextArea.Bar(rows, total, scroll) is not { } bar) return;
        for (int i = 0; i < rows; i++) Put(top + i, x, bar[i] ? "┃" : "│", bar[i] ? Bright : Dim);
    }

    private void Label(int y, int lx, string label, bool focused) =>
        Put(y, lx - 2, focused ? "→ " + label : "  " + label, focused ? Bright : Fg);

    // Значение-выбор «◄ значение ►» в поле фиксированной ширины: стрелки на месте, значение по центру
    // (клик по стрелкам — листать).
    private void ChoiceValue(int y, int vx, int width, string value, bool focused, string id, string focusId, string? counter = null)
    {
        int inner = width - 4;
        int rx = vx + 2 + inner + 1;
        if (counter != null)
        {
            // «вариант из скольких» — приглушённо у правой стрелки; значение — по центру оставшегося места.
            Put(y, vx + 2 + inner - counter.Length, counter, Dim);
            inner -= counter.Length + 1;
        }
        string text = Fit(value, inner);
        Put(y, vx, "◄", _frozen ? Dim : focused || _hovered == id + ":-1" ? Bright : Fg);
        Hit(y, vx, 1, id + ":-1");
        Put(y, vx + 2 + (inner - text.Length) / 2, text, focused ? Bright : Fg);
        Put(y, rx, "►", _frozen ? Dim : focused || _hovered == id + ":1" ? Bright : Fg);
        Hit(y, rx, 1, id + ":1");
        Hit(y, vx + 2, inner, focusId);
    }

    // Зона клика; пока экран «заморожен» (ждём нейронку) — зон нет: ни наведения, ни кликов.
    private void Hit(int row, int x, int w, string id)
    {
        if (!_frozen) _hits.Add((row, x, w, id));
    }

    private string? HitAt(short x, short y)
    {
        int col = x - _x0, row = y - _bodyTop;
        foreach (var (r, hx, w, id) in _hits)
            if (r == row && col >= hx && col < hx + w) return id;
        return null;
    }

    // Текст экрана; место карты (если она там же) не затирается — карта не мигает при наведении мыши.
    private void Flush(int mapLeft = -1, int mapTop = -1, int mapW = 0, int mapH = 0)
    {
        Console.CursorVisible = false;
        if (_mapRect != (mapLeft, mapTop, mapW, mapH)) _mapDrawn = null;   // карта сместилась — нарисовать заново
        _mapRect = (mapLeft, mapTop, mapW, mapH);
        bool keepMap = _mapDrawn != null;
        for (int r = 0; r < _height; r++)
        {
            if (keepMap && r >= mapTop && r < mapTop + mapH)
            {
                WriteRow(r, 0, mapLeft);
                WriteRow(r, mapLeft + mapW, _width);
            }
            else WriteRow(r, 0, _width);
        }
    }

    private void DrawBox(int y, int x, int w, int h, string title)
    {
        var line = Dim;
        string t = $" {title} ";
        Put(y, x, "╭" + new string('─', w - 2) + "╮", line);
        Put(y, x + (w - t.Length) / 2, t, Bright);
        for (int r = 1; r < h - 1; r++)
        {
            Put(y + r, x, "│", line);
            Put(y + r, x + w - 1, "│", line);
        }
        Put(y + h - 1, x, "╰" + new string('─', w - 2) + "╯", line);
    }

    private int CenterIn(int row, int left, int width, string text, List<int> color)
    {
        int x = left + Math.Max(0, (width - text.Length) / 2);
        Put(row, x, text, color);
        return x;
    }

    private static string Fit(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";

    // Перенос по словам на ширину.
    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var cur = "";
            foreach (var word in para.Split(' '))
            {
                if (cur.Length > 0 && cur.Length + 1 + word.Length > width) { lines.Add(cur); cur = ""; }
                string w = word;
                while (w.Length > width) { lines.Add(w[..width]); w = w[width..]; }
                cur = cur.Length == 0 ? w : cur + " " + w;
            }
            if (cur.Length > 0) lines.Add(cur);
        }
        return lines;
    }

    private void Put(int row, int x, string text, List<int> color, List<int>? bg = null)
    {
        if (row < 0 || row >= _height) return;
        for (int i = 0; i < text.Length && x + i < _width; i++)
        {
            if (x + i < 0) continue;
            _chars[row, x + i] = text[i];
            _fg[row, x + i] = color;
            _bgc[row, x + i] = bg;
        }
    }

    private void WriteRow(int row, int from, int to)
    {
        if (from >= to) return;
        Console.SetCursorPosition(_x0 + from, _bodyTop + row);
        int c = from;
        while (c < to)
        {
            var fg = _fg[row, c];
            var bg = _bgc[row, c];
            int start = c;
            while (c < to && ReferenceEquals(_fg[row, c], fg) && ReferenceEquals(_bgc[row, c], bg)) c++;
            var run = new string(Enumerable.Range(start, c - start).Select(i => _chars[row, i]).ToArray());
            ColorHelper.WriteColored(run, fgColor: fg ?? _display.MainForeground, bgColor: bg ?? _display.MainBackground);
        }
    }
}
