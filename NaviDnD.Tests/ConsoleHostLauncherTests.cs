using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class ConsoleHostLauncherTests
{
    [Fact]
    public void RelaunchPreservesPathsAndArgumentsWithoutShellInterpolation()
    {
        var start = ConsoleHostLauncher.CreateStartInfo(@"C:\Games with spaces\NaviDnD.exe",
            ["a b", "value&literal", ConsoleHostLauncher.RelaunchFlag], @"C:\Games with spaces");
        Assert.EndsWith("conhost.exe", start.FileName);
        Assert.Equal(@"C:\Games with spaces", start.WorkingDirectory);
        Assert.Equal(new[] { @"C:\Games with spaces\NaviDnD.exe", "a b", "value&literal", ConsoleHostLauncher.RelaunchFlag }, start.ArgumentList);
    }

    [Fact]
    public void DotnetLaunchPreservesEntryAssemblyBeforeGameArguments()
    {
        var start = ConsoleHostLauncher.CreateStartInfo("dotnet.exe", ["arg"], "work", "NaviDnD.dll");
        Assert.Equal(new[] { "dotnet.exe", "NaviDnD.dll", "arg", ConsoleHostLauncher.RelaunchFlag }, start.ArgumentList);
    }

    // Временный выбор классической консоли возвращает прежние настройки игрока как были (в том числе «не задано»).
    [Fact]
    public void ForcedConhostDelegationIsRestored()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string path = @"Console\%%Startup";
        object? Read(string name)
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name);
        }
        var before = (Read("DelegationConsole"), Read("DelegationTerminal"));
        var saved = ConsoleHostLauncher.ForceConhostDelegation();
        try
        {
            Assert.Equal(ConsoleHostLauncher.ConhostDelegation, Read("DelegationConsole"));
            Assert.Equal(ConsoleHostLauncher.ConhostDelegation, Read("DelegationTerminal"));
        }
        finally { ConsoleHostLauncher.RestoreDelegation(saved); }
        Assert.Equal(before, (Read("DelegationConsole"), Read("DelegationTerminal")));
    }

    // Перезапущенная игра возвращает настройки из файла, оставленного первым запуском, и удаляет файл.
    [Fact]
    public void PendingRestoreReturnsSettingsAndRemovesFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string original = ConsoleHostLauncher.RestorePath;
        ConsoleHostLauncher.RestorePath = Path.Combine(Path.GetTempPath(), $"navidnd-restore-test-{Guid.NewGuid():N}.json");
        try
        {
            object? Read(string name)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Console\%%Startup");
                return key?.GetValue(name);
            }
            var before = (Read("DelegationConsole"), Read("DelegationTerminal"));
            var saved = ConsoleHostLauncher.ForceConhostDelegation();
            File.WriteAllText(ConsoleHostLauncher.RestorePath, System.Text.Json.JsonSerializer.Serialize(saved));
            ConsoleHostLauncher.RestorePending();
            Assert.Equal(before, (Read("DelegationConsole"), Read("DelegationTerminal")));
            Assert.False(File.Exists(ConsoleHostLauncher.RestorePath));
        }
        finally { ConsoleHostLauncher.RestorePath = original; }
    }

    [Fact]
    public void RelaunchedGameNeverStartsAnotherHost()
    {
        Assert.False(ConsoleHostLauncher.TryRelaunch([ConsoleHostLauncher.RelaunchFlag]));
    }
}
