using System.Collections;
using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class SettingsConnectionTests
{
    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void ConnectionActionsFitTheSettingsScreenAndAdvancedFieldsStayHidden(string provider)
    {
        var config = new AppConfig { AiProvider = provider };
        var screen = new SettingsDisplay(new WorldState(), new DisplayConfig(), config, new Storage(), () => "Settings");
        var type = typeof(SettingsDisplay);
        type.GetMethod("BuildItems", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(screen, null);
        var items = ((IEnumerable)type.GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(screen)!).Cast<object>().ToList();
        object Field(object item, string key) => item.GetType().GetField(key)!.GetValue(item)!;
        bool Visible(object item) => ((Func<bool>)Field(item, "Visible"))();
        var shown = items.Where(Visible).ToList();
        Assert.Equal(provider == "claude", shown.Any(i => (string)Field(i, "Label") == L.T("CLAUDE TOKEN")));
        Assert.DoesNotContain(shown, i => (string)Field(i, "Label") == L.T("CODEX PATH"));
        Assert.DoesNotContain(shown, i => (string)Field(i, "Label") == L.T("CLAUDE PATH"));
        var actions = shown.Where(i => Field(i, "Kind").ToString() == "Button").ToList();
        Assert.Single(actions);
        Assert.Contains(shown, i => (string)Field(i, "Label") == L.T("ACCOUNT"));
        int rows = shown.Count + shown.Count(i => Field(i, "Group") != null) * 2 + 4;
        Assert.True(rows <= 57, $"Settings require {rows} rows.");
    }
}
