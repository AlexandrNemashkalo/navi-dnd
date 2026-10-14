using System.Reflection;
using NaviDnD.Clients;
using NaviDnD.Data;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class ThirdPassMechanicsTests
{
    private static Storage Scene()
    {
        var s = new Storage();
        s.WorldState.Hero = new Hero { Name = "Hero", Symbol = "HRO", Position = [3,3], Hp = "20/20", SpeedMax = 30, SpeedLeft = 30 };
        s.WorldState.Map.Rooms = [new Room { Name = "Room", Positions = [.. Enumerable.Range(1, 15).SelectMany(c => Enumerable.Range(1,10).Select(r => new List<int>{c,r}))] }];
        return s;
    }

    [Fact]
    public void StealthAndAmbushRemainInTriggerContextAndCanBeClearedIndependently()
    {
        var s = Scene();
        s.WorldState.Hero.Stealth = 19;
        var enemy = new LivingEntity { Name = "Scout", Symbol = "SCT", Hidden = true, Position = [5,3], Hp = "8/8", DarkvisionFt = 60 };
        s.WorldState.Map.Entities = [enemy];
        string context = new AiContextBuilder(s.WorldState).TriggerState([(enemy, "spotted")], [], []);
        Assert.Contains("Скрытность 19", context);
        Assert.Contains("\"hidden\":true", context);
        s.ApplyUpdateWorldState("""{"hero":{"stealth":null},"map":{"entities":[{"id":0,"hidden":false}]}}""");
        Assert.Null(s.WorldState.Hero.Stealth);
        Assert.False(s.WorldState.Map.Entities[0].Hidden);
    }

    [Fact]
    public void HeroTorchIlluminatesNearbyCellsAndStopsWhenExtinguished()
    {
        var s = Scene();
        s.WorldState.Time.PartOfDay = PartsOfDay.Night;
        s.WorldState.Hero.Light = new LightSource { On = true, Ft = 20 };
        Assert.True(MovementCalculator.IsCellLit(s.WorldState, 6,3));
        Assert.False(MovementCalculator.IsCellLit(s.WorldState, 8,3));
        s.WorldState.Hero.Light.On = false;
        Assert.False(MovementCalculator.IsCellLit(s.WorldState, 6,3));
    }

    [Fact]
    public void RemovedLightSourceDoesNotRevealCellsOnMap()
    {
        var s=Scene();var ws=s.WorldState;
        ws.Time.PartOfDay=PartsOfDay.Night;ws.Hero.DarkvisionFt=0;
        ws.Map.Rooms=[];
        ws.Map.Chunks=[new(){Theme="forest",Terrain=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList()}];
        ws.Map.Objects=[new(){Name="Extinguished torch",Symbol="(L)",Position=[8,3],Deleted=true,Light=new(){On=true,Ft=30}}];
        new MapObjectsProvider(ws,new DisplayConfig(),s).Refresh();
        Assert.DoesNotContain((8,3),s.VisibleCells);
    }

    [Fact]
    public void NightVisibilityAndEnemySightRespondToTorchAndDarkvision()
    {
        var s=Scene();var ws=s.WorldState;
        ws.Time.PartOfDay=PartsOfDay.Night;ws.Hero.DarkvisionFt=0;
        ws.Map.Rooms=[];
        ws.Map.Chunks=[new(){Theme="forest",Terrain=Enumerable.Repeat(new string('.',MapChunk.Cols),MapChunk.Rows).ToList()}];
        var e=new LivingEntity{Name="Watcher",Symbol="WCH",Position=[8,3],VisionFt=60,DarkvisionFt=0};ws.Map.Entities=[e];
        var provider=new MapObjectsProvider(ws,new DisplayConfig(),s);
        provider.Refresh();Assert.DoesNotContain((8,3),s.VisibleCells);
        Assert.False(MovementCalculator.EntitySeesHero(ws,e));
        ws.Hero.Light=new(){On=true,Ft=30};provider.Refresh();
        Assert.Contains((8,3),s.VisibleCells);Assert.True(MovementCalculator.EntitySeesHero(ws,e));
        ws.Hero.Light.On=false;ws.Hero.DarkvisionFt=30;e.DarkvisionFt=30;provider.Refresh();
        Assert.Contains((8,3),s.VisibleCells);Assert.True(MovementCalculator.EntitySeesHero(ws,e));
        ws.Hero.DarkvisionFt=0;e.DarkvisionFt=0;provider.Refresh();
        Assert.DoesNotContain((8,3),s.VisibleCells);Assert.False(MovementCalculator.EntitySeesHero(ws,e));
    }

    [Theory]
    [InlineData("cone")]
    [InlineData("sphere")]
    [InlineData("line")]
    [InlineData("cube")]
    public void AreaContainsAllyAndEnemyButExcludesHiddenAndDeletedTargets(string shape)
    {
        var s = Scene();
        s.WorldState.Map.Entities = [
            new() { Name="Enemy", Symbol="ENM", Position=[5,3] },
            new() { Name="Ally", Symbol="ALY", Position=[4,3] },
            new() { Name="Hidden", Symbol="HID", Position=[5,3], Hidden=true },
            new() { Name="Gone", Symbol="GON", Position=[5,3], Deleted=true }];
        var cells = TargetGeometry.AreaCells(s.WorldState, 5,3,shape,15);
        var targets = TargetGeometry.CreaturesIn(s.WorldState,cells);
        Assert.Contains(targets,t=>t.Symbol=="ENM");
        Assert.Contains(targets,t=>t.Symbol=="ALY");
        Assert.DoesNotContain(targets,t=>t.Symbol is "HID" or "GON");
        var selectable = TargetGeometry.SelectableCells(s.WorldState, new TargetSelectionRequest { Mode="creature", RangeFt=5 }, (_,_)=>true,(_,_)=>true);
        Assert.DoesNotContain((5,3),selectable);
    }

    [Fact]
    public void InventorySplitDropAndEquipmentPatchPreservesTotalsAndOtherFields()
    {
        var s = Scene();
        s.ApplyUpdateWorldState("""{"hero":{"inventory":[{"name":"Coins","quantity":10},{"name":"Shield","equipmentedSlot":[]}],"stats":[{"key":"armorClass","name":"AC","value":"12"}]}}""");
        s.ApplyUpdateWorldState("""{"hero":{"inventory":[{"id":0,"quantity":6},{"id":1,"equipmentedSlot":["leftHand"]}],"stats":[{"id":0,"value":"14"}]},"map":{"objects":[{"name":"Coins","symbol":"(C)","position":[3,3],"aiInfo":["4 coins"]}]}}""");
        Assert.Equal(6,s.WorldState.Hero.Inventory[0].Quantity);
        Assert.Equal("Shield",s.WorldState.Hero.Inventory[1].Name);
        Assert.Single(s.WorldState.Hero.Inventory[1].EquipmentedSlot!);
        Assert.Equal("14",s.WorldState.Hero.Stat(StatKeys.ArmorClass)!.Value);
        Assert.Single(s.WorldState.Map.Objects!);
    }

    [Fact]
    public void TravelFoodSummaryIncludesSeparateAndPackedFoodWithoutSpendingIt()
    {
        var s = Scene();
        s.WorldState.Hero.Inventory = [new() { Name="Рационы",Quantity=4 },new() {Name="Набор",Description="хлеб на два дня"}];
        var summary = (string)typeof(TravelService).GetMethod("FoodLine",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[s.WorldState,2])!;
        Assert.Contains("Рационы (id 0) ×4",summary);
        Assert.Contains("Набор",summary);
        Assert.Contains("2 дня",summary);
        Assert.Equal(4,s.WorldState.Hero.Inventory[0].Quantity);
    }

    [Fact]
    public void NpcMemoryAndQuestStepsAppendAcrossConversationsAndPreserveSecrets()
    {
        var s=Scene();
        s.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"name":"Innkeeper","memory":["Hero said: blue"],"dmNotes":"secret","met":true}],"plotThreads":[{"name":"Key","steps":["Met keeper"],"status":"активна"}]}}""");
        s.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"id":0,"notes":"Helped","attitude":"friendly","memory":["Hero returned key"]}],"plotThreads":[{"id":0,"steps":["Returned key"],"status":"завершена"}]}}""");
        Assert.Equal(2,s.WorldState.Narrative.Npcs[0].Memory!.Count);
        Assert.Equal("secret",s.WorldState.Narrative.Npcs[0].DmNotes);
        Assert.Equal(2,s.WorldState.Narrative.PlotThreads[0].Steps!.Count);
        Assert.Equal("завершена",s.WorldState.Narrative.PlotThreads[0].Status);
    }

    [Fact]
    public void StairsWorkBothWaysInCombatAndUpperFloorContextExcludesLowerFloorPopulation()
    {
        var s=Scene();
        var ws=s.WorldState;
        ws.Map.Cols=40;
        ws.Map.Chunks=[new(){X=0,Y=0,Z=0,Theme="building",Populated=true},new(){X=1,Y=0,Z=1,Theme="building",Purpose="Upper"}];
        ws.Map.Rooms!.Add(new Room { Name="Upper",Positions=[[23,3],[24,3]] });
        ws.Map.Stairs=[new(){A=[4,3],B=[23,3]}];
        ws.Combat=new(){Active=true,CurrentTurn="HRO"};
        var handler=new MovementHandler(ws,s,new DisplayConfig());
        handler.TryMove("MoveEast",out _,out _,out _,out _);
        Assert.Equal(new[]{23,3},ws.Hero.Position);
        Assert.Equal(25,ws.Hero.SpeedLeft);
        Assert.Same(ws.Map.Chunks[1],LocationGrower.UnpopulatedNearHero(ws));
        Assert.Contains("Upper",new AiContextBuilder(ws).MinimalState("look"));
        handler.TryMove("MoveEast",out _,out _,out _,out _);
        handler.TryMove("MoveWest",out _,out _,out _,out _);
        Assert.Equal(new[]{4,3},ws.Hero.Position);
        Assert.Equal(15,ws.Hero.SpeedLeft);
    }
}
