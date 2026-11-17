using System.Collections;
using System.Reflection;
using System.Text.Json;
using NaviDnD.Data;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class JournalAttitudeTests
{
    [Theory]
    [InlineData("friendly", "Friendly", "Дружелюбное")]
    [InlineData("neutral", "Neutral", "Нейтральное")]
    [InlineData("hostile", "Hostile", "Враждебное")]
    [InlineData("unknown", "Unknown", "Неизвестно")]
    [InlineData("Cautious but grateful", "Cautious but grateful", "Cautious but grateful")]
    public void KnownAttitudesFollowInterfaceLanguageAndCustomDescriptionsRemainIntact(string value,string english,string russian)
    {
        var storage = new Storage();
        storage.WorldState.Narrative = new() { Npcs = [new() { Name = "Oren", Met = true, Attitude = value }] };
        string before = JsonSerializer.Serialize(storage.WorldState);
        try
        {
            foreach (var (language,expected) in new[] { (L.English,english),(L.Russian,russian) })
            {
                L.SetLanguage(language);
                var display = new JournalDisplay(storage.WorldState,new DisplayConfig());
                var entries = (IEnumerable)typeof(JournalDisplay).GetMethod("BuildEntries",BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(display,[JournalDisplay.Section.People])!;
                var entry = Assert.Single(entries.Cast<object>());
                var details = (List<(string text,List<int> color)>)entry.GetType().GetProperty("Details")!.GetValue(entry)!;
                Assert.Contains(details,line => line.text == L.T("Attitude: ") + expected);
            }
        }
        finally { L.SetLanguage(L.Russian); }
        Assert.Equal(before,JsonSerializer.Serialize(storage.WorldState));
    }
}
