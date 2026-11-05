using System.Reflection;

namespace NaviDnD.Tests;

public class MacAppTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalAlwaysLaunchesFullscreenRegardlessOfSavedWindowMode(bool savedFullscreen)
    {
        var method = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.MacApp")!
            .GetMethod("TerminalArguments", BindingFlags.Static | BindingFlags.NonPublic)!;
        var config = new AppConfig { Fullscreen = savedFullscreen };
        var arguments = (List<string>)method.Invoke(null, [config, Array.Empty<string>()])!;
        Assert.Contains("window.startup_mode=\"SimpleFullscreen\"", arguments);
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("window.position="));
    }

    [Theory]
    [InlineData(1440, 900, 2, 32)]
    [InlineData(1280, 800, 2, 0)]
    [InlineData(1920, 1080, 1, 0)]
    [InlineData(1512, 982, 2, 37)]
    public void AutoFontFitsSquareArtworkPixels(double width, double height, double scale, double notch)
    {
        var type = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.MacApp")!;
        var fit = type.GetMethod("Fit", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cell = type.GetMethod("Cell", BindingFlags.Static | BindingFlags.NonPublic)!;
        double size = (double)fit.Invoke(null, [(width, height, scale, notch)])!;
        double pixels = (double)cell.Invoke(null, [size, scale])! * scale;
        Assert.Equal(Math.Floor(pixels), pixels);
        Assert.True(150 * pixels / scale <= width);
        Assert.True(63 * 2 * pixels / scale <= height - notch);
        double next = (double)cell.Invoke(null, [size + 0.5, scale])!;
        Assert.True(150 * next > width
            || 63 * 2 * next > height - notch);
    }

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
