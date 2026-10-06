using Xunit.Abstractions;

namespace NaviDnD.UiTests;

// E2E: статус-эффекты (CellEntity.Effects/StatusEffect) заменили прежнее свободнотекстовое поле
// State. Fixtures/effects_game.json: герой с тремя эффектами разной длительности (ограничен
// раундами / до долгого отдыха / бессрочно), видимый гоблин «GB1» с эффектом «Горит» и контрольный
// здоровый гоблин «GB2» без эффектов.
public class EffectsScenarioTests
{
    private readonly ITestOutputHelper _output;

    public EffectsScenarioTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Legend_ShowsEntityEffectName_NextToEntity()
    {
        using var game = GameSession.Launch(fixtureFileName: "effects_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.MenuSelect(game.Pid, "ПРОДОЛЖИТЬ");
        Assert.True(
            GameConsole.WaitForRowContains(game.Pid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000),
            "Не дождались экрана Карты после [F1] в меню.");
        _output.WriteLine("Экран Карты отрисован.");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();

        _output.WriteLine("Проверяю, что горящий гоблин «GB1» показывает в легенде имя эффекта «Горит»...");
        var burningRow = rows.FirstOrDefault(r => r.Contains("GB1"));
        if (burningRow == null) failures.Add("Строка с «GB1» не найдена в легенде.");
        else if (!burningRow.Contains("Горит"))
            failures.Add($"У горящего гоблина «GB1» не отображается эффект «Горит»: «{burningRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine("Проверяю, что здоровый гоблин «GB2» — без эффектов и без строки «Горит»...");
        var healthyRow = rows.FirstOrDefault(r => r.Contains("GB2"));
        if (healthyRow == null) failures.Add("Строка с «GB2» не найдена в легенде.");
        else if (healthyRow.Contains("Горит"))
            failures.Add($"У здорового гоблина «GB2» неожиданно отображается эффект «Горит»: «{healthyRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0 ? "Все проверки пройдены." : $"Провалено проверок: {failures.Count}.");
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    [Fact]
    public void CharacterEffectsTab_ShowsActiveStatusesWithDuration()
    {
        using var game = GameSession.Launch(fixtureFileName: "effects_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован (вкладка Инвентарь).");
        TestPacing.Step();

        _output.WriteLine("Отправляю [F8] — жду подвкладку Состояния...");
        GameConsole.SendKey(game.Pid, ConsoleKey.F8);
        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ СОСТОЯНИЯ ←", 5000),
            "Подвкладка Состояния не активировалась после нажатия [F8].");
        _output.WriteLine("Подвкладка Состояния отрисована.");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        var failures = new List<string>();

        // Маркер строки списка на подвкладке Состояния (HeroDisplay.DrawEffectsSection) — просто
        // пробел, без видимого символа. Ищем по "Имя:" (после имени сразу следует ": <длительность>",
        // см. InvLine.Rest ниже) — отличает строку списка от краткой сводки имён в карточке героя
        // выше («Состояния   Отравлен, ...»), где после имени идёт запятая, а не двоеточие.
        _output.WriteLine("Проверяю эффект с ограничением по раундам («Отравлен» — до раунда 8)...");
        var poisonRow = rows.FirstOrDefault(r => r.Contains("Отравлен:"));
        if (poisonRow == null) failures.Add("Строка списка «Отравлен:» не найдена на подвкладке Состояния.");
        else if (!poisonRow.Contains("до раунда 8"))
            failures.Add($"Эффект «Отравлен» не показывает длительность «до раунда 8»: «{poisonRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine("Проверяю эффект до долгого отдыха («Истощение»)...");
        var exhaustionRow = rows.FirstOrDefault(r => r.Contains("Истощение:"));
        if (exhaustionRow == null) failures.Add("Строка списка «Истощение:» не найдена на подвкладке Состояния.");
        else if (!exhaustionRow.Contains("до долгого отдыха"))
            failures.Add($"Эффект «Истощение» не показывает «до долгого отдыха»: «{exhaustionRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine("Проверяю бессрочный эффект («Благословение духов»)...");
        var blessingRow = rows.FirstOrDefault(r => r.Contains("Благословение духов:"));
        if (blessingRow == null) failures.Add("Строка списка «Благословение духов:» не найдена на подвкладке Состояния.");
        else if (!blessingRow.Contains("бессрочно"))
            failures.Add($"Бессрочный эффект не показывает «бессрочно»: «{blessingRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine("Проверяю, что краткой сводки эффектов в карточке героя больше нет (только на подвкладке Состояния)...");
        var summaryRow = rows.FirstOrDefault(r => r.Contains("Состояния")); // метка "СОСТОЯНИЯ" вкладки — верхним регистром, не совпадёт
        if (summaryRow != null)
            failures.Add($"В карточке героя неожиданно осталась строка-метка «Состояния» вне подвкладки: «{summaryRow.TrimEnd()}»");
        TestPacing.Step();

        _output.WriteLine(failures.Count == 0 ? "Все проверки пройдены." : $"Провалено проверок: {failures.Count}.");
        Assert.True(failures.Count == 0, "UI-тест FAIL:\n" + string.Join("\n", failures.Select(f => "  - " + f)));
    }

    // Регрессия: переключение подвкладки КЛИКОМ МЫШИ идёт через отдельный путь
    // (MouseUiHelper → display.PendingCommand → InputBox.ReadKeyWithPolling), не через прямое
    // чтение ConsoleKey — у него был свой список известных команд ("F1".."F7","Esc"), не
    // обновлённый вместе с остальными при добавлении подвкладки Состояний. Клавиатурный [F8]
    // (см. тест выше) эту ветку не задевает и бага не ловил — процесс падал с необработанным
    // InvalidOperationException «Unknown pending command: F8» только по клику мышью.
    [Fact]
    public void CharacterEffectsTab_MouseClick_SwitchesTabWithoutCrashing()
    {
        using var game = GameSession.Launch(fixtureFileName: "effects_game.json");
        _output.WriteLine($"Игра запущена (pid {game.Pid}).");

        Navigation.ToCharacterScreen(game.Pid);
        _output.WriteLine("Экран Персонажа отрисован (вкладка Инвентарь).");
        TestPacing.Step();

        var rows = GameConsole.ReadRows(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        int rowIdx = rows.FindIndex(r => r.Contains("[F8]"));
        Assert.True(rowIdx >= 0, "Метка «[F8]СОСТОЯНИЯ» не найдена на подвкладке Инвентарь — некуда кликать.");
        var tab = ScreenAssertions.FindTab(rows[rowIdx], "[F8]");
        Assert.NotNull(tab);

        _output.WriteLine("Кликаю мышью по метке «[F8]СОСТОЯНИЯ»...");
        GameConsole.SendLeftClick(game.Pid, tab!.Value.startX, rowIdx);

        Assert.True(
            GameConsole.WaitForAnyRowContains(game.Pid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ СОСТОЯНИЯ ←", 5000),
            "Подвкладка Состояния не активировалась после клика мышью по [F8] (или игра упала).");
        _output.WriteLine("Подвкладка Состояния отрисована, игра не упала.");
    }
}
