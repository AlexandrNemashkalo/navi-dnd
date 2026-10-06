using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: SendAction (мок) снимает лук, надевает меч, удаляет один предмет и добавляет новый —
// проверяем, что карточка героя (слоты экипировки + список инвентаря) отражает это корректно.
// AI-провайдер замокан (Fixtures/MockResponses/Inventory/SendAction/response.json) — реальная
// нейронка не вызывается.
public class InventoryScenarioTests
{
    private readonly ITestOutputHelper _output;

    public InventoryScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EquipUnequipAndInventoryChanges_DisplayCorrectly()
    {
        using var game = GameSession.Launch(mockedActions: ["SendAction"], mockResponsesFolder: "Inventory");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");
        var failures = new List<string>();

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа (вкладка Инвентарь) отрисован.");

        // Fixtures/full_game.json: hero.inventory[0]=Длинный лук (Л/П Рука), [1]=Длинный меч
        // (не экипирован), [6]=Кинжал. Мок-ответ: лук снят, меч надет в [П Рука], кинжал удалён,
        // добавлено «Волшебное кольцо».
        _output.WriteLine("Отправляю игровое действие — жду ответ SendAction (мок)...");
        GameConsole.SendText(game.Pid, "убираю лук за спину, беру меч в руку — кинжал куда-то делся");
        GameConsole.SendKey(game.Pid, ConsoleKey.Enter);

        // "кольцо" — ПОСЛЕДНЕЕ слово сообщения (см. response.json): пока идёт печать текста
        // по буквам (SequentialHistoryPlayer, ~25мс/символ), карточка героя слева ещё СТАРАЯ —
        // она перерисовывается только когда SendAction() полностью завершится и внешний игровой
        // цикл (Program.cs) перерисует экран заново. Патч на hero.inventory при этом уже применён
        // (он применяется ДО начала печати истории — см. SequentialHistoryPlayer.PlayAsync), но
        // экран этого ещё не показывает. Ждём последнее слово + небольшой запас, а не первое
        // попавшееся слово фразы (иначе поймаем данные ДО применения патча/перерисовки).
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                "кольцо", 15000),
            "Не дождались ответа SendAction (мок) — сообщение из истории не появилось.");
        _output.WriteLine("Ответ применён. Жду перерисовку карточки героя...");

        // НЕ "sleep фиксированное время + один снимок": карточка героя перерисовывается ДРУГИМ
        // вызовом и в ДРУГОЕ время, чем диалоговая панель — под нагрузкой (весь набор тестов
        // подряд) фиксированной паузы иногда не хватает. НЕ ищем просто "Длинный меч" —
        // он УЖЕ виден в обычном списке инвентаря (неэкипирован) ДО патча, простой Contains
        // дал бы ложное срабатывание на устаревших данных. Ждём именно СЛОТ [П Рука] с мечом.
        Assert.True(
            GameConsole.WaitForRowsMatch(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth,
                rows => rows.Any(r => r.Contains("П Рука") && r.Contains("Длинный меч")), 5000),
            "Карточка героя не перерисовалась с обновлённой экипировкой после ответа SendAction (мок).");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);

        _output.WriteLine("Проверяю смену экипировки: лук снят ([Л Рука] пусто), меч надет в [П Рука]...");
        CheckEquipSlot(rows, "П Рука", "Длинный меч", failures);
        CheckEquipSlotEmpty(rows, "Л Рука", failures);
        TestPacing.Step();

        _output.WriteLine("Проверяю добавление/удаление предметов инвентаря (с перелистыванием страниц)...");
        var allPagesText = CollectAcrossPages(game.Pid);
        if (!allPagesText.Any(r => r.Contains("Волшебное кольцо")))
            failures.Add("Добавленный предмет «Волшебное кольцо» не найден ни на одной странице инвентаря.");
        if (allPagesText.Any(r => r.Contains("Кинжал")))
            failures.Add("Удалённый предмет «Кинжал» всё ещё виден на одной из страниц инвентаря.");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0
            ? "Все проверки пройдены."
            : $"Провалено проверок: {failures.Count}.");
        // Финальный Step() — до Assert, а не после: если проверки провалились, Assert.True ниже
        // бросит исключение и код после него не выполнится, а пауза для отладки нужна в любом случае.
        TestPacing.Step();
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // Слот экипировки — строка вида "П Рука   Длинный меч" (см. HeroDisplay.DrawHeroCard —
    // leftSlots рисует имя слота и имя предмета на ОДНОЙ строке).
    private static void CheckEquipSlot(List<string> rows, string slotName, string expectedItem, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains(slotName));
        if (row == null) { failures.Add($"Слот экипировки «{slotName}» не найден на экране."); return; }
        if (!row.Contains(expectedItem))
            failures.Add($"Слот «{slotName}» не показывает «{expectedItem}»: «{row.TrimEnd()}»");
    }

    private static void CheckEquipSlotEmpty(List<string> rows, string slotName, List<string> failures)
    {
        var row = rows.FirstOrDefault(r => r.Contains(slotName));
        if (row == null) { failures.Add($"Слот экипировки «{slotName}» не найден на экране."); return; }
        if (!row.Contains('—'))
            failures.Add($"Слот «{slotName}» должен быть пуст («—»), но: «{row.TrimEnd()}»");
    }

    // Проходит по всем страницам инвентаря (► пока есть), собирая весь текст — чтобы проверить
    // присутствие/отсутствие предмета независимо от того, на какой странице он оказался.
    private static List<string> CollectAcrossPages(int gamePid, int maxPages = 10)
    {
        var all = new List<string>();
        for (int page = 0; page < maxPages; page++)
        {
            var rows = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
            all.AddRange(rows);

            int arrowRow = rows.FindIndex(r => r.Contains('►'));
            if (arrowRow < 0) break;
            int arrowCol = rows[arrowRow].IndexOf('►');
            GameConsole.SendLeftClick(gamePid, arrowCol, arrowRow);
            Thread.Sleep(400);
            TestPacing.Step();
        }
        return all;
    }
}
