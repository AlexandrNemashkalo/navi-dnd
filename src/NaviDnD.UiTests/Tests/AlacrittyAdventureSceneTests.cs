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
        [FieldOffset(12)] public uint Flags;
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
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
        start.Environment["NAVIDND_TEST_WORLDSTATE"] = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        start.Environment["NAVIDND_TEST_MOCKED_ACTIONS"] = "*";
        start.Environment["NAVIDND_MUTE"] = "1";
    }

    private static int GamePid(string runtime) => Process.GetProcessesByName("NaviDnD").Single(p =>
        p.MainModule?.FileName == Path.Combine(runtime, "NaviDnD.exe")).Id;

    private static void WaitForInput(string runtime) => Assert.True(GameConsole.WaitForAnyRowContains(
        GamePid(runtime), 60, 3, GameConsole.TitleReadWidth, "Ввод:", 30000), "Game has not finished drawing input.");

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
                        else outside++;
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
                Assert.Equal(File.ReadAllBytes(a), File.ReadAllBytes(b));
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
        void KeyAndExpect(ConsoleKey key, string text, int timeout = 5000)
        {
            int offset = Read().Length;
            Press(terminal, key);
            Assert.True(Wait(s => s.Length >= offset && (key == ConsoleKey.F11
                ? s[offset..].Count(c => c is >= '\u2580' and <= '\u259f' or >= '\u2800' and <= '\u28ff') > 1000
                : s[offset..].Contains(text)), timeout), $"{key}: missing {text} after keypress.");
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
            KeyAndExpect(ConsoleKey.F1, "[F11] Другой вид");
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
            File.WriteAllText(Path.Combine(output, "scene-transitions.txt"), Read());
        }
        finally
        {
            if (!terminal.HasExited) ScreenCapture.SaveTerminalWindow(terminal.MainWindowHandle, Path.Combine(output, "transition-last.png"));
            if (!terminal.HasExited) { terminal.Kill(entireProcessTree: true); terminal.WaitForExit(5000); }
        }
    }
}
