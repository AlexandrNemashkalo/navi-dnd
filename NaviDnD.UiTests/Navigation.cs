namespace NaviDnD.UiTests;

// Общая навигация Меню → Карта → Персонаж — переиспользуется тест-кейсами, которым сам переход
// не важен (он уже отдельно проверяется в GameScenarioTests.FullGameplayScenario).
internal static class Navigation
{
    // Главное меню без горячих клавиш (MainMenuDisplay): стрелкой ↓ до пункта «→ ИМЯ ←», затем Enter.
    public static void MenuSelect(int gamePid, string item)
    {
        string marker = $"→ {item} ←";
        for (int i = 0; i < 6; i++)
        {
            if (GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, marker, i == 0 ? 1000 : 400))
            {
                GameConsole.SendKey(gamePid, ConsoleKey.Enter);
                return;
            }
            GameConsole.SendKey(gamePid, ConsoleKey.DownArrow);
        }
        var screen = GameConsole.ReadRows(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth);
        throw new InvalidOperationException($"Не удалось выбрать пункт меню «{item}». Экран:\n" + string.Join("\n", screen));
    }

    // Меню → «НОВАЯ ИГРА» → шаг «Мир»: новый мир обязан иметь описание — Enter (без описания — фокус на ввод),
    // ↑ на описание, текст, [F2] — дальше → анкета героя.
    public static bool ToAnketa(int gamePid)
    {
        MenuSelect(gamePid, "НОВАЯ ИГРА");
        if (!GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "ОПИСАНИЕ МИРА", 5000)) return false;
        Thread.Sleep(300);
        GameConsole.SendKey(gamePid, ConsoleKey.Enter);
        Thread.Sleep(200);
        GameConsole.SendKey(gamePid, ConsoleKey.UpArrow);
        Thread.Sleep(200);
        GameConsole.SendText(gamePid, "Тестовый мир");
        Thread.Sleep(200);
        GameConsole.SendKey(gamePid, ConsoleKey.F2);
        return GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "АНКЕТА", 5000);
    }

    // Редактор героя [F4] → шаг «Приключение» → «НАЧАТЬ ИГРУ» (Tab — по полям до кнопки, она последняя; Enter — старт).
    public static bool StartFromEditor(int gamePid)
    {
        GameConsole.SendKey(gamePid, ConsoleKey.F4);
        if (!GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ ПРИКЛЮЧЕНИЕ ←", 5000))
        {
            GameConsole.SendKey(gamePid, ConsoleKey.F4);   // изредка первое [F4] не долетает под нагрузкой
            if (!GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ ПРИКЛЮЧЕНИЕ ←", 5000)) return false;
        }
        Thread.Sleep(300);
        for (int i = 0; i < 12; i++) { GameConsole.SendKey(gamePid, ConsoleKey.Tab); Thread.Sleep(60); }
        GameConsole.SendKey(gamePid, ConsoleKey.Enter);
        return true;
    }

    public static void ToCharacterScreen(int gamePid)
    {
        MenuSelect(gamePid, "ПРОДОЛЖИТЬ");
        if (!GameConsole.WaitForRowContains(gamePid, 1, GameConsole.TitleReadWidth, "→ КАРТА ←", 5000))
            throw new InvalidOperationException("Не дождались экрана Карты после [F1] в меню.");

        string mapTitle = GameConsole.ReadRow(gamePid, 1, GameConsole.TitleReadWidth);
        var f2Tab = ScreenAssertions.FindTab(mapTitle, "[F4]")
            ?? throw new InvalidOperationException("Не нашли вкладку [F4] на экране карты.");

        GameConsole.SendLeftClick(gamePid, f2Tab.startX, 1);
        if (!GameConsole.WaitForRowContains(gamePid, 1, GameConsole.TitleReadWidth, "→ ПЕРСОНАЖ ←", 5000))
            throw new InvalidOperationException("Не дождались экрана Персонажа после клика по [F4].");
    }
}
