using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Display;
using NaviDnD.Helpers;

namespace NaviDnD.Tests;

public class TravelPreviewInputTests
{
    [Theory]
    [InlineData(MapLevel.World, true, 0)]
    [InlineData(MapLevel.Location, true, 1)]
    [InlineData(MapLevel.World, false, 0)]
    public void DialogF11OnlyChangesVisibleAdventureScene(MapLevel level, bool mapVisible, int expected)
    {
        int toggles = 0;
        var display = new DisplayConfig
        {
            MapLevel = level, MapHoverEnabled = mapVisible,
            IsAdventureScene = () => level == MapLevel.Location,
            OnSceneVariant = () => toggles++,
        };
        var dialog = new DialogDisplay(new WorldState(), display, null!);
        InputBox.IsFKeyRepeat(ConsoleKey.F2);
        typeof(ConsoleMouseReader).GetMethod("QueueF11", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        var read = typeof(DialogDisplay).GetMethod("ReadDialogKey", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Null(read.Invoke(dialog, null));
        Assert.Equal(expected, toggles);
    }

    [Fact]
    public void SceneInputReceivesF11ForwardedByClassicConsoleHook()
    {
        var display = new DisplayConfig { ArrowKeysMovement = true, MapLevel = MapLevel.Location, IsAdventureScene = () => true };
        var input = new InputBox(new WorldState(), display);
        typeof(ConsoleMouseReader).GetMethod("QueueF11", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        var read = typeof(InputBox).GetMethod("ReadKeyWithPolling", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var key = (ConsoleKeyInfo)read.Invoke(input, null)!;
        Assert.Equal(ConsoleKey.F11, key.Key);
    }
}
