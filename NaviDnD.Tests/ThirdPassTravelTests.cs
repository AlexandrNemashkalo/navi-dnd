using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class ThirdPassTravelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TravelMovesAlongRouteAdvancesDaysReportsFoodAndStopsAtEncounter(bool encounter)
    {
        // In-memory library entry; this test neither creates worlds on disk nor changes the active world.
        var worlds=(Dictionary<string,WorldMap>)typeof(WorldLibrary).GetField("_loaded",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        string id="travel-test-"+Guid.NewGuid().ToString("N");
        worlds[id]=new WorldMap {Id=id,Name="Test",Width=12,Height=3,Biomes=["............","............","............"],Owners=["............","............","............"],Roads=["222222222222","222222222222","222222222222"],Rivers=["000000000000","000000000000","000000000000"]};
        try
        {
            var ws=new WorldState {World=new(){Id=id,X=1,Y=1},Hero=new(){Position=[3,3],Inventory=[new(){Name="Рационы",Quantity=5}]},Time=new(){Day=2,PartOfDay=PartsOfDay.Morning,TotalRounds=40},ScheduledEvents=[new(){Name="Old scene",FireAtRound=50}]};
            var path=Enumerable.Range(1,10).Select(x=>(x,y:1)).ToList();
            var plan=new TravelService.Plan((1,1),(10,1),null,new WorldAtlas.RouteInfo(1.6875,1,path,""));
            string report=TravelService.Go(ws,plan,TravelService.Pace.Normal,new FixedRandom(encounter?0:0.99));
            Assert.Equal(encounter?6:10,ws.World.X);
            Assert.Equal(1,ws.World.Y);
            Assert.Equal(encounter?2:3,ws.Time.Day);
            Assert.Equal(PartsOfDay.Evening,ws.Time.PartOfDay);
            Assert.Equal(1,ws.Time.TotalRounds);
            Assert.Empty(ws.ScheduledEvents!);
            Assert.Contains("Food:",report);
            Assert.Contains("id 0",report);
            Assert.Equal(5,ws.Hero.Inventory[0].Quantity); // only the master consumes food from the report
            Assert.Contains("Rest stops:",report);
            Assert.Equal(encounter,report.Contains("ENCOUNTER"));
            Assert.False(GameWorld.HasLocation(ws));
        }
        finally { worlds.Remove(id); }
    }

    private sealed class FixedRandom(double value):Random
    {
        public override double NextDouble()=>value;
    }
}
