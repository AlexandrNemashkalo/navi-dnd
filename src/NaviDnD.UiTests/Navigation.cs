namespace NaviDnD.UiTests;

// General navigation Menu → Map → Character - reused by test cases for which the transition itself is not
// important (it is already checked separately in GameScenarioTests.FullGameplayScenario).
internal static class Navigation
{
    // Main menu without hotkeys (MainMenuDisplay): arrow ↓ to “→ NAME ←”, then Enter.
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

    // Menu → “NEW GAME” → step “World”: the new world must have a description - Enter (without description -
    // focus on input), ↑ on description, text, [F2] - further → hero profile.
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

    // Hero editor [F4] → step “Adventure” → “START GAME” (Tab - along the fields to the button, it’s the last one; Enter - start).
    public static bool StartFromEditor(int gamePid)
    {
        GameConsole.SendKey(gamePid, ConsoleKey.F4);
        if (!GameConsole.WaitForAnyRowContains(gamePid, 0, GameConsole.ScanRowCount, GameConsole.TitleReadWidth, "→ ПРИКЛЮЧЕНИЕ ←", 5000))
        {
            GameConsole.SendKey(gamePid, ConsoleKey.F4);   // occasionally the first [F4] does not fly under load
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
