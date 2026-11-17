using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class VillageForestEdgeTests
{
    [Fact]
    public void SmallForestGroupsKeepBoundariesRoutesWaterAndDryComponents()
    {
        int witnesses=0;
        for(int seed=1;seed<=40;seed++)
        {
            var grid=new char[20,15];
            for(int c=0;c<20;c++)for(int r=0;r<15;r++) grid[c,r]=c==0||c==19||r==0||r==14?'T':'.';
            for(int c=0;c<20;c++) grid[c,7]='=';
            for(int r=1;r<14;r++) grid[9,r]=r==7?'=':'W';
            grid[2,3]='#';grid[3,3]='G';grid[4,3]='#';
            HashSet<(int c,int r)> protect=[(16,3),(16,4),(17,3),(17,4)];
            var before=(char[,])grid.Clone();var oldParts=Components(before,protect);
            var added=BuildingGenerator.SoftenVillageForestEdge(grid,protect,seed);
            var repeat=(char[,])before.Clone();
            Assert.Equal(added,BuildingGenerator.SoftenVillageForestEdge(repeat,protect,seed));
            Assert.InRange(added.Count,0,10); if(added.Count>0)witnesses++;
            foreach(var p in added)
            {
                Assert.Equal('.',before[p.c,p.r]);Assert.Equal('T',grid[p.c,p.r]);
                Assert.InRange(Math.Min(Math.Min(p.c,19-p.c),Math.Min(p.r,14-p.r)),1,2);
                Assert.DoesNotContain(protect,q=>Math.Abs(q.c-p.c)<=1&&Math.Abs(q.r-p.r)<=1);
                for(int dc=-1;dc<=1;dc++)for(int dr=-1;dr<=1;dr++)
                    Assert.DoesNotContain(before[p.c+dc,p.r+dr],new[]{'=','W','#','G'});
            }
            for(int c=0;c<20;c++)for(int r=0;r<15;r++)
                if(!added.Contains((c,r)))Assert.Equal(before[c,r],grid[c,r]);
            var afterParts=Components(grid,protect);
            foreach(var old in oldParts)
            {
                var remaining=old.Except(added).ToList();
                if(remaining.Count>0) Assert.Contains(afterParts,part=>remaining.All(part.Contains));
            }
        }
        Assert.True(witnesses>20);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NarrowDryNeckIsKeptEvenWhenShallowsWouldOfferAnotherWay(bool shallowDetour)
    {
        for(int seed=1;seed<=20;seed++)
        {
            var grid=new char[20,15];for(int c=0;c<20;c++)for(int r=0;r<15;r++)grid[c,r]='T';
            // Every otherwise eligible lobe spans both rows and would sever this dry strip.
            for(int c=1;c<19;c++){grid[c,1]='.';grid[c,2]='.';if(shallowDetour)grid[c,3]='~';}
            var before=(char[,])grid.Clone();
            Assert.Empty(BuildingGenerator.SoftenVillageForestEdge(grid,[(1,1),(18,1)],seed));
            for(int c=0;c<20;c++)for(int r=0;r<15;r++)Assert.Equal(before[c,r],grid[c,r]);
            Assert.Single(Components(grid,[]));
        }
    }

    [Fact]
    public void NonForestBoundaryDoesNotGrowTrees()
    {
        var grid=new char[20,15];for(int c=0;c<20;c++)for(int r=0;r<15;r++)grid[c,r]='.';
        Assert.Empty(BuildingGenerator.SoftenVillageForestEdge(grid,[],3));
    }

    private static List<HashSet<(int c,int r)>> Components(char[,] grid,HashSet<(int c,int r)> protect)
    {
        var remaining=Enumerable.Range(0,20).SelectMany(c=>Enumerable.Range(0,15).Select(r=>(c,r)))
            .Where(p=>!protect.Contains(p)&&grid[p.c,p.r] is not ('W' or '~')&&TerrainCatalog.Get(grid[p.c,p.r]) is {BlocksMove:false}).ToHashSet();
        var parts=new List<HashSet<(int c,int r)>>();
        while(remaining.Count>0)
        {
            var start=remaining.First();remaining.Remove(start);var part=new HashSet<(int c,int r)>{start};var queue=new Queue<(int c,int r)>(part);
            while(queue.TryDequeue(out var p))foreach(var n in new[]{(p.c-1,p.r),(p.c+1,p.r),(p.c,p.r-1),(p.c,p.r+1)})
                if(remaining.Remove(n)){part.Add(n);queue.Enqueue(n);}
            parts.Add(part);
        }
        return parts;
    }
}
