using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class AiCliDiscoveryTests
{
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

    [Fact]
    public void EditorExtensionOutsideStandardVsCodeFolderIsFound()
    {
        string root = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N"));
        string exe = Path.Combine(root, "anthropic.claude-code-1.0", "resources", "native-binary", "claude.exe");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
            File.WriteAllText(exe, "fixture");
            Assert.Equal(exe, AiCliDiscovery.Find("claude", [], [root]));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WorkingManualPathIsPreservedAndMissingPathIsRepaired()
    {
        string exe = Path.Combine(Path.GetTempPath(), "discovery-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(exe, "fixture");
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
        string old = Path.Combine(root, "2.1.9", "old-hash", "claude.exe");
        string latest = Path.Combine(root, "2.1.293", "new-hash", "claude.exe");
        try
        {
            foreach (string exe in new[] { old, latest })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
                File.WriteAllText(exe, "fixture");
            }
            Directory.CreateDirectory(Path.Combine(root, "2.1.294", "unfinished"));
            Assert.Equal(latest, AiCliDiscovery.FindDesktopClaude([root]));
            var config = new NaviDnD.AppConfig { ClaudeCliPath = Path.Combine(root, "deleted", "claude.exe") };
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
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
