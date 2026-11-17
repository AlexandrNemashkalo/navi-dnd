using System.Collections;
using System.Reflection;
using System.Text.Json;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class JournalMentionedPlacesTests
{
    [Theory]
    [InlineData("rumor")]
    [InlineData("offered")]
    [InlineData("revealed")]
    public void PublicMentionsResolveKnownAliasesWithLazyBoundedReadOnlyRoutes(string status)
    {
        string id="mentioned-"+Guid.NewGuid().ToString("N");
        var worlds=(Dictionary<string,WorldMap>)typeof(WorldLibrary).GetField("_loaded",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var world=new WorldMap {Id=id,Width=8,Height=4,Biomes=Enumerable.Repeat("........",4).ToList(),
            Roads=Enumerable.Repeat("11111111",4).ToList(),Rivers=Enumerable.Repeat("00000000",4).ToList(),Owners=Enumerable.Repeat("........",4).ToList(),
            Chronicle=new(){Places=[new(){Name="Mill",FormerNames=["Waterwheel"],X=2,Y=1},new(){Name="Tower",X=4,Y=2},new(){Name="Harbor",X=6,Y=1},new(){Name="Secret",X=7,Y=3}]}};
        worlds[id]=world;
        try
        {
            var state=new WorldState {World=new(){Id=id,X=0,Y=0,Known=["Mill","Tower","Harbor"]},
                Narrative=new(){PlotThreads=[new(){Name="Clue",Status=status,Description="Waterwheel; Secret",Steps=["Tower; Harbor"],DmNotes="Secret"},
                    new(){Name="Other",Status=status,Description="Millstone; Secret",DmNotes="Tower"},
                    new(){Name="Hidden",Status=status,Hidden=true,Description="Tower"},
                    new(){Name="Done",Status="completed",Description="Tower"}]}};
            GameWorld.ForDisplay(state);
            string before=JsonSerializer.Serialize(state),worldBefore=JsonSerializer.Serialize(world);
            var journal=new JournalDisplay(state,new DisplayConfig());
            var entries=((IEnumerable)typeof(JournalDisplay).GetMethod("BuildEntries",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(journal,[JournalDisplay.Section.Quests])!).Cast<object>().ToList();
            Assert.Equal(3,entries.Count);
            object Entry(string name)=>entries.Single(e=>(string)e.GetType().GetProperty("Title")!.GetValue(e)! == name);
            Lazy<List<(string text,List<int> color)>>? Route(object e)=>(Lazy<List<(string,List<int>)>>?)e.GetType().GetProperty("RouteDetails")!.GetValue(e);
            var route=Route(Entry("Clue"))!;
            Assert.False(route.IsValueCreated);
            Assert.False(Route(Entry("Other"))!.IsValueCreated);
            Assert.Null(Route(Entry("Done")));
            var details=route.Value;
            Assert.Contains(details,d=>d.text==L.T("Mentioned places"));
            Assert.Contains(details,d=>d.text=="Mill");Assert.Contains(details,d=>d.text=="Tower");
            Assert.DoesNotContain(details,d=>d.text is "Harbor" or "Secret");
            Assert.Equal(2,details.Count(d=>d.text.Contains("—") && d.text.Contains('%')));
            Assert.Same(details,route.Value);
            Assert.Empty(Route(Entry("Other"))!.Value);
            Assert.Equal(before,JsonSerializer.Serialize(state));Assert.Equal(worldBefore,JsonSerializer.Serialize(world));
        }
        finally {worlds.Remove(id);}
    }
}
