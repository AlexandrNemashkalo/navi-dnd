using System.Text.RegularExpressions;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class MapFixturesRenderingTests
{
    [Theory]
    [InlineData('T')]
    [InlineData('F')]
    public void ForestContinuesBeyondLocationWithoutClosingItsOuterCrown(char tree)
    {
        var storage=Fixture();
        var rows=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
        var top=rows[^1].ToCharArray(); top[4]=tree; rows[^1]=new string(top);
        storage.WorldState.Map.Chunks[0].Terrain=rows;
        var p=Provider(storage);
        Assert.Equal("   ",Plain(Capture(()=>p.WriteLine(5,15.5,true))));
        Assert.Contains(Plain(Capture(()=>p.WriteLine(4.5,15,false))), new[]{"\u2590","\u259d"});
        Assert.Equal("\u259c",Plain(Capture(()=>p.WriteCorner(4.5,15.5,CornerType.TopMiddle))));
        Assert.All(Plain(Capture(()=>p.WriteLine(5,14.5,true))), c=>Assert.Contains(c,"\u2580\u259b\u259c\u2588"));
    }
    [Theory]
    [InlineData('T')]
    [InlineData('F')]
    public void CrownCornerMatchesEveryAdjacentEdgeIncludingDiagonalTrees(char tree)
    {
        string[] expected = ["+","\u2598","\u259d","\u2580","\u2596","\u258c","\u259e","\u259b","\u2597","\u259a","\u2590","\u259c","\u2584","\u2599","\u259f","\u2588"];
        for(int mask=0;mask<16;mask++)
        {
            var storage=Fixture();
            var grid=Enumerable.Range(0,MapChunk.Rows).Select(_=>new string('.',MapChunk.Cols).ToCharArray()).ToArray();
            var cells=new[]{(3,4),(4,4),(3,3),(4,3)};
            for(int i=0;i<4;i++) if((mask&(1<<i))!=0) grid[cells[i].Item2-1][cells[i].Item1-1]=tree;
            storage.WorldState.Map.Chunks[0].Terrain=grid.Select(r=>new string(r)).ToList();
            Assert.Equal(expected[mask],Plain(Capture(()=>Provider(storage).WriteCorner(3.5,3.5,CornerType.Cross))));
        }
    }

    [Fact]
    public void KnownCrownHasIdenticalAnsiAgainstUnknownTreeThicketOrGrass()
    {
        string Draw(char hidden)
        {
            var storage=Fixture(); storage.WorldState.Hero.VisionFt=0;
            var rows=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
            var row=rows[2].ToCharArray(); row[2]='T'; row[3]=hidden; rows[2]=new string(row);
            storage.WorldState.Map.Chunks[0].Terrain=rows;
            var p=new MapObjectsProvider(storage.WorldState,new DisplayConfig(),storage,new AppConfig());
            storage.VisibleCells.Clear(); storage.ExploredCells.Clear(); storage.VisibleCells.Add((3,3));
            return Capture(()=>{
                NaviDnD.Helpers.ColorHelper.SetForegroundColor([200,200,200]);
                NaviDnD.Helpers.ColorHelper.SetBackgroundColor([0,0,0]);
                p.WriteLine(3.5,3,false); p.WriteCorner(3.5,3.5,CornerType.Cross);
            });
        }
        Assert.Equal(Draw('.'),Draw('T')); Assert.Equal(Draw('.'),Draw('F'));
        Assert.DoesNotContain("\u2502", Plain(Draw('.')));
    }
    [Fact]
    public void CrownCornerDoesNotReplaceVisibleGroundWithFogFromAnotherQuadrant()
    {
        var storage=Fixture(); storage.WorldState.Hero.VisionFt=0;
        var rows=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
        rows[2]="..T.................";
        storage.WorldState.Map.Chunks[0].Terrain=rows;
        var p=new MapObjectsProvider(storage.WorldState,new DisplayConfig(),storage,new AppConfig());
        storage.VisibleCells.Clear(); storage.ExploredCells.Clear();
        storage.VisibleCells.UnionWith([(3,3),(4,3)]);
        string Draw()=>Capture(()=>{
            NaviDnD.Helpers.ColorHelper.SetForegroundColor([200,200,200]);
            NaviDnD.Helpers.ColorHelper.SetBackgroundColor([0,0,0]);
            p.WriteCorner(3.5,3.5,CornerType.Cross);
        });
        string partial=Draw();
        storage.VisibleCells.UnionWith([(3,4),(4,4)]);
        Assert.Equal(Draw(),partial);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FloorFogShadesSeamsWithoutChangingVisibilityOrRevealingTerrain(bool indoors, bool furniture)
    {
        string Draw(char hidden)
        {
            var storage=Fixture(); storage.WorldState.Hero.VisionFt=0;
            var rows=Enumerable.Repeat(new string(indoors?'w':'.',MapChunk.Cols),MapChunk.Rows).ToList();
            var row=rows[3].ToCharArray(); row[2]=hidden; rows[3]=new string(row);
            storage.WorldState.Map.Chunks[0].Terrain=rows;
            if(indoors)
            {
                storage.WorldState.Hero.Position=[3,3];
                storage.WorldState.Map.Chunks[0].Theme=MapChunk.Building;
                storage.WorldState.Map.Rooms=[new(){Positions=[ [2,2],[3,2],[4,2],[5,2], [2,3],[3,3],[4,3],[5,3], [2,4],[3,4],[4,4],[5,4], [2,5],[3,5],[4,5],[5,5] ],Color=[74,60,46]}];
                if(furniture) storage.WorldState.Map.Furniture=[new(){Kind="bench",Positions=[[3,3]]}];
            }
            var p=new MapObjectsProvider(storage.WorldState,new DisplayConfig(),storage,new AppConfig());
            storage.VisibleCells.Clear(); storage.ExploredCells.Clear(); storage.VisibleCells.Add((3,3));
            string result=Capture(()=>{
                NaviDnD.Helpers.ColorHelper.SetForegroundColor([200,200,200]);
                NaviDnD.Helpers.ColorHelper.SetBackgroundColor([0,0,0]);
                p.WriteLine(3,3.5,true); p.WriteCorner(3.5,3.5,CornerType.Cross);
            });
            Assert.Equal("    ",Plain(result));
            Assert.Single(storage.VisibleCells); Assert.Empty(storage.ExploredCells);
            // A three-character seam has distinct corner and centre shades.
            Assert.True(Regex.Matches(result,@"\x1b\[48;2;").Count>=3);
            return result;
        }
        Assert.Equal(Draw('.'),Draw('T'));
        Assert.Equal(Draw('.'),Draw('F'));
    }

    [Fact]
    public void TreeLobesVaryButKeepConnectedEdgesAndTheirShapeInFog()
    {
        var shapes=new HashSet<string>();
        for(int x=3;x<=15;x++)
        {
            var storage=Fixture(); storage.WorldState.Hero.VisionFt=0;
            var rows=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
            var row=rows[3].ToCharArray(); row[x-1]='T'; rows[3]=new string(row);
            storage.WorldState.Map.Chunks[0].Terrain=rows;
            var p=new MapObjectsProvider(storage.WorldState,new DisplayConfig(),storage,new AppConfig());
            storage.VisibleCells.Clear(); storage.ExploredCells.Clear();
            for(int a=x-1;a<=x+1;a++) for(int b=3;b<=5;b++) storage.ExploredCells.Add((a,b));
            string Draw()=>Plain(Capture(()=>{p.WriteLine(x,4.5,true);p.WriteLine(x-.5,4,false);p.WriteLine(x+.5,4,false);}));
            string fog=Draw(); shapes.Add(fog);
            Assert.All(fog[..3],c=>Assert.Contains(c,"▄▟▙█"));
            int raised=fog[..3].Count(c=>c!='▄');
            Assert.True(raised==0 || raised>=2);
            storage.VisibleCells.UnionWith(storage.ExploredCells);
            Assert.Equal(fog,Draw());
        }
        Assert.True(shapes.Count>=3);
        Assert.Contains(shapes,s=>s[3]=='█');
        Assert.Contains(shapes,s=>s[4]=='█');
        Assert.Contains(shapes,s=>s[3]=='▐');
        Assert.Contains(shapes,s=>s[4]=='▌');
    }

    [Fact]
    public void ExploredCrownKeepsItsShapeWhenVisibilityChanges()
    {
        var storage=Fixture(); storage.WorldState.Hero.VisionFt=0;
        var rows=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
        rows[2]="..TF................";
        storage.WorldState.Map.Chunks[0].Terrain=rows;
        var p=new MapObjectsProvider(storage.WorldState,new DisplayConfig(),storage,new AppConfig());
        storage.VisibleCells.Clear(); storage.ExploredCells.Clear();
        for(int x=2;x<=5;x++) for(int y=2;y<=4;y++) storage.ExploredCells.Add((x,y));
        string Draw() => Capture(()=>{
            NaviDnD.Helpers.ColorHelper.SetForegroundColor([200,200,200]);
            NaviDnD.Helpers.ColorHelper.SetBackgroundColor([0,0,0]);
            p.WriteLine(2.5,3,false); p.WriteLine(3,2.5,true);
            p.WriteCorner(2.5,2.5,CornerType.Cross);
            p.WriteLine(3.5,3,false); p.WriteCorner(3.5,2.5,CornerType.Cross);
        });
        string fog=Draw();
        storage.VisibleCells.UnionWith(storage.ExploredCells);
        string visible=Draw();
        Assert.Equal(Plain(visible),Plain(fog));
        Assert.NotEqual(visible,fog);
        storage.VisibleCells.Clear(); storage.VisibleCells.Add((3,3));
        Assert.Equal(Plain(visible),Plain(Draw()));
    }

    [Fact]
    public void TreeCrownsJoinWithoutInternalGridAndYieldToWalls()
    {
        var storage = Fixture();
        var rows = Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
        rows[2] = "..TT................";
        storage.WorldState.Map.Chunks[0].Terrain = rows;
        var p = Provider(storage);
        Assert.Equal(" ", Plain(Capture(() => p.WriteLine(3.5,3,false))));
        Assert.All(Plain(Capture(() => p.WriteLine(3,3.5,true))), c=>Assert.Contains(c,"\u2584\u2599\u259f\u2588"));
        Assert.Equal("\u2597", Plain(Capture(() => p.WriteCorner(2.5,3.5,CornerType.Cross))));
        Assert.Equal("\u2584", Plain(Capture(() => p.WriteCorner(3.5,3.5,CornerType.Cross))));
        rows[3] = "..T.................";
        Assert.Equal("\u2599", Plain(Capture(() => Provider(storage).WriteCorner(3.5,3.5,CornerType.Cross))));
        storage.WorldState.Map.Rooms = [new() { Positions = [[2,3]], Color = [74,60,46] }];
        Assert.Equal("║", Plain(Capture(() => Provider(storage).WriteLine(2.5,3,false))));
    }

    [Fact]
    public void UnknownTreeNeverAddsACrownBesideVisibleGrass()
    {
        string Render(char hidden)
        {
            var storage = Fixture();
            storage.WorldState.Hero.VisionFt = 0;
            var rows = Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
            var row = rows[2].ToCharArray(); row[3] = hidden; rows[2] = new string(row);
            storage.WorldState.Map.Chunks[0].Terrain = rows;
            var p = new MapObjectsProvider(storage.WorldState,new DisplayConfig(),storage,new AppConfig());
            storage.VisibleCells.Clear(); storage.ExploredCells.Clear();
            storage.VisibleCells.Add((3,3)); storage.ExploredCells.Add((3,3));
            return Plain(Capture(() => { p.WriteLine(3.5,3,false); p.WriteCorner(3.5,3.5,CornerType.Cross); }));
        }
        Assert.Equal(Render('.'),Render('T'));
    }
    [Fact]
    public void ActualWallCornerIgnoresPerpendicularCellGrid()
    {
        var storage = Fixture();
        storage.WorldState.Map.Rooms = [new() { Positions = [[3,3],[4,3]], Color = [74,60,46] }];
        Assert.Equal("═", Plain(Capture(() => Provider(storage).WriteCorner(3.5,3.5,CornerType.Cross))));
        var symbols = new WallGridSymbolProvider();
        Assert.Equal('╬', symbols.GetCorner("2222"));
        Assert.Equal('╔', symbols.GetCorner("0220"));
        Assert.Equal('║', symbols.GetCorner("2121"));
    }

    [Theory]
    [InlineData('F', false, "●")]
    [InlineData('.', false, "\u258c")]
    [InlineData(' ', false, " ")]
    [InlineData('F', true, "║")]
    public void ThicketFillsOnlySharedOutdoorEdges(char neighbor, bool door, string expected)
    {
        var storage = Fixture();
        var rows = Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList();
        var row = rows[2].ToCharArray(); row[2] = 'F'; row[3] = neighbor; rows[2] = new string(row);
        storage.WorldState.Map.Chunks[0].Terrain = rows;
        if (door) storage.WorldState.Map.Doors = [new() { From = [3,3], To = [4,3], IsDoor = true }];
        Assert.Equal(expected, Plain(Capture(() => Provider(storage).WriteLine(3.5,3,false))));
    }

    [Fact]
    public void ThicketConnectorsRespectFogAndRooms()
    {
        var storage = Fixture();
        storage.WorldState.Map.Chunks[0].Terrain = Enumerable.Repeat(new string('F',MapChunk.Cols),MapChunk.Rows).ToList();
        storage.WorldState.Hero.VisionFt = 0;
        var provider = new MapObjectsProvider(storage.WorldState, new DisplayConfig(), storage, new AppConfig());
        storage.VisibleCells.Clear(); storage.ExploredCells.Clear();
        Assert.Equal(" ", Plain(Capture(() => provider.WriteLine(3.5,3,false))));
        storage.VisibleCells.Add((3,3)); storage.ExploredCells.Add((3,3));
        Assert.Contains(Plain(Capture(() => provider.WriteLine(3.5,3,false))),new[]{"\u258c","\u2596"});
        storage.VisibleCells.Clear();
        storage.ExploredCells.UnionWith([(3,3),(4,3)]);
        string fog = Capture(() => provider.WriteLine(3.5,3,false));
        Assert.Equal("●", Plain(fog));
        storage.VisibleCells.UnionWith([(3,3),(4,3)]);
        Assert.NotEqual(fog,Capture(() => provider.WriteLine(3.5,3,false)));
        storage.WorldState.Map.Rooms = [new() { Positions = [[3,3],[4,3]], Color = [74,60,46] }];
        Assert.Equal(" ", Plain(Capture(() => Provider(storage).WriteLine(3.5,3,false))));
    }

    [Theory]
    [InlineData(true, false, "═══")]
    [InlineData(true, true, "═══")]
    [InlineData(false, false, "║")]
    [InlineData(false, true, "║")]
    public void WindowsAreContinuousBluePanes(bool horizontal, bool open, string expected)
    {
        var storage = Fixture();
        storage.WorldState.Map.Doors = [new() { From = [3, 3], To = horizontal ? [3, 4] : [4, 3], IsWindow = true, IsDoor = true, IsDoorOpen = open }];
        var provider = Provider(storage);
        string ansi = Capture(() => provider.WriteLine(horizontal ? 3 : 3.5, horizontal ? 3.5 : 3, horizontal));
        Assert.Equal(expected, Plain(ansi));
        Assert.Contains("38;2;150;196;226", ansi);
    }

    [Theory]
    [InlineData(false, "═══")]
    [InlineData(true, "═  ")]
    public void HorizontalDoorStaysOnTheWallAxis(bool open, string expected)
    {
        var storage = Fixture();
        storage.WorldState.Map.Doors = [new() { From = [3, 3], To = [3, 4], IsDoor = true, IsDoorOpen = open }];
        Assert.Equal(expected, Plain(Capture(() => Provider(storage).WriteLine(3, 3.5, true))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PairedDoorsKeepTheSameStrokeWeightAndTheirOuterHinges(bool open)
    {
        var storage = Fixture();
        storage.WorldState.Map.Doors = [
            new() { From = [3,3], To = [4,3], IsDoor = true, IsDoorOpen = open },
            new() { From = [3,4], To = [4,4], IsDoor = true, IsDoorOpen = open },
            new() { From = [7,3], To = [7,4], IsDoor = true, IsDoorOpen = open },
            new() { From = [8,3], To = [8,4], IsDoor = true, IsDoorOpen = open }
        ];
        var p = Provider(storage);
        Assert.Equal(open ? "╥" : "║", Plain(Capture(() => p.WriteLine(3.5,3,false))));
        Assert.Equal(open ? "╨" : "║", Plain(Capture(() => p.WriteLine(3.5,4,false))));
        Assert.Equal(open ? " " : "║", Plain(Capture(() => p.WriteCorner(3.5,3.5,CornerType.Cross))));
        Assert.Equal(open ? "═  " : "═══", Plain(Capture(() => p.WriteLine(7,3.5,true))));
        Assert.Equal(open ? "  ═" : "═══", Plain(Capture(() => p.WriteLine(8,3.5,true))));
        Assert.Equal(open ? " " : "═", Plain(Capture(() => p.WriteCorner(7.5,3.5,CornerType.Cross))));
    }

    [Theory]
    [InlineData("bench", "└─────┘", "─")]
    [InlineData("counter", "╘═════╛", "═")]
    [InlineData("stall", "▐▀▀▀▀▀▌", "▀")]
    public void JoinedFurnitureHasEndSupportsAndAnUnbrokenSeat(string kind, string expected, string joint)
    {
        var storage = Fixture();
        storage.WorldState.Map.Furniture = [new() { Kind = kind, Positions = [[3, 3], [4, 3]] }];
        var provider = Provider(storage);
        string row = Capture(() => { provider.WriteCellEntity(3, 3); provider.WriteLine(3.5, 3, false); provider.WriteCellEntity(4, 3); });
        Assert.Equal(expected, Plain(row));
        Assert.Equal(joint, Plain(Capture(() => provider.WriteLine(3.5, 3, false))));
        storage.WorldState.Map.Furniture = [new() { Kind = kind, Positions = [[3, 3], [3, 4]] }];
        provider.Refresh();
        Assert.Equal(kind == "counter" ? "╙─╜" : kind == "bench" ? " ┴ " : "▐█▌", Plain(Capture(() => provider.WriteCellEntity(3,3))));
        Assert.Equal(kind == "counter" ? "║ ║" : kind == "bench" ? " │ " : "▐█▌", Plain(Capture(() => provider.WriteLine(3,3.5,true))));
        if (kind == "counter") Assert.Equal("╓─╖", Plain(Capture(() => provider.WriteCellEntity(3,4))));
        if (kind == "bench") Assert.Equal(" ┬ ", Plain(Capture(() => provider.WriteCellEntity(3,4))));
        // Adjacent independent pieces retain their own ends and never acquire a shared beam.
        storage.WorldState.Map.Furniture = [new() { Kind = kind, Positions = [[3, 3]] }, new() { Kind = kind, Positions = [[4, 3]] }];
        provider.Refresh();
        Assert.NotEqual(joint, Plain(Capture(() => provider.WriteLine(3.5, 3, false))));
    }

    [Fact]
    public void BridgeRailsSitBesideTheDeckAndYieldToDoors()
    {
        var storage = Fixture();
        storage.WorldState.Map.Furniture = [new() { Kind = "bridge", Positions = [[3,3],[4,3],[5,3]] }];
        var provider = Provider(storage);
        Assert.Equal("   ", Plain(Capture(() => provider.WriteCellEntity(4,3))));
        Assert.Equal("───", Plain(Capture(() => provider.WriteLine(4,3.5,true))));
        Assert.Equal("───", Plain(Capture(() => provider.WriteLine(4,2.5,true))));
        storage.WorldState.Map.Doors = [new() { From = [4,3], To = [4,4], IsDoor = true, IsDoorOpen = false }];
        provider = Provider(storage);
        Assert.Equal("═══", Plain(Capture(() => provider.WriteLine(4,3.5,true))));
        storage.WorldState.Map.Doors.Clear();
        storage.WorldState.Map.Furniture = [new() { Kind = "bridge", Positions = [[3,3],[3,4],[3,5]] }];
        provider.Refresh();
        Assert.Equal("   ", Plain(Capture(() => provider.WriteCellEntity(3,4))));
        Assert.Equal("│", Plain(Capture(() => provider.WriteLine(2.5,4,false))));
        Assert.Equal("│", Plain(Capture(() => provider.WriteLine(3.5,4,false))));
    }

    [Fact]
    public void BridgeHoverHighlightsOnlyTheThreeDeckCharacters()
    {
        var storage = Fixture();
        storage.WorldState.Map.Furniture = [new() { Kind = "bridge", Positions = [[3,3],[4,3],[5,3]] }];
        var display = new DisplayConfig();
        var provider = new MapObjectsProvider(storage.WorldState, display, storage, new AppConfig { RevealMap = true });
        string deck = Capture(() => provider.WriteCellEntity(4,3));
        string rail = Capture(() => provider.WriteLine(4,3.5,true));
        Assert.Equal("   ", Plain(deck));
        Assert.True(Regex.Matches(deck, "\\x1b\\[48;2;[0-9;]+m").Select(m => m.Value).Distinct().Count() > 1);
        display.HoveredCell = (4,3);
        Assert.NotEqual(deck, Capture(() => provider.WriteCellEntity(4,3)));
        Assert.Equal(rail, Capture(() => provider.WriteLine(4,3.5,true)));
        Assert.Equal("───", Plain(Capture(() => provider.WriteLine(4,3.5,true))));
    }

    [Fact]
    public void ShoreSmoothingUsesSpareSeamsWithoutShrinkingWaterOrCoveringDoors()
    {
        var storage = Fixture();
        var chunk = storage.WorldState.Map.Chunks![0];
        chunk.Theme = "plains";
        chunk.Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList();
        string[] Backgrounds(string ansi) => Regex.Matches(ansi, @"(48;2;\d+;\d+;\d+)m\x1b\[[0-9;]+m[^\x1b]")
            .Select(m => m.Groups[1].Value).ToArray();
        var dryGrass = Backgrounds(Capture(() => Provider(storage).WriteCellEntity(4,3)));
        chunk.Terrain[2] = "..W.................";
        var original = chunk.Terrain.ToArray();
        var provider = Provider(storage);
        var edge = Capture(() => provider.WriteLine(3,3.5,true));
        Assert.Equal("   ", Plain(edge));
        var shades = Regex.Matches(edge, @"(48;2;\d+;\d+;\d+)m\x1b\[[0-9;]+m ").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(3, shades.Length);
        Assert.True(shades.Distinct().Count()>1);
        Assert.Equal(edge,Capture(() => provider.WriteLine(3,3.5,true)));
        Assert.Equal(" ", Plain(Capture(() => provider.WriteLine(3.5,3,false))));
        Assert.Equal(" ", Plain(Capture(() => provider.WriteCorner(3.5,3.5,CornerType.Cross))));
        Assert.NotEqual(Backgrounds(Capture(() => provider.WriteCellEntity(3,3)))[0],
            Backgrounds(Capture(() => provider.WriteCorner(3.5,3.5,CornerType.Cross)))[0]);
        var wetGrass = Backgrounds(Capture(() => provider.WriteCellEntity(4,3)));
        Assert.Equal(dryGrass, wetGrass);
        chunk.Terrain[3] = "..WW................";
        provider = Provider(storage);
        var bendRender = Capture(() => provider.WriteCellEntity(4,3));
        Assert.DoesNotContain(Plain(bendRender), c => "▀▄▌▐".Contains(c));
        var bendGrass = Backgrounds(bendRender);
        Assert.Equal(dryGrass, bendGrass);
        var bendEdge = Backgrounds(Capture(() => provider.WriteLine(4,3.5,true)));
        Assert.NotEqual(bendEdge[0], bendEdge[1]);
        Assert.Equal(3,bendEdge.Length);
        chunk.Terrain[3] = original[3];
        Assert.DoesNotContain(Plain(Capture(() => provider.WriteCellEntity(3,3))), c => "▀▄▌▐▖▗▘▝".Contains(c));
        Assert.Equal(original, chunk.Terrain);
        storage.WorldState.Map.Doors = [new() { From = [3,3], To = [3,4], IsDoor = true }];
        provider = Provider(storage);
        Assert.Equal("═══", Plain(Capture(() => provider.WriteLine(3,3.5,true))));
    }

    [Fact]
    public void BoulderGroupsFillSeamsButRespectDoors()
    {
        var storage=Fixture();
        var chunk=storage.WorldState.Map.Chunks![0];
        chunk.Theme="plains";
        chunk.Terrain=Enumerable.Repeat(new string('.',20),15).ToList();
        chunk.Terrain[2]="..oo................";
        chunk.Terrain[3]="..oo................";
        var provider=Provider(storage);
        Assert.Equal("█",Plain(Capture(()=>provider.WriteLine(3.5,3,false))));
        Assert.Equal("███",Plain(Capture(()=>provider.WriteLine(3,3.5,true))));
        Assert.Equal("█",Plain(Capture(()=>provider.WriteCorner(3.5,3.5,CornerType.Cross))));
        Assert.EndsWith("██",Plain(Capture(()=>provider.WriteCellEntity(3,3))));
        storage.WorldState.Map.Doors=[new(){From=[3,3],To=[3,4],IsDoor=true}];
        provider=Provider(storage);
        Assert.Equal("═══",Plain(Capture(()=>provider.WriteLine(3,3.5,true))));
    }

    [Fact]
    public void PathBlendsIntoSpareCharactersButLeavesDoorsIntact()
    {
        var storage=Fixture();
        var chunk=storage.WorldState.Map.Chunks![0];
        chunk.Theme="plains";
        chunk.Terrain=Enumerable.Repeat(new string('.',20),15).ToList();
        var provider=Provider(storage);
        var grass=Capture(()=>provider.WriteLine(3.5,3,false));
        chunk.Terrain[2]="..=.................";
        provider=Provider(storage);
        var bank=Capture(()=>provider.WriteLine(3.5,3,false));
        Assert.Equal(" ",Plain(bank));
        Assert.NotEqual(grass,bank);
        Assert.Equal("   ",Plain(Capture(()=>provider.WriteLine(3,3.5,true))));
        Assert.Equal(" ",Plain(Capture(()=>provider.WriteCorner(3.5,3.5,CornerType.Cross))));
        storage.WorldState.Map.Doors=[new(){From=[3,3],To=[3,4],IsDoor=true}];
        provider=Provider(storage);
        Assert.Equal("═══",Plain(Capture(()=>provider.WriteLine(3,3.5,true))));
    }

    private static Storage Fixture()
    {
        var storage = new Storage();
        storage.WorldState.Map = new() { Chunks = [new() { Terrain = Enumerable.Repeat(new string('w', 21), 15).ToList() }] };
        storage.WorldState.Hero = new() { Position = [1, 1] };
        return storage;
    }

    private static MapObjectsProvider Provider(Storage storage) => new(storage.WorldState, new DisplayConfig(), storage, new AppConfig { RevealMap = true });
    private static string Plain(string text) => Regex.Replace(text, "\u001b\\[[0-9;]*m", "");
    private static string Capture(Action draw)
    {
        var previous = Console.Out;
        using var writer = new StringWriter();
        try { Console.SetOut(writer); draw(); return writer.ToString(); }
        finally { Console.SetOut(previous); }
    }
}
