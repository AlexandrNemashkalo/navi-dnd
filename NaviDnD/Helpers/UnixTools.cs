using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace NaviDnD.Helpers;

// Command-line tools the game calls outside Windows (pbpaste, osascript, zenity, python3, the AI CLIs). A game started
// from a bare terminal or a launcher may lack Homebrew on PATH, so the usual install folders are searched as well.
[UnsupportedOSPlatform("windows")]
internal static class UnixTools
{
    internal static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // PATH first (the user's choice), then the standard system and package-manager folders.
    internal static IEnumerable<string> SearchDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/snap/bin"])
            .Distinct(StringComparer.Ordinal);

    // "~/x" → the home folder; other paths unchanged.
    internal static string ExpandHome(string path) =>
        path == "~" ? Home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(Home, path[2..]) : path;

    internal static bool IsExecutable(string path)
    {
        try
        {
            return File.Exists(path)
                && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    // Full path of an executable by name, or null. A rooted name is checked as is.
    internal static string? Which(string name, IEnumerable<string>? extraDirectories = null)
    {
        name = ExpandHome(name.Trim().Trim('"'));
        if (name.Length == 0) return null;
        if (Path.IsPathRooted(name)) return IsExecutable(name) ? name : null;
        if (name.Contains('/')) return null;
        foreach (string directory in SearchDirectories().Concat(extraDirectories ?? []))
        {
            if (!Path.IsPathRooted(directory)) continue;
            string candidate = Path.Combine(directory, name);
            if (IsExecutable(candidate)) return candidate;
        }
        return null;
    }

    // PATH for a child process: the executable's own folder first (an npm or nvm launcher finds its `node` there), then the
    // current PATH and the standard folders.
    internal static string ChildPath(string? executable)
    {
        var directories = new List<string>();
        if (!string.IsNullOrEmpty(executable) && Path.IsPathRooted(executable) && Path.GetDirectoryName(executable) is { Length: > 0 } own)
            directories.Add(own);
        directories.AddRange(SearchDirectories());
        directories.Add(Path.Combine(Home, ".local", "bin"));
        return string.Join(Path.PathSeparator, directories.Distinct(StringComparer.Ordinal));
    }

    // Runs a tool with no terminal access (stdin closed, output captured). Null — it did not start or did not finish in time.
    internal static (int ExitCode, string Output)? Run(string file, IEnumerable<string> arguments, TimeSpan? timeout,
        Action<ProcessStartInfo>? configure = null)
    {
        try
        {
            var start = new ProcessStartInfo(file)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            configure?.Invoke(start);
            using var process = Process.Start(start);
            if (process == null) return null;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeout ?? Timeout.InfiniteTimeSpan))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            process.WaitForExit();
            Task.WaitAll([output, errors], TimeSpan.FromSeconds(2));
            return (process.ExitCode, output.IsCompletedSuccessfully ? output.Result : "");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { return null; }
    }
}
