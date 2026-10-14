using System.Reflection;
using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class ChunkPopulationTests
{
    private static (WorldState World, MapChunk Chunk) Scene()
    {
        var chunk=new MapChunk { X=1,Y=0,Theme="forest" };
        return (new WorldState { Hero=new(){Symbol="HRO",Position=[3,3],Hp="10/10"}, Map=new(){Cols=40,Chunks=[new(){X=0,Y=0,Populated=true},chunk],Rooms=[new(){Name="Old",Positions=[[22,3],[23,3]]}]} },chunk);
    }

    [Fact]
    public async Task EarlyArrivalWaitsForSameRequestAndResultIsConsumedOnlyOnce()
    {
        var (world,chunk)=Scene();
        var queue=new ChunkPopulationQueue();
        var source=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls=0;
        Assert.True(queue.TryStart(world.Map,chunk,()=>{calls++;return source.Task;}));
        Assert.False(queue.TryStart(world.Map,chunk,()=>throw new Exception("duplicate")));
        Assert.Null(await queue.TakeAsync(false));
        var waiting=queue.TakeAsync(true);
        Assert.False(waiting.IsCompleted);
        Assert.Null(await queue.TakeAsync(true));
        source.SetResult("{}");
        Assert.Same(chunk,(await waiting)!.Chunk);
        Assert.Null(await queue.TakeAsync(true));
        Assert.Equal(1,calls);
        Assert.False(queue.TryStart(world.Map,chunk,()=>Task.FromResult("{}")));
    }

    [Fact]
    public async Task FailedFetchReleasesQueueWithoutRepeatedAttempts()
    {
        var (world,chunk)=Scene(); var queue=new ChunkPopulationQueue();
        Assert.True(queue.TryStart(world.Map,chunk,()=>Task.FromException<string>(new IOException("offline"))));
        await Assert.ThrowsAsync<IOException>(()=>queue.TakeAsync(true));
        Assert.False(queue.HasPending);
        Assert.False(queue.TryStart(world.Map,chunk,()=>Task.FromResult("{}")));
        var another=new MapChunk {X=2}; world.Map.Chunks!.Add(another);
        Assert.True(queue.TryStart(world.Map,another,()=>Task.FromResult("{}")));
        Assert.NotNull(await queue.TakeAsync(true));
    }

    [Fact]
    public async Task ConcurrentStartsProduceOneFetch()
    {
        var (world,chunk)=Scene();var queue=new ChunkPopulationQueue();
        var source=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls=0;
        var starts=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>Task.Run(()=>queue.TryStart(world.Map,chunk,()=>{Interlocked.Increment(ref calls);return source.Task;}))));
        Assert.Single(starts.Where(b=>b)); Assert.Equal(1,calls);
        source.SetResult("{}"); await queue.TakeAsync(true);
    }

    [Fact]
    public void AbandonedLocationDiscardsCompletedResult()
    {
        var (world,chunk)=Scene();var result=new ChunkPopulationQueue.Result(world.Map,chunk,"{}");
        world.Map=new();
        Assert.Null(ChunkPopulationPatch.Prepare(world,result));
    }

    [Theory]
    [InlineData("{\"map\":{\"entities\":[{\"id\":0,\"position\":[22,3],\"symbol\":\"NEW\"}]}}")]
    [InlineData("{\"map\":{\"entities\":[{\"ID\":0,\"position\":[22,3],\"symbol\":\"NEW\"}]}}")]
    [InlineData("{\"map\":{\"entities\":[{\"position\":[3,3],\"symbol\":\"NEW\"}]}}")]
    [InlineData("{\"map\":{\"rooms\":[{\"id\":0,\"positions\":[[22,3]]}]}}")]
    [InlineData("{\"map\":{\"doors\":[]}}") ]
    [InlineData("{\"history\":[]}")]
    [InlineData("{\"map\":{\"objects\":[{\"position\":[22,3],\"symbol\":\"HRO\"}]}}")]
    public void UnsafePopulationCannotModifyCurrentState(string json)
    {
        var (world,chunk)=Scene();
        Assert.Throws<InvalidDataException>(()=>ChunkPopulationPatch.Prepare(world,new(world.Map,chunk,json)));
        Assert.Equal("Old",world.Map.Rooms![0].Name);
        Assert.False(chunk.Populated);
    }

    [Fact]
    public void OnlyMapAdditionsAreAppliedWithoutRollingBackHeroTimeOrNarrative()
    {
        var (world,chunk)=Scene();
        var s=new Storage();s.WorldState.Map=world.Map;s.WorldState.Hero=world.Hero;
        s.WorldState.Time.TotalRounds=44;
        string json="""{"hero":{"hp":"1/10"},"time":{"totalRounds":1},"history":[{"text":"spoiler"}],"map":{"rooms":[{"id":0,"name":"New"}],"objects":[{"symbol":"[C]","name":"Chest","position":[22,3]}]}}""";
        var patch=ChunkPopulationPatch.Prepare(s.WorldState,new(world.Map,chunk,json))!;
        Assert.Empty(s.WorldState.Map.Objects??[]);
        s.ApplyUpdateWorldState(patch);
        Assert.Equal("10/10",s.WorldState.Hero.Hp);
        Assert.Equal(44,s.WorldState.Time.TotalRounds);
        Assert.Empty(s.WorldState.History!);
        Assert.Equal("New",s.WorldState.Map.Rooms![0].Name);
        Assert.Single(s.WorldState.Map.Objects!);
    }

    [Fact]
    public void CurrentOccupancyIsCheckedAfterFetch()
    {
        var (world,chunk)=Scene();
        var result=new ChunkPopulationQueue.Result(world.Map,chunk,"""{"map":{"objects":[{"symbol":"[C]","position":[22,3]}]}}""");
        world.Hero!.Position=[22,3];
        Assert.Throws<InvalidDataException>(()=>ChunkPopulationPatch.Prepare(world,result));
    }

    [Fact]
    public void PrefetchThresholdIsEarlierAndStaysOnSameFloor()
    {
        var (world,chunk)=Scene(); world.Hero!.Position=[12,3];
        Assert.Null(LocationGrower.UnpopulatedNearHero(world));
        Assert.Same(chunk,LocationGrower.UnpopulatedNearHero(world,10));
        chunk.Z=1;
        Assert.Null(LocationGrower.UnpopulatedNearHero(world,10));
    }

    [Fact]
    public async Task ClientFetchesSnapshotAndDoesNotApplyUntilUiConsumesIt()
    {
        var (world,chunk)=Scene();var s=new Storage();s.WorldState.Map=world.Map;s.WorldState.Hero=world.Hero;
        string dir=Path.Combine(AppConfig.ProjectRoot,"Prompts","PopulationTest","PopulateChunk");
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"systemPrompt.md"),"Populate test");
        try
        {
            var provider=new DeferredProvider();
            var client=new GameAiClient(s.WorldState,s,provider,new AppConfig{RuleSet="PopulationTest"});
            Assert.True(client.TryPrefetchChunk(chunk));
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            string snapshot=provider.Snapshot;
            Assert.NotEqual(Storage.SavePath,snapshot);
            Assert.True(File.Exists(snapshot));
            s.WorldState.Hero!.Hp="8/10";
            Assert.Contains("10/10",File.ReadAllText(snapshot));
            Assert.Empty(s.WorldState.Map.Objects??[]);
            var arrival=client.PopulateChunk(chunk);
            provider.Response.SetResult("""{"map":{"objects":[{"symbol":"[C]","name":"Chest","position":[22,3]}]}}""");
            await arrival.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1,provider.Calls);
            Assert.True(chunk.Populated);
            Assert.Single(s.WorldState.Map.Objects!);
            Assert.Equal("8/10",s.WorldState.Hero.Hp);
            Assert.False(File.Exists(snapshot));
        }
        finally { Directory.Delete(Path.Combine(AppConfig.ProjectRoot,"Prompts","PopulationTest"),true); }
    }

    [Fact]
    public async Task ForegroundModelRequestWaitsUntilBackgroundRequestFinishes()
    {
        var (world,chunk)=Scene();var s=new Storage();s.WorldState.Map=world.Map;s.WorldState.Hero=world.Hero;
        string dir=Path.Combine(AppConfig.ProjectRoot,"Prompts","PopulationGateTest","PopulateChunk");
        Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"systemPrompt.md"),"Populate test");
        try
        {
            var provider=new DeferredProvider();
            var client=new GameAiClient(s.WorldState,s,provider,new AppConfig{RuleSet="PopulationGateTest"});
            Assert.True(client.TryPrefetchChunk(chunk));
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var complete=typeof(GameAiClient).GetMethod("CompleteConfigured",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var foreground=(Task<string>)complete.Invoke(client,[new[]{"Master"},"action","SendAction","model"])!;
            Assert.False(foreground.IsCompleted);
            Assert.Equal(1,provider.Calls);
            provider.Response.SetResult("{\"map\":{}}");
            await foreground.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2,provider.Calls);
            Assert.Null(provider.Snapshot); // snapshot override belongs only to the background async context
            await client.FlushPopulationAsync(true);
        }
        finally { Directory.Delete(Path.Combine(AppConfig.ProjectRoot,"Prompts","PopulationGateTest"),true); }
    }

    private sealed class DeferredProvider : IAiProvider
    {
        public int Calls;
        public string Snapshot="";
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<string> Response=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> Complete(IReadOnlyList<string> blocks,string message,string path,string model)
        {
            Calls++;
            var tools=typeof(GameAiClient).Assembly.GetType("NaviDnD.Clients.AiActionTools")!;
            var local=(AsyncLocal<string?>)tools.GetField("StatePathOverride",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
            Snapshot=local.Value!;
            Started.TrySetResult();return Response.Task;
        }
        public Task<string> CompleteWithStreaming(IReadOnlyList<string> blocks,string message,string path,string model,Action<string>? onChunk)=>Complete(blocks,message,path,model);
    }
}
