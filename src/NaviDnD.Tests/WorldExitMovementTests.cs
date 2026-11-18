using System.Text.Json;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class WorldExitMovementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenCountrysideDepartureKeepsHeroAndMapUntilConfirmed(bool combat)
    {
        var storage = new Storage();
        var state = storage.WorldState;
        state.World = new();
        state.Hero = new() { Position = [1, 7], SpeedLeft = 30 };
        state.Combat = new() { Active = combat };
        state.Map = new() { Cols = 20, Rows = 15, Chunks = [new() { Theme = "plains",
            Terrain = Enumerable.Repeat(new string('.', 20), 15).ToList() }] };
        state.History = [new() { Text = L.T("The way is blocked.") }];
        var handler = new MovementHandler(state, storage, new());
        string before = JsonSerializer.Serialize(state.Map);
        Assert.False(handler.TryMove("MoveWest", out _, out var entities, out var areas, out var doors));
        Assert.Equal(!combat, handler.LeaveRequested);
        Assert.Equal(new[] { 1, 7 }, state.Hero.Position);
        Assert.Equal(30, state.Hero.SpeedLeft);
        Assert.Equal(before, JsonSerializer.Serialize(state.Map));
        Assert.Empty(entities); Assert.Empty(areas); Assert.Empty(doors);
    }
    [Theory]
    [InlineData(false,false,false,false,true)]
    [InlineData(false,false,true,false,true)]
    [InlineData(true,false,false,false,false)]
    [InlineData(false,true,false,false,false)]
    [InlineData(true,true,true,false,false)]
    [InlineData(false,false,false,true,false)]
    [InlineData(false,false,true,true,false)]
    public void OnlyVisibleExistingWorldExitRequestsDepartureOutsideCombat(bool hidden,bool missing,bool open,bool combat,bool leaves)
    {
        string path=Path.Combine(Path.GetTempPath(),"navidnd-exit-"+Guid.NewGuid().ToString("N"),"state.json");
        using var scope=(IDisposable)typeof(Storage).GetMethod("UseSavePath",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[path])!;
        try
        {
            var storage=new Storage();var state=storage.WorldState;
            state.Hero=new(){Position=[1,7],SpeedLeft=30};
            state.Combat=new(){Active=combat};
            state.Map=new(){Cols=20,Rows=15,Doors=[new(){From=[1,7],To=[0,7],IsWorldExit=true,IsEntry=true,
                IsDoor=true,IsDoorOpen=open,Hidden=hidden}]};
            if(missing) state.Map.Doors.Clear();
            // The closed marker convention and visibility flags survive a map serialization round-trip.
            state.Map=JsonSerializer.Deserialize<MapConfig>(JsonSerializer.Serialize(state.Map))!;
            var handler=new MovementHandler(state,storage,new());
            string before=JsonSerializer.Serialize(state.Map);
            Assert.False(handler.TryMove("MoveWest",out _,out var entities,out var areas,out var doors));
            Assert.Equal(leaves,handler.LeaveRequested);
            Assert.Equal(new[]{1,7},state.Hero.Position);
            Assert.Equal(30,state.Hero.SpeedLeft);
            Assert.Equal(before,JsonSerializer.Serialize(state.Map));
            Assert.Empty(entities);Assert.Empty(areas);Assert.Empty(doors);
        }
        finally {if(Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!,true);}
    }
    [Fact]
    public void HiddenEntryIntoUngeneratedSpaceDoesNotRevealExitInBlockMessage()
    {
        var storage=new Storage();var state=storage.WorldState;
        state.Hero=new(){Position=[2,7]};
        state.Map=new(){Cols=20,Rows=15,Rooms=[new(){Positions=[[2,7]]}],
            Doors=[new(){From=[2,7],To=[1,7],IsEntry=true,IsWorldExit=true,Hidden=true}]};
        // Pre-existing generic message also avoids any disk write on the blocked repeated step.
        state.History=[new(){Text=L.T("The way is blocked.")}];
        var handler=new MovementHandler(state,storage,new());
        Assert.False(handler.TryMove("MoveWest",out _,out _,out _,out _));
        Assert.False(handler.LeaveRequested);
        Assert.Equal(L.T("The way is blocked."),Assert.Single(state.History).Text);
        Assert.Equal(new[]{2,7},state.Hero.Position);
    }
}
