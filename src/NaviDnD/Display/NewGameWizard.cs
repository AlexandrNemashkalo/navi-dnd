using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Helpers;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Display;

// New game step by step, in the style of a hero profile (frames in the center, fields on the left, preview on
// the right): 1 WORLD - new (map right away, concept from the AI using “IMAGE”) or a world where you have
// already played; 2 HERO - profile (NewGameDisplay: new or from the library of heroes); 3 ADVENTURE - after the
// editor: wishes for the plot and, if desired, the starting location by clicking on the map. Blocks:
// “WORLD”/“ADVENTURE” (selection and fields), “MAP” (preview), “ABOUT THE WORLD” (concept).
public class NewGameWizard
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly GameAiClient _ai;
    private readonly BorderDrawer _border;
    public int StartTop { get; }

    // Screen title (steps “[F1]WORLD [F2]HERO ...”, “[Esc]MENU”) - click on it; CanGo - what step is already possible?
    public string Title = "";
    public Func<int, bool> CanGo = _ => false;
    // The world with which we moved on (step 1).
    public string? ChosenWorldId { get; private set; }
    // State of the “World” step between visits to it (returned from another step - everything is as it was).
    private sealed class WorldPick
    {
        public List<WorldEntry> Entries = [];
        public int Sel, Seed;
        public int[] Param = [];
        public string Name = "", DraftKey = "", NewDesc = "", Prompt = "";
        public bool NameEdited;
        public WorldMap? Draft;
        public string? RegisteredId;   // the world of this game creation, already recorded in the library
    }
    private WorldPick? _pick;
    private string? _titleHover;
    private int _clickCol;   // canvas column of the last click (cursor in the text on click) Map with start selection: cell under the
    // cursor, dragging (start - remember the camera, shift in characters).
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
    // What is currently drawn in place of the map: the same map (world, concept, mark) - is not redrawn.
    private string? _mapDrawn;
    private (int left, int top, int w, int h)? _mapRect;

    private List<int> Fg => _display.MainForeground;
    private List<int> Bright => ColorHelper.Pale(_display.MainForeground, 0.75);
    private List<int> Dim => ColorHelper.Darker(_display.MainForeground, 0.5);

    // Layout: form 46, preview 68 (map up to 64x24 - the whole world), gap 3.
    private const int FormWidth = 46, MapBoxWidth = 68, Gap = 3, MapW = 64, MapH = 24;
    private const int LabelCol = 3, ValueCol = 16;
    private const int TopBoxHeight = MapH + 2, InfoHeight = 9;

    public NewGameWizard(WorldState settings, DisplayConfig display, GameAiClient ai)
    {
        _settings = settings;
        _display = display;
        _ai = ai;
        _border = new BorderDrawer(settings, display);
        StartTop = Term.CursorTop;
    }

    // ── Step 1: peace ───────────────────────────── ──────────────────────────────

    // World in the list: from the library (Id), a variant of this game creation (Draft - ready, to the file
    // only if selected) or new (both null).
    private sealed record WorldEntry(string? Id, string Label, WorldMap? Draft = null);

    // New world parameters: ←→ on the field - the next option; the map is immediately rebuilt (the same seed).
    // How much of something is there in the world; index 3 (“usually”) is 0 for the generator, “no” is -3.
    private static string[] Amount => [L.T("none"), L.T("very few"), L.T("few"), L.T("normal"), L.T("many"), L.T("very many")];

    // def - default option; for scales it is also the “zero” of the generator (value = index - def).
    private static (string label, string[] options, int def)[] Params =>
    [
        (L.T("SIZE"), [L.T("small"), L.T("large")], 0),
        (L.T("CONTINENTS"), [L.T("auto"), "1", "2", "3", "4", "5"], 0),
        (L.T("WATER"), [L.T("very few"), L.T("few"), L.T("normal"), L.T("many"), L.T("very many")], 2),
        (L.T("CLIMATE"), [L.T("frozen"), L.T("cold"), L.T("temperate"), L.T("warm"), L.T("hot")], 2),
        (L.T("MOUNTAINS"), Amount, 3),
        (L.T("FORESTS"), Amount, 3),
        (L.T("DESERTS"), Amount, 3),
        (L.T("RIVERS"), Amount, 3),
        (L.T("KINGDOMS"), [L.T("auto"), "1", "2", "3", "4", "5", "6", "7", "8"], 0),
    ];

    private const int DescHeight = 13, DescLines = 9;

    // Semantic groups of parameters (land / nature / peoples) - what parameters start with; There is an empty line between groups.
    private static readonly int[] ParamGroups = [3, 8];

    private static int GroupHeading(int p, int y, int left, int lx) => ParamGroups.Contains(p) ? y + 1 : y;

    // World for a new game: id of the library world (new - saved and made active) or null - cancel (in the
    // menu). On the left is “WORLD” - terrain and map (parameters, “OTHER MAP”, “RANDOM”); on the right is
    // “MAP”; below “DESCRIPTION OF THE WORLD” - your text or edits, a request to the AI and “IMAGE”
    // (description + names of kingdoms and capitals; before that - names from the generator). Returns the step
    // to go to (2.. - the world is selected, ChosenWorldId), or 0 - exit to the menu.
    public async Task<int> PickWorld()
    {
        Begin();
        var rnd = new Random();
        if (_pick == null)
        {
            _pick = new WorldPick { Seed = rnd.Next(), Param = [.. Params.Select(p => p.def)] };
            _pick.Name = WorldGenerator.DefaultName(_pick.Seed, L.Language);
            _pick.Entries.Add(new(null, L.T("new world")));
            string? active = null;
            try { active = WorldLibrary.Current.Id; } catch { }
            foreach (var w in WorldLibrary.All().OrderBy(w => w.id == active ? 0 : 1))
                _pick.Entries.Add(new(w.id, w.name));
        }
        var ps = _pick;
        var entries = ps.Entries;
        int sel = Math.Clamp(ps.Sel, 0, entries.Count - 1);
        int seed = ps.Seed;
        int[] param = ps.Param;   // variant indexes Params
        string name = ps.Name;
        bool nameEdited = ps.NameEdited;
        WorldMap? draft = ps.Draft;
        string draftKey = ps.DraftKey;
        // Description of the world (your own or from the AI) and the cursor in it; request to the AI.
        string? descFor = null;
        string newDesc = ps.NewDesc;   // description of the new world - stored while you flip through the finished ones
        bool descLocked = false;       // ready-made world with description - read only description
        string desc = "";
        int descCur = 0;
        int descScroll = 0, descW = 1;   // scroll description (first visible line), text width
        bool descFollow = true;          // scrolling - behind the cursor (after editing); wheel - how the player spun it
        int lx0 = 0;                     // left edge of blocks (for clicking on text)
        string prompt = ps.Prompt;
        // Focus: 0 - world, 1 - name, 2.. - parameters, FB - map buttons, FD - description, FQ - request, FA -
        // IMAGINE, FN - NEXT.
        int FB = 2 + Params.Length, FD = FB + 1, FQ = FD + 1, FA = FQ + 1, FN = FA + 1;
        int focus = 0;
        int mapAction = 0;
        Task<bool>? aiTask = null;
        WorldMap? aiWorld = null;
        string message = "";
        bool confirmDelete = false;   // wait [Enter] - delete selected world
        int frame = 0;

        WorldMap Draft()
        {
            string key = $"{seed}|{string.Join(",", param)}";
            if (draft == null || draftKey != key)
            {
                draft = WorldGenerator.Generate(new WorldGenerator.Options(seed, param[0] == 1 ? WorldSizes.Large : WorldSizes.Small,
                    Continents: param[1], Water: V(2), Climate: V(3), Mountains: V(4), Forests: V(5),
                    Deserts: V(6), Rivers: V(7), Kingdoms: param[8], Language: L.Language));
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
            // The description is different for each selected world (the finished one is from the chronicle).
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
                if (aiTask.IsFaulted)
                    message = aiTask.Exception?.GetBaseException() is AiSetupException setup
                        ? setup.Message : L.T("The AI didn't respond — try again.");
                else if (aiTask.IsCompletedSuccessfully && aiTask.Result && aiWorld == world)
                {
                    desc = world.Chronicle.Description ?? desc;
                    descCur = desc.Length;
                    descScroll = 0;
                    descFollow = false;   // new text - from the beginning
                    if (!isNew) WorldLibrary.Save(world);   // world from the library - directly to file
                    message = "";
                    prompt = "";   // request completed - input line is empty again
                }
                else if (!aiTask.IsCompletedSuccessfully || !aiTask.Result) message = L.T("The AI didn't respond — try again.");
                aiTask = null;
            }
            if (!Visible(focus)) focus = 0;

            // ── Rendering ──
            _frozen = aiTask != null;   // AI thinks - nothing is pressed
            Clear();
            int total = FormWidth + Gap + MapBoxWidth;
            int left = Math.Max(0, (_width - total) / 2);
            lx0 = left;
            int blockH = TopBoxHeight + 1 + DescHeight + 1 + 3;
            int top = Math.Max(1, (_height - blockH) / 2);

            // Block “WORLD”: terrain and map.
            DrawBox(top, left, FormWidth, TopBoxHeight, L.T("WORLD"));
            int vx = left + ValueCol, lx = left + LabelCol, valueW = FormWidth - ValueCol - 3;
            int y = top + 2;
            Label(y, lx, L.T("WORLD"), focus == 0);
            ChoiceValue(y, vx, valueW, entry.Label, focus == 0, "world", "f:0", $"{sel + 1}/{entries.Count}");
            Hit(y, lx - 2, ValueCol - LabelCol + 2, "f:0");
            y += 2;
            if (isNew)
            {
                Label(y, lx, L.T("TITLE"), focus == 1);
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
                // Map buttons are at the bottom of the block, in squares.
                int bx = lx;
                bx = Button(top + TopBoxHeight - 3, bx, L.T("ANOTHER MAP"), focus == FB && mapAction == 0, "regen:0") + 2;
                Button(top + TopBoxHeight - 3, bx, L.T("RANDOM"), focus == FB && mapAction == 1, "regen:1");
            }
            else
            {
                // Ready world: the same fields, but read-only - without arrows.
                string Level(int p, int v) => Params[p].options[Math.Clamp(v + Params[p].def, 0, Params[p].options.Length - 1)];
                string[] values =
                [
                    world.Size == WorldSizes.Large ? L.T("large") : L.T("small"),
                    world.Continents > 0 ? world.Continents.ToString() : L.T("auto"),
                    Level(2, world.Water), Level(3, world.Climate),
                    Level(4, world.Mountains), Level(5, world.Forests), Level(6, world.Deserts), Level(7, world.RiverAmount),
                    world.Kingdoms.Count.ToString(),
                ];
                ReadOnlyRow(y, lx, vx, valueW, L.T("TITLE"), world.Chronicle.Name is { Length: > 0 } cn ? cn : world.Name, center: false);
                y += 2;
                for (int p = 0; p < Params.Length; p++, y++)
                {
                    y = GroupHeading(p, y, left, lx);
                    ReadOnlyRow(y, lx, vx, valueW, Params[p].label, values[p], center: true);
                }
                if (world.Chronicle.Places.Count > 0) ReadOnlyRow(y++, lx, vx, valueW, L.T("PLACES"), world.Chronicle.Places.Count.ToString(), center: true);
            }

            // Block "MAP".
            int mapBoxLeft = left + FormWidth + Gap;
            DrawBox(top, mapBoxLeft, MapBoxWidth, TopBoxHeight, L.T("MAP"));
            var (mw, mh) = MapSize(world, MapW, MapH);
            int mapLeft = mapBoxLeft + (MapBoxWidth - mw) / 2, mapTop = top + 1 + (MapH - mh) / 2;

            // Block “DESCRIPTION OF THE WORLD”: at the top is the text (your own or edited, the kingdoms are in
            // it), under the separator - enter a request to the AI and INVENT/CHANGE.
            int dTop = top + TopBoxHeight + 1;
            string shownName = isNew ? name : world.Chronicle.Name is { Length: > 0 } wn ? wn : world.Name;
            DrawBox(dTop, left, total, DescHeight, L.T("WORLD DESCRIPTION") + " · " + shownName.ToUpperInvariant());
            int dW = total - 6;
            if (descLocked)
            {
                // Ready-made world: description for the entire block, without input or buttons.
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
            // Text: word wrapping, cursor (highlighted cell) and scrolling - wheel, ↑↓, PgUp/PgDn.
            descW = dW;
            bool descEditing = focus == FD;
            if (desc.Length == 0 && !descEditing) Put(dTop + 1, lx, Fit(L.T("your own text about the world — or let the AI come up with it"), dW), Dim);
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
            // Enter a request and submit button on the right.
            int iy = sepY + 1;
            string goText = desc.Length > 0 ? L.T("CHANGE") : L.T("INVENT");
            int gx = left + total - 3 - (goText.Length + 4);
            // The AI thinks - the entire line (without “Enter:” and the button) is a status.
            if (aiTask != null)
            {
                Put(iy, lx, Fit(Spinner.Frames[frame++ % Spinner.Frames.Length] + " " + L.T("The AI is thinking: description, kingdoms and capitals…"), total - 6), Bright);
            }
            else
            {
            string inputLabel = L.T("Input: ");
            Put(iy, lx, inputLabel, focus == FQ ? Bright : Fg);
            int px = lx + inputLabel.Length, promptW = gx - 2 - px;
            // There is a result (ready / error) and the field is empty - instead of a hint in the input line it.
            string status = message.Split('\n')[0];
            bool showStatus = status.Length > 0 && prompt.Length == 0 && focus != FQ;
            string promptShown = showStatus ? status
                : prompt.Length == 0 && focus != FQ ? L.T("what to invent or how to change the description (optional)") : TailFit(prompt + (focus == FQ ? "▌" : ""), promptW);
            Put(iy, px, Fit(promptShown, promptW), showStatus ? Bright : prompt.Length == 0 && focus != FQ ? Dim : focus == FQ ? Bright : Fg);
            Hit(iy, left + 1, gx - 2 - left, $"f:{FQ}");
            Button(iy, gx, goText, focus == FA, "ai");
            }
            }

            // NEXT and a hint.
            int by = dTop + DescHeight + 1;
            string nextText = L.T("NEXT: HERO");
            Button(by, left + (total - nextText.Length - 4) / 2, nextText, focus == FN, "next", disabled: desc.Trim().Length == 0);
            // Hint; instead, there is a confirmation of deletion or a message (the ready-made world does not have an input line).
            if (confirmDelete) CenterIn(by + 2, left, total, L.F("Delete world \"{0}\" with all its locations?   [Enter]YES   [Esc]NO", entry.Label), Bright);
            else if (message.Length > 0)
            {
                var lines = message.Split('\n');
                for (int i = 0; i < lines.Length && i < 2; i++)
                    CenterIn(by + 2 + i, left, total, Fit(lines[i], total), Bright);
            }
            else CenterIn(by + 2, left, total, L.T("[↑↓]FIELD   [←→]CHOOSE/CURSOR   [Enter]NEXT") + (isNew ? "" : "   " + L.T("[Del]DELETE WORLD")) + "   " + L.T("[Esc]MENU"), Dim);
            // Map: wheel - scale, drag - shift (click does nothing); another world - camera from the beginning.
            if (_worldCam.For != world.Id) _worldCam.Reset(world.Id);
            Flush(mapLeft, mapTop, mw, mh);
            DrawMapAt(world, mapLeft, mapTop, mw, mh, null, _worldCam);
            SetMapPan(_worldCam, () => DrawMapAt(world, mapLeft, mapTop, mw, mh, null, _worldCam));

            // ── Enter ──
            var input = await WaitInput(aiTask != null ? 200 : 0, MapMode.Pan);
            if (input == null) continue;
            var (key, click) = input.Value;
            if (confirmDelete && (key != null || click != null))
            {
                // Deletion confirmation: Enter - yes, Esc/any other key or click - no.
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
            // Wheel above description - scrolling text.
            if (click is { } wh && wh.StartsWith("wheel:"))
            {
                var parts = wh.Split(':', 3);
                if (parts[2].StartsWith("desc")) { descScroll -= int.Parse(parts[1]); descFollow = false; }
                continue;
            }
            if (aiTask != null) continue;   // the AI thinks - input is waiting Steps: further - only with the selected world (as “NEXT”).
            if (click is { } sc && sc.StartsWith("step:"))
            {
                int to = int.Parse(sc[5..]);
                if (to >= 2 && (to == 2 || CanGo(to)) && Proceed() != null) return Leave(to);
                continue;
            }
            if (click is { } c)
            {
                if (c == "world:-1") CycleWorld(-1);
                else if (c == "world:1") CycleWorld(1);
                else if (c.StartsWith("p") && c.Contains(':')) Step(int.Parse(c[1..c.IndexOf(':')]), int.Parse(c[(c.IndexOf(':') + 1)..]));
                else if (c.StartsWith("regen:")) { mapAction = int.Parse(c[6..]); focus = FB; RunMap(); }
                else if (c == "ai") { focus = FA; RunAi(); }
                else if (c.StartsWith("desc:"))
                {
                    Sound.PlayClick();
                    // Click on the text - the cursor is in this place.
                    focus = FD;
                    descCur = TextArea.CursorAt(desc, descScroll, descW, int.Parse(c[5..]), _clickCol - (lx0 + LabelCol));
                    descFollow = true;
                }
                else if (c.StartsWith("f:")) { Sound.PlayClick(); focus = int.Parse(c[2..]); if (focus == FD) descCur = desc.Length; }
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
                    else if (focus == FQ) { if (prompt.Trim().Length > 0) { focus = FA; RunAi(); } }   // empty input - do not send
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
                message = L.T("Wait — the AI is thinking.");
                return true;
            }

            void CycleWorld(int dir)
            {
                if (Busy()) return;
                sel = (sel + dir + entries.Count) % entries.Count;
                message = "";
                Sound.PlayClick();
            }

            // New world parameter: another option is a new map, like “OTHER MAP” (new terrain; name from the
            // generator - new, if not edited; names of the kingdoms - again from the generator).
            void Step(int p, int dir)
            {
                if (Busy()) return;
                Archive();
                param[p] = (param[p] + dir + Params[p].options.Length) % Params[p].options.Length;
                seed = rnd.Next();
                if (!nameEdited) name = WorldGenerator.DefaultName(seed, L.Language);
                message = "";
                Sound.PlayClick();
            }

            // “OTHER MAP” - new terrain with the same parameters; “RANDOM” - random parameters and name.
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
                if (!nameEdited) name = WorldGenerator.DefaultName(seed, L.Language);
                message = "";
            }

            // The new world already has a description - before the new map, it becomes a ready-made option in
            // the leaflet (only for the duration of this creation of the game; it will end up in the library if
            // you move on from it), the description is from scratch.
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
                message = L.T("The previous version is in the world list (◄ ►) while this game is being created.");
            }

            // [Del] - delete the selected world (with confirmation). The world in which the game exists is not deleted.
            void AskDelete()
            {
                Sound.PlayClick();
                if (Busy()) return;
                if (isNew) { message = L.T("The new world isn't saved yet — nothing to delete."); return; }
                if (entry.Id != null && WorldLibrary.GamesUsing(entry.Id) is { Count: > 0 } games)
                {
                    message = L.F("Can't delete: this world has {0} {1} — delete them in \"My games\" first.", games.Count == 1 ? L.T("game") : L.T("games"),
                              string.Join(", ", games.Select(g => $"«{g.hero}»")));
                    return;
                }
                confirmDelete = true;
            }

            void DeleteWorld()
            {
                Sound.PlayClick();
                if (entry.Id != null && !WorldLibrary.Delete(entry.Id)) { message = L.T("Could not delete the world."); return; }
                entries.RemoveAt(sel);
                sel = Math.Min(sel, entries.Count - 1);
                descFor = null;
                message = L.F("World \"{0}\" deleted.", entry.Label);
            }

            void RunAi()
            {
                Sound.PlayClick();
                if (Busy()) return;
                // Without a request, the AI just comes up with ideas from scratch; rewriting a ready-made description “about nothing” is a no-no.
                if (prompt.Trim().Length == 0 && desc.Trim().Length > 0)
                {
                    message = L.T("Write in \"Input\" what to change in the description");   // visible in the input line
                    return;
                }
                aiWorld = world;
                aiTask = _ai.CreateWorld(world, prompt, isNew ? name : null, desc);
            }

            // Leave the step: remember everything filled in (let's go back - as it was).
            int Leave(int step)
            {
                ps.Sel = sel; ps.Seed = seed; ps.Param = param; ps.Name = name; ps.NameEdited = nameEdited;
                ps.Draft = draft; ps.DraftKey = draftKey; ps.Prompt = prompt;
                ps.NewDesc = descFor == "new" ? desc : newDesc;
                return step;
            }

            // World selected: to library/active. A world that was recorded earlier in the same game creation
            // and is now not selected is deleted (if there are no games in it): only the one with which you
            // moved on remains in the library.
            string? Proceed()
            {
                string? id = ProceedCore();
                if (id == null) return null;
                if (ps.RegisteredId != null && ps.RegisteredId != id && WorldLibrary.GamesUsing(ps.RegisteredId).Count == 0)
                    WorldLibrary.Delete(ps.RegisteredId);
                ps.RegisteredId = entry.Id == null ? id : null;
                ChosenWorldId = id;
                // The hero and adventure are in the language of the selected world (the ready-made world is in its own language, the new one is in the interface language).
                L.SetWorld(WorldLibrary.Get(id)?.Language);
                return id;
            }

            string? ProceedCore()
            {
                Sound.PlayClick();
                if (Busy()) return null;
                // A finished world must have a description.
                if (desc.Trim().Length == 0)
                {
                    message = L.T("First the world description: write your own or press \"INVENT\".");
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

    // ── Step 3: adventure ───────────────────────── ──────────────────────────

    // Adventure: wishes, start (place or cell), parameters (indices of options) and they are also a line for the GM.
    public sealed record Adventure(string Wish, string? StartPlace, (int x, int y)? StartTile, int[]? Params = null, string Style = "");

    // Suggestions for the plot and starting place (optional). null - back to the hero editor. Exiting game
    // creation in the menu: warning that the completed content will not be saved. true - exit.
    public async Task<bool> ConfirmExit()
    {
        Begin();
        while (true)
        {
            Clear();
            const int w = 64, h = 7;
            int left = Math.Max(0, (_width - w) / 2), top = Math.Max(1, (_height - h) / 2 - 2);
            DrawBox(top, left, w, h, L.T("EXIT TO MENU?"));
            CenterIn(top + 2, left, w, L.T("New game progress will not be saved."), Bright);
            string quit = L.T("QUIT"), stay = L.T("STAY");
            int bx = left + (w - quit.Length - stay.Length - 10) / 2;
            bx = Button(top + 4, bx, quit, false, "yes") + 2;
            Button(top + 4, bx, stay, false, "no");
            CenterIn(top + h + 1, left, w, L.T("[Enter]EXIT   [Esc]STAY"), Dim);
            Flush();
            var input = await WaitInput(0);
            if (input == null) continue;
            var (key, click) = input.Value;
            if (click?.StartsWith("wheel:") == true) continue;
            if (click == "yes" || key is { Key: ConsoleKey.Enter }) { Sound.PlayClick(); return true; }
            if (click != null || key != null) { Sound.PlayClick(); return false; }
        }
    }

    // Game creation abandoned: the world written to the library on this creation is deleted (if there are no games in it).
    public void DiscardWorld()
    {
        if (_pick?.RegisteredId is { } id && WorldLibrary.GamesUsing(id).Count == 0) WorldLibrary.Delete(id);
    }

    // Step “Adventure”: returns completed and where next - StartStep (start game), step 1-3 or 0 (exit to menu).
    public const int StartStep = 5;

    // Adventure rules parameters (Prompts/{RuleSet}/adventureOptions.json): group, label, options, default.
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
        catch { /*no file - only wishes and start*/ }
        return _advOptions;
    }

    // Adventure parameters in a line for the GM - in the language of the world: “Genre: dark fantasy;
    // Difficulty: difficult; ..." The file contains Russian signatures and variants (translation keys): on the
    // screen - L.T/L.W, for the GM - L.W.
    private string StyleText(int[] values)
    {
        var opts = AdventureOptions();
        return string.Join("; ", opts.Select((o, i) =>
        {
            string label = L.W(o.label);
            return $"{char.ToUpperInvariant(label[0])}{label[1..].ToLowerInvariant()}: {L.W(o.options[Math.Clamp(values[i], 0, o.options.Length - 1)])}";
        }));
    }

    // Map camera (scale: -1 - the whole world; center: NaN - the middle of the world): wheel - scale at the
    // cursor, dragging - shift; “The world through the whole window” does not move further away, it does not go
    // beyond the edge of the world. The “World” and “Adventure” steps have their own.
    private sealed class MapCam
    {
        public int Zoom = -1;
        public double Cx = double.NaN, Cy = double.NaN;
        public string? For;   // the world for which the camera (another world - from the beginning)

        public void Reset(string id) { For = id; Zoom = -1; Cx = Cy = double.NaN; }

        public void Clamp(WorldMapView v, WorldMap world, int w, int h)
        {
            Zoom = Math.Clamp(Zoom < 0 ? v.MaxZoom : Zoom, 0, v.MaxZoom);
            double s = v.Scale(Zoom), halfW = w / 2.0 * s, halfH = h / 2.0 * 2 * s;
            if (double.IsNaN(Cx)) { Cx = world.Width / 2.0; Cy = world.Height / 2.0; }
            Cx = halfW * 2 >= world.Width ? world.Width / 2.0 : Math.Clamp(Cx, halfW, world.Width - halfW);
            Cy = halfH * 2 >= world.Height ? world.Height / 2.0 : Math.Clamp(Cy, halfH, world.Height - halfH);
        }

        // Wheel: world point under the cursor (the col,row area symbol w×h) remains under the cursor.
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

    // Dragging the map (calls WaitInput while the button is held down): remember the center, move by (dx, dy) characters.
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

    // The start mark on the map is a solid circle of a color that is not on the map.
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
        // Focus: 0.. - parameters, FB - start buttons (RESET / RANDOM), FW - wishes, FS - START GAME.
        int FB = opts.Count, FW = FB + 1, FS = FW + 1;
        int focus = 0, action = 0;   // action: 0 - RESET, 1 - RANDOM
        const int WishRows = DescHeight - 2;
        int mw = MapW, mh = MapH;   // what is under the cursor is in the gap in the bottom line of the frame
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

            // Block “ADVENTURE”: parameters in groups, start and buttons.
            DrawBox(top, left, FormWidth, TopBoxHeight, L.T("ADVENTURE"));
            int y = top + 2;
            for (int p = 0; p < opts.Count; p++, y++)
            {
                if (p > 0 && opts[p].group != opts[p - 1].group) y++;
                Label(y, lx, L.T(opts[p].label), focus == p);
                ChoiceValue(y, vx, valueW, L.W(opts[p].options[values[p]]), focus == p, $"p{p}", $"f:{p}");
                Hit(y, lx - 2, ValueCol - LabelCol + 2, $"f:{p}");
            }
            y++;
            Label(y, lx, L.T("START"), false);
            string start = startPlace ?? (startTile is { } st ? TileText(world, st) : L.T("the DM decides"));
            var startLines = Wrap(start, valueW);
            for (int i = 0; i < startLines.Count && i < 2; i++) Put(y + i, vx, startLines[i], hasStart ? StartMarkColor : Dim);
            if (!hasStart) Put(y + 1, vx, Fit(L.T("or click on the map"), valueW), Dim);
            int bx = lx;
            if (hasStart) bx = Button(top + TopBoxHeight - 3, bx, L.T("RESET"), focus == FB && action == 0, "reset") + 2;
            Button(top + TopBoxHeight - 3, bx, L.T("RANDOM START"), focus == FB && (action == 1 || !hasStart), "random");

            // Map block: wheel - scale, drag - shift, click - start.
            int mapBoxLeft = left + FormWidth + Gap;
            DrawBox(top, mapBoxLeft, MapBoxWidth, TopBoxHeight, (world.Chronicle.Name is { Length: > 0 } wn ? wn : L.T("MAP")).ToUpperInvariant());
            int mapLeft = mapBoxLeft + (MapBoxWidth - mw) / 2, mapTop = top + 1;
            string under = "";   // what's under the cursor
            var underColor = Dim;
            if (_map is { } mp && _mapHover is { } hc)
            {
                int place = mp.view.PlaceAtCell(hc.col, hc.row);
                var t = mp.view.TileAt(hc.col, hc.row, mw, mh, _advCam.Cx, _advCam.Cy, _advCam.Zoom);
                if (place >= 0) { under = $"{mp.world.Places[place].Name} — {L.T(WorldPlaceTypes.Label(mp.world.Places[place].Type))}"; underColor = Fg; }
                else if (mp.world.InBounds(t.x, t.y))
                {
                    under = WorldBiomes.IsWater(mp.world.BiomeAt(t.x, t.y)) ? L.T("water — can't start here") : TileText(mp.world, t);
                    underColor = Fg;
                }
            }
            if (under.Length > 0) CenterIn(top + TopBoxHeight - 1, mapBoxLeft, MapBoxWidth, $" {Fit(under, MapBoxWidth - 6)} ", underColor);

            // “STORY WISHES” block (optional): a field with a cursor and scrolling, as a description of the world.
            int dTop = top + TopBoxHeight + 1;
            DrawBox(dTop, left, total, DescHeight, L.T("STORY WISHES"));
            wishW = total - 6;
            bool wishEditing = focus == FW;
            if (wish.Length == 0 && !wishEditing)
                Put(dTop + 1, lx, Fit(L.T("what you want from the story: plot, enemies, mood — optional, the settings above are enough"), wishW), Dim);
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
            string startText = L.T("START GAME");
            Button(by, left + (total - startText.Length - 4) / 2, startText, focus == FS, "next");
            CenterIn(by + 2, left, total, L.T("[↑↓]FIELD   [←→]CHOOSE   [Enter]START   [Esc]MENU"), Dim);

            // Map: first the canvas (the map area is not erased), then the map itself with the camera and the start mark.
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
                if (c.StartsWith("step:")) { Sound.PlayClick(); int to = int.Parse(c[5..]); if (to < 4 && CanGo(to)) return Leave(to); continue; }
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

        // “RANDOM START” - a random land cell (not water or spades); map when approaching - to it.
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

    // Map with camera (scale and shift), start mark - circle.
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
        bool vis = Term.CursorVisible;
        Term.CursorVisible = false;
        for (int r = 0; r < h; r++)
        {
            Term.SetCursorPosition(_x0 + left, _bodyTop + top + r);
            Console.Write(view.RenderRow(r, w, h, cam.Cx, cam.Cy, cam.Zoom, -1));
        }
        ColorHelper.SetBackgroundColor(_display.MainBackground);
        ColorHelper.SetForegroundColor(_display.MainForeground);
        Term.CursorVisible = vis;
    }

    private static string TileText(WorldMap w, (int x, int y) t)
    {
        int own = w.OwnerAt(t.x, t.y);
        string land = own >= 0 && own < w.Kingdoms.Count ? (w.Chronicle.Kingdoms.FirstOrDefault(k => k.Id == own)?.Name ?? w.Kingdoms[own].Name) : L.T("unclaimed lands");
        return $"{L.T(WorldBiomes.Get(w.BiomeAt(t.x, t.y)).Name).ToLowerInvariant()}, {land} ({L.T(WorldAtlas.Compass(w, t.x, t.y))})";
    }

    // ── Map ──────────────────────────────── ────────────────────────────────

    private readonly Dictionary<(string id, int w, int h), WorldMapView> _views = [];

    // The entire chronicle is visible (the player selects the world/start - we show everything that is known about the world).
    private static GameWorldLink KnownAll(WorldMap w) => new() { Id = w.Id, Known = w.Chronicle.Places.Select(p => p.Name).ToList() };

    private WorldMap Preview(WorldMap geo) => GameWorld.Compose(geo, KnownAll(geo), includeHidden: false);

    // Preview - the whole world in a frame: w characters × h lines (line - 2 pixels) according to the proportions of the world.
    private static (int w, int h) MapSize(WorldMap world, int maxW, int maxH)
    {
        int w = maxW, h = (int)Math.Round(w * world.Height / (double)world.Width / 2);
        if (h > maxH) { h = maxH; w = (int)Math.Round(h * 2.0 * world.Width / world.Height); }
        return (Math.Max(8, w), Math.Max(4, h));
    }

    // ── Enter ──────────────────────────────── ─────────────────────────────────

    // Waiting for a press or click (click - id zone, "map:c,r" - map cell). Change of pointing - redraw (null).
    // timeoutMs > 0 - return null by timeout (animation of AI waiting). mapClicks - map is clickable (start
    // selection). Map in WaitInput: is not present / shift and scale (Pan) / also click - select the cell under
    // the cursor (Pick).
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
                // The AI thinks - nothing is clicked: a regular pointer, header tabs are not highlighted.
                string? tab = _frozen ? null : MouseUiHelper.GetHoveredTabKey(m.x, m.y, tabs);
                if (tab != _titleHover)
                {
                    if (_titleHover != null) MouseUiHelper.SetTabHighlight(Title, tabs, _titleHover, false, _display);
                    if (tab != null) MouseUiHelper.SetTabHighlight(Title, tabs, tab, true, _display);
                    _titleHover = tab;
                }
                ConsoleMouseReader.SetCursorShape(h != null || tab != null || mapPan && MapCell(m.x, m.y) != null);
                // Change hover - redraw; but if there is a click in the same pack, it comes first (otherwise
                // the click with a slight mouse shift was lost and the map did not begin to be dragged).
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
                    // Header: step "[Fn]..." or "[Esc]MENU"; the rest is dragging the window.
                    if (!_frozen && MouseUiHelper.GetHoveredTabKey(c.x, c.y, MouseUiHelper.ComputeTitleTabs(Title)) is { } tk)
                    {
                        if (tk == "Esc") Sound.PlayClick();
                        return (null, tk == "Esc" ? "esc" : "step:" + tk[1..]);
                    }
                    ConsoleMouseReader.StartWindowDrag();
                    continue;
                }
                _clickCol = c.x - _x0;
                if (HitAt(c.x, c.y) is { } id) return (null, id);
                if (mapPan && MapCell(c.x, c.y) is { } cell)
                {
                    // If you press and pull, the map follows the cursor; let go without moving - click (select start, if possible).
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
            // Wheel: “wheel:clicks: area under the cursor” (text scrolling; the wheel does not pick up the map).
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

    // Text field: print, Backspace, insert. true - the key has been processed.
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

    // ── Canvas ──────────────────────────────── ────────────────────────────────

    private void Begin()
    {
        Term.CursorVisible = false;
        Term.SetCursorPosition(0, StartTop);
        _border.DrawSeparator();
        _bodyTop = StartTop + 1;
        _width = _display.InnerWidth(_display.ViewCols(_settings.Map));
        _height = Math.Max(TopBoxHeight + InfoHeight + 8, Term.WindowHeight - _bodyTop - 1);
        _x0 = DisplayConfig.LeftMargin + 1;
        Term.SetCursorPosition(0, _bodyTop);
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

    // Button-solid “TEXT” on the background fill (it can be seen that it is pressed); under the mouse or in
    // focus - the fill is brighter, inaccessible - dimmed. Returns x of the right edge.
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

    // Read-only field (ready world options): label and value are muted, no arrows.
    private void ReadOnlyRow(int y, int lx, int vx, int width, string label, string value, bool center)
    {
        Put(y, lx, label, Fg);
        string text = Fit(value, width - 4);
        Put(y, center ? vx + 2 + (width - 4 - text.Length) / 2 : vx, text, Fg);
    }

    // Field label as in the questionnaire: “→ NAME” with the field in focus. Text scroll bar on the right:
    // slider “┃”, track “│”; the text fits - nothing.
    private void DrawBar(int top, int x, int rows, int total, int scroll)
    {
        if (TextArea.Bar(rows, total, scroll) is not { } bar) return;
        for (int i = 0; i < rows; i++) Put(top + i, x, bar[i] ? "┃" : "│", bar[i] ? Bright : Dim);
    }

    private void Label(int y, int lx, string label, bool focused) =>
        Put(y, lx - 2, focused ? "→ " + label : "  " + label, focused ? Bright : Fg);

    // Value-selection “◄ value ►” in a fixed-width field: arrows in place, value in the center (click on the
    // arrows - scroll).
    private void ChoiceValue(int y, int vx, int width, string value, bool focused, string id, string focusId, string? counter = null)
    {
        int inner = width - 4;
        int rx = vx + 2 + inner + 1;
        if (counter != null)
        {
            // “option of how many” - muted near the right arrow; the value is in the center of the remaining space.
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

    // Click zone; While the screen is “frozen” (waiting for a AI) there are no zones: no pointing, no clicks.
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

    // Screen text; the location of the map (if it is there) is not overwritten - the map does not blink when you hover the mouse.
    private void Flush(int mapLeft = -1, int mapTop = -1, int mapW = 0, int mapH = 0)
    {
        Term.CursorVisible = false;
        if (_mapRect != (mapLeft, mapTop, mapW, mapH)) _mapDrawn = null;   // map has shifted - draw again
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

    // Word wrap to width.
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
        Term.SetCursorPosition(_x0 + from, _bodyTop + row);
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
