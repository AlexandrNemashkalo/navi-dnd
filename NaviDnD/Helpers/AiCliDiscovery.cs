using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NaviDnD.Helpers;

public static class AiCliDiscovery
{
    public static string NormalizePath(string path) => OperatingSystem.IsWindows()
        ? Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))
        : UnixTools.ExpandHome(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"', '\'')));

    public static bool IsAvailable(string name, string configuredPath)
    {
        string path = NormalizePath(configuredPath);
        if (Path.IsPathRooted(path)) return File.Exists(path);
        return Find(name) != null;
    }

    public static string? Resolve(string name, string configuredPath)
    {
        string path = NormalizePath(configuredPath);
        if (!OperatingSystem.IsWindows())
            return Path.IsPathRooted(path) && UnixTools.IsExecutable(path) ? path : Find(name);
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
        if (!OperatingSystem.IsWindows()) return FindUnix(name);
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
        if (!OperatingSystem.IsWindows()) return FindUnix(name, directories, extensionRoots);
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
        string[] executables = OperatingSystem.IsWindows() ? ["claude.exe"]
            : ["claude", Path.Combine("claude.app", "Contents", "MacOS", "claude")];
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
                foreach (string file in executables)
                {
                    string direct = Path.Combine(item.path, file);
                    if (File.Exists(direct)) return direct;
                }
                foreach (string hash in Directory.EnumerateDirectories(item.path).OrderByDescending(Directory.GetLastWriteTimeUtc))
                    foreach (string file in executables)
                    {
                        string executable = Path.Combine(hash, file);
                        if (File.Exists(executable)) return executable;
                    }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException) { }
        }
        return null;
    }

    // macOS/Linux: родной установщик (~/.local/bin), старая локальная установка (~/.claude/local), Homebrew, npm (любой префикс, nvm,
    // volta, bun, pnpm). Запускатель из npm — скрипт, который выполняет `node`; провайдеры ставят его папку первой в PATH дочернего процесса.
    [UnsupportedOSPlatform("windows")]
    private static string? FindUnix(string name)
    {
        string home = UnixTools.Home;
        var directories = new List<string>(UnixTools.SearchDirectories())
        {
            Path.Combine(home, ".local", "bin"), Path.Combine(home, ".claude", "local"), Path.Combine(home, ".claude", "bin"),
            Path.Combine(home, ".npm-global", "bin"), Path.Combine(home, ".npm", "bin"), Path.Combine(home, ".volta", "bin"),
            Path.Combine(home, ".bun", "bin"), Path.Combine(home, "Library", "pnpm"), Path.Combine(home, ".local", "share", "pnpm"),
            Path.Combine(home, ".cargo", "bin"), Path.Combine(home, ".codex", "bin"),
            Environment.GetEnvironmentVariable("PNPM_HOME") ?? "", Environment.GetEnvironmentVariable("NVM_BIN") ?? "",
        };
        foreach (string prefix in NpmPrefixes(home)) directories.Add(Path.Combine(prefix, "bin"));
        directories.AddRange(NvmBinDirectories(home));
        var extensions = new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf", ".vscode-server" }
            .Select(editor => Path.Combine(home, editor, "extensions"))
            .Append(Environment.GetEnvironmentVariable("VSCODE_EXTENSIONS") ?? "");
        string? cli = FindUnix(name, directories, extensions);
        if (cli != null || name != "claude") return cli;
        return FindDesktopClaude([Path.Combine(home, "Library", "Application Support", "Claude", "claude-code"),
            Path.Combine(home, ".config", "Claude", "claude-code")]);
    }

    [UnsupportedOSPlatform("windows")]
    private static string? FindUnix(string name, IEnumerable<string> directories, IEnumerable<string> extensionRoots)
    {
        foreach (string raw in directories.Distinct(StringComparer.Ordinal))
        {
            string directory = NormalizePath(raw);
            if (!Path.IsPathRooted(directory) || IsTerminalShim(directory)) continue;
            string candidate = Path.Combine(directory, name);
            if (UnixTools.IsExecutable(candidate)) return candidate;
        }
        foreach (string raw in extensionRoots.Distinct(StringComparer.Ordinal))
        {
            try
            {
                string root = NormalizePath(raw);
                if (!Path.IsPathRooted(root) || !Directory.Exists(root)) continue;
                string pattern = name == "codex" ? "openai.chatgpt-*" : "anthropic.claude-code-*";
                foreach (string extension in Directory.EnumerateDirectories(root, pattern).OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    // Только один уровень папок платформ: bin/<платформа>/codex, resources/native-binary[/<платформа>]/claude.
                    string parent = name == "codex" ? Path.Combine(extension, "bin") : Path.Combine(extension, "resources", "native-binary");
                    if (!Directory.Exists(parent)) continue;
                    var candidates = Directory.EnumerateDirectories(parent).Select(platform => Path.Combine(platform, name)).Prepend(Path.Combine(parent, name));
                    foreach (string candidate in candidates)
                        if (UnixTools.IsExecutable(candidate)) return Path.GetFullPath(candidate);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            { }
        }
        return null;
    }

    // Терминалы вроде cmux ставят в начало PATH скрипты-обёртки на сеанс (~/.cmuxterm/cmux-cli-shims/<сеанс>/claude): они
    // добавляют к каждому вызову настройки и хуки терминала и исчезают вместе с сеансом — игре нужен настоящий CLI.
    public static bool IsTerminalShim(string directory) =>
        directory.Replace('\\', '/').Contains("/.cmuxterm/", StringComparison.Ordinal);

    // Глобальный префикс npm без запуска npm: окружение, потом ~/.npmrc.
    [UnsupportedOSPlatform("windows")]
    private static IEnumerable<string> NpmPrefixes(string home)
    {
        foreach (string variable in new[] { "NPM_CONFIG_PREFIX", "npm_config_prefix" })
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } prefix) yield return prefix;
        string[] lines;
        try { lines = File.Exists(Path.Combine(home, ".npmrc")) ? File.ReadAllLines(Path.Combine(home, ".npmrc")) : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { lines = []; }
        foreach (string line in lines)
        {
            string[] pair = line.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim() == "prefix") yield return UnixTools.ExpandHome(pair[1].Trim().Trim('"'));
        }
    }

    // ~/.nvm/versions/node/<версия>/bin, сначала новые.
    private static IEnumerable<string> NvmBinDirectories(string home)
    {
        string root = Path.Combine(Environment.GetEnvironmentVariable("NVM_DIR") is { Length: > 0 } nvm ? nvm : Path.Combine(home, ".nvm"), "versions", "node");
        try
        {
            if (!Directory.Exists(root)) return [];
            return Directory.EnumerateDirectories(root)
                .Select(d => (dir: d, version: Version.TryParse(Path.GetFileName(d).TrimStart('v'), out var v) ? v : new Version()))
                .OrderByDescending(d => d.version).Select(d => Path.Combine(d.dir, "bin")).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    // PATH для дочернего процесса CLI на macOS/Linux (см. UnixTools.ChildPath).
    public static string ChildPath(string executable) => OperatingSystem.IsWindows()
        ? Environment.GetEnvironmentVariable("PATH") ?? "" : UnixTools.ChildPath(executable);
}
