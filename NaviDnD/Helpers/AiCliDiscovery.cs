using System.Runtime.InteropServices;

namespace NaviDnD.Helpers;

public static class AiCliDiscovery
{
    public static string NormalizePath(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    public static bool IsAvailable(string name, string configuredPath)
    {
        string path = NormalizePath(configuredPath);
        if (Path.IsPathRooted(path)) return File.Exists(path);
        return Find(name) != null;
    }

    public static string? Resolve(string name, string configuredPath)
    {
        string path = NormalizePath(configuredPath);
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !(name == "claude" && IsDesktopApp(path))) return path;
            // npm launchers are scripts; providers need the native executable beside their package.
            if (Find(name, [Path.GetDirectoryName(path)!], []) is { } native) return native;
        }
        return Find(name);
    }

    // Run on a worker thread: no CLI launches, recursive disk scans or network requests.
    public static string? Find(string name)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var paths = new[] {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
        }.SelectMany(p => (p ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        var directories = paths.Concat(new[] {
            Path.Combine(home, ".local", "bin"), Path.Combine(home, ".cargo", "bin"),
            Path.Combine(roaming, "npm"), Path.Combine(local, "Microsoft", "WinGet", "Links"),
            Environment.GetEnvironmentVariable("NVM_SYMLINK") ?? "",
            Environment.GetEnvironmentVariable("PNPM_HOME") ?? ""
        });
        var extensions = new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf" }
            .Select(editor => Path.Combine(home, editor, "extensions"))
            .Append(Environment.GetEnvironmentVariable("VSCODE_EXTENSIONS") ?? "");
        string? cli = Find(name, directories, extensions);
        if (cli != null || name != "claude") return cli;
        return FindDesktopClaude([
            Path.Combine(roaming, "Claude", "claude-code"),
            Path.Combine(local, "Packages", "Claude_pzs8sxrjxfjjc", "LocalCache", "Roaming", "Claude", "claude-code")
        ]);
    }

    public static string? Find(string name, IEnumerable<string> directories, IEnumerable<string> extensionRoots)
    {
        string arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        string triple = arch == "arm64" ? "aarch64-pc-windows-msvc" : "x86_64-pc-windows-msvc";
        foreach (string raw in directories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string directory = NormalizePath(raw);
                if (!Path.IsPathRooted(directory)) continue;
                string scope = Path.Combine(directory, "node_modules", name == "codex" ? "@openai" : "@anthropic-ai");
                string package = Path.Combine(scope, name == "codex" ? "codex" : "claude-code");
                string platformPackage = Path.Combine(scope, name == "codex" ? $"codex-win32-{arch}" : $"claude-code-win32-{arch}");
                string[] candidates = name == "codex" ? [
                    Path.Combine(directory, "codex.exe"),
                    Path.Combine(package, "vendor", triple, "codex", "codex.exe"),
                    Path.Combine(platformPackage, "vendor", triple, "codex", "codex.exe"),
                    Path.Combine(platformPackage, "codex.exe"),
                    Path.Combine(package, "node_modules", "@openai", $"codex-win32-{arch}", "vendor", triple, "codex", "codex.exe")
                ] : [
                    Path.Combine(directory, "claude.exe"), Path.Combine(package, "claude.exe"),
                    Path.Combine(platformPackage, "claude.exe"),
                    Path.Combine(package, "vendor", triple, "claude.exe"),
                    Path.Combine(package, "node_modules", "@anthropic-ai", $"claude-code-win32-{arch}", "claude.exe")
                ];
                foreach (string candidate in candidates)
                    if (File.Exists(candidate) && !(name == "claude" && IsDesktopApp(candidate))) return Path.GetFullPath(candidate);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { }
        }
        foreach (string raw in extensionRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string root = NormalizePath(raw);
                if (!Path.IsPathRooted(root) || !Directory.Exists(root)) continue;
                string pattern = name == "codex" ? "openai.chatgpt-*" : "anthropic.claude-code-*";
                foreach (string extension in Directory.EnumerateDirectories(root, pattern).OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    string windows = arch == "arm64" ? "windows-aarch64" : "windows-x86_64";
                    string[] candidates = name == "codex" ? [Path.Combine(extension, "bin", windows, "codex.exe")]
                        : [Path.Combine(extension, "resources", "native-binary", "claude.exe"),
                           Path.Combine(extension, "resources", "native-binary", $"win32-{arch}", "claude.exe")];
                    foreach (string candidate in candidates)
                        if (File.Exists(candidate) && !(name == "claude" && IsDesktopApp(candidate))) return Path.GetFullPath(candidate);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { }
        }
        return null;
    }
    // Electron Desktop's GUI executable is also named claude.exe, but is not a CLI.
    private static bool IsDesktopApp(string executable) =>
        File.Exists(Path.Combine(Path.GetDirectoryName(executable)!, "resources", "app.asar"));

    public static string? FindDesktopClaude(IEnumerable<string> roots)
    {
        var versions = new List<(Version version, string path)>();
        foreach (string raw in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string root = NormalizePath(raw);
                if (!Path.IsPathRooted(root) || !Directory.Exists(root)) continue;
                foreach (string directory in Directory.EnumerateDirectories(root))
                    if (Version.TryParse(Path.GetFileName(directory), out var version))
                        versions.Add((version, directory));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        // Inspect just version/hash directories; never walk WindowsApps or the user's whole profile.
        foreach (var item in versions.OrderByDescending(v => v.version))
        {
            try
            {
                string direct = Path.Combine(item.path, "claude.exe");
                if (File.Exists(direct)) return direct;
                foreach (string hash in Directory.EnumerateDirectories(item.path).OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    string executable = Path.Combine(hash, "claude.exe");
                    if (File.Exists(executable)) return executable;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        return null;
    }
}
