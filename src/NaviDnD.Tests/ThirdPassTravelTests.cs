using System.Reflection;
using NaviDnD.Data;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class ThirdPassTravelTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void EncounterPreservesReachedDestinationWithoutForcingRest(int targetX)
    {
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary)
            .GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "arrival-encounter-" + Guid.NewGuid().ToString("N");
        var world = new WorldMap { Id = id, Width = 12, Height = 1,
            Biomes = ["............"], Roads = ["111111111111"], Rivers = ["000000000000"], Owners = ["............"],
            Chronicle = new() { Places = [new() { Name = "Old mill", Type = WorldPlaceTypes.Village, X = targetX, Y = 0 }] } };
        worlds[id] = world;
        try
        {
            var state = new WorldState { Hero = new(), World = new() { Id = id, X = 0, Y = 0, Known = ["Old mill"] },
                Time = new() { Day = 1, PartOfDay = PartsOfDay.Morning, TotalRounds = 1 } };
            var plan = Assert.IsType<TravelService.Plan>(TravelService.PlanTo(state, (targetX,0)));
            string report = TravelService.Go(state, plan, TravelService.Pace.Normal, new FixedRandom(0));
            if (targetX == 1)
            {
                Assert.Equal(targetX, state.World.X);
                Assert.Equal("Old mill", state.World.Place);
                Assert.Contains("Old mill", state.World.Visited);
                Assert.Contains("Destination reached: Old mill", report);
            }
            else
            {
                Assert.NotEqual(targetX, state.World.X);
                Assert.Null(state.World.Place);
                Assert.DoesNotContain("Old mill", state.World.Visited);
                Assert.NotNull(TravelService.PlanTo(state, (targetX,0)));
                Assert.DoesNotContain("Destination reached:", report);
            }
            Assert.Contains("ENCOUNTER", report);
            Assert.Contains("await their choice", report);
            Assert.DoesNotContain("take one long_rest", report);
            Assert.Equal(1, state.Time.Day);
        }
        finally { worlds.Remove(id); }
    }

    [Theory]
    [InlineData(TravelService.Pace.Normal, 2, false)]
    [InlineData(TravelService.Pace.Slow, 3, false)]
    [InlineData(TravelService.Pace.Fast, 2, false)]
    [InlineData(TravelService.Pace.Normal, 1, true)]
    [InlineData(TravelService.Pace.Slow, 1, true)]
    [InlineData(TravelService.Pace.Fast, 1, true)]
    public void DifficultCellKeepsEveryDayOfTravel(TravelService.Pace pace, int expectedDays, bool encounter)
    {
        var worlds = (Dictionary<string, WorldMap>)typeof(WorldLibrary).GetField("_loaded", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        string id = "travel-duration-" + Guid.NewGuid().ToString("N");
        var world = new WorldMap { Id = id, Width = 3, Height = 1,
            Biomes = [".m."], Roads = ["000"], Rivers = ["010"], Owners = ["..."] };
        worlds[id] = world;
        try
        {
            var ws = new WorldState { World = new() { Id = id, X = 0, Y = 0 },
                Hero = new() { Effects = [new() { Name = "Protection", ExpiresAtRound = 50000 }] },
                Time = new() { Day = 1, PartOfDay = PartsOfDay.Morning, TotalRounds = 1 } };
            var route = WorldAtlas.Route(world, (0, 0), (2, 0))!;
            Assert.Equal(1.625, route.Days);
            if (!encounter) Assert.Equal(expectedDays, new TravelService.Plan((0, 0), (2, 0), null, route).Days(pace));
            var rng = new FixedRandom(encounter ? 0 : 0.99);
            string report = TravelService.Go(ws, new((0, 0), (2, 0), null, route), pace, rng);
            Assert.Equal(expectedDays, ws.Time.Day);
            Assert.Equal(encounter ? 2 : expectedDays, rng.Calls);
            Assert.Equal(encounter ? 1 : 2, ws.World.X);
            Assert.Contains($"Food: traveling for {expectedDays} {(expectedDays == 1 ? "day" : "days")}", report);
            Assert.Equal(encounter, report.Contains("ENCOUNTER"));
            Assert.Equal(50000 - (expectedDays - 1) * 14400 - 7200, ws.Hero.Effects![0].ExpiresAtRound);
        }
        finally { worlds.Remove(id); }
    }

    [Theory]
    [InlineData(false, PartsOfDay.Morning, 3)]
    [InlineData(true, PartsOfDay.Morning, 2)]
    [InlineData(false, PartsOfDay.Night, 3)]
    [InlineData(true, PartsOfDay.Night, 2)]
    [InlineData(false, PartsOfDay.Evening, 4)]
    [InlineData(true, PartsOfDay.Evening, 3)]
    public void TravelMovesAlongRouteAdvancesDaysReportsFoodAndStopsAtEncounter(bool encounter, string startPart, int expectedDay)
    {
        // In-memory library entry; this test neither creates worlds on disk nor changes the active world.
        var worlds=(Dictionary<string,WorldMap>)typeof(WorldLibrary).GetField("_loaded",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        string id="travel-test-"+Guid.NewGuid().ToString("N");
        worlds[id]=new WorldMap {Id=id,Name="Test",Width=12,Height=3,Biomes=["............","............","............"],Owners=["............","............","............"],Roads=["222222222222","222222222222","222222222222"],Rivers=["000000000000","000000000000","000000000000"]};
        try
        {
            var ws=new WorldState {World=new(){Id=id,X=1,Y=1},Hero=new(){Position=[3,3],Inventory=[new(){Name="Рационы",Quantity=5,
                Effects=[new(){Name="Preserved food",ExpiresAtRound=50000}]}],
                Effects=[new(){Name="Long protection",ExpiresAtRound=50000},
                    new(){Name="Short protection",ExpiresAtRound=50,OnExpire=new(){Effect="The temporary ward fades"}},
                    new(){Name="Until rest",UntilLongRest=true}]},Time=new(){Day=2,PartOfDay=startPart,TotalRounds=40},ScheduledEvents=[new(){Name="Old scene",FireAtRound=50}]};
            var path=Enumerable.Range(1,10).Select(x=>(x,y:1)).ToList();
            var plan=new TravelService.Plan((1,1),(10,1),null,new WorldAtlas.RouteInfo(1.6875,1,path,""));
            string report=TravelService.Go(ws,plan,TravelService.Pace.Normal,new FixedRandom(encounter?0:0.99));
            Assert.Equal(encounter?7:10,ws.World.X); // The day includes the cell being crossed at nightfall.
            Assert.Equal(1,ws.World.Y);
            Assert.Equal(expectedDay,ws.Time.Day);
            Assert.Equal(PartsOfDay.Evening,ws.Time.PartOfDay);
            Assert.Equal(1,ws.Time.TotalRounds);
            Assert.Empty(ws.ScheduledEvents!);
            Assert.Contains("Food:",report);
            Assert.Contains("id 0",report);
            Assert.Equal(5,ws.Hero.Inventory[0].Quantity); // only the master consumes food from the report
            Assert.Contains("Rest stops:",report);
            Assert.Equal(encounter,report.Contains("ENCOUNTER"));
            if (encounter)
            {
                Assert.Contains("await their choice", report);
                Assert.DoesNotContain("take one long_rest", report);
            }
            else Assert.Contains("take one long_rest unless interrupted", report);
            Assert.False(GameWorld.HasLocation(ws));
            int offset = (expectedDay - 2) * 14400 + (3 - Array.IndexOf(PartsOfDay.Cycle, startPart)) * 3600;
            Assert.Equal(50000 - offset, ws.Hero.Effects![0].ExpiresAtRound);
            Assert.Equal(50000 - offset, ws.Hero.Inventory[0].Effects![0].ExpiresAtRound);
            Assert.Equal(0, ws.Hero.Effects[1].ExpiresAtRound);
            Assert.True(ws.Hero.Effects[2].UntilLongRest);
            // Deadlines survive saving and are consumed by the same handler as ordinary round changes.
            var reloaded = new Storage();
            reloaded.ApplySavedState(System.Text.Json.JsonSerializer.Serialize(ws,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            Assert.Equal("Short protection", Assert.Single(reloaded.TakeRoundBundle()!.ExpiredEffects).Effect.Name);
            Assert.Null(reloaded.TakeRoundBundle());
            string restFile = Path.Combine(Path.GetTempPath(), $"travel-rest-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(restFile, System.Text.Json.JsonSerializer.Serialize(reloaded.WorldState,
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
                reloaded.ApplyUpdateWorldState(new GameActionTools(new McpServerConfig(restFile, LogPath: null)).LongRest());
                Assert.Equal(50000 - offset - 3600, reloaded.WorldState.Hero!.Effects![0].ExpiresAtRound);
                Assert.DoesNotContain(reloaded.WorldState.Hero.Effects, effect => effect.Name == "Until rest");
                Assert.Equal((expectedDay + 1, PartsOfDay.Night, 1201),
                    (reloaded.WorldState.Time.Day, reloaded.WorldState.Time.PartOfDay, reloaded.WorldState.Time.TotalRounds));
                Assert.Null(reloaded.TakeRoundBundle()); // A delivered expiry is not redelivered by the next clock rebase.
            }
            finally { File.Delete(restFile); }
        }
        finally { worlds.Remove(id); }
    }

    private sealed class FixedRandom(double value):Random
    {
        public int Calls { get; private set; }
        public override double NextDouble() { Calls++; return value; }
    }
}
