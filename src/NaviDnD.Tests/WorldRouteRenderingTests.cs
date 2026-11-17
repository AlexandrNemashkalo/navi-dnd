using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.Display;

namespace NaviDnD.Tests;

public class WorldRouteRenderingTests
{
    private static List<(double x,double y)> Smooth(List<(int x,int y)> route) =>
        (List<(double,double)>)typeof(WorldMapView).GetMethod("SmoothRoute",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[route])!;

    [Fact]
    public void SmoothingKeepsEverySegmentInsideTheActualRouteCorridor()
    {
        List<(int,int)>[] routes=[[(0,1),(0,0),(1,0),(2,0),(2,1)],
            [(0,0),(1,0),(2,0),(2,1),(2,2),(3,2),(4,2),(4,3),(4,4)],
            [(0,0),(1,1),(2,1),(3,2),(3,3),(4,4)]];
        foreach(var route in routes)
        {
            var points=Smooth(route);var cells=route.ToHashSet();
            Assert.Equal((route[0].Item1+0.5,route[0].Item2+0.5),points[0]);
            Assert.Equal((route[^1].Item1+0.5,route[^1].Item2+0.5),points[^1]);
            for(int i=1;i<points.Count;i++)
                for(int sample=0;sample<=200;sample++)
                {
                    double t=sample/200.0,x=points[i-1].x+(points[i].x-points[i-1].x)*t,y=points[i-1].y+(points[i].y-points[i-1].y)*t;
                    Assert.True(new[]{-1e-8,1e-8}.Any(dx=>new[]{-1e-8,1e-8}.Any(dy=>cells.Contains(((int)Math.Floor(x+dx),(int)Math.Floor(y+dy))))),$"Off-route point {x},{y}");
                }
        }
        Assert.Equal(2,Smooth([(0,0),(1,0),(2,0),(3,0)]).Count);
    }

    [Fact]
    public void RenderOceanDetourWitness()
    {
        var world=new WorldMap {Width=8,Height=8,Biomes=Enumerable.Repeat("........",8).ToList(),
            Heights=Enumerable.Repeat("22222222",8).ToList(),Roads=Enumerable.Repeat("00000000",8).ToList(),
            Rivers=Enumerable.Repeat("00000000",8).ToList(),Owners=Enumerable.Repeat("........",8).ToList()};
        world.Biomes[3]="..."+WorldBiomes.Ocean+"....";
        var view=new WorldMapView(world,40,20){Hero=(2,3),Route=[(2,3),(2,2),(3,2),(4,2),(4,3)]};
        var rows=Enumerable.Range(0,20).Select(r=>view.RenderRow(r,40,20,3,3,0,-1)).ToList();
        Assert.All(rows,row=>Assert.NotEmpty(row));
        // Same-length alternate detour to the same destination must invalidate the overlay.
        view.Route=[(2,3),(2,4),(3,4),(4,4),(4,3)];
        var changed=Enumerable.Range(0,20).Select(r=>view.RenderRow(r,40,20,3,3,0,-1)).ToList();
        Assert.NotEqual(string.Join("\n",rows),string.Join("\n",changed));
        view.Route=view.Route;
        Assert.Equal(changed,Enumerable.Range(0,20).Select(r=>view.RenderRow(r,40,20,3,3,0,-1)).ToList());
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !Directory.Exists(Path.Combine(root.FullName,"scripts"))) root=root.Parent;
        Assert.NotNull(root);
        string directory=Path.Combine(root.FullName,"artifacts","world-route-review");Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"latest.json"),JsonSerializer.Serialize(rows));
    }
}
