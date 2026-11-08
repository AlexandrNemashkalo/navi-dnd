using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class AiCliDiscoveryTests
{
    [Fact]
    public void StaleCodexPathFallsBackToInstalledCli()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "codex.exe");
        Assert.Equal(AiCliDiscovery.Find("codex"), AiCliDiscovery.Resolve("codex", missing));
    }

    [Fact]
    public void MissingConfiguredExecutableDoesNotFallBackToAnotherInstallation()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "codex.exe");
        Assert.False(AiCliDiscovery.IsAvailable("codex", path));
        Assert.False(AiCliDiscovery.IsAvailable("claude", path));
    }

    [Fact]
    public void CustomExecutableIsRecognizedWithoutLaunchingIt()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        try
        {
            File.WriteAllText(path, "test fixture");
            Assert.True(AiCliDiscovery.IsAvailable("codex", path));
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void ModernNpmPlatformPackageIsFoundWithoutRecursiveSearch(string name)
    {
        if (!OperatingSystem.IsWindows()) { LauncherInBinFolderIsFoundOnUnix(name); return; }
        string root = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N"));
        string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        string relative = name == "claude" ? $"node_modules/@anthropic-ai/claude-code-win32-{arch}/claude.exe"
            : $"node_modules/@openai/codex-win32-{arch}/vendor/{(arch == "arm64" ? "aarch64" : "x86_64")}-pc-windows-msvc/codex/codex.exe";
        string exe = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "fixture");
            Assert.Equal(exe, AiCliDiscovery.Find(name, [root], []));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // macOS/Linux: in the bin folder of npm/nvm/Homebrew there is the launcher itself; it must be executable.
    private static void LauncherInBinFolderIsFoundOnUnix(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N"));
        string bin = Path.Combine(root, "bin");
        try
        {
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, name), "fixture");
            Assert.Null(AiCliDiscovery.Find(name, [bin], []));   // not executable
            string exe = Fixture(Path.Combine(bin, name));
            Assert.Equal(exe, AiCliDiscovery.Find(name, [bin], []));
            Assert.Null(AiCliDiscovery.Find(name, [Path.Combine(root, "missing")], []));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // The terminal wrapper for the session (cmux) previously in PATH is skipped - the real CLI is taken after it.
    [Fact]
    public void TerminalShimIsSkippedForRealCli()
    {
        if (OperatingSystem.IsWindows()) return;
        string root = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            string shim = Path.Combine(root, ".cmuxterm", "cmux-cli-shims", "SESSION");
            Fixture(Path.Combine(shim, "claude"));
            string real = Fixture(Path.Combine(root, "bin", "claude"));
            Assert.Equal(real, AiCliDiscovery.Find("claude", [shim, Path.Combine(root, "bin")], []));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // Test file; outside Windows is marked executable - there the search requires it.
    private static string Fixture(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    // The name of the executable file on this OS.
    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void EditorExtensionOutsideStandardVsCodeFolderIsFound(string name)
    {
        string root = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N"));
        string platform = OperatingSystem.IsWindows()
            ? System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64
                ? "windows-aarch64" : "windows-x86_64"
            : "macos-aarch64";
        string exe = name == "codex" ? Path.Combine(root, "openai.chatgpt-1.0", "bin", platform, Exe("codex"))
            : Path.Combine(root, "anthropic.claude-code-1.0", "resources", "native-binary", Exe("claude"));
        try
        {
            Fixture(exe);
            Assert.Equal(exe, AiCliDiscovery.Find(name, [], [root]));
            var config = new NaviDnD.AppConfig { CodexCliPath = Path.Combine(root, "deleted", Exe("codex")) };
            if (name == "codex")
            {
                Assert.True(config.ApplyAiPaths(null, AiCliDiscovery.Find(name, [], [root]), false));
                Assert.Equal(exe, config.CodexCliPath);
                Assert.Equal("claude", config.AiProvider);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WorkingManualPathIsPreservedAndMissingPathIsRepaired()
    {
        string exe = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            Fixture(exe);
            var config = new NaviDnD.AppConfig { ClaudeCliPath = "\"" + exe + "\"", CodexCliPath = "missing.exe" };
            var found = await config.DiscoverAiPathsAsync();
            Assert.Equal(exe, found.claude);
            Assert.True(config.ApplyAiPaths(found.claude, null, false));
            Assert.Equal(exe, config.ClaudeCliPath);
            Assert.Equal("missing.exe", config.CodexCliPath);
            Assert.True(config.ApplyAiPaths(null, exe, false));
            Assert.Equal(exe, config.CodexCliPath);
            Assert.Equal("claude", config.AiProvider);
        }
        finally { File.Delete(exe); }
    }
    [Fact]
    public void DesktopSearchUsesNewestCompleteVersionAndRepairsSavedPath()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-discovery-" + Guid.NewGuid().ToString("N"));
        string old = Path.Combine(root, "2.1.9", "old-hash", Exe("claude"));
        string latest = Path.Combine(root, "2.1.293", "new-hash", Exe("claude"));
        try
        {
            foreach (string exe in new[] { old, latest })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
                File.WriteAllText(exe, "fixture");
            }
            Directory.CreateDirectory(Path.Combine(root, "2.1.294", "unfinished"));
            Assert.Equal(latest, AiCliDiscovery.FindDesktopClaude([root]));
            var config = new NaviDnD.AppConfig { ClaudeCliPath = Path.Combine(root, "deleted", Exe("claude")) };
            Assert.True(config.ApplyAiPaths(AiCliDiscovery.FindDesktopClaude([root]), null, false));
            Assert.Equal(latest, config.ClaudeCliPath);
            File.Delete(latest);
            Assert.Equal(old, AiCliDiscovery.FindDesktopClaude([root]));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DesktopGuiIsNotMistakenForCliInSearchDirectories()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "resources"));
            File.WriteAllText(Path.Combine(root, "claude.exe"), "fixture");
            File.WriteAllText(Path.Combine(root, "resources", "app.asar"), "fixture");
            Assert.Null(AiCliDiscovery.Find("claude", [root], []));
            Assert.True(AiCliDiscovery.IsClaudeDesktopInstalled(Path.Combine(root, "claude.exe")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void MacDesktopBundledCliIsFoundInsideItsAppBundle()
    {
        if (OperatingSystem.IsWindows()) return;   // layout Windows: DesktopSearchUsesNewestCompleteVersionAndRepairsSavedPath
        string root = Path.Combine(Path.GetTempPath(), "desktop-discovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            string cli = Fixture(Path.Combine(root, "2.1.295", "9d6405864fdc", "claude.app", "Contents", "MacOS", "claude"));
            Assert.Equal(cli, AiCliDiscovery.FindDesktopClaude([root]));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void UnixDefaultsHaveNoExeSuffixAndHomeIsExpanded()
    {
        if (OperatingSystem.IsWindows()) return;
        var config = new NaviDnD.AppConfig();
        Assert.Equal("claude", config.ClaudeCliPath);
        Assert.EndsWith("NaviDnD.McpServer", config.McpServerPath);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude"),
            AiCliDiscovery.NormalizePath(" \"~/.local/bin/claude\" "));
    }
}
