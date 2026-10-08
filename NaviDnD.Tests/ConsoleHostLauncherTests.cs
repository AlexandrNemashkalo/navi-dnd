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

    [Fact]
    public void RelaunchedGameNeverStartsAnotherHost()
    {
        Assert.False(ConsoleHostLauncher.TryRelaunch([ConsoleHostLauncher.RelaunchFlag]));
    }
}
