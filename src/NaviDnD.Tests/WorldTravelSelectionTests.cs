using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class WorldTravelSelectionTests
{
    [Fact]
    public void FailedSelectionKeepsExplanationUntilSuccessClearOrLocalEntry()
    {
        string id="route-status-"+Guid.NewGuid().ToString("N");
        var worlds=(Dictionary<string,WorldMap>)typeof(WorldLibrary).GetField("_loaded",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var world=new WorldMap {Id=id,Width=5,Height=3,Biomes=Enumerable.Repeat(".."+WorldBiomes.Ocean+"..",3).ToList(),
            Roads=Enumerable.Repeat("00000",3).ToList(),Rivers=Enumerable.Repeat("00000",3).ToList(),Owners=Enumerable.Repeat(".....",3).ToList(),
            Chronicle=new(){Places=[new(){Name="Island",X=4,Y=1},new(){Name="Nearby",X=1,Y=1},new(){Name="Here",X=0,Y=1}]}};
        worlds[id]=world;
        try
        {
            var storage=new Storage();var state=storage.WorldState;state.Hero=new(){Position=[1,1]};state.Map=new();
            state.World=new(){Id=id,X=0,Y=1,Known=["Island","Nearby","Here"]};
            var display=new DisplayConfig();var visible=GameWorld.ForDisplay(state);
            string before=JsonSerializer.Serialize(state);
            var legend=new LegendDisplay(storage,display,new AppConfig()){WorldLegend=()=> (visible,Enumerable.Range(0,visible.Places.Count).ToList())};
            WorldTravelSelection.Select(state,display,visible,(4,1));
            Assert.True(display.WorldRouteUnavailable);Assert.Null(display.WorldTarget);Assert.Null(display.WorldRouteInfo);
            var rows=Render(legend);
            Assert.Contains(rows,r=>r.Contains(L.T("No walking route from your current location.")));
            Assert.DoesNotContain(rows,r=>r.Contains("F10"));Assert.Equal(33,rows.Count);
            // Showing/redrawing a place card does not clear the selection feedback.
            display.SelectedWorldPlace=0;
            Assert.Contains(Render(legend),r=>r.Contains(L.T("No walking route from your current location.")));
            Assert.DoesNotContain("WorldRouteUnavailable",JsonSerializer.Serialize(display));
            WorldTravelSelection.Select(state,display,visible,(1,1));
            Assert.False(display.WorldRouteUnavailable);Assert.NotNull(display.WorldTarget);Assert.NotNull(display.WorldRoute);
            WorldTravelSelection.Select(state,display,visible,(4,1));
            WorldTravelSelection.Clear(display);
            Assert.False(display.WorldRouteUnavailable);Assert.Null(display.WorldTarget);Assert.Null(display.WorldRoute);Assert.Null(display.WorldRouteInfo);
            WorldTravelSelection.Select(state,display,visible,(4,1));
            WorldTravelSelection.Select(state,display,visible,(0,1));
            Assert.False(display.WorldRouteUnavailable);Assert.Equal((0,1),display.WorldTarget);
            Assert.Contains(Render(legend),r=>r.Contains(L.T("[F10] enter")));
            Assert.Equal(before,JsonSerializer.Serialize(state));
            state.World.Place="Here";
            WorldTravelSelection.Select(state,display,visible,(0,1));
            Assert.False(display.WorldRouteUnavailable);Assert.Null(display.WorldTarget);
        }
        finally {worlds.Remove(id);}
    }

    private static List<string> Render(LegendDisplay legend)
    {
        var lines=new List<Action>();typeof(LegendDisplay).GetMethod("AddWorldLegendLines",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(legend,[lines,100]);
        var result=new List<string>();var old=Console.Out;
        try {foreach(var line in lines){using var writer=new StringWriter();Console.SetOut(writer);line();result.Add(Regex.Replace(writer.ToString(),"\u001b\\[[0-9;]*m",""));}}
        finally {Console.SetOut(old);}
        return result;
    }
}
