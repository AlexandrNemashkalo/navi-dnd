using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class OutdoorNaturalPassTests
{
    [Theory]
    [InlineData("forest", 'F', '"')]
    [InlineData("hills", '^', 'g')]
    [InlineData("mountain", '^', 'g')]
    public void ShortPassCreatesARealDifficultAlternativeWithoutRemovingTheOriginalDetour(string theme, char barrier, char surface)
    {
        var (ground, trail) = Bend(barrier);
        var original = (char[,])ground.Clone();
        var before = Scene(ground);
        int previousCost = Cost(before, (6,6), (10,6));
        var opened = OutdoorGenerator.AddNaturalPass(ground, trail, theme, 17);
        Assert.Equal(3, opened.Count);
        var after = Scene(ground);
        Assert.True(Cost(after, (6,6), (10,6)) < previousCost); // Measured for this fixture, not assumed for every generated pass.
        foreach (var p in opened)
        {
            Assert.Equal(surface, ground[p.c,p.r]);
            Assert.Equal(10, after.Map.TerrainAt(p.c+1,p.r+1)!.StepCostFt);
        }
        // The original ten-step bend and the new four-step crossing are independently walkable.
        foreach (var p in trail) Assert.Equal(original[p.c,p.r], ground[p.c,p.r]);
        for (int c = 6; c < 10; c++) Assert.True(MovementCalculator.CanMoveTo(after,c+1,7,c+2,7,-1));
        Assert.Equal(50, trail.Where(p => p != (6,6)).Sum(p => MovementCalculator.GetStepCost(after,p.c+1,p.r+1)));
        for (int c = 0; c < MapChunk.Cols; c++)
            for (int r = 0; r < MapChunk.Rows; r++)
                if (!opened.Contains((c,r))) Assert.Equal(original[c,r],ground[c,r]);
        var repeat = (char[,])original.Clone();
        Assert.Equal(opened, OutdoorGenerator.AddNaturalPass(repeat,trail,theme,17));
    }

    [Theory]
    [InlineData('o')]
    [InlineData('W')]
    [InlineData('~')]
    public void ProtectedRockOrWaterCannotBeUsedToCompleteTheCrossing(char protectedCell)
    {
        var (ground,trail) = Bend('F');
        ground[8,6] = protectedCell;
        Assert.Empty(OutdoorGenerator.AddNaturalPass(ground,trail,"forest",17));
        Assert.Equal(protectedCell,ground[8,6]);
        Assert.Equal('F',ground[7,6]);
    }

    [Fact]
    public void AlreadyOpenCrossingAndBoundaryBendsAreLeftAlone()
    {
        var (open,trail) = Bend('.');
        Assert.Empty(OutdoorGenerator.AddNaturalPass(open,trail,"forest",17));
        var (nearEdge,edgeTrail) = Bend('F',-4);
        Assert.Empty(OutdoorGenerator.AddNaturalPass(nearEdge,edgeTrail,"forest",17));
        Assert.Equal('F',nearEdge[3,6]);
        var (rare,rareTrail) = Bend('F');
        Assert.Empty(OutdoorGenerator.AddNaturalPass(rare,rareTrail,"forest",1));
        Assert.Empty(OutdoorGenerator.AddNaturalPass(rare,rareTrail,"swamp",17));
    }

    [Theory]
    [InlineData("forest",23,12,8,'F','"')]
    [InlineData("hills",147,8,10,'^','g')]
    public void GeneratedLandmarkBendOffersBothOriginalTrailAndNewCrossing(string theme,int seed,int first,int row,char obstruction,char surface)
    {
        var chunk = new MapChunk { Theme = theme,Seed = seed };
        var result = OutdoorGenerator.Generate(chunk,
            [new() { Side = ChunkSide.Left,Offset = 7 },new() { Side = ChunkSide.Right,Offset = 7 }],
            Enum.GetValues<ChunkSide>().ToDictionary(side => side,_ => 'T'),seed);
        var ground = new char[MapChunk.Cols,MapChunk.Rows];
        for (int c = 0; c < MapChunk.Cols; c++)
            for (int r = 0; r < MapChunk.Rows; r++) ground[c,r] = result.Terrain[r][c];
        var after = Scene(ground);
        for (int c = first; c < first + 3; c++)
        {
            Assert.Equal(surface,ground[c-1,row-1]);
            ground[c-1,row-1] = obstruction;
        }
        var before = Scene(ground);
        Assert.InRange(Cost(before,(first-2,row-1),(first+2,row-1)),1,1000); // Original detour is still available.
        Assert.InRange(Cost(after,(0,6),(19,6)),1,1000); // The location's real approaches remain connected.
        for (int c = first-1; c < first+3; c++)
            Assert.True(MovementCalculator.CanMoveTo(after,c,row,c+1,row,-1));
        Assert.False(MovementCalculator.CanMoveTo(before,first-1,row,first,row,-1));
    }

    private static (char[,] ground,HashSet<(int c,int r)> trail) Bend(char barrier,int shift = 0)
    {
        var ground = new char[MapChunk.Cols,MapChunk.Rows];
        for (int c = 0; c < MapChunk.Cols; c++)
            for (int r = 0; r < MapChunk.Rows; r++) ground[c,r] = 'W';
        var trail = new HashSet<(int c,int r)>();
        for (int r = 3; r <= 6; r++) { trail.Add((6+shift,r)); trail.Add((10+shift,r)); }
        for (int c = 6; c <= 10; c++) trail.Add((c+shift,3));
        foreach (var p in trail) ground[p.c,p.r] = '=';
        for (int c = 7; c <= 9; c++) ground[c+shift,6] = barrier;
        return (ground,trail);
    }

    private static WorldState Scene(char[,] ground)
    {
        var rows = Enumerable.Range(0,MapChunk.Rows).Select(r =>
            new string(Enumerable.Range(0,MapChunk.Cols).Select(c => ground[c,r]).ToArray())).ToList();
        return new WorldState { Map = new() { Cols = 20,Rows = 15,Chunks = [new() { Theme = "forest",Terrain = rows }] } };
    }

    private static int Cost(WorldState world,(int c,int r) start,(int c,int r) goal) =>
        MovementCalculator.Dijkstra(world,start.c+1,start.r+1,1000,-1)
            .Where(p => p.Key.col == goal.c+1 && p.Key.row == goal.r+1).Min(p => p.Value.cost);
}
