namespace NaviDnD.Helpers;

public static class AiCliDiscovery
{
    public static bool IsAvailable(string name, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath)) return File.Exists(configuredPath);
        string command = Path.GetFileNameWithoutExtension(configuredPath);
        return Find(string.IsNullOrWhiteSpace(command) ? name : command) != null;
    }

    // No CLI invocation or login: discovery only checks known installation directories.
    public static string? Find(string name)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim().Trim('"'))
            .Concat(new[] {
                Path.Combine(home, ".local", "bin"),
                Path.Combine(home, ".cargo", "bin"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm")
            });
        foreach (string directory in directories)
        {
            try
            {
                string executable = Path.GetFullPath(Path.Combine(directory, name + ".exe"));
                if (File.Exists(executable)) return executable;
                string package = Path.Combine(directory, "node_modules",
                    name == "codex" ? "@openai" : "@anthropic-ai",
                    name == "codex" ? "codex" : "claude-code");
                if (Directory.Exists(package))
                {
                    string? native = Directory.EnumerateFiles(package, name + ".exe", SearchOption.AllDirectories)
                        .FirstOrDefault(path => !path.Contains("aarch64", StringComparison.OrdinalIgnoreCase)
                            && !path.Contains("arm64", StringComparison.OrdinalIgnoreCase));
                    if (native != null) return Path.GetFullPath(native);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { /* Unavailable installation: continue with other locations. */ }
        }
        foreach (string editor in new[] { ".vscode", ".vscode-insiders" })
        {
            string extensions = Path.Combine(home, editor, "extensions");
            try
            {
                if (!Directory.Exists(extensions)) continue;
                string pattern = name == "codex" ? "openai.chatgpt-*" : "anthropic.claude-code-*";
                foreach (string extension in Directory.EnumerateDirectories(extensions, pattern)
                             .OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    string executable = name == "codex"
                        ? Path.Combine(extension, "bin", "windows-x86_64", "codex.exe")
                        : Path.Combine(extension, "resources", "native-binary", "claude.exe");
                    if (File.Exists(executable)) return Path.GetFullPath(executable);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { /* The editor may not be installed or accessible. */ }
        }
        return null;
    }
}
