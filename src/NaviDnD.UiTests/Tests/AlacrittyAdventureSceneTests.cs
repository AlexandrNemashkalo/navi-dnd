using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace NaviDnD.UiTests;

// Opt-in, isolated installation only. These tests use the real bundled Alacritty and physical keys.
public class AlacrittyAdventureSceneTests
{
    public sealed class AlacrittyFactAttribute : FactAttribute
    {
        public AlacrittyFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("NAVIDND_ALACRITTY_TEST_RUNTIME") == null)
                Skip = "Requires an isolated Windows runtime under artifacts via NAVIDND_ALACRITTY_TEST_RUNTIME.";
        }
    }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct KeyInput
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Key;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, KeyInput[] inputs, int size);
    private static void Press(Process terminal, ConsoleKey key)
    {
        terminal.Refresh();
        Assert.NotEqual(0, terminal.MainWindowHandle);
        SetForegroundWindow(terminal.MainWindowHandle);
        Thread.Sleep(180);
        Assert.Equal(2u, SendInput(2, [new() { Type = 1, Key = (ushort)key }, new() { Type = 1, Key = (ushort)key, Flags = 2 }], 40));
    }

    private static string Runtime()
    {
        string runtime = Path.GetFullPath(Environment.GetEnvironmentVariable("NAVIDND_ALACRITTY_TEST_RUNTIME")!);
        Assert.Contains(Path.DirectorySeparatorChar + "artifacts" + Path.DirectorySeparatorChar, runtime);
        string world = Path.Combine(runtime, "Storage", "Worlds", "world-uitest");
        Directory.CreateDirectory(world);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_uitest.json"), Path.Combine(world, "world.json"), true);
        return runtime;
    }

    private static Process? FindTerminal(string runtime)
    {
        return Process.GetProcessesByName("alacritty").FirstOrDefault(p =>
        {
            try { return p.MainModule?.FileName == Path.Combine(runtime, "alacritty.exe") && p.MainWindowHandle != 0; }
            catch { return false; }
        });
    }

    private static void Configure(ProcessStartInfo start, string fixture = "world_game.json")
    {
        // A pointer left on a previous window's menu item overrides the initial keyboard selection.
        SetCursorPos(0, 0);
        start.Environment["NAVIDND_TEST_WORLDSTATE"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        start.Environment["NAVIDND_TEST_MOCKED_ACTIONS"] = "*";
        start.Environment["NAVIDND_MUTE"] = "1";
    }

    private static int GamePid(string runtime) => Process.GetProcessesByName("NaviDnD").Single(p =>
        p.MainModule?.FileName == Path.Combine(runtime, "NaviDnD.exe")).Id;

    private static void WaitForInput(string runtime) => Assert.True(GameConsole.WaitForAnyRowContains(
        GamePid(runtime), 60, 3, GameConsole.TitleReadWidth, "Ввод:", 30000), "Game has not finished drawing input.");

    [AlacrittyFact]
    public void NarrativeTypingKeepsReadableCadence()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        string mockDir = Path.Combine(output, "typing-mocks", "SendAction");
        Directory.CreateDirectory(mockDir);
        string text = "В камине спокойно горит огонь. Хозяйка приносит горячий хлеб, за окном шумят осенние деревья. Вечер только начинается. КОНЕЦ.";
        File.WriteAllText(Path.Combine(mockDir, "response.json"), new JsonObject { ["history"] = new JsonArray(new JsonObject { ["text"] = text }) }.ToJsonString());
        var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
        Configure(start);
        start.Environment["NAVIDND_TEST_MOCK_RESPONSES_DIR"] = Path.GetDirectoryName(mockDir);
        using var launcher = Process.Start(start)!;
        Process? terminal = null;
        try
        {
            var watch = Stopwatch.StartNew();
            while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
            Assert.NotNull(terminal);
            Thread.Sleep(2000);
            Press(terminal, ConsoleKey.Enter);
            WaitForInput(runtime);
            Press(terminal, ConsoleKey.X);
            Press(terminal, ConsoleKey.Enter);
            watch.Restart();
            Assert.True(GameConsole.WaitForAnyRowContains(GamePid(runtime), 39, 20, 110, "КОНЕЦ.", 20000));
            File.WriteAllText(Path.Combine(output, "typing-ms.txt"), watch.ElapsedMilliseconds.ToString());
            WaitForInput(runtime);
        }
        finally
        {
            if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
            terminal?.Dispose();
        }
    }

    [AlacrittyFact]
    public void WorldWizardAcceptsLongDescriptionAndKeepsItAcrossSteps()
    {
        string runtime = Runtime();
        var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
        Configure(start);
        using var launcher = Process.Start(start)!;
        Process? terminal = null;
        try
        {
            var watch = Stopwatch.StartNew();
            while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
            Assert.NotNull(terminal);
            Thread.Sleep(2000);
            Press(terminal, ConsoleKey.DownArrow); Press(terminal, ConsoleKey.Enter);
            int pid = GamePid(runtime);
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 60, 150, "ОПИСАНИЕ МИРА", 5000));
            Press(terminal, ConsoleKey.Enter); Press(terminal, ConsoleKey.UpArrow);
            string description = string.Concat(Enumerable.Repeat("Лесной тракт, уютные деревни и старые башни. ", 8)) + "ФОНАРИ ЗАЖЖЕНЫ";
            var inputs = description.SelectMany(c => new[] { new KeyInput { Type = 1, Scan = c, Flags = 4 }, new KeyInput { Type = 1, Scan = c, Flags = 6 } }).ToArray();
            watch.Restart();
            Assert.Equal((uint)inputs.Length, SendInput((uint)inputs.Length, inputs, 40));
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 60, 150, "ФОНАРИ ЗАЖЖЕНЫ", 4000), "Pasted description took too long to finish.");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(runtime)!, "wizard-input-ms.txt"), watch.ElapsedMilliseconds.ToString());
            Press(terminal, ConsoleKey.F2);
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 60, 150, "АНКЕТА", 5000));
            Press(terminal, ConsoleKey.F1);
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 60, 150, "ФОНАРИ ЗАЖЖЕНЫ", 5000));
        }
        finally
        {
            if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
            terminal?.Dispose();
        }
    }

    [AlacrittyFact]
    public void MainMenuKeepsGameStyleAndKeyboardNavigation()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        foreach (bool hasSave in new[] { true, false })
        {
            var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
            Configure(start);
            if (!hasSave) start.Environment["NAVIDND_TEST_WORLDSTATE"] = Path.Combine(output, "missing-menu-" + Guid.NewGuid().ToString("N") + ".json");
            using var launcher = Process.Start(start)!;
            Process? terminal = null;
            try
            {
                var watch = Stopwatch.StartNew();
                while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(50); }
                Assert.NotNull(terminal);
                int pid = 0;
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    foreach (var process in Process.GetProcessesByName("NaviDnD"))
                    {
                        using (process)
                            if (process.MainModule?.FileName == Path.Combine(runtime, "NaviDnD.exe")) { pid = process.Id; return true; }
                    }
                    return false;
                }, 10000), "Game process did not start.");
                string selected = hasSave ? "→ ПРОДОЛЖИТЬ ←" : "→ НОВАЯ ИГРА ←";
                Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 63, 150, selected, 10000));
                // Keyboard navigation remains available with and without a saved game.
                Press(terminal, ConsoleKey.DownArrow);
                Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 63, 150, hasSave ? "→ НОВАЯ ИГРА ←" : "→ МОИ ИГРЫ ←", 2000));
                Press(terminal, ConsoleKey.UpArrow);
                Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 63, 150, selected, 2000));
                Thread.Sleep(500);
                var rows = GameConsole.ReadRows(pid, 0, 63, 150);
                Assert.DoesNotContain(rows.Skip(3), row => row.Any(c => c is >= '\u2800' and <= '\u28ff'));
                Assert.Contains(rows, row => row.Contains("██"));
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, $"main-menu-{hasSave}-a.png"));
                Thread.Sleep(900);
                Assert.Equal(rows, GameConsole.ReadRows(pid, 0, 63, 150));
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, $"main-menu-{hasSave}-b.png"));
                if (hasSave) { Press(terminal, ConsoleKey.Enter); WaitForInput(runtime); }
                else
                {
                    Press(terminal, ConsoleKey.UpArrow);
                    Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 63, 150, "→ ВЫХОД ←", 2000));
                    Press(terminal, ConsoleKey.Enter);
                    Assert.True(terminal.WaitForExit(5000));
                }
            }
            finally
            {
                if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
                terminal?.Dispose();
            }
        }
    }

    [AlacrittyFact]
    public void SpellStatsAppearOnLeftAndOnlyLearnedSpellsAreListed()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_game.json")))!;
        fixture["formatVersion"] = NaviDnD.StorageFormat.Current;
        fixture["hero"]!["stats"] = JsonNode.Parse("""[{"name":"Сила","value":"+0 (10)"},{"name":"Сложность спасброска от заклинаний","value":"DC 12"}]""");
        fixture["hero"]!["spells"] = JsonNode.Parse("""[{"name":"Свет","level":0,"color":[240,220,120]},{"name":"Огненный шар","level":3,"deleted":true}]""");
        string fixturePath = Path.Combine(output, "spell-stats-fixture.json");
        File.WriteAllText(fixturePath, fixture.ToJsonString());
        var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
        Configure(start);
        start.Environment["NAVIDND_TEST_WORLDSTATE"] = fixturePath;
        using var launcher = Process.Start(start)!;
        Process? terminal = null;
        try
        {
            var watch = Stopwatch.StartNew();
            while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
            Assert.NotNull(terminal);
            Thread.Sleep(2000);
            Press(terminal, ConsoleKey.Enter);
            WaitForInput(runtime);
            int pid = GamePid(runtime);
            Press(terminal, ConsoleKey.F4);
            Press(terminal, ConsoleKey.F9);
            string label = NaviDnD.L.T("Spell save DC");
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 2, 18, 150, label, 5000));
            var rows = GameConsole.ReadRows(pid, 0, 63, 150);
            string statRow = Assert.Single(rows.Where(row => row.Contains(label)));
            Assert.InRange(statRow.IndexOf(label, StringComparison.Ordinal), 0, 48);
            Assert.InRange(statRow.IndexOf("DC 12", StringComparison.Ordinal), 0, 48);
            Assert.DoesNotContain(rows, row => row.Contains("Сложность спасброска от заклинаний"));
            Assert.Contains(rows, row => row.Contains("Свет"));
            Assert.DoesNotContain(rows, row => row.Contains("Огненный шар") || row.Contains("Волшебная рука"));
            ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "spell-stats-fixed.png"));
            Press(terminal, ConsoleKey.F6);
            Assert.True(SpinWait.SpinUntil(() => !GameConsole.ReadRows(pid, 2, 18, 150).Any(row => row.Contains(label)), 5000));
        }
        finally
        {
            if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
            terminal?.Dispose();
        }
    }

    [AlacrittyFact]
    public void NpcFacesAndTerrainPicturesUsePixelsInMapAndJournal()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_location_game.json")))!;
        fixture["iconPack"] = "dcss";
        fixture["formatVersion"] = NaviDnD.StorageFormat.Current;
        fixture["narrative"] = JsonNode.Parse("""{"npcs":[{"name":"Аглая","gender":"female","met":true,"notes":"Хозяйка трактира.","attitude":"friendly"}]}""");
        fixture["map"]!["entities"] = JsonNode.Parse("""[{"name":"Аглая","symbol":"AGR","position":[31,31],"color":[160,190,120],"hp":"8/8","image":"dcss/monster/human"}]""");
        fixture["map"]!["entities"]![0]!["image"] = NaviDnD.Helpers.ArtPictures.Load("Dnd5e", Path.Combine(runtime, "ArtLibrary")).Find("Commoner", "creature").Single();
        fixture["map"]!["objects"] = JsonNode.Parse("""[{"name":"Трава","symbol":"GRS","position":[30,30],"color":[100,180,90],"image":"delapouite/grass"},{"name":"Благовония","symbol":"INC","position":[30,32],"color":[180,140,90],"image":"lorc/incense"},{"name":"Тропа","symbol":"PTH","position":[29,31],"color":[160,140,90],"image":"delapouite/trail"},{"name":"Чаща","symbol":"THK","position":[29,30],"color":[80,150,60],"image":"delapouite/forest"}]""");
        string fixturePath = Path.Combine(output, "npc-art-fixture.json");
        File.WriteAllText(fixturePath, fixture.ToJsonString());
        var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
        Configure(start);
        start.Environment["NAVIDND_TEST_WORLDSTATE"] = fixturePath;
        using var launcher = Process.Start(start)!;
        Process? terminal = null;
        try
        {
            var watch = Stopwatch.StartNew();
            while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
            Assert.NotNull(terminal);
            Thread.Sleep(2000);
            Press(terminal, ConsoleKey.Enter);
            WaitForInput(runtime);
            int pid = GamePid(runtime);
            string Panel() => string.Join('\n', GameConsole.ReadRows(pid, 39, 20, 150).Select(row => row.Length > 112 ? row[112..] : ""));
            var remaining = new HashSet<string> { "Аглая", "Трава", "Благовония", "Тропа", "Чаща" };
            for (int i = 0; i < 12 && remaining.Count > 0; i++)
            {
                Press(terminal, ConsoleKey.Tab);
                Thread.Sleep(250);
                string panel = Panel();
                foreach (string name in remaining.Where(panel.Contains).ToArray())
                {
                    Assert.Contains(panel, c => c is '▀' or '▄');
                    Assert.DoesNotContain(panel, c => c is >= '\u2800' and <= '\u28ff');
                    ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "npc-art-" + remaining.Count + ".png"));
                    remaining.Remove(name);
                }
            }
            Assert.Empty(remaining);
            Press(terminal, ConsoleKey.F3);
            Press(terminal, ConsoleKey.F7);
            Thread.Sleep(500);
            Press(terminal, ConsoleKey.Tab);
            Thread.Sleep(350);
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 35, 150, "Аглая", 5000));
            Assert.Contains(Panel(), c => c is '▀' or '▄');
            Assert.DoesNotContain(Panel(), c => c is >= '\u2800' and <= '\u28ff');
            ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "npc-art-journal.png"));
        }
        finally
        {
            if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
            terminal?.Dispose();
        }
    }

    [AlacrittyFact]
    public void F11CyclesAll64ScenesAndWrapsToFirst()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
        Configure(start);
        using var launcher = Process.Start(start)!;
        Process? terminal = null;
        try
        {
            var watch = Stopwatch.StartNew();
            while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
            Assert.NotNull(terminal);
            Thread.Sleep(2000);
            Press(terminal, ConsoleKey.Enter);
            WaitForInput(runtime);
            Press(terminal, ConsoleKey.F12);
            int pid = GamePid(runtime);
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 60, 150, "Анимация: пауза", 5000));
            // Input can be visible before the saved dialogue finishes its initial redraw.
            // Compare gallery navigation against the loaded dialogue, not a transient blank panel.
            Assert.True(GameConsole.WaitForAnyRowContains(pid, 39, 19, 110, "Осматриваюсь по сторонам.", 5000));
            var dialog = GameConsole.ReadRows(pid, 39, 19, 110);
            List<string>? first = null;
            for (int index = 1; index <= 65; index++)
            {
                Press(terminal, ConsoleKey.F11);
                int number = index == 65 ? 1 : index;
                Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 36, 150, $"ГАЛЕРЕЯ СЦЕН {number}/64", 5000), $"Gallery stopped at scene {number}.");
                Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 36, 150, $"Вариант {(number - 1) % 2 + 1}/2", 5000));
                if (index == 1) first = GameConsole.ReadRows(pid, 3, 33, 150);
                if (index == 64)
                {
                    Assert.True(GameConsole.WaitForAnyRowContains(pid, 0, 36, 150, "лагерь", 5000));
                    Thread.Sleep(200);
                    ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "gallery-all-last.png"));
                }
            }
            Assert.Equal(first, GameConsole.ReadRows(pid, 3, 33, 150));
            Assert.Equal(dialog, GameConsole.ReadRows(pid, 39, 19, 110));
            Thread.Sleep(200);
            ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "gallery-all-first.png"));
        }
        finally
        {
            if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
            terminal?.Dispose();
        }
    }

    [AlacrittyFact]
    public void LauncherCapturesFourRenderingStylesAndVariants()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        foreach (string style in new[] { "Braille", "Mosaic", "HalfBlock", "Quadrants" })
        {
            var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
            Configure(start);
            start.Environment["NAVIDND_TEST_SCENE_STYLE"] = style;
            using var launcher = Process.Start(start)!;
            Process? terminal = null;
            try
            {
                var watch = Stopwatch.StartNew();
                while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
                Assert.NotNull(terminal);
                Thread.Sleep(2000);
                Press(terminal, ConsoleKey.Enter);
                WaitForInput(runtime);
                Thread.Sleep(500);
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, $"scene-{style}-a.png"));
                Press(terminal, ConsoleKey.F11);
                Thread.Sleep(500);
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, $"scene-{style}-b.png"));
                Assert.NotEqual(File.ReadAllBytes(Path.Combine(output, $"scene-{style}-a.png")), File.ReadAllBytes(Path.Combine(output, $"scene-{style}-b.png")));
                Press(terminal, ConsoleKey.F2);
                Thread.Sleep(500);
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, $"world-{style}.png"));
            }
            finally
            {
                if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
                terminal?.Dispose();
            }
        }
    }

    [AlacrittyFact]
    public void RepresentativeScenesAnimateAndPauseWithoutChangingDialog()
    {
        // The empty input field's terminal caret blinks independently of scene animation.
        static bool InputCaret(int x, int y, int width, int height) =>
            x >= width * 9 / 150 && x < width * 10 / 150 && y >= height * 61 / 63 && y < height * 62 / 63;
        static IEnumerable<int> StablePixels(System.Drawing.Bitmap bitmap)
        {
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (!InputCaret(x, y, bitmap.Width, bitmap.Height)) yield return bitmap.GetPixel(x, y).ToArgb();
        }
        foreach (string type in new[] { "port", "cave", "tavern", "ruins", "forest", "snow", "camp", "dungeon" })
        {
            string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
            var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_game.json")))!;
            var worldPath = Path.Combine(runtime, "Storage", "Worlds", "world-uitest", "world.json");
            var world = JsonNode.Parse(File.ReadAllText(worldPath), new JsonNodeOptions { PropertyNameCaseInsensitive = true })!;
            string place = fixture["world"]!["place"]!.GetValue<string>();
            var current = world["chronicle"]!["places"]!.AsArray().Single(p => p!["name"]!.GetValue<string>() == place)!;
            current["type"] = type;
            if (type is "forest" or "snow")
            {
                int x = fixture["world"]!["x"]!.GetValue<int>(), y = fixture["world"]!["y"]!.GetValue<int>();
                var row = world["biomes"]![y]!.GetValue<string>().ToCharArray();
                row[x] = type == "forest" ? 'f' : 'w';
                world["biomes"]![y] = new string(row);
            }
            File.WriteAllText(worldPath, world.ToJsonString());
            var start = new ProcessStartInfo(Path.Combine(runtime, "NaviDnD.Launcher.exe")) { WorkingDirectory = runtime, UseShellExecute = false };
            Configure(start);
            using var launcher = Process.Start(start)!;
            Process? terminal = null;
            try
            {
                var watch = Stopwatch.StartNew();
                while (terminal == null && watch.Elapsed < TimeSpan.FromSeconds(15)) { terminal = FindTerminal(runtime); Thread.Sleep(100); }
                Assert.NotNull(terminal);
                Thread.Sleep(2000);
                Press(terminal, ConsoleKey.Enter);
                WaitForInput(runtime);
                Thread.Sleep(1000);
                string a = Path.Combine(output, $"gallery-{type}-a.png"), b = Path.Combine(output, $"gallery-{type}-b.png");
                bool fire = type is "tavern" or "camp" or "dungeon";
                var shapes = fire ? GameConsole.ReadRows(GamePid(runtime), 3, 33, 90) : null;
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, a);
                Thread.Sleep(1200);
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, b);
                if (fire) Assert.Equal(shapes, GameConsole.ReadRows(GamePid(runtime), 3, 33, 90));
                // Some glints have symmetric sine phases; sample more than one pair before
                // declaring the animation stuck.
                bool animated = type is "tavern" or "snow" or "camp" or "dungeon";
                for (int attempt = 0; animated && attempt < 5 && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)); attempt++)
                {
                    Thread.Sleep(500);
                    ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, b);
                }
                using var first = new System.Drawing.Bitmap(a);
                using var next = new System.Drawing.Bitmap(b);
                int inside = 0, outside = 0;
                for (int y = 0; y < first.Height; y++)
                for (int x = 0; x < first.Width; x++)
                    if (first.GetPixel(x, y) != next.GetPixel(x, y))
                    {
                        if (x >= first.Width * 2 / 150 && x < first.Width * 89 / 150 && y >= first.Height * 3 / 63 && y < first.Height * 36 / 63) inside++;
                        else if (!InputCaret(x, y, first.Width, first.Height)) outside++;
                    }
                
                
                if (!animated) Assert.Equal(0, inside);
                else Assert.True(inside > 0, $"{type}: animation did not change any viewport pixels.");
                Assert.Equal(0, outside);
                first.Dispose(); next.Dispose(); // release source PNG handles before capturing paused frames
                if (type == "camp")
                {
                    // Count changes in actual displayed pixels, not renderer timer callbacks.
                    var cadence = Stopwatch.StartNew();
                    var changes = new List<long>();
                    string? previous = null;
                    while (cadence.ElapsedMilliseconds < 2500)
                    {
                        using var frame = ScreenCapture.CaptureTerminalWindow(terminal.MainWindowHandle);
                        var rectangle = new System.Drawing.Rectangle(frame.Width * 2 / 150, frame.Height * 3 / 63,
                            frame.Width * 87 / 150, frame.Height * 33 / 63);
                        var data = frame.LockBits(rectangle, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        string hash;
                        try
                        {
                            var pixels = new byte[data.Width * data.Height * 4];
                            for (int row = 0; row < data.Height; row++)
                                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * data.Width * 4, data.Width * 4);
                            hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
                        }
                        finally { frame.UnlockBits(data); }
                        if (previous != null && previous != hash) changes.Add(cadence.ElapsedMilliseconds);
                        previous = hash;
                        Thread.Sleep(10);
                    }
                    File.WriteAllText(Path.Combine(output, "animation-cadence.json"), System.Text.Json.JsonSerializer.Serialize(
                        new { elapsedMs = cadence.ElapsedMilliseconds, visibleFrameChanges = changes.Count, timestamps = changes }));
                    Assert.True(changes.Count >= 25, $"Only {changes.Count} visible updates in {cadence.ElapsedMilliseconds} ms.");
                }
                Press(terminal, ConsoleKey.F12);
                Thread.Sleep(500);
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, a);
                Thread.Sleep(1000);
                ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, b);
                using var pausedFirst = new System.Drawing.Bitmap(a);
                using var pausedNext = new System.Drawing.Bitmap(b);
                Assert.Equal(StablePixels(pausedFirst), StablePixels(pausedNext));
            }
            finally
            {
                if (terminal is { HasExited: false }) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
                terminal?.Dispose();
            }
        }
    }

    [AlacrittyFact]
    public void AdventureWorldStreamingAndTacticalTransitionsUseRealKeys()
    {
        string runtime = Runtime(), output = Path.GetDirectoryName(runtime)!;
        string recording = Path.Combine(output, "alacritty.recording");
        if (File.Exists(recording)) File.Delete(recording);
        string mockDir = Path.Combine(output, "mocks", "SendAction");
        Directory.CreateDirectory(mockDir);
        File.WriteAllText(Path.Combine(mockDir, "response.json"), "{\"history\":[{\"text\":\"Путь свободен.\"}]}");
        var tactical = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world_location_game.json")))!;
        File.WriteAllText(Path.Combine(mockDir, "response2.json"), new JsonObject
        {
            ["map"] = tactical["map"]!.DeepClone(),
            ["hero"] = new JsonObject { ["position"] = tactical["hero"]!["position"]!.DeepClone() },
            ["history"] = new JsonArray(),
        }.ToJsonString());
        var start = new ProcessStartInfo(Path.Combine(runtime, "alacritty.exe")) { WorkingDirectory = output, UseShellExecute = false };
        foreach (string arg in new[] { "--ref-test", "--config-file", Path.Combine(runtime, "Terminal", "alacritty-windows.toml"), "--title", "NaviDnD", "-e", Path.Combine(runtime, "NaviDnD.exe"), "--navidnd-alacritty" }) start.ArgumentList.Add(arg);
        Configure(start);
        int execute = start.ArgumentList.IndexOf("-e");
        start.ArgumentList.Insert(execute, "-o");
        start.ArgumentList.Insert(execute + 1, NaviDnD.Helpers.WindowsTerminalSizing.FontOverride(runtime));
        NaviDnD.Helpers.WindowsTerminalSizing.PrepareLiveConfig(start);
        start.Environment["NAVIDND_TEST_MOCK_RESPONSES_DIR"] = Path.GetDirectoryName(mockDir);
        using var terminal = Process.Start(start)!;
        string Read()
        {
            if (!File.Exists(recording)) return "";
            using var file = new FileStream(recording, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(file, Encoding.UTF8);
            return Regex.Replace(reader.ReadToEnd(), @"\x1b\[[0-9;?]*[A-Za-z]", "");
        }
        bool Wait(Func<string, bool> predicate, int timeout = 12000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeout) { if (predicate(Read())) return true; Thread.Sleep(50); }
            return false;
        }
        var sceneSwitchMilliseconds = new List<long>();
        void KeyAndExpect(ConsoleKey key, string text, int timeout = 5000)
        {
            var keyWatch = Stopwatch.StartNew();
            int offset = Read().Length;
            Press(terminal, key);
            Assert.True(Wait(s => s.Length >= offset && (key == ConsoleKey.F11
                ? s[offset..].Count(c => c is >= '\u2580' and <= '\u259f' or >= '\u2800' and <= '\u28ff') > 1000
                : s[offset..].Contains(text)), timeout), $"{key}: missing {text} after keypress.");
            File.AppendAllText(Path.Combine(output, "transition-timings.txt"), $"{key}: {keyWatch.ElapsedMilliseconds} ms\n");
            if (key == ConsoleKey.F1) sceneSwitchMilliseconds.Add(keyWatch.ElapsedMilliseconds);
        }
        try
        {
            Assert.True(Wait(s => s.Contains("МЕНЮ")));
            Thread.Sleep(1500); // wait until the menu's reveal animation has released input
            KeyAndExpect(ConsoleKey.Enter, "[F11] Другой вид");
            Assert.True(Read().Count(c => c is >= '\u2580' and <= '\u259f' or >= '\u2800' and <= '\u28ff') >= 87 * 33, "Scene must fill the whole viewport.");
            KeyAndExpect(ConsoleKey.F11, "[F11] Другой вид");
            KeyAndExpect(ConsoleKey.F12, "Анимация: пауза");
            KeyAndExpect(ConsoleKey.F12, "Анимация: вкл.");
            KeyAndExpect(ConsoleKey.F2, "Вы: Ключи");
            KeyAndExpect(ConsoleKey.Tab, "Путь: Светлоград");
            KeyAndExpect(ConsoleKey.F1, "[F11] Другой вид");
            for (int cycle = 0; cycle < 5; cycle++)
            {
                KeyAndExpect(ConsoleKey.F2, "Вы: Ключи");
                KeyAndExpect(ConsoleKey.F1, "[F11] Другой вид");
            }
            // Includes Press's 180 ms focus delay. Sleep(1) per write used to inflate
            // these scene transitions to 600–700 ms on the same terminal.
            long medianSceneSwitch = sceneSwitchMilliseconds.Order().ElementAt(sceneSwitchMilliseconds.Count / 2);
            Assert.True(medianSceneSwitch < 500, $"Scene redraws are too slow: median {medianSceneSwitch} ms.");
            Press(terminal, ConsoleKey.X);
            KeyAndExpect(ConsoleKey.Enter, "Ожидание");
            KeyAndExpect(ConsoleKey.F3, "ЖУРНАЛ");
            KeyAndExpect(ConsoleKey.F2, "Вы: Ключи");
            KeyAndExpect(ConsoleKey.F1, "[F11] Другой вид");
            KeyAndExpect(ConsoleKey.F11, "[F11] Другой вид", 1500);
            WaitForInput(runtime);
            // Next mocked response creates tactical cells; the same F1 tab must show them automatically.
            Press(terminal, ConsoleKey.X);
            Press(terminal, ConsoleKey.Enter);
            Assert.True(GameConsole.WaitForAnyRowContains(GamePid(runtime), 0, 37, GameConsole.TitleReadWidth, "Время:", 25000));
            WaitForInput(runtime);
            Thread.Sleep(300);
            ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "tactical-after-scene.png"));
            // Fixture's hero stands at an external exit. Leaving returns to scenery on F1.
            Press(terminal, ConsoleKey.DownArrow);
            Assert.True(GameConsole.WaitForAnyRowContains(GamePid(runtime), 0, 37, GameConsole.TitleReadWidth, "[F11] Другой вид", 8000));
            WaitForInput(runtime);
            Thread.Sleep(300);
            ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "scene-after-exit.png"));
            Assert.DoesNotContain("ГАЛЕРЕЯ СЦЕН", string.Join("\n", GameConsole.ReadRows(GamePid(runtime), 0, 36, 150)));
            File.WriteAllText(Path.Combine(output, "scene-transitions.txt"), Read());
        }
        finally
        {
            if (!terminal.HasExited) ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "transition-last.png"));
            if (!terminal.HasExited) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
        }
    }
}
