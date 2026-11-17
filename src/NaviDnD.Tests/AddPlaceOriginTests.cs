using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

public class AddPlaceOriginTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"navi-add-origin-"+Guid.NewGuid().ToString("N"));
    private readonly WorldMap world;
    private string StatePath=>Path.Combine(root,"state.json");
    private string WorldPath=>Path.Combine(root,"Worlds","origin","world.json");
    public AddPlaceOriginTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WorldPath)!);
        world=new(){Id="origin",Width=24,Height=12,Biomes=Enumerable.Repeat(new string('.',24),12).ToList(),
            Roads=Enumerable.Repeat(new string('0',24),12).ToList(),Rivers=Enumerable.Repeat(new string('0',24),12).ToList(),
            Owners=Enumerable.Repeat(new string('.',24),12).ToList(),
            Chronicle=new(){Places=[new(){Name="Home",FormerNames=["Old Home"],X=18,Y=6,Type="village"}]}};
        File.WriteAllText(WorldPath,JsonSerializer.Serialize(world));
    }
    private string Add(string? current,string? near=null,int? x=3,int? y=6, string? direction="east",double? days=1,string name="Roadside shrine")
    {
        File.WriteAllText(StatePath,JsonSerializer.Serialize(new{world=new{id="origin",place=current,x,y}}));
        var tools=new WorldQueryTools(new McpServerConfig(StatePath,LogPath:null));
        string before=File.ReadAllText(StatePath),geoBefore=File.ReadAllText(WorldPath);
        string result=tools.AddPlace(name,"shrine","A discovered shrine",near:near,direction:direction,days:days);
        Assert.Equal(before,File.ReadAllText(StatePath));Assert.Equal(geoBefore,File.ReadAllText(WorldPath));
        return result;
    }
    [Theory]
    [InlineData("Lost place")]
    [InlineData("")]
    public void UnresolvedCurrentPlaceUsesSameCoordinatesAsUnnamedHero(string current)
    {
        string expected=Add(null);Assert.Contains("Patch for the response:",expected);Assert.Contains("From",expected);
        Assert.Equal(expected,Add(current));
    }
    [Theory]
    [InlineData("Home")]
    [InlineData("Old Home")]
    public void ResolvedCurrentPlaceTakesPriorityOverCoordinates(string current)
    {
        Assert.Equal(Add(null,near:"Home"),Add(current));
    }
    [Fact]
    public void ExplicitUnknownOriginRemainsAnErrorAndQueriesDoNotInventFallbacks()
    {
        string response=Add("Lost place",near:"Unknown");
        Assert.DoesNotContain("Patch for the response:",response);
        var tools=new WorldQueryTools(new McpServerConfig(StatePath,LogPath:null));
        Assert.Contains("does not exist",tools.WorldQuery("near:Unknown"));
        Assert.Contains("does not exist",tools.WorldQuery("route:Unknown;Home"));
    }
    [Fact]
    public void OutOfBoundsCoordinatesAreNotUsedAsAnOrigin()
    {
        string response=Add("Lost place",x:-1,y:6);
        Assert.Contains("Relative placement requires an origin.",response);
        Assert.DoesNotContain("Patch for the response:",response);
        Assert.DoesNotContain("From",response);
        Assert.DoesNotContain("No walking route from",response);
    }
    [Theory]
    [InlineData("east",null)]
    [InlineData(null,1.0)]
    [InlineData("north",2.0)]
    public void RelativeConstraintsWithoutOriginReturnActionableError(string? direction,double? days)
    {
        string response=Add(null,x:null,y:null,direction:direction,days:days);
        Assert.Contains("Relative placement requires an origin.",response);
        Assert.Contains("Supply near with an existing place",response);
        Assert.DoesNotContain("Patch for the response:",response);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WorldCreationWithoutRelativeConstraintsStillSelectsGlobalSite(string? direction)
    {
        string response=Add(null,x:null,y:null,direction:direction,days:null);
        Assert.Contains("Patch for the response:",response);
        Assert.DoesNotContain("Relative placement requires",response);
        using var json=JsonDocument.Parse(response.Split("Patch for the response:")[1]);
        var place=json.RootElement.GetProperty("world").GetProperty("places")[0];
        Assert.True(world.InBounds(place.GetProperty("x").GetInt32(),place.GetProperty("y").GetInt32()));
    }
    [Fact]
    public void ExplicitExistingOriginWorksBeforeHeroHasCoordinates()
    {
        string response=Add(null,near:"Home",x:null,y:null);
        Assert.Contains("From",response);Assert.Contains("Home",response);Assert.Contains("Patch for the response:",response);
        string error=Add(null,near:"Unknown",x:null,y:null);
        Assert.Contains("does not exist",error);Assert.DoesNotContain("Relative placement requires",error);
    }
    [Theory]
    [InlineData("Home")]
    [InlineData("Old Home")]
    public void RevealingExistingPlaceDoesNotRequireOriginOrReplaceItsIdentity(string name)
    {
        string response=Add(null,x:null,y:null,name:name);
        Assert.Contains("already exists",response);
        using var json=JsonDocument.Parse(response.Split("Patch for the response:")[1]);
        var place=json.RootElement.GetProperty("world").GetProperty("places")[0];
        Assert.Equal("Home",place.GetProperty("name").GetString());
        Assert.False(place.GetProperty("hidden").GetBoolean());
        Assert.False(place.TryGetProperty("x",out _));
        Assert.False(place.TryGetProperty("description",out _));
    }
    public void Dispose(){File.Delete(StatePath);File.Delete(WorldPath);Directory.Delete(Path.GetDirectoryName(WorldPath)!);Directory.Delete(Path.Combine(root,"Worlds"));Directory.Delete(root);}
}
