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

    [Fact]
    public void RelaunchedGameNeverStartsAnotherHost()
    {
        Assert.False(ConsoleHostLauncher.TryRelaunch([ConsoleHostLauncher.RelaunchFlag]));
    }
}
