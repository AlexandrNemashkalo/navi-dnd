using System.Diagnostics;

namespace NaviDnD.UiTests;

// Подымает НАСТОЯЩИЙ NaviDnD.exe (без изменений в игровой логике, кроме тестовых точек входа —
// NAVIDND_TEST_WORLDSTATE/NAVIDND_TEST_MOCKED_ACTIONS/NAVIDND_TEST_MOCK_RESPONSES_DIR, см.
// Program.cs/AppConfig.cs самой игры) с состоянием из указанной фикстуры и ждёт экран Меню.
// Каждый тест-кейс создаёт СВОЙ независимый экземпляр — процессы игры друг от друга не зависят
// и не переиспользуются между кейсами.
internal sealed class GameSession : IDisposable
{
    public int Pid { get; }

    // Реальная (боевая, не тестовая) папка NaviDnD/Storage — нужна тестам, которые сами
    // инжектируют roll_request.json/ask_player_request.json, чтобы вызвать диалог броска/вопроса
    // без реального MCP-инструмента (см. GameAiClient.SpinAsync — поллит эти файлы независимо
    // от того, мок это или реальный вызов).
    public string RealStorageDir { get; }

    private readonly Process _process;
    private readonly Dictionary<string, byte[]?> _realSaveBackup;
    private HashSet<string> _worldDirsBefore = [];

    private static HashSet<string> WorldDirs(string storageDir)
    {
        string dir = Path.Combine(storageDir, "Worlds");
        return Directory.Exists(dir) ? [.. Directory.GetDirectories(dir)] : [];
    }

    private GameSession(Process process, Dictionary<string, byte[]?> realSaveBackup, string realStorageDir)
    {
        _process = process;
        Pid = process.Id;
        _realSaveBackup = realSaveBackup;
        RealStorageDir = realStorageDir;
    }

    public static GameSession Launch(
        string fixtureFileName = "full_game.json", string[]? mockedActions = null, string mockResponsesFolder = "") =>
        LaunchInternal(fixtureFileName, mockedActions, mockResponsesFolder);

    // Для сценариев "создать нового героя и начать игру": включает SelectiveMockProvider
    // (CreateNewGame + FixHeroForNewGame + StartNewGame) на канонических моках из
    // Fixtures/MockResponses/ — реальная нейронка не вызывается ни разу. FixHeroForNewGame
    // мокнут всегда (даже если конкретный тест его не вызывает) — безвредно, просто не используется.
    public static GameSession LaunchForNewGame() =>
        LaunchInternal("full_game.json", ["CreateNewGame", "FixHeroForNewGame", "StartNewGame"], mockResponsesFolder: "");

    // mockedActions — см. AppConfig.MockedActions ([] = выкл, ["*"] = все, или конкретные имена).
    // mockResponsesFolder — подпапка внутри Fixtures/MockResponses/, чтобы разные тесты не делили
    // один и тот же response.json на одно и то же имя действия (например, разные ответы SendAction
    // для теста инвентаря и теста броска кубика).
    // Любое мокнутое действие может безусловно писать в БОЕВЫЕ NaviDnD/Storage/worldState.json и
    // newGameState.json (StartNewGame.Save()/SendAction.Save() и т.п., не в тестовую фикстуру) —
    // их содержимое снимается побайтово перед запуском и восстанавливается в Dispose().
    private static GameSession LaunchInternal(string fixtureFileName, string[]? mockedActions, string mockResponsesFolder)
    {
        SelfWindow.ApplyFixedWidth();
        SelfWindow.MoveToRightEdge();

        string fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureFileName);
        string gameExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "NaviDnD", "bin", "Debug", "net9.0", "NaviDnD.exe"));

        if (!File.Exists(gameExe))
            throw new InvalidOperationException(
                "Не найден NaviDnD.exe по пути " + gameExe + " — сначала собери основной проект (NaviDnD.csproj).");

        string realStorageDir = Path.Combine(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(gameExe)!, "..", "..", "..")), "Storage");

        // UseShellExecute=true не даёт задавать ProcessStartInfo.EnvironmentVariables напрямую —
        // выставляем переменные на себе, дочерний процесс унаследует их как обычно.
        Environment.SetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE", fixturePath);

        // Бэкап БЕЗУСЛОВНЫЙ, не только при mockedActions: Storage.Save() пишет в этот ФИКСИРОВАННЫЙ
        // путь (SavePath) при ЛЮБОМ игровом событии, которое обычно сохраняется — в том числе при
        // простом движении стрелками (MovementHandler.TryMove → ApplyClientMovement → Save()),
        // вообще без единого AI-вызова. NAVIDND_TEST_WORLDSTATE подменяет только то, откуда игра
        // ГРУЗИТСЯ — Storage.Save() всё равно льёт в боевой файл. Раньше бэкап был условным (только
        // при мок-действиях), из-за чего любой тест без mockedActions (движение/туман войны/раунд)
        // молча и БЕЗВОЗВРАТНО затирал реальное сохранение пользователя без возможности восстановить.
        var realSaveBackup = new Dictionary<string, byte[]?>();
        foreach (string name in new[] { "worldState.json", "newGameState.json", "worldState.explored.json", "newGameState.explored.json" })
        {
            string path = Path.Combine(realStorageDir, name);
            // Байты, не текст — File.ReadAllText/WriteAllText молча снимают/не пишут BOM,
            // что превратило бы восстановление в "почти то же самое" вместо байт-в-байт.
            realSaveBackup[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        // Шаг «Мир» новой игры пишет мир в библиотеку и делает его активным — активный мир тоже восстанавливается,
        // а миры, появившиеся за время теста, удаляются (Dispose).
        string activeWorld = Path.Combine(realStorageDir, "Worlds", "active.txt");
        realSaveBackup[activeWorld] = File.Exists(activeWorld) ? File.ReadAllBytes(activeWorld) : null;
        var worldDirsBefore = WorldDirs(realStorageDir);

        if (mockedActions is { Length: > 0 })
        {
            string mockResponsesDir = string.IsNullOrEmpty(mockResponsesFolder)
                ? Path.Combine(AppContext.BaseDirectory, "Fixtures", "MockResponses")
                : Path.Combine(AppContext.BaseDirectory, "Fixtures", "MockResponses", mockResponsesFolder);
            Environment.SetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS", string.Join(",", mockedActions));
            Environment.SetEnvironmentVariable("NAVIDND_TEST_MOCK_RESPONSES_DIR", mockResponsesDir);
        }
        else
        {
            // Сброс на случай, если предыдущий тест в этом же процессе включал мок-режим —
            // переменные окружения процесса иначе утекли бы в следующий обычный запуск.
            Environment.SetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS", null);
            Environment.SetEnvironmentVariable("NAVIDND_TEST_MOCK_RESPONSES_DIR", null);
        }

        var psi = new ProcessStartInfo
        {
            FileName = gameExe,
            UseShellExecute = true, // отдельное окно для игры
            WorkingDirectory = Path.GetDirectoryName(gameExe)!,
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить процесс игры.");
        var session = new GameSession(process, realSaveBackup, realStorageDir) { _worldDirsBefore = worldDirsBefore };

        try
        {
            if (!GameConsole.WaitForRowContains(process.Id, 1, GameConsole.TitleReadWidth, "МЕНЮ", 5000))
                throw new InvalidOperationException("Не дождались экрана Меню при запуске NaviDnD.exe.");
            GameConsole.MoveToTopLeft(process.Id);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        return session;
    }

    public void Dispose()
    {
        // КРИТИЧНО: Kill() только ЗАПРАШИВАЕТ завершение процесса и возвращает управление сразу,
        // не дожидаясь фактической остановки — если убитый процесс был в этот момент посреди
        // Storage.Save() (пишет в БОЕВОЙ NaviDnD/Storage/worldState.json), его запись может
        // долететь до диска ПОСЛЕ того, как мы восстановим бэкап ниже, безвозвратно затерев
        // реальное сохранение пользователя тестовыми данными. WaitForExit() устраняет эту гонку.
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch { /* окно могло закрыться само */ }
        _process.Dispose();

        foreach (string dir in WorldDirs(RealStorageDir).Except(_worldDirsBefore))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }

        foreach (var (path, original) in _realSaveBackup)
        {
            try
            {
                if (original == null) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllBytes(path, original);
            }
            catch { /* восстановление best-effort — не должно ронять тест на выходе */ }
        }

        // Тесты, инжектирующие roll_request.json/ask_player_request.json (см. InteractiveResponseTests,
        // NewGameScenarioTests), оставляют их отвеченными (Answered=true), но не удаляют — сама игра
        // подчищает их только при СЛЕДУЮЩЕМ запуске (Program.cs). Подчищаем сразу, а не ждём следующий тест.
        foreach (string name in new[] { "roll_request.json", "ask_player_request.json" })
        {
            try
            {
                string path = Path.Combine(RealStorageDir, name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* best-effort */ }
        }
    }
}
