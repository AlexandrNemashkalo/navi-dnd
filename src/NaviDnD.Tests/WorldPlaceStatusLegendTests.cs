using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class WorldPlaceStatusLegendTests
{
    [Theory]
    [InlineData(false,"here","You are here")]
    [InlineData(false,"visited","You've been here")]
    [InlineData(false,"rumor","Known by hearsay")]
    [InlineData(true,"visited","You've been here")]
    public void StatusPreservesFixedFooterAndSelectedPlaceUnderOverflow(bool overflow,string stateKind,string label)
    {
        var storage=new Storage();var state=storage.WorldState;
        int count=overflow?60:3;
        var world=new WorldMap {Width=80,Height=1,Biomes=[new string('.',80)],Owners=[new string('.',80)],
            Places=Enumerable.Range(0,count).Select(i=>new WorldPlace{Name=$"Place{i:00}",X=i,Y=0}).ToList()};
        var selected=world.Places[^1];selected.FormerNames=["Old Name"];
        state.World=new(){Place=stateKind=="here"?"OLD NAME":null,Visited=stateKind=="visited"?["old name"]:[]};
        var display=new DisplayConfig {SelectedWorldPlace=count-1,HoveredWorldPlace=-1};
        var legend=new LegendDisplay(storage,display,new AppConfig()){WorldLegend=()=> (world,Enumerable.Range(0,count).ToList())};
        string before=JsonSerializer.Serialize(state);
        var rows=Render(legend,display.GetLegendWidth(20));var plain=Plain(rows);
        Assert.Equal(33,rows.Count);
        Assert.StartsWith(" "+L.T(label),plain[^4]);
        Assert.Contains(selected.Name,plain[^4]);
        Assert.Contains(plain.Take(29),line=>line.Contains(selected.Name) && line.Contains('←'));
        Assert.Equal(before,JsonSerializer.Serialize(state));
        Export(overflow?"overflow":"normal-"+stateKind,rows);
    }

    [Fact]
    public void HoverTakesPriorityButHiddenOrInvalidIndexesNeverExposeNames()
    {
        var storage=new Storage();storage.WorldState.World=new(){Visited=["Visited"]};
        var world=new WorldMap {Width=1,Height=1,Biomes=["."],Owners=["."],Places=[
            new(){Name="Visited"},new(){Name="New place with an extremely long name that exceeds the entire panel width"},new(){Name="SECRET",Hidden=true}]};
        var display=new DisplayConfig {SelectedWorldPlace=0,HoveredWorldPlace=1};
        var legend=new LegendDisplay(storage,display,new AppConfig()){WorldLegend=()=> (world,[0,1,2,999,-1])};
        var rows=Plain(Render(legend,40));
        Assert.StartsWith(" "+L.T("Known by hearsay"),rows[^4]);Assert.DoesNotContain(rows,r=>r.Contains("SECRET"));
        display.HoveredWorldPlace=2;
        Assert.StartsWith(" "+L.T("You've been here"),Plain(Render(legend,40))[^4]);
        display.HoveredWorldPlace=999;display.SelectedWorldPlace=2;
        Assert.True(string.IsNullOrWhiteSpace(Plain(Render(legend,40))[^4]));
    }

    private static List<string> Render(LegendDisplay legend,int width)
    {
        var lines=new List<Action>();
        typeof(LegendDisplay).GetMethod("AddWorldLegendLines",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(legend,[lines,width]);
        var output=new List<string>();var old=Console.Out;
        try {foreach(var line in lines){using var writer=new StringWriter();Console.SetOut(writer);line();output.Add(writer.ToString());}}
        finally {Console.SetOut(old);}
        return output;
    }
    private static List<string> Plain(List<string> rows)=>rows.Select(r=>Regex.Replace(r,"\u001b\\[[0-9;]*m","")).ToList();
    private static void Export(string name,List<string> rows)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!Directory.Exists(Path.Combine(root.FullName,"scripts")))root=root.Parent;
        Assert.NotNull(root);string dir=Path.Combine(root.FullName,"artifacts","world-place-status");Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,name+".json"),JsonSerializer.Serialize(rows));
    }
}
