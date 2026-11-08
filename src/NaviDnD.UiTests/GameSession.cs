using System.Diagnostics;

namespace NaviDnD.UiTests;

// Raises the REAL NaviDnD.exe (without changes to the game logic, except for the test entry points -
// NAVIDND_TEST_WORLDSTATE/NAVIDND_TEST_MOCKED_ACTIONS/NAVIDND_TEST_MOCK_RESPONSES_DIR, see
// Program.cs/AppConfig.cs of the game itself) with the state from the specified fixture and waits for the Menu
// screen. Each test case creates its own independent instance - the game processes do not depend on each other
// and are not reused between cases.
internal sealed class GameSession : IDisposable
{
    public int Pid { get; }

    // Real (live, not test) folder NaviDnD/Storage - needed by tests that themselves inject
    // roll_request.json/ask_player_request.json to trigger the throw/question dialog without a real MCP- tool
    // (see GameAiClient.SpinAsync - pollits these files regardless of whether it's a mock or a real call).
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

    // For the “create a new hero and start the game” scenarios: includes SelectiveMockProvider (CreateNewGame +
    // FixHeroForNewGame + StartNewGame) on canonical mocks from Fixtures/MockResponses/ - the real AI is never
    // called. FixHeroForNewGame always gets wet (even if a specific test does not call it) - harmless, just not
    // used.
    public static GameSession LaunchForNewGame() =>
        LaunchInternal("full_game.json", ["CreateNewGame", "FixHeroForNewGame", "StartNewGame"], mockResponsesFolder: "");

    // mockedActions - See AppConfig.MockedActions ([] = off, ["*"] = all, or specific names).
    // mockResponsesFolder is a subfolder inside Fixtures/MockResponses/ to prevent different tests sharing the
    // same response.json with the same action name (for example, different SendAction answers for an inventory
    // test and a dice roll test). Any wet action can certainly write to COMBAT NaviDnD/Storage/worldState.json
    // and newGameState.json (StartNewGame.Save()/SendAction.Save(), etc., not to the test fixture) - their
    // contents are removed byte-by-byte before launch and restored to Dispose().
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

        // UseShellExecute=true does not allow setting ProcessStartInfo.EnvironmentVariables directly - we set
        // the variables on ourselves, the child process will inherit them as usual.
        Environment.SetEnvironmentVariable("NAVIDND_TEST_WORLDSTATE", fixturePath);

        // The backup is UNCONDITIONAL, not only when mockedActions: Storage.Save() writes to this FIXED path
        // (SavePath) for ANY game event that is usually saved - including when simply moving the arrows
        // (MovementHandler.TryMove → ApplyClientMovement → Save()), without a single AI- call.
        // NAVIDND_TEST_WORLDSTATE only replaces where the game is LOADED from - Storage.Save() still pours into
        // the battle file. Previously, the backup was conditional (only for mock actions), which is why any
        // test without mockedActions (movement/fog of war/round) silently and IRREVOCABLY overwritten the
        // user's real save without the ability to restore.
        var realSaveBackup = new Dictionary<string, byte[]?>();
        foreach (string name in new[] { "worldState.json", "newGameState.json", "worldState.explored.json", "newGameState.explored.json" })
        {
            string path = Path.Combine(realStorageDir, name);
            // Bytes, not text - File.ReadAllText/WriteAllText is silently stripped/not written to BOM, which
            // would turn the recovery into "almost the same" instead of byte-for-byte.
            realSaveBackup[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        // The “World” step of the new game writes the world to the library and makes it active - the active
        // world is also restored, and worlds that appeared during the test are deleted (Dispose).
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
            // Reset in case the previous test in the same process included mock mode - the process environment
            // variables would otherwise leak into the next normal launch.
            Environment.SetEnvironmentVariable("NAVIDND_TEST_MOCKED_ACTIONS", null);
            Environment.SetEnvironmentVariable("NAVIDND_TEST_MOCK_RESPONSES_DIR", null);
        }

        var psi = new ProcessStartInfo
        {
            FileName = gameExe,
            UseShellExecute = true, // separate window for the game
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
        // CRITICAL: Kill() only REQUESTS the termination of the process and returns control immediately,
        // without waiting for the actual stop - if the killed process was at that moment in the middle of
        // Storage.Save() (writes in BATTLE NaviDnD/Storage/worldState.json), its record can reach the disk
        // AFTER we restore the backup below, irretrievably overwriting the user's real save with test data.
        // WaitForExit() eliminates this race.
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch { /*the window could close on its own*/ }
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
            catch { /*recovery best-effort - should not drop the output test*/ }
        }

        // Tests that inject roll_request.json/ask_player_request.json (see InteractiveResponseTests,
        // NewGameScenarioTests), leave them answered (Answered=true), but do not delete them - the game itself
        // clears them only at the NEXT start (Program.cs). We clear them immediately, and do not wait for the
        // next test.
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
