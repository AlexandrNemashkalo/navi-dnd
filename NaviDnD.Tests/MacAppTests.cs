using System.Reflection;

namespace NaviDnD.Tests;

public class MacAppTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(null, false)]
    public void RelaunchRequiresSuccessfulNewBundleLaunch(int? exitCode, bool expected)
    {
        int calls = 0;
        Func<string[], int?> launch = arguments =>
        {
            calls++;
            Assert.Equal(new[] { "-n", "/Applications/Games & fun/NaviDnD.app", "--args", "--navidnd-settings=7" }, arguments);
            Assert.DoesNotContain("--navidnd-terminal", arguments);
            return exitCode;
        };
        var method = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.MacApp")!
            .GetMethod("RequestRelaunch", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool result = (bool)method.Invoke(null, ["/Applications/Games & fun/NaviDnD.app", 7, launch])!;
        Assert.Equal(expected, result);
        Assert.Equal(1, calls);
    }
}
