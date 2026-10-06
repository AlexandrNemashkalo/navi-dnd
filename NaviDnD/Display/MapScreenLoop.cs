using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;
using NaviDnD.Helpers;

namespace NaviDnD;

internal class MapScreenLoop
{
    private readonly WorldState _settings;
    private readonly DisplayConfig _display;
    private readonly Storage _storage;
    private readonly GameAiClient _aiClient;
    private readonly AppConfig _config;
    private readonly MapDisplay _mapDisplay;
    private readonly LegendDisplay _legendDisplay;
    internal MapScreenLoop(
        WorldState settings, DisplayConfig display, Storage storage,
        GameAiClient aiClient, AppConfig config,
        MapDisplay mapDisplay, LegendDisplay legendDisplay)
    {
        _settings = settings;
        _display = display;
        _storage = storage;
        _aiClient = aiClient;
        _config = config;
        _mapDisplay = mapDisplay;
        _legendDisplay = legendDisplay;
    }

    internal async Task RunAsync(ScreenConfig screen, string title)
    {
        _display.ArrowKeysMovement = true;
        MouseUiHelper.SetDefaultImage(_display, _settings.Map.DefaultImage);
        var movementHandler = new MovementHandler(_settings, _storage, _display);
        int mapDrawTop = Console.CursorTop;
        _display.MapDrawTop = mapDrawTop;
        bool firstMapDraw = true;
        var mapObjectsProvider = new MapObjectsProvider(_settings, _display, _storage, _config);
        // Камера следует за героем, только когда он сдвинулся — иначе после перетаскивания карты мышью
        // камера сразу прыгала бы обратно к нему.
        List<int>? followedHeroPos = null;
        // Последнее наведение на карте мира (место, клетка) — UpdateWorldHover.
        (int place, int x, int y)? lastWorldHit = null;
        void FollowHeroIfMoved()
        {
            var hp = _settings.Hero?.Position;
            if (hp == null || (followedHeroPos != null && hp.SequenceEqual(followedHeroPos))) return;
            bool first = followedHeroPos == null;
            // Лестница на другой этаж (он на поле в другом месте): камера сразу к герою.
            bool jumped = !first && hp.Count >= 2
                && _settings.Map.FloorAt(hp[0], hp[1]) != _settings.Map.FloorAt(followedHeroPos![0], followedHeroPos[1]);
            if (jumped)
            {
                // Этажи одного здания (общий контур): камера сдвигается на сдвиг блока — здание стоит на месте
                // экрана, меняется только содержимое. Подвал-подземелье/пещера (другая планировка) — герой в центр.
                var from = _settings.Map.ChunkAt(followedHeroPos![0], followedHeroPos[1]);
                var to = _settings.Map.ChunkAt(hp[0], hp[1]);
                if (_mapDisplay.LockToFloorIfFits()) { }
                else if (from is { Theme: MapChunk.Building } && to is { Theme: MapChunk.Building })
                {
                    _display.CameraCol += to.OriginCol - from.OriginCol;
                    _display.CameraRow += to.OriginRow - from.OriginRow;
                    _mapDisplay.ClampCamera();
                    if (!_mapDisplay.HeroInView()) _mapDisplay.CenterOnHeroIfNeeded(force: true);
                }
                else _mapDisplay.CenterOnHeroIfNeeded(force: true);
            }
            followedHeroPos = [.. hp];
            if (first) _mapDisplay.CenterOnHeroIfNeeded();
            // После лестницы — без «догоняния» у края окна: шаг на лестницу у стены иначе сдвигал карту на клетку.
            if (!jumped) _mapDisplay.FollowHero();
        }

        Action redrawAction = () =>
        {
            Console.CursorVisible = false;
            Console.SetCursorPosition(0, mapDrawTop);
            mapObjectsProvider.Refresh();
            FollowHeroIfMoved();
            _mapDisplay.DrawMap(mapObjectsProvider);
        };
        _display.OnMapRedraw = redrawAction;
        _display.OnMapCellsRedraw = cells =>
        {
            bool vis = Console.CursorVisible;
            Console.CursorVisible = false;
            int sl = Console.CursorLeft, st = Console.CursorTop;
            foreach (var (c, r) in cells) _mapDisplay.RedrawCellFrame(c, r, mapObjectsProvider);
            Console.SetCursorPosition(sl, st);
            Console.CursorVisible = vis;
        };
        _display.OnTabKey = () =>
        {
            int total = _legendDisplay.GetSelectableMapItemCount();
            if (total > 0)
            {
                _display.SelectedMapItemIndex = _display.SelectedMapItemIndex < 0 ? 0
                    : (_display.SelectedMapItemIndex + 1) % total;
                MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings);
            }
        };
        _display.OnShiftTabKey = () =>
        {
            int total = _legendDisplay.GetSelectableMapItemCount();
            if (total > 0)
            {
                _display.SelectedMapItemIndex = _display.SelectedMapItemIndex <= 0 ? total - 1
                    : _display.SelectedMapItemIndex - 1;
                MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings);
            }
        };
        DialogDisplay? hoverDialog = null;
        DateTime? hoverSince = null;
        (int col, int row)? pendingHoverCell = null;
        // Мебель/лестница под курсором, чья картинка сейчас в блоке картинок (наведение на клетку без существа).
        object? shownFurniture = null;
        Door? pendingHoverDoor = null;
        (int col, int row)? mouseHoverCandidate = null;
        DateTime? doorHoverSince = null;
        (int col, int row, bool isHorizontal)? hoveredDoorWall = null;
        var titleTabs = MouseUiHelper.ComputeTitleTabs(title);
        string? hoveredTabKey = null;
        _display.PollAction = () =>
        {
            // During streaming the active title may change (SwitchTab); recompute tabs from it.
            var activeTitle = _display.StreamingTabTitle ?? title;
            var activeTabs = _display.StreamingTabTitle != null
                ? MouseUiHelper.ComputeTitleTabs(activeTitle)
                : titleTabs;

            var (mousePos, clickPos, _) = ConsoleMouseReader.DrainMouseEvents();
            MouseUiHelper.HandleDialogArrowClick(clickPos, hoverDialog);
            if (clickPos.HasValue)
            {
                var clickedTabKey = MouseUiHelper.GetHoveredTabKey(clickPos.Value.x, clickPos.Value.y, activeTabs);
                if (clickedTabKey != null) { Sound.PlayClick(); _display.PendingCommand = clickedTabKey; return; }
                if (clickPos.Value.y is >= 0 and <= 2) { ConsoleMouseReader.StartWindowDrag(); return; }
                // Кнопка «[F10] КОНЕЦ ХОДА» в строке «Порядок боя» легенды.
                if (_display.MapLevel == MapLevel.Location && _legendDisplay.IsHeroTurn && _legendDisplay.EndTurnButton is { x0: >= 0 } eb
                    && clickPos.Value.y == _display.MapDrawTop + 1 + _legendDisplay.CombatLineIndex
                    && clickPos.Value.x >= _mapDisplay.LegendX + eb.x0 && clickPos.Value.x <= _mapDisplay.LegendX + eb.x1)
                {
                    Sound.PlayClick();
                    _display.PendingCommand = "F10";
                    return;
                }
                // Карта мира: клик по переключателю фильтра в легенде.
                if (_display.MapHoverEnabled && hoverDialog != null && _display.MapLevel == MapLevel.World)
                {
                    var zones = _mapDisplay.WorldLegendHitZones();
                    int zi = Array.FindIndex(zones, z => clickPos.Value.y == z.row && clickPos.Value.x >= z.x0 && clickPos.Value.x <= z.x1);
                    if (zi == 0)
                    {
                        // Отладка этапа 1: новый мир другого размера.
                        var cur = _mapDisplay.WorldView.World;
                        var made = WorldLibrary.Create(cur.Size == WorldSizes.Large ? WorldSizes.Small : WorldSizes.Large, new Random().Next());
                        if (_settings.World != null) _settings.World = new GameWorldLink { Id = made.Id };   // отладка: игра — на новый мир
                        lastWorldHit = null;
                        RedrawWorldKeepCursor();
                        return;
                    }
                    if (zi == 1) { ToggleWorldFilter(null); return; }
                    if (zi >= 2) { ToggleWorldFilter(WorldMapView.Categories[zi - 2].key); return; }
                }
                // Карта мира: зажал и тянешь — изображение едет за курсором (как карта в браузере); клик без
                // сдвига по месту — выбрать его.
                if (_display.MapHoverEnabled && hoverDialog != null && _display.MapLevel == MapLevel.World
                    && _mapDisplay.IsInMapArea(clickPos.Value))
                {
                    int clickedPlace = _mapDisplay.WorldHit(clickPos.Value).place;
                    double startX = _display.WorldCenterX, startY = _display.WorldCenterY;
                    bool worldPanned = false;
                    ConsoleMouseReader.TrackDrag(clickPos.Value, (dx, dy) =>
                    {
                        worldPanned = true;
                        _mapDisplay.PanWorld(startX, startY, dx, dy);
                        RedrawWorldKeepCursor();
                    });
                    if (!worldPanned)
                    {
                        // Клик — цель пути (место или любая клетка, в т.ч. в неизведанное); по месту — ещё и выбрать его.
                        var hit = _mapDisplay.WorldHit(clickPos.Value);
                        if (clickedPlace >= 0) _display.SelectedWorldPlace = clickedPlace;
                        Sound.PlayClick();
                        if (hit is { } th) SetTravelTarget(clickedPlace >= 0 ? (_mapDisplay.WorldView.World.Places[clickedPlace].X, _mapDisplay.WorldView.World.Places[clickedPlace].Y) : (th.x, th.y));
                        if (clickedPlace >= 0) ShowWorldPlaceCard(clickedPlace);
                        RedrawWorldKeepCursor();
                        RerenderRightPanelKeepCursor();
                    }
                    return;
                }
                // Локация — поле до 100×100: зажал и тянешь — камера едет. Отпустил, не сдвинув, — обычный клик.
                if (_display.MapHoverEnabled && hoverDialog != null && _display.MapLevel == MapLevel.Location
                    && _mapDisplay.IsInMapArea(clickPos.Value))
                {
                    int startCol = _display.CameraCol, startRow = _display.CameraRow;
                    int lastCol = startCol, lastRow = startRow;
                    bool panned = false;
                    ConsoleMouseReader.TrackDrag(clickPos.Value, (dx, dy) =>
                    {
                        _mapDisplay.PanCamera(startCol, startRow, dx, dy);
                        // Мышь сдвинулась меньше, чем на клетку, — камера та же, кадр не перерисовываем.
                        if (_display.CameraCol == lastCol && _display.CameraRow == lastRow) return;
                        lastCol = _display.CameraCol;
                        lastRow = _display.CameraRow;
                        panned = true;
                        _display.HoveredCell = null;
                        bool vis = Console.CursorVisible;
                        int sl = Console.CursorLeft, st = Console.CursorTop;
                        Console.CursorVisible = false;
                        _mapDisplay.RedrawLocationViewport(mapObjectsProvider); // только область карты, одной записью
                        Console.SetCursorPosition(sl, st);
                        Console.CursorVisible = vis;
                    });
                    if (!panned) HandleMapClick(clickPos.Value);
                    return;
                }
                if (_display.MapHoverEnabled) HandleMapClick(clickPos.Value);
            }

            // Карта мира: колесо мыши — приблизить (от себя) / отдалить, относительно точки под курсором.
            // Прокрутку забираем всегда — иначе накопленная на других видах сработала бы позже.
            int wheel = ConsoleMouseReader.TakeWheel(out var wheelPos);
            if (MouseUiHelper.HandleDialogWheel(hoverDialog, wheel, wheelPos)) { }   // над диалогом — листать историю
            else if (wheel != 0 && _display.MapHoverEnabled && hoverDialog != null
                && _display.MapLevel == MapLevel.World && _mapDisplay.IsInMapArea(wheelPos))
            {
                _mapDisplay.ZoomWorld(wheel, wheelPos);
                RedrawWorldKeepCursor();
            }
            // Местность: колесо над легендой — прокрутка списка (от себя — вверх).
            else if (wheel != 0 && hoverDialog != null && _display.MapLevel == MapLevel.Location
                && wheelPos.x >= _mapDisplay.LegendX && wheelPos.y > _display.MapDrawTop
                && wheelPos.y <= _display.MapDrawTop + _legendDisplay.SeparatorLineIndex)
            {
                _display.LegendScroll = Math.Max(0, _display.LegendScroll - wheel);
                bool lvis = Console.CursorVisible;
                int lsl = Console.CursorLeft, lst = Console.CursorTop;
                Console.CursorVisible = false;
                _mapDisplay.RefreshLegend();
                Console.SetCursorPosition(lsl, lst);
                Console.CursorVisible = lvis;
            }

            if (!_display.MapHoverEnabled || _display.MapLevel != MapLevel.Location)
            {
                if (mousePos.HasValue)
                {
                    var newKey = MouseUiHelper.GetHoveredTabKey(mousePos.Value.x, mousePos.Value.y, activeTabs);
                    if (newKey != hoveredTabKey)
                    {
                        if (hoveredTabKey != null) MouseUiHelper.SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, false, _display);
                        hoveredTabKey = newKey;
                        if (hoveredTabKey != null) MouseUiHelper.SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, true, _display);
                    }
                    if (hoveredTabKey != null)
                    {
                        ConsoleMouseReader.SetCursorShape(true);
                    }
                    else
                    {
                        var (isOnUp, isOnDown) = MouseUiHelper.DialogArrowHover(mousePos.Value, hoverDialog);
                        // Карту мира можно тянуть — над ней курсор-«рука».
                        bool overWorld = _display.MapHoverEnabled && hoverDialog != null
                            && _display.MapLevel == MapLevel.World && _mapDisplay.IsInMapArea(mousePos.Value);
                        if (_display.MapLevel == MapLevel.World && hoverDialog != null) UpdateWorldHover(overWorld ? mousePos.Value : null);
                        ConsoleMouseReader.SetCursorShape(isOnUp || isOnDown || overWorld);
                        hoverDialog?.SetArrowHover(isOnUp, isOnDown);
                    }
                }
                return;
            }

            // Commit pending entity selection after 50 ms of stable hover
            if (pendingHoverCell.HasValue && hoverSince.HasValue
                && pendingHoverCell == _display.HoveredCell
                && (DateTime.UtcNow - hoverSince.Value).TotalMilliseconds >= 50)
            {
                pendingHoverCell = null;
                hoverSince = null;
                int commitIdx = _display.HoveredCell.HasValue
                    ? _legendDisplay.GetSelectableIndexForCell(_display.HoveredCell.Value.col, _display.HoveredCell.Value.row)
                    : -1;
                if (commitIdx != _display.SelectedMapItemIndex)
                {
                    _display.SelectedMapItemIndex = commitIdx;
                    if (commitIdx >= 0)
                    {
                        MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings);
                    }
                    else
                    {
                        MouseUiHelper.SetSelectedImage(_display, null, null, null);
                    }
                    bool vis0 = Console.CursorVisible;
                    Console.CursorVisible = false;
                    int sl0 = Console.CursorLeft, st0 = Console.CursorTop;
                    Console.SetCursorPosition(0, _display.MapDrawTop);
                    _mapDisplay.DrawMap(mapObjectsProvider);
                    hoverDialog?.RerenderRightPanel();
                    ApplyPostRedrawDrain();
                    SyncHoverImage();
                    Console.SetCursorPosition(sl0, st0);
                    Console.CursorVisible = vis0;
                }
            }

            // Commit door image after 50 ms of stable hover
            if (pendingHoverDoor != null && doorHoverSince.HasValue
                && _display.HoveredDoor == pendingHoverDoor
                && (DateTime.UtcNow - doorHoverSince.Value).TotalMilliseconds >= 50)
            {
                pendingHoverDoor = null;
                doorHoverSince = null;
                var committedDoor = _display.HoveredDoor!;
                string doorLabel = committedDoor.IsWorldExit == true ? "Выход на карту мира"
                    : committedDoor.IsWindow == true ? ((committedDoor.IsDoorOpen ?? false) ? "Окно открыто" : "Окно")
                    : (committedDoor.IsDoorOpen ?? false) ? "Дверь открыта" : "Дверь закрыта";
                MouseUiHelper.SetSelectedImage(_display, MouseUiHelper.DoorImage(committedDoor, _settings.Map), doorLabel, committedDoor.Color);
                bool visDoor = Console.CursorVisible;
                Console.CursorVisible = false;
                int slDoor = Console.CursorLeft, stDoor = Console.CursorTop;
                hoverDialog?.RerenderRightPanel();
                Console.SetCursorPosition(slDoor, stDoor);
                Console.CursorVisible = visDoor;
            }

            if (mousePos == null) return;

            // Title tab hover
            var newTabKey = MouseUiHelper.GetHoveredTabKey(mousePos.Value.x, mousePos.Value.y, activeTabs);
            if (newTabKey != hoveredTabKey)
            {
                if (hoveredTabKey != null) MouseUiHelper.SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, false, _display);
                hoveredTabKey = newTabKey;
                if (hoveredTabKey != null) MouseUiHelper.SetTabHighlight(activeTitle, activeTabs, hoveredTabKey, true, _display);
            }
            if (hoveredTabKey != null) { ConsoleMouseReader.SetCursorShape(true); return; }

            // Check for wall/door before cell
            var wall = _mapDisplay.ScreenToWall(mousePos.Value.x, mousePos.Value.y);
            var wallDoor = wall.HasValue
                ? mapObjectsProvider.GetHoveredDoor(wall.Value.col, wall.Value.row, wall.Value.isHorizontal)
                : null;

            if (wallDoor != null)
            {
                ConsoleMouseReader.SetCursorShape(true);
                hoverDialog?.SetArrowHover(false, false);

                if (wallDoor != _display.HoveredDoor)
                {
                    bool visD = Console.CursorVisible;
                    Console.CursorVisible = false;
                    int slD = Console.CursorLeft, stD = Console.CursorTop;

                    var doorOldCell = _display.HoveredCell;
                    var oldDoorWall = hoveredDoorWall;
                    int oldSelectedIdx = _display.SelectedMapItemIndex;

                    _display.HoveredCell = null;
                    pendingHoverCell = null;
                    hoverSince = null;
                    shownFurniture = null; // блок картинок теперь занят дверью — мебель при возврате показать заново

                    if (oldSelectedIdx >= 0)
                    {
                        // Had entity selected — full redraw to clear legend arrow
                        _display.SelectedMapItemIndex = -1;
                        _display.HoveredDoor = wallDoor;
                        hoveredDoorWall = wall;
                        MouseUiHelper.SetSelectedImage(_display, null, null, null);
                        Console.SetCursorPosition(0, _display.MapDrawTop);
                        _mapDisplay.DrawMap(mapObjectsProvider);
                        hoverDialog?.RerenderRightPanel();
                    }
                    else
                    {
                        // Targeted redraws
                        if (oldDoorWall.HasValue)
                        {
                            _display.HoveredDoor = null;
                            RedrawDoor(oldDoorWall.Value);
                        }
                        if (doorOldCell.HasValue)
                            _mapDisplay.RedrawCell(doorOldCell.Value.col, doorOldCell.Value.row, mapObjectsProvider);
                        _display.HoveredDoor = wallDoor;
                        hoveredDoorWall = wall;
                        RedrawDoor(wall.Value);
                        MouseUiHelper.SetSelectedImage(_display, null, null, null);
                        hoverDialog?.RerenderRightPanel();
                    }

                    pendingHoverDoor = wallDoor;
                    doorHoverSince = DateTime.UtcNow;

                    Console.SetCursorPosition(slD, stD);
                    Console.CursorVisible = visD;
                }
                return;
            }
            else if (_display.HoveredDoor != null)
            {
                // Left a door — clear hover
                bool visC = Console.CursorVisible;
                Console.CursorVisible = false;
                int slC = Console.CursorLeft, stC = Console.CursorTop;

                if (hoveredDoorWall.HasValue)
                {
                    _display.HoveredDoor = null;
                    RedrawDoor(hoveredDoorWall.Value);
                }
                else
                {
                    _display.HoveredDoor = null;
                }
                hoveredDoorWall = null;
                pendingHoverDoor = null;
                doorHoverSince = null;
                shownFurniture = null;
                MouseUiHelper.SetSelectedImage(_display, null, null, null);
                hoverDialog?.RerenderRightPanel();

                Console.SetCursorPosition(slC, stC);
                Console.CursorVisible = visC;
            }

            var cell = KnownCellOrNull(_mapDisplay.ScreenToCell(mousePos.Value.x, mousePos.Value.y));

            var (isOnUpArrow, isOnDownArrow) = MouseUiHelper.DialogArrowHover(mousePos.Value, cell.HasValue ? null : hoverDialog);
            var (isOnNoteLeft, isOnNoteRight) = MouseUiHelper.NotePageArrowHover(mousePos.Value, cell.HasValue ? null : hoverDialog, _display);
            ConsoleMouseReader.SetCursorShape(cell.HasValue || isOnUpArrow || isOnDownArrow || isOnNoteLeft || isOnNoteRight);
            hoverDialog?.SetArrowHover(isOnUpArrow, isOnDownArrow);
            hoverDialog?.SetNoteArrowHover(isOnNoteLeft, isOnNoteRight);

            if (cell == _display.HoveredCell)
            {
                mouseHoverCandidate = null;
                return;
            }

            // Jitter filter: require the same cell in two consecutive polls before committing.
            // One-poll delay (~10ms) is enough to suppress hardware jitter without visible lag.
            if (cell != mouseHoverCandidate)
            {
                mouseHoverCandidate = cell;
                return;
            }

            mouseHoverCandidate = null;
            var oldCell = _display.HoveredCell;
            _display.HoveredCell = cell;
            bool vis = Console.CursorVisible;
            Console.CursorVisible = false;
            int sl = Console.CursorLeft, st = Console.CursorTop;

            int newEntityIdx = cell.HasValue
                ? _legendDisplay.GetSelectableIndexForCell(cell.Value.col, cell.Value.row)
                : -1;
            // Навели на существо/объект — показываем его; пустая клетка или курсор вне карты —
            // закреплённое кликом (если есть), иначе ничего.
            int desiredIdx = cell.HasValue && newEntityIdx >= 0 ? newEntityIdx : PinnedIndex();

            if (desiredIdx == _display.SelectedMapItemIndex)
            {
                // Same committed entity (or same "none") — cancel any pending
                pendingHoverCell = null;
                hoverSince = null;
                if (oldCell.HasValue) _mapDisplay.RedrawCell(oldCell.Value.col, oldCell.Value.row, mapObjectsProvider);
                if (cell.HasValue)    _mapDisplay.RedrawCell(cell.Value.col, cell.Value.row, mapObjectsProvider);
            }
            else if (cell.HasValue && newEntityIdx >= 0)
            {
                // Different entity — highlight cell immediately, start/restart 50ms timer
                pendingHoverCell = cell;
                hoverSince = DateTime.UtcNow;
                if (oldCell.HasValue) _mapDisplay.RedrawCell(oldCell.Value.col, oldCell.Value.row, mapObjectsProvider);
                _mapDisplay.RedrawCell(cell.Value.col, cell.Value.row, mapObjectsProvider);
            }
            else
            {
                // Пустая клетка или курсор ушёл с карты — вернуть закреплённое (или снять выбор).
                pendingHoverCell = null;
                hoverSince = null;
                RestorePinnedOrClear();
                Console.SetCursorPosition(0, _display.MapDrawTop);
                _mapDisplay.DrawMap(mapObjectsProvider);
                hoverDialog?.RerenderRightPanel();
                ApplyPostRedrawDrain();
            }

            SyncHoverImage();

            UpdateHoverInfo(_display.HoveredCell);
            Console.SetCursorPosition(sl, st);
            Console.CursorVisible = vis;

            // Мебель или лестница под курсором (на клетке нет существа/объекта) — картинка и название в блоке картинок.
            // По фактической клетке под курсором: после полной перерисовки ApplyPostRedrawDrain мог её сдвинуть.
            void SyncHoverImage()
            {
                var cell = _display.HoveredCell;
                int newEntityIdx = cell.HasValue ? _legendDisplay.GetSelectableIndexForCell(cell.Value.col, cell.Value.row) : -1;
                bool knownCell = cell is { } kc && newEntityIdx < 0 && (_config.RevealMap || _storage.ExploredCells.Contains(kc));
                var furnitureHere = knownCell ? _settings.Map.FurnitureAt(cell!.Value.col, cell.Value.row) : null;
                var terrainHere = knownCell && furnitureHere == null ? _settings.Map.TerrainAt(cell!.Value.col, cell.Value.row) : null;
                var stairHere = terrainHere != null && TerrainCatalog.IsStair(terrainHere.Code) ? terrainHere : null;
                // Рельеф — по виду (соседние клетки того же вида картинку не перерисовывают).
                object? hoverThing = (object?)furnitureHere ?? (stairHere != null ? cell : terrainHere);
                if (!Equals(hoverThing, shownFurniture))
                {
                    shownFurniture = hoverThing;
                    if (furnitureHere != null)
                    {
                        string label = FurnitureCatalog.DisplayName(furnitureHere) + (string.IsNullOrEmpty(furnitureHere.State) ? "" : $" ({furnitureHere.State})");
                        if (FurnitureCatalog.Terrain(furnitureHere) is { } fk && TerrainCatalog.Properties(fk) is { } fprops) label += "\n" + fprops;
                        MouseUiHelper.SetSelectedImage(_display, furnitureHere.Image ?? FurnitureCatalog.Get(furnitureHere.Kind)?.Image, label,
                            FurnitureCatalog.Terrain(furnitureHere)?.LegendColor);
                        hoverDialog?.RerenderRightPanel();
                    }
                    else if (stairHere != null)
                    {
                        var (sc, sr) = cell!.Value;
                        string label = _settings.Map.StairTarget(sc, sr) is var (tc, tr)
                            ? $"{stairHere.Name} → {MapConfig.FloorName(_settings.Map.FloorAt(tc, tr))}" : stairHere.Name;
                        MouseUiHelper.SetSelectedImage(_display, TerrainCatalog.Image(stairHere), label, stairHere.LegendColor);
                        hoverDialog?.RerenderRightPanel();
                    }
                    else if (terrainHere != null)
                    {
                        string label = terrainHere.Name + (TerrainCatalog.Properties(terrainHere) is { } tprops ? "\n" + tprops : "");
                        MouseUiHelper.SetSelectedImage(_display, TerrainCatalog.Image(terrainHere), label, terrainHere.LegendColor ?? terrainHere.Fg);
                        hoverDialog?.RerenderRightPanel();
                    }
                    else if (_display.SelectedMapItemIndex < 0)
                    {
                        MouseUiHelper.SetSelectedImage(_display, null, null, null);
                        hoverDialog?.RerenderRightPanel();
                    }
                }
            }

            void RedrawDoor((int col, int row, bool isHorizontal) dw)
            {
                if (dw.isHorizontal)
                    _mapDisplay.RedrawHorizontalWall(dw.col, dw.row, mapObjectsProvider);
                else
                    _mapDisplay.RedrawVerticalWall(dw.col, dw.row, mapObjectsProvider);
            }

            void ApplyPostRedrawDrain()
            {
                var latest = ConsoleMouseReader.DrainMouseMoves();
                if (latest == null) return;
                var latestCell = KnownCellOrNull(_mapDisplay.ScreenToCell(latest.Value.x, latest.Value.y));
                if (latestCell == _display.HoveredCell) return;
                var prev = _display.HoveredCell;
                _display.HoveredCell = latestCell;
                ConsoleMouseReader.SetCursorShape(latestCell.HasValue);
                int latestEntityIdx = latestCell.HasValue
                    ? _legendDisplay.GetSelectableIndexForCell(latestCell.Value.col, latestCell.Value.row)
                    : -1;
                int latestDesired = latestCell.HasValue && latestEntityIdx >= 0 ? latestEntityIdx : PinnedIndex();
                if (latestDesired != _display.SelectedMapItemIndex)
                {
                    if (latestCell.HasValue && latestEntityIdx >= 0)
                    {
                        // New entity — start pending timer
                        pendingHoverCell = latestCell;
                        hoverSince = DateTime.UtcNow;
                        if (prev.HasValue) _mapDisplay.RedrawCell(prev.Value.col, prev.Value.row, mapObjectsProvider);
                        _mapDisplay.RedrawCell(latestCell!.Value.col, latestCell.Value.row, mapObjectsProvider);
                    }
                    else
                    {
                        // Пустая клетка или вне карты — вернуть закреплённое (или снять выбор).
                        RestorePinnedOrClear();
                        Console.SetCursorPosition(0, _display.MapDrawTop);
                        _mapDisplay.DrawMap(mapObjectsProvider);
                        hoverDialog?.RerenderRightPanel();
                    }
                }
                else
                {
                    if (prev.HasValue)       _mapDisplay.RedrawCell(prev.Value.col, prev.Value.row, mapObjectsProvider);
                    if (latestCell.HasValue) _mapDisplay.RedrawCell(latestCell.Value.col, latestCell.Value.row, mapObjectsProvider);
                }
            }
        };
        var capturedMapPollAction = _display.PollAction;
        _display.MapPollActionFactory = _ => capturedMapPollAction;

        // Клетка под курсором «интерактивна» (рука + подсветка + подсказки), только если герой её
        // видит или уже исследовал — нащупывать мышью то, чего не видел, нельзя.
        (int col, int row)? KnownCellOrNull((int col, int row)? c) =>
            c is { } k && _display.MapLevel == MapLevel.Location
            && (_config.RevealMap || _storage.ExploredCells.Contains(k) || _storage.VisibleCells.Contains(k)) ? c : null;

        // Наведение на карте мира: место — подсветка значка и карточка (картинка, название, тип, королевство),
        // иначе — рельеф клетки и чья земля.
        void UpdateWorldHover((short x, short y)? pos)
        {
            (int place, int x, int y)? hit = pos is { } p ? _mapDisplay.WorldHit(p) : null;
            if (hit == lastWorldHit) return;
            var world = _mapDisplay.WorldView.World;
            bool placeChanged = (hit?.place ?? -1) != _display.HoveredWorldPlace;
            bool tileInfoChanged = hit is not { } h0 || lastWorldHit is not { } l0 || h0.place != l0.place
                || (h0.place < 0 && (world.BiomeAt(h0.x, h0.y) != world.BiomeAt(l0.x, l0.y) || world.OwnerAt(h0.x, h0.y) != world.OwnerAt(l0.x, l0.y)
                    || world.RoadAt(h0.x, h0.y) != world.RoadAt(l0.x, l0.y) || (world.RiverAt(h0.x, h0.y) > 0) != (world.RiverAt(l0.x, l0.y) > 0)
                    || (GameWorld.HeroTile(_settings) is { } ht0 && (ht0 == (h0.x, h0.y)) != (ht0 == (l0.x, l0.y)))));
            lastWorldHit = hit;
            _display.HoveredWorldPlace = hit?.place ?? -1;
            if (placeChanged) RedrawWorldKeepCursor();
            if (!tileInfoChanged) return;
            if (hit is not { } h)
                ShowWorldPlaceCard(_display.SelectedWorldPlace);
            else if (h.place >= 0)
                ShowWorldPlaceCard(h.place);
            else if (world.InBounds(h.x, h.y))
            {
                char b = world.BiomeAt(h.x, h.y);
                int own = world.OwnerAt(h.x, h.y);
                string land = WorldBiomes.IsWater(b) ? "" : own >= 0 && own < world.Kingdoms.Count ? world.Kingdoms[own].Name : "ничьи земли";
                // Река или дорога на клетке — главное (своя картинка и название), рельеф — во второй строке.
                int road = world.RoadAt(h.x, h.y);
                var biome = WorldBiomes.Get(b);
                var (title, image, color) = world.RiverAt(h.x, h.y) > 0 && road > 0 ? ("Мост", road == 2 ? "delapouite/stone-bridge" : "delapouite/arch-bridge", new List<int> { 150, 108, 70 })
                    : world.RiverAt(h.x, h.y) > 0 ? ("Река", "delapouite/river", new List<int> { 72, 122, 178 })
                    : road == 2 ? ("Тракт", "delapouite/stone-path", new List<int> { 176, 148, 102 })
                    : road == 1 ? ("Тропа", "delapouite/trail", new List<int> { 146, 128, 96 })
                    : (biome.Name, WorldMapView.BiomeImage(b), biome.Color);
                string terrain = title == biome.Name ? "" : biome.Name.ToLowerInvariant();
                // Клетка героя — «Вы здесь» его портретом.
                if (GameWorld.HeroTile(_settings) == (h.x, h.y))
                {
                    terrain = title == biome.Name ? biome.Name.ToLowerInvariant() : title.ToLowerInvariant();
                    (title, image, color) = ("Вы здесь", _settings.Hero?.Image ?? image, _settings.Hero?.Color ?? color);
                }
                string second = string.Join(" · ", new[] { terrain, land }.Where(t => t.Length > 0));
                MouseUiHelper.SetSelectedImage(_display, image, title + (second.Length > 0 ? "\n" + second : ""), color);
            }
            else ShowWorldPlaceCard(_display.SelectedWorldPlace);
            RerenderRightPanelKeepCursor();
        }

        // Карточка места мира в блоке картинок (наведение или выбор).
        void ShowWorldPlaceCard(int place)
        {
            var world = _mapDisplay.WorldView.World;
            if (place < 0 || place >= world.Places.Count) { MouseUiHelper.SetSelectedImage(_display, null, null, null); return; }
            var pl = world.Places[place];
            string kingdom = pl.Kingdom >= 0 && pl.Kingdom < world.Kingdoms.Count ? " · " + world.Kingdoms[pl.Kingdom].Name : "";
            MouseUiHelper.SetSelectedImage(_display, WorldMapView.PlaceImage(pl.Type), pl.Name + "\n" + WorldPlaceTypes.Label(pl.Type) + kingdom,
                _mapDisplay.WorldView.PlaceLabelColor(pl));
        }

        void RerenderRightPanelKeepCursor()
        {
            bool vis = Console.CursorVisible;
            int sl = Console.CursorLeft, st = Console.CursorTop;
            Console.CursorVisible = false;
            hoverDialog?.RerenderRightPanel();
            Console.SetCursorPosition(sl, st);
            Console.CursorVisible = vis;
        }

        // Tab / Shift+Tab на карте мира — следующее/предыдущее место из тех, что в окне карты (как в легенде);
        // карта не двигается, набор мест не меняется.
        void SelectWorldPlace(int delta)
        {
            var list = _mapDisplay.VisibleWorldPlaces();
            if (list.Count == 0) return;
            int pos = list.IndexOf(_display.SelectedWorldPlace);
            pos = pos < 0 ? (delta > 0 ? 0 : list.Count - 1) : (pos + delta + list.Count) % list.Count;
            _display.SelectedWorldPlace = list[pos];
            Sound.PlayClick();
            ShowWorldPlaceCard(_display.SelectedWorldPlace);
            RedrawWorldKeepCursor();
            RerenderRightPanelKeepCursor();
        }

        // Цель пути на карте мира: маршрут от героя (клик по клетке героя — снять цель).
        void SetTravelTarget((int x, int y) target)
        {
            bool heroTile = GameWorld.HeroTile(_settings) == target;
            var plan = heroTile ? null : TravelService.PlanTo(_settings, target);
            if (plan == null && !heroTile && GameWorld.HeroTile(_settings) != null)
                MouseUiHelper.SetSelectedImage(_display, "lorc/waves", "Пешком не дойти\nвода или горные пики на пути", [110, 150, 200]);
            _display.WorldTarget = plan != null ? target : null;
            _display.WorldRoute = plan?.Route.Path;
            var w = _mapDisplay.WorldView.World;
            _display.WorldRouteInfo = plan == null ? null
                : (plan.TargetPlace ?? NaviDnD.MapGen.Generators.WorldAtlas.TileName(w, target.x, target.y), plan.Route.Days, plan.Route.RoadShare);
        }

        // В путь: код ведёт героя по маршруту (дни, встречи), мастер одним вызовом описывает дорогу.
        async Task StartTravel(DialogDisplay dialog)
        {
            _settings.History ??= [];
            if (_display.WorldTarget is not { } target || TravelService.PlanTo(_settings, target) is not { } plan)
            {
                _settings.History.Add(new Data.Models.DialogMessage { Text = "Выбери цель пути кликом по карте мира, затем [F10] — в путь." });
                _storage.Save();
                return;
            }
            if (_settings.Combat?.Active == true)
            {
                _settings.History.Add(new Data.Models.DialogMessage { Text = "Идёт бой — уйти в путь нельзя." });
                _storage.Save();
                return;
            }
            var pace = (TravelService.Pace)_display.TravelPace;
            string dest = plan.TargetPlace ?? (GameWorld.ForMaster(_settings) is { } tw ? NaviDnD.MapGen.Generators.WorldAtlas.TileName(tw, target.x, target.y) : "неизведанное");
            string report = TravelService.Go(_settings, plan, pace, new Random());
            // Пришёл в место с сохранённой локацией — «всё как было», герой у входа.
            if (plan.TargetPlace != null && _settings.World?.Place == plan.TargetPlace && GameWorld.Geo(_settings) is { } geo
                && GameWorld.SavedLocationJson(geo, plan.TargetPlace) is { } json)
            {
                _storage.ApplyUpdateWorldState("{\"map\":" + json + "}");
                if (_settings.Hero != null && LocationGrower.EntranceCell(_settings.Map) is var (ec, er)) _settings.Hero.Position = [ec, er];
            }
            _display.WorldTarget = null;
            _display.WorldRoute = null;
            _display.WorldRouteInfo = null;
            if (GameWorld.HeroTile(_settings) is { } h) { _display.WorldCenterX = h.x + 0.5; _display.WorldCenterY = h.y + 0.5; }
            _settings.History.Add(new Data.Models.DialogMessage { Author = _settings.Hero?.Name ?? "Hero", Text = $"В путь: {dest} (темп {TravelService.PaceName(pace)})." });
            _storage.Save();
            _display.DialogScrollOffset = 0;
            _aiClient.ActiveDialog = dialog;
            dialog.PrepareStreaming();
            MusicDirector.Traveling = true;   // пока мастер описывает дорогу — музыка пути
            try { await _aiClient.Travel(report, dialog.AppendStreamChunk, dialog.NewStreamingMessage, dialog.TickSpinner, null); }
            finally { MusicDirector.Traveling = false; }
            dialog.FinalizeStreaming();
            while (Console.KeyAvailable) Console.ReadKey(true);
            _display.PendingCommand = GameWorld.HasLocation(_settings) ? "F1" : null;
            RedrawWorldKeepCursor();
        }

        // key == null — «Все»: включить все категории (если уже все — выключить все).
        void ToggleWorldFilter(string? key)
        {
            if (key == null)
            {
                bool all = WorldMapView.Categories.All(c => _display.WorldFilter.Contains(c.key));
                _display.WorldFilter.Clear();
                if (!all) foreach (var c in WorldMapView.Categories) _display.WorldFilter.Add(c.key);
            }
            else if (!_display.WorldFilter.Remove(key)) _display.WorldFilter.Add(key);
            Sound.PlayClick();
            var world = _mapDisplay.WorldView.World;
            int sel = _display.SelectedWorldPlace;
            if (sel >= 0 && sel < world.Places.Count && !_display.WorldFilter.Contains(WorldMapView.Category(world.Places[sel].Type)))
            {
                _display.SelectedWorldPlace = -1;
                MouseUiHelper.SetSelectedImage(_display, null, null, null);
            }
            RedrawWorldKeepCursor();
        }

        void RedrawWorldKeepCursor()
        {
            bool vis = Console.CursorVisible;
            int sl = Console.CursorLeft, st = Console.CursorTop;
            Console.CursorVisible = false;
            _mapDisplay.RedrawWorldViewport();
            Console.SetCursorPosition(sl, st);
            Console.CursorVisible = vis;
        }

        // Подсказка при наведении — строка блока боя: дистанция, а в бою в ход героя — путь; подсветка
        // комнаты/зоны под курсором (панель картинок не трогаем, там только картинка выбранного).
        // Перерисовываются только изменившиеся строки легенды.
        void UpdateHoverInfo((int col, int row)? c)
        {
            _display.HoverPath = ComputeHoverPath(c);
            _mapDisplay.RefreshLegend();
        }

        // Дистанция до клетки всегда; в бою в ход героя — ещё и путь (до соседней с существом клетки, если курсор на нём).
        HoverPathInfo? ComputeHoverPath((int col, int row)? c)
        {
            if (c is not { } t || _settings.Hero?.Position is not { Count: >= 2 } hp || (hp[0], hp[1]) == t) return null;
            int distanceFt = TargetGeometry.DistanceFt(hp[0], hp[1], t.col, t.row);
            if (_settings.Combat?.Active != true || _settings.Combat.CurrentTurn != _settings.Hero.Symbol)
                return new HoverPathInfo(distanceFt, false, null, null);

            var reached = MovementCalculator.Dijkstra(_settings, hp[0], hp[1], 1000, -1, _storage.DiagonalUsed > 0, passPeaceful: true);
            int? Cost(int col, int row)
            {
                if ((col, row) == (hp[0], hp[1])) return 0;
                int? best = null;
                foreach (bool d in (bool[])[false, true])
                    if (reached.TryGetValue((col, row, d), out var v) && (best == null || v.cost < best)) best = v.cost;
                return best;
            }

            var occupant = (_settings.Map.Entities ?? []).FirstOrDefault(e => e.Deleted != true
                && e.Position is { Count: >= 2 } p && p[0] == t.col && p[1] == t.row);
            if (occupant == null) return new HoverPathInfo(distanceFt, true, Cost(t.col, t.row), null);

            int? toNeighbour = null;
            for (int dc = -1; dc <= 1; dc++)
            for (int dr = -1; dr <= 1; dr++)
                if ((dc != 0 || dr != 0) && Cost(t.col + dc, t.row + dr) is int v && (toNeighbour == null || v < toNeighbour))
                    toNeighbour = v;
            return new HoverPathInfo(distanceFt, true, toNeighbour, occupant.Symbol);
        }

        // Курсор ушёл с карты: вернуть закреплённый кликом (если он ещё на карте и виден) или снять выбор.
        void RestorePinnedOrClear()
        {
            var pinned = _display.PinnedMapEntity;
            int pinnedIdx = pinned?.Position is { Count: >= 2 } pp && pinned.Deleted != true
                ? _legendDisplay.GetSelectableIndexForCell(pp[0], pp[1]) : -1;
            if (pinnedIdx < 0) _display.PinnedMapEntity = null;
            SelectMapItem(pinnedIdx);
        }

        void SelectMapItem(int idx)
        {
            _display.SelectedMapItemIndex = idx;
            if (idx >= 0) MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings);
            else
            {
                MouseUiHelper.SetSelectedImage(_display, null, null, null);
                _display.NotePage = 0;
                _display.NotePageCount = 0;
            }
        }

        int PinnedIndex() => _display.PinnedMapEntity?.Position is { Count: >= 2 } p
            ? _legendDisplay.GetSelectableIndexForCell(p[0], p[1]) : -1;

        // Клик на карте: ◄ ► карточки монстра в панели картинки — листание (стрелки клавиатуры на карте
        // заняты движением); существо/объект — закрепить его картинку (повторный клик ничего не меняет);
        // пустая изученная клетка — открепить.
        void HandleMapClick((short x, short y) click)
        {
            var na = hoverDialog?.NotePageArrowPositions;
            if (na is { } a && a.arrowY >= 0 && click.y == a.arrowY && _display.NotePageCount > 1
                && (click.x == a.leftX || click.x == a.rightX))
            {
                int page = Math.Clamp(_display.NotePage + (click.x == a.leftX ? -1 : 1), 0, _display.NotePageCount - 1);
                if (page == _display.NotePage) return;
                Sound.PlayClick();
                MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings, page);
                hoverDialog?.RerenderRightPanel();
                return;
            }

            var clicked = KnownCellOrNull(_mapDisplay.ScreenToCell(click.x, click.y));
            if (clicked is not { } cc) return;

            int idx = _legendDisplay.GetSelectableIndexForCell(cc.col, cc.row);
            if (idx < 0)
            {
                if (_display.PinnedMapEntity == null) return;
                _display.PinnedMapEntity = null;
                Sound.PlayClick();
                SelectMapItem(-1);
                RedrawSelection(true);
                return;
            }

            pendingHoverCell = null;
            hoverSince = null;
            bool selectionChanged = idx != _display.SelectedMapItemIndex;
            _display.SelectedMapItemIndex = idx;
            var entity = _legendDisplay.GetSelectedEntity();
            if (entity == null) return;
            if (entity != _display.PinnedMapEntity) Sound.PlayClick(); // уже закреплён — повторный клик ничего не меняет
            _display.PinnedMapEntity = entity;
            if (!selectionChanged) return;
            MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings);
            RedrawSelection(true);
        }

        // Выбор элемента карты сменился: легенда (маркер «←» выбранного — полная перерисовка карты) и панель картинки.
        void RedrawSelection(bool redrawMap)
        {
            bool vis = Console.CursorVisible;
            int sl = Console.CursorLeft, st = Console.CursorTop;
            Console.CursorVisible = false;
            if (redrawMap)
            {
                Console.SetCursorPosition(0, _display.MapDrawTop);
                _mapDisplay.DrawMap(mapObjectsProvider);
            }
            hoverDialog?.RerenderRightPanel();
            Console.SetCursorPosition(sl, st);
            Console.CursorVisible = vis;
        }
        _display.MapOnTabKey = _display.OnTabKey;
        _display.MapOnShiftTabKey = _display.OnShiftTabKey;
        _display.RedrawCurrentContent = () =>
        {
            int sl = Console.CursorLeft, st = Console.CursorTop;
            Console.CursorVisible = false;
            Console.SetCursorPosition(0, mapDrawTop);
            mapObjectsProvider.Refresh();
            _mapDisplay.DrawMap(mapObjectsProvider);
            Console.SetCursorPosition(sl, st);
        };

        async Task RunEnemyTurn()
        {
            if (_settings.Combat?.Active != true) return;
            if (string.IsNullOrEmpty(_settings.Combat.CurrentTurn)) return;
            var entitySymbols = _settings.Map.Entities?
                .Where(e => e.Deleted != true)
                .Select(e => e.Symbol)
                .ToHashSet() ?? [];
            if (!entitySymbols.Contains(_settings.Combat.CurrentTurn)) return;

            Console.SetCursorPosition(0, mapDrawTop);
            mapObjectsProvider.Refresh();
            _mapDisplay.DrawMap(mapObjectsProvider);
            var enemyHistory = new DialogDisplay(_settings, _display, _storage);
            _aiClient.ActiveDialog = (enemyHistory);
            enemyHistory.PrepareStreaming();
            await _aiClient.FireEnemyTurn(
                enemyHistory.AppendStreamChunk,
                enemyHistory.NewStreamingMessage,
                enemyHistory.TickSpinner,
                onRedraw: redrawAction);
            enemyHistory.FinalizeStreaming();
            while (Console.KeyAvailable) Console.ReadKey(true);
        }

        // spotted проверяется не только на шаге героя (TryMove): ответ ИИ мог открыть дверь или убрать
        // преграду, а враги в бою — подойти к ещё не вступившему союзнику. Без этой проверки увидевшие
        // героя враги молчали до его следующего шага, и он успевал уйти.
        // Наполнение локации по блокам (LocationGrower): геометрия всех блоков плана уже есть, один запрос к
        // нейронке наполняет блок, когда герой подходит к нему — до того, как он туда войдёт.
        var populateAttempted = new HashSet<MapChunk>();
        async Task<bool> GrowLocationIfNeeded()
        {
            if (_display.MapLevel != MapLevel.Location || _config.DisableTriggers) return false;
            // Одна попытка на блок за запуск: если ответ ИИ упал, не долбить запросами на каждом шаге.
            var chunk = LocationGrower.UnpopulatedNearHero(_settings);
            if (chunk == null || !populateAttempted.Add(chunk)) return false;

            var dialog = new DialogDisplay(_settings, _display, _storage);
            _aiClient.ActiveDialog = dialog;
            dialog.PrepareStreaming();
            await _aiClient.PopulateChunk(chunk, dialog.AppendStreamChunk, dialog.NewStreamingMessage, dialog.TickSpinner, redrawAction);
            dialog.FinalizeStreaming();
            _aiClient.ActiveDialog = null;
            // Буфер клавиш не чистим: наполнение запустил не игрок, и набранное за это время — его следующее
            // действие (раньше текст молча пропадал, и ход в бою уходил впустую).
            return true;
        }

        async Task FireEncounterTriggers(DialogDisplay dialog)
        {
            if (_config.DisableTriggers || _config.RevealMap) return;
            var encounters = movementHandler.CollectEncounterTriggers();
            if (encounters.Count == 0) return;

            redrawAction();
            _aiClient.ActiveDialog = dialog;
            dialog.PrepareStreaming();
            await _aiClient.FireTrigger(encounters, [], [], dialog.AppendStreamChunk, dialog.NewStreamingMessage, dialog.TickSpinner, redrawAction);
            dialog.FinalizeStreaming();
            _aiClient.ActiveDialog = null;
            while (Console.KeyAvailable) Console.ReadKey(true);
        }

        // Финальная запись хода врагов тоже может продвинуть раунд (totalRounds+1, см. § БОЙ) —
        // то же напоминание об onRound/onExpire эффектах должно сработать и здесь через тот же
        // дедуплицированный путь, иначе периодические эффекты (например, горящий враг) никогда не
        // тикают во время боя — тот же класс бага, что чинили для SendAction вне боя.
        async Task RunEnemyTurnAndCheckRoundEnd()
        {
            await RunEnemyTurn();
            if (_settings.Combat?.Active == true)
                await FireEncounterTriggers(new DialogDisplay(_settings, _display, _storage));

            var bundle = _storage.TakeRoundBundle();
            if (bundle != null && !_config.DisableTriggers && !_config.RevealMap)
            {
                var roundHistory = new DialogDisplay(_settings, _display, _storage);
                _aiClient.ActiveDialog = (roundHistory);
                roundHistory.PrepareStreaming();
                await _aiClient.FireRoundTransition(bundle, roundHistory.AppendStreamChunk, roundHistory.NewStreamingMessage, roundHistory.TickSpinner, redrawAction);
                roundHistory.FinalizeStreaming();
                _aiClient.ActiveDialog = (null);
                while (Console.KeyAvailable) Console.ReadKey(true);
            }
        }

        bool IsHeroTurnOver()
        {
            if (_settings.Combat?.Active != true) return false;
            if (_settings.Combat.CurrentTurn != _settings.Hero?.Symbol) return false;
            var actions = _settings.Hero?.Actions;
            if (actions == null || actions.Count == 0) return false;
            return actions.All(a => a.Deleted == true || a.Value == 0)
                && (_settings.Hero!.SpeedLeft ?? 1) == 0;
        }

        void PanByKey(string input)
        {
            var (dx, dy) = input switch
            {
                "PanLeft" => (-1, 0),
                "PanRight" => (1, 0),
                "PanUp" => (0, 1),
                _ => (0, -1),
            };
            int sl = Console.CursorLeft, st = Console.CursorTop;
            bool vis = Console.CursorVisible;
            Console.CursorVisible = false;
            if (_display.MapLevel == MapLevel.World)
            {
                // Карта мира: Y растёт вниз, шаг — несколько символов экрана.
                _mapDisplay.PanWorld(_display.WorldCenterX, _display.WorldCenterY, -dx * 6, dy * 3);
                _mapDisplay.RedrawWorldViewport();
            }
            else
            {
                const int step = 2;
                _display.CameraCol += dx * step;
                _display.CameraRow += dy * step;
                _mapDisplay.ClampCamera();
                _display.HoveredCell = null;
                _mapDisplay.RedrawLocationViewport(mapObjectsProvider);
            }
            Console.SetCursorPosition(sl, st);
            Console.CursorVisible = vis;
        }

        bool movedFrameDrawn = false;
        int downTurnRound = -1;

        // Служебная строка в диалоге без повтора (игрок жмёт одно и то же).
        void AddNoteOnce(string text)
        {
            _settings.History ??= [];
            if (_settings.History.LastOrDefault()?.Text == text) return;
            _settings.History.Add(new Data.Models.DialogMessage { Text = text });
            _storage.Save();
        }

        // Ход героя закончен — следующему живому участнику по инициативе (по кругу). false — передавать некому.
        bool PassTurnToNext()
        {
            if (_settings.Combat is not { Active: true, Initiative: { Count: > 0 } init } combat) return false;
            var order = init.Where(i => i.Deleted != true).OrderByDescending(i => i.Score).ToList();
            int at = order.FindIndex(i => i.Symbol == combat.CurrentTurn);
            var next = order.Skip(at + 1).Concat(order.Take(at + 1)).FirstOrDefault(i => i.Symbol != _settings.Hero?.Symbol
                && _settings.Map.Entities?.Any(e => e.Deleted != true && e.Symbol == i.Symbol) == true);
            if (next == null) return false;
            _settings.History ??= [];
            _settings.History.Add(new Data.Models.DialogMessage { Author = _settings.Hero?.Name ?? "Hero", Text = "Завершаю ход." });
            combat.CurrentTurn = next.Symbol;
            _storage.Save();
            return true;
        }

        while (true)
        {
            // Hide cursor before any repositioning/drawing to avoid visual jumping
            Console.CursorVisible = false;

            // On movement: restore cursor and redraw in-place (no Console.Clear)
            if (!firstMapDraw)
                Console.SetCursorPosition(0, mapDrawTop);
            firstMapDraw = false;

            // Герой сдвинулся / ИИ поменял мир — путь до клетки под курсором пересчитать.
            // После простого шага кадр уже нарисован быстрым путём (RedrawAfterMove) — второй раз не нужно.
            if (!movedFrameDrawn)
            {
                _display.HoverPath = ComputeHoverPath(KnownCellOrNull(_display.HoveredCell));
                mapObjectsProvider.Refresh();
                FollowHeroIfMoved();
                _mapDisplay.DrawMap(mapObjectsProvider);
            }
            else
                Console.SetCursorPosition(_mapDisplay.DrawEndLeft, _mapDisplay.DrawEndTop);
            movedFrameDrawn = false;

            // Новая игра: вступление мастера — посимвольно, со звуком, как обычный ответ.
            if (_aiClient.HasPendingIntro)
            {
                var introDialog = new DialogDisplay(_settings, _display, _storage);
                _aiClient.ActiveDialog = introDialog;
                introDialog.PrepareStreaming();
                await _aiClient.PlayIntro(introDialog.AppendStreamChunk, introDialog.NewStreamingMessage, redrawAction);
                introDialog.FinalizeStreaming();
                _aiClient.ActiveDialog = null;
                while (Console.KeyAvailable) Console.ReadKey(true);
                continue;
            }

            // Герой подошёл к выходу к ещё не созданному блоку — код его генерирует, нейронка наполняет.
            if (await GrowLocationIfNeeded()) continue;

            var mapHistory = new DialogDisplay(_settings, _display, _storage);
            hoverDialog = mapHistory;
            // Ход героя, а он без сознания (0 хитов): сам он не действует — мастер ведёт его ход (спасбросок от
            // смерти и т.п. по правилам НРИ), раз в раунд, игроку писать нечего.
            string? downTurn = null;
            if (_settings.Hero?.Dead == true) AddNoteOnce($"{_settings.Hero.Name} погиб. Игра окончена — [Esc] меню.");
            if (_settings.Combat is { Active: true } dc && dc.CurrentTurn == _settings.Hero?.Symbol
                && _settings.Hero is { IsDown: true, Dead: not true } && downTurnRound != _settings.Time.TotalRounds)
            {
                downTurnRound = _settings.Time.TotalRounds;
                downTurn = $"[{_settings.Hero.Name} без сознания — его ход ведёт мастер]";
            }
            var mapInput = downTurn ?? mapHistory.Draw();
            hoverDialog = null;

            // Герой погиб — игра окончена: действия и движение не принимаются (вкладки и меню — как обычно).
            if (_settings.Hero?.Dead == true && mapInput is { Length: > 0 } && !mapInput.StartsWith('F') && mapInput != "Esc")
            {
                AddNoteOnce($"{_settings.Hero.Name} погиб. Игра окончена — [Esc] меню.");
                continue;
            }

            // [F10] (или кнопка в блоке боя) — конец хода героя: ход следующему по инициативе, ходы врагов — обычным
            // вызовом «ход врагов». Раньше уходил текст «Завершаю ход» от героя, и мастер дописывал за него
            // незаявленное («отступаешь на полшага», «переводишь дух»).
            if (mapInput == "F10" && _display.MapLevel == MapLevel.Location)
            {
                if (!_legendDisplay.IsHeroTurn) continue;
                if (PassTurnToNext())
                {
                    await RunEnemyTurnAndCheckRoundEnd();
                    break;
                }
                mapInput = "Завершаю ход.";
            }
            if (mapInput == "F9" && _display.MapLevel == MapLevel.Location) {
                var braille = File.ReadAllText(Path.Combine(AppConfig.ProjectRoot, "Storage", "test.txt"));
                _settings.Images ??= [];
                _settings.Images.RemoveAll(i => i.Key == "test");
                _settings.Images.Add(new WorldImage { Key = "test", Content = braille, Height = braille.Split('\n').Length });
                _settings.ActiveImageKey = "test";
                continue;
            } // DEBUG

            // Карта мира: [F10] — в путь к выбранной цели, [F5] — темп.
            if (_display.MapLevel == MapLevel.World && mapInput == "F10")
            {
                await StartTravel(mapHistory);
                if (_display.PendingCommand != null) break;
                continue;
            }
            // [F12] — темп пути (медленный / обычный / быстрый).
            if (_display.MapLevel == MapLevel.World && mapInput == "F12")
            {
                _display.TravelPace = (_display.TravelPace + 1) % 3;
                RedrawWorldKeepCursor();
                continue;
            }

            if (string.IsNullOrEmpty(mapInput)) break;
            if (screen.Commands.TryGetValue(mapInput, out var screenCmd)) { screenCmd(); break; }
            if (_display.MapLevel == MapLevel.World && mapInput is "F6" or "F7" or "F8" or "F9")
            {
                ToggleWorldFilter(mapInput == "F6" ? null : WorldMapView.Categories[int.Parse(mapInput[1..]) - 7].key);
                movedFrameDrawn = true;
                continue;
            }
            if (_display.MapLevel == MapLevel.World && mapInput is "Tab" or "ShiftTab")
            {
                SelectWorldPlace(mapInput == "Tab" ? 1 : -1);
                while (Console.KeyAvailable) Console.ReadKey(intercept: true);
                movedFrameDrawn = true;
                continue;
            }
            if (mapInput is "F6" or "F7" or "F8" or "NoteLeft" or "NoteRight") continue;   // служебные команды — не текст мастеру

            // Ctrl+стрелки — сдвиг камеры без движения героя (как перетаскивание мышью).
            // Ctrl+«+»/«−» — масштаб карты мира (на карте локации масштаба нет).
            if (mapInput is "ZoomIn" or "ZoomOut")
            {
                if (_display.MapLevel == MapLevel.World)
                {
                    int sl = Console.CursorLeft, st = Console.CursorTop;
                    bool vis = Console.CursorVisible;
                    Console.CursorVisible = false;
                    _mapDisplay.ZoomWorldAtCenter(mapInput == "ZoomIn" ? 1 : -1);
                    _mapDisplay.RedrawWorldViewport();
                    Console.SetCursorPosition(sl, st);
                    Console.CursorVisible = vis;
                }
                movedFrameDrawn = true;
                continue;
            }

            if (mapInput.StartsWith("Pan"))
            {
                PanByKey(mapInput);
                movedFrameDrawn = true; // кадр уже перерисован — полный DrawMap не нужен
                continue;
            }
            // Alt+←/→ — листание карточки в панели картинки (обычные стрелки на карте — движение).
            // Без карточки команда просто игнорируется, а не уходит нейронке как текст действия.
            if (mapInput is "Left" or "Right")
            {
                int page = Math.Clamp(_display.NotePage + (mapInput == "Left" ? -1 : 1), 0, Math.Max(0, _display.NotePageCount - 1));
                if (_display.SelectedMapItemIndex >= 0 && _display.NotePageCount > 1 && page != _display.NotePage)
                {
                    Sound.PlayClick();
                    MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings, page);
                }
                continue;
            }

            if (mapInput == "Tab" || mapInput == "ShiftTab")
            {
                int totalCount = _legendDisplay.GetSelectableMapItemCount();
                if (totalCount > 0)
                {
                    if (mapInput == "Tab")
                        _display.SelectedMapItemIndex = _display.SelectedMapItemIndex < 0 ? 0
                            : (_display.SelectedMapItemIndex + 1) % totalCount;
                    else
                        _display.SelectedMapItemIndex = _display.SelectedMapItemIndex <= 0 ? totalCount - 1
                            : _display.SelectedMapItemIndex - 1;
                    MouseUiHelper.SetSelectedMapEntity(_display, _legendDisplay, _settings);
                }
                while (Console.KeyAvailable) Console.ReadKey(intercept: true);
                continue;
            }

            // На карте мира стрелки двигают карту, а не героя (он на локации).
            if (_display.MapLevel == MapLevel.World && mapInput.StartsWith("Move"))
            {
                if (mapInput.Contains("North")) PanByKey("PanUp");
                if (mapInput.Contains("South")) PanByKey("PanDown");
                if (mapInput.Contains("West")) PanByKey("PanLeft");
                if (mapInput.Contains("East")) PanByKey("PanRight");
                movedFrameDrawn = true;
                continue;
            }

            var moved = movementHandler.TryMove(mapInput, out var aiInput, out var activatedTriggers, out var activatedAreaTriggers, out var activatedDoorTriggers);
            if (movementHandler.LeaveRequested)
            {
                // Шаг через выход на карту мира: локация сохраняется за местом, вкладка «Карта» — мир.
                movementHandler.LeaveRequested = false;
                string where = _settings.World?.Place ?? "локацию";
                GameWorld.LeaveLocation(_settings);
                _settings.History ??= [];
                _settings.History.Add(new Data.Models.DialogMessage { Text = $"Ты покидаешь {where} — карта мира: выбери цель кликом, [F10] — в путь." });
                _storage.Save();
                _display.PendingCommand = "F2";
                break;
            }
            if (moved == null)
            {
                _settings.History ??= [];
                _settings.History.Add(new Data.Models.DialogMessage { Author = _settings.Hero?.Name ?? "Hero", Text = mapInput });
                _display.DialogScrollOffset = 0;
                _aiClient.ActiveDialog = (mapHistory);
                mapHistory.PrepareStreaming();
                await _aiClient.SendAction(aiInput, mapHistory.AppendStreamChunk, mapHistory.NewStreamingMessage, mapHistory.TickSpinner, redrawAction);
                mapHistory.FinalizeStreaming();
                while (Console.KeyAvailable) Console.ReadKey(true);
                // Мастер построил сцену (plan_location) — на карту местности; тактическая карта пропала — на мир.
                if ((_display.MapLevel == MapLevel.World) == GameWorld.HasLocation(_settings))
                {
                    _display.PendingCommand = GameWorld.HasLocation(_settings) ? "F1" : "F2";
                    break;
                }
                await FireEncounterTriggers(mapHistory);

                // SendAction сам может продвинуть раунд (истощение ресурсов действия вне боя) —
                // напоминание об onRound/onExpire эффектах должно идти тем же дедуплицированным
                // путём, что и после движения, а не полагаться на то, что AI сам сообразит
                // применить их внутри своего же ответа (было причиной повторного/самовольного
                // урона от периодических эффектов).
                var sendActionRoundBundle = _storage.TakeRoundBundle();
                if (sendActionRoundBundle != null && !_config.DisableTriggers && !_config.RevealMap)
                {
                    _aiClient.ActiveDialog = (mapHistory);
                    mapHistory.PrepareStreaming();
                    await _aiClient.FireRoundTransition(sendActionRoundBundle, mapHistory.AppendStreamChunk, mapHistory.NewStreamingMessage, mapHistory.TickSpinner, redrawAction);
                    mapHistory.FinalizeStreaming();
                    _aiClient.ActiveDialog = (null);
                    while (Console.KeyAvailable) Console.ReadKey(true);
                    break;
                }

                await RunEnemyTurnAndCheckRoundEnd();
                break;
            }
            // Hero moved — show new position before any AI call
            Console.CursorVisible = false;
            _display.HoverPath = ComputeHoverPath(KnownCellOrNull(_display.HoveredCell));
            mapObjectsProvider.Refresh();
            FollowHeroIfMoved();
            _mapDisplay.RedrawAfterMove(mapObjectsProvider);
            movedFrameDrawn = true;
            var roundBundle = _storage.TakeRoundBundle();
            if (roundBundle != null && !_config.DisableTriggers && !_config.RevealMap)
            {
                _aiClient.ActiveDialog = (mapHistory);
                mapHistory.PrepareStreaming();
                await _aiClient.FireRoundTransition(roundBundle, mapHistory.AppendStreamChunk, mapHistory.NewStreamingMessage, mapHistory.TickSpinner, redrawAction);
                mapHistory.FinalizeStreaming();
                _aiClient.ActiveDialog = (null);
                while (Console.KeyAvailable) Console.ReadKey(true);
                break;
            }
            if (moved == true && !_config.DisableTriggers && !_config.RevealMap)
            {
                _aiClient.ActiveDialog = (mapHistory);
                mapHistory.PrepareStreaming();
                await _aiClient.FireTrigger(activatedTriggers, activatedAreaTriggers, activatedDoorTriggers, mapHistory.AppendStreamChunk, mapHistory.NewStreamingMessage, mapHistory.TickSpinner, redrawAction);
                mapHistory.FinalizeStreaming();
                _aiClient.ActiveDialog = (null);
                while (Console.KeyAvailable) Console.ReadKey(true);
                await FireEncounterTriggers(mapHistory);
                await RunEnemyTurnAndCheckRoundEnd();
                break;
            }
            if (IsHeroTurnOver())
            {
                await RunEnemyTurnAndCheckRoundEnd();
                break;
            }
            // Local move: loop back for in-place redraw.
            // Зажатая стрелка: автоповтор клавиатуры копит события быстрее, чем идёт шаг с перерисовкой, —
            // без сброса герой «доезжал» по накопленному буферу уже после отпускания (как раньше с Tab).
            // Пока клавиша зажата, новые повторы продолжают приходить — движение не прерывается.
            while (Console.KeyAvailable) Console.ReadKey(intercept: true);
        }

        _display.ArrowKeysMovement = false;
        _display.PollAction = null;
        // OnMapRedraw is intentionally kept so tab-switching from Character/Abilities
        // screens can still show the map view during streaming.
        _display.HoveredCell = null;
        _display.HoveredDoor = null;
        ConsoleMouseReader.SetCursorShape(false);
    }
}
