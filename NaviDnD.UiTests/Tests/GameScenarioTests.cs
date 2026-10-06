using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// Каждый тест-кейс поднимает СВОЙ независимый NaviDnD.exe через GameSession.Launch() —
// кейсы друг от друга не зависят и не переиспользуют состояние игры.
//
// Состояние — из Fixtures/full_game.json, собранного из реального сохранения игры
// (герой + карта подземелья), а не выдуманное с нуля — чтобы проверки покрывали
// реалистичный объём данных (полный лист статов/навыков, 9 предметов инвентаря,
// карта на 6 комнат/10 дверей/4 существа).
//
// _output.WriteLine пишет в лог теста (виден в `dotnet test` при -v n/detailed, в Test
// Explorer — всегда) — так по логу видно, ЧТО именно проверялось, а не только итоговый
// PASS/FAIL по накопленному списку failures.
public class GameScenarioTests
{
    private readonly ITestOutputHelper _output;

    public GameScenarioTests(ITestOutputHelper output) => _output = output;

    // Сценарий: Меню → [F1] → Карта (проверка рамок и вкладок) → клик по [F3] → Персонаж,
    // вкладка Инвентарь (карточка + диалоговое окно с текстом истории, наведение на вкладку,
    // пагинация) → [F7] → вкладка Способности (карточка + пагинация).
    [Fact]
    public void FullGameplayScenario()
    {
        using var game = GameSession.Launch();
        _output.WriteLine($"Игра запущена (pid {game.Pid}), экран Меню отрисован.");
        var failures = new List<string>();

        const string MapActiveMarker = "→ КАРТА ←";
        const string CharacterActiveMarker = "→ ПЕРСОНАЖ ←";

        _output.WriteLine("Отправляю [F1] — жду экран Карты...");
        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, MapActiveMarker, 5000),
            "Не дождались экрана Карты после нажатия [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован.");
        CheckMapScreen(game.Pid, failures);
        TestPacing.Step();

        string mapTitle = GameConsole.ReadRow(game.Pid, 1, GameConsole.TitleReadWidth);
        var f2Tab = ScreenAssertions.FindTab(mapTitle, "[F4]");
        Assert.NotNull(f2Tab); // иначе дальше некуда кликать — сообщаем через явный Assert, а не failures

        _output.WriteLine("Кликаю мышью по вкладке [F4] — жду экран Персонажа...");
        GameConsole.SendLeftClick(game.Pid, f2Tab!.Value.startX, 1);
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, CharacterActiveMarker, 5000),
            "Не дождались экрана Персонажа после клика мышью по вкладке [F4].");
        // Заголовок экрана (строка 1) и тело карточки (диалоговое окно, инвентарь) рисуются РАЗНЫМИ
        // вызовами — заголовок мог смениться раньше, чем дорисовалось диалоговое окно ниже (см. ту же
        // гонку, что чинили в DialogDisplay.Draw()). Явно ждём тело карточки, а не полагаемся на то,
        // что оно успело нарисоваться к моменту смены заголовка — иначе CheckCharacterScreen ниже
        // читает экран раньше времени и не находит "ДИАЛОГОВОЕ ОКНО"/содержимое инвентаря.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ДИАЛОГОВОЕ ОКНО", 5000),
            "Диалоговое окно не отрисовалось после перехода на экран Персонажа.");
        _output.WriteLine("Экран Персонажа отрисован (вкладка Инвентарь).");
        CheckCharacterScreen(game.Pid, expectAbilities: false, failures);
        TestPacing.Step();

        CheckTopTabHoverEffects(game.Pid, "вкладка Инвентарь", failures);
        TestPacing.Step();
        CheckInventoryPagination(game.Pid, "вкладка Инвентарь", failures);
        TestPacing.Step();

        _output.WriteLine("Отправляю [F7] — жду подвкладку Способности...");
        GameConsole.SendKey(game.Pid, ConsoleKey.F7);
        // "→ ПЕРСОНАЖ ←" не меняется при смене подвкладки (это заголовок ЭКРАНА, не подвкладки) —
        // ждать нужно маркер активной ПОДВКЛАДКИ (рисуется внутри карточки, не в строке 1),
        // иначе проверка пройдёт мгновенно и без подтверждения, что [F7] вообще был обработан.
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ СПОСОБНОСТИ ←", 5000),
            "Подвкладка Способности не активировалась после нажатия [F7].");
        _output.WriteLine("Подвкладка Способности отрисована.");
        CheckCharacterScreen(game.Pid, expectAbilities: true, failures);
        TestPacing.Step();
        CheckAbilitiesPagination(game.Pid, "подвкладка Способности", failures);
        TestPacing.Step();

        TestPacing.Step();
        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        // Финальный Step() — до Assert, а не после: если проверки провалились, Assert.True ниже
        // бросит исключение и код после него не выполнится, а пауза для отладки нужна в любом случае.
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    private void CheckMapScreen(int gamePid, List<string> failures)
    {
        _output.WriteLine("[экран карты] Проверяю вкладки [F2]/[F3]/[F4]/[F5]/[Esc]...");
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        ScreenAssertions.RequireTab(title, "[F2]", "МИР", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[F3]", "ЖУРНАЛ", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[F4]", "ПЕРСОНАЖ", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[F5]", "СПУТНИК", "экран карты", failures);
        ScreenAssertions.RequireTab(title, "[Esc]", "МЕНЮ", "экран карты", failures);
        TestPacing.Step();

        _output.WriteLine("[экран карты] Проверяю рамки...");
        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        ScreenAssertions.CheckBorders(rows, "экрана карты", failures);
        TestPacing.Step();

        _output.WriteLine("[экран карты] Проверяю номера колонок сетки...");
        // Заголовки колонок карты (подряд идущие номера) — признак того, что сетка реально отрисована,
        // а не просто пустая рамка. Номера — координаты поля под камерой, не обязательно с 1.
        if (!rows.Any(r => System.Text.RegularExpressions.Regex.Matches(r, @"\b\d{1,3}\b").Count >= 10))
            failures.Add("На экране карты не найдены номера колонок сетки.");
        TestPacing.Step();
    }

    private void CheckCharacterScreen(int gamePid, bool expectAbilities, List<string> failures)
    {
        string context = expectAbilities ? "подвкладка Способности" : "вкладка Инвентарь";

        _output.WriteLine($"[{context}] Проверяю вкладки [F1]/[F5]/[Esc]...");
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        ScreenAssertions.RequireTab(title, "[F1]", "КАРТА", context, failures);
        ScreenAssertions.RequireTab(title, "[F5]", "СПУТНИК", context, failures);
        ScreenAssertions.RequireTab(title, "[Esc]", "МЕНЮ", context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю рамки...");
        var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        ScreenAssertions.CheckBorders(rows, context, failures);
        TestPacing.Step();

        _output.WriteLine($"[{context}] Проверяю имя героя, диалоговое окно и текст истории (реплики ДМ и героя)...");
        bool sawHeroName = rows.Any(r => r.Contains("Рустем"));
        bool sawDialogWindow = rows.Any(r => r.Contains("ДИАЛОГОВОЕ ОКНО"));
        // Из фикстуры (Fixtures/full_game.json → history) — проверяем, что реальный текст истории
        // действительно долетает до экрана, а не только рамка диалогового окна.
        bool sawDmMessage = rows.Any(r => r.Contains("факелы едва потрескивают"));
        bool sawHeroMessage = rows.Any(r => r.Contains("Осматриваюсь"));

        if (!sawHeroName) failures.Add($"[{context}] Имя героя «Рустем» не найдено на экране.");
        if (!sawDialogWindow) failures.Add($"[{context}] Диалоговое окно («ДИАЛОГОВОЕ ОКНО») не найдено.");
        if (!sawDmMessage) failures.Add($"[{context}] Текст реплики DM из истории не найден.");
        if (!sawHeroMessage) failures.Add($"[{context}] Текст реплики героя из истории не найден.");
        TestPacing.Step();

        if (expectAbilities)
        {
            _output.WriteLine($"[{context}] Проверяю список способностей...");
            // Из фикстуры (hero.abilities) — несколько разных способностей должны быть видны
            // одновременно (проверяет, что список не обрезан после одной записи).
            // "Второе дыхание" сюда намеренно не включено — это ОДНОВРЕМЕННО и способность,
            // и ресурс (hero.resources), а ресурсы всегда видны в верхней части карточки
            // независимо от подвкладки — совпадение по этому имени ничего не доказывает.
            string[] abilityNames = ["Боевой стиль: Стрельба", "Фейское наследие", "Обострённые чувства"];
            foreach (var name in abilityNames)
                if (!rows.Any(r => r.Contains(name)))
                    failures.Add($"[{context}] Способность «{name}» не найдена на экране.");
        }
        else
        {
            _output.WriteLine($"[{context}] Проверяю подвкладку ИНВЕНТАРЬ и предметы (включая количество)...");
            if (!rows.Any(r => r.Contains("ИНВЕНТАРЬ")))
                failures.Add($"[{context}] Подвкладка «ИНВЕНТАРЬ» не найдена.");

            // Из фикстуры (hero.inventory) — несколько разных предметов должны быть видны
            // одновременно, включая количество (×38 у стрел) — проверяет разметку колонок.
            string[] itemNames = ["Длинный лук", "Кольчуга", "Стрелы"];
            foreach (var name in itemNames)
                if (!rows.Any(r => r.Contains(name)))
                    failures.Add($"[{context}] Предмет «{name}» не найден на экране.");
            if (!rows.Any(r => r.Contains("×38")))
                failures.Add($"[{context}] Количество стрел (×38) не найдено — возможна проблема с отступами колонки.");
        }
        TestPacing.Step();

        CheckHeroNameColor(gamePid, rows, context, failures);
        TestPacing.Step();
    }

    // hero.color из Fixtures/full_game.json — ожидаемый цвет имени героя.
    private static readonly (int r, int g, int b) HeroNameColor = (80, 160, 120);
    // DisplayConfig.MainBackground по умолчанию — с чем сравнивать, чтобы найти "чернила" текста.
    private static readonly (byte r, byte g, byte b) MainBackground = (18, 18, 22);
    private const int ColorTolerance = 60;

    private void CheckHeroNameColor(int gamePid, List<string> rows, string context, List<string> failures)
    {
        if (!rows.Any(r => r.Contains("Рустем"))) return; // сам факт отсутствия текста уже сообщён отдельной проверкой
        _output.WriteLine($"[{context}] Проверяю цвет имени героя (скриншот + сэмплинг пикселя)...");
        ScreenAssertions.CheckTextColor(gamePid, rows, "Рустем", HeroNameColor, ColorTolerance, MainBackground, context, failures);
    }

    // Наведение на кликабельный элемент должно менять цвет (MouseUiHelper.SetTabHighlight) —
    // проверяем на неактивной вкладке [F1] (на экране персонажа эта вкладка сейчас ничего не
    // делает при наведении кроме подсветки — сравниваем состояние ДО/ПОСЛЕ, а не конкретный
    // ожидаемый цвет, это надёжнее). Курсор (ConsoleMouseReader.SetCursorShape) не проверяем:
    // это реальный системный курсор (GetCursorInfo), который зависит от того, где физически
    // стоит настоящий указатель мыши в ОС, а не от синтетических событий WriteConsoleInput —
    // подсветка вызывается тем же хэндлером наведения и надёжно подтверждает его срабатывание.
    private const int HoverColorDelta = 10;

    private void CheckTopTabHoverEffects(int gamePid, string context, List<string> failures)
    {
        string title = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        var tab = ScreenAssertions.FindTab(title, "[F1]");
        if (tab == null) return; // отсутствие вкладки уже сообщено RequireTab

        _output.WriteLine($"[{context}] Проверяю смену цвета вкладки [F1] при наведении мышью...");

        (byte r, byte g, byte b) background = (18, 18, 22);
        int sampleCol = tab.Value.startX + 1;

        // Нейтральная точка подальше от любых вкладок/стрелок — сбрасываем наведение перед замером.
        GameConsole.SendMouseMove(gamePid, 2, 10);
        Thread.Sleep(250);

        (byte r, byte g, byte b) colorBefore;
        try { colorBefore = GameConsole.SampleCellColor(gamePid, sampleCol, 1, background); }
        catch (Exception ex) { failures.Add($"[{context}] Не удалось снять цвет вкладки [F1] до наведения: {ex.Message}"); return; }

        GameConsole.SendMouseMove(gamePid, sampleCol, 1);
        Thread.Sleep(300);

        (byte r, byte g, byte b) colorAfter;
        try { colorAfter = GameConsole.SampleCellColor(gamePid, sampleCol, 1, background); }
        catch (Exception ex) { failures.Add($"[{context}] Не удалось снять цвет вкладки [F1] после наведения: {ex.Message}"); return; }

        bool colorChanged = Math.Abs(colorBefore.r - colorAfter.r) > HoverColorDelta
                         || Math.Abs(colorBefore.g - colorAfter.g) > HoverColorDelta
                         || Math.Abs(colorBefore.b - colorAfter.b) > HoverColorDelta;
        if (!colorChanged)
            failures.Add($"[{context}] Цвет вкладки [F1] не изменился при наведении: до ({colorBefore.r},{colorBefore.g},{colorBefore.b}), после ({colorAfter.r},{colorAfter.g},{colorAfter.b}).");
        TestPacing.Step();

        // Уводим мышь, чтобы не мешать следующим шагам сценария.
        GameConsole.SendMouseMove(gamePid, 2, 10);
        Thread.Sleep(150);
    }

    // Фикстура (Fixtures/full_game.json) специально содержит много предметов/способностей
    // с длинными описаниями — список не помещается на одну страницу, поэтому стрелки
    // ◄/► должны реально листать. Маркер — первый НЕэкипированный предмет: экипированные
    // (напр. "Длинный лук") всегда видны в блоке слотов экипировки сверху карточки,
    // независимо от страницы инвентаря, и как маркер страницы не годятся.
    private void CheckInventoryPagination(int gamePid, string context, List<string> failures) =>
        CheckPagination(gamePid, context, "Длинный меч", "предмет", failures);

    private void CheckAbilitiesPagination(int gamePid, string context, List<string> failures) =>
        CheckPagination(gamePid, context, "Боевой стиль: Стрельба", "способность", failures);

    private void CheckPagination(int gamePid, string context, string firstMarker, string itemKind, List<string> failures)
    {
        _output.WriteLine($"[{context}] Проверяю пагинацию ({itemKind}): ► вперёд, ◄ назад...");
        var page1 = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        if (!page1.Any(r => r.Contains(firstMarker)))
        {
            failures.Add($"[{context}] Первый {itemKind} «{firstMarker}» не найден на первой странице.");
            return;
        }

        int arrowRow = page1.FindIndex(r => r.Contains('►'));
        if (arrowRow < 0)
        {
            failures.Add($"[{context}] Стрелка «►» не найдена — список должен не помещаться на одну страницу (в фикстуре достаточно записей).");
            return;
        }
        int arrowCol = page1[arrowRow].IndexOf('►');

        GameConsole.SendLeftClick(gamePid, arrowCol, arrowRow);
        Thread.Sleep(500);
        TestPacing.Step();

        var page2 = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        if (page2.Any(r => r.Contains(firstMarker)))
            failures.Add($"[{context}] После клика по ► «{firstMarker}» всё ещё виден — страница не сменилась.");

        int backArrowRow = page2.FindIndex(r => r.Contains('◄'));
        if (backArrowRow < 0)
        {
            failures.Add($"[{context}] Стрелка «◄» не найдена после перехода на вторую страницу.");
            return;
        }
        int backArrowCol = page2[backArrowRow].IndexOf('◄');

        GameConsole.SendLeftClick(gamePid, backArrowCol, backArrowRow);
        Thread.Sleep(500);
        TestPacing.Step();

        var page1Again = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        if (!page1Again.Any(r => r.Contains(firstMarker)))
            failures.Add($"[{context}] После клика по ◄ не вернулись на первую страницу — «{firstMarker}» не виден.");
        TestPacing.Step();
    }
}
