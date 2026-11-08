using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NaviDnD.Clients;
using NaviDnD.Data.Models;
using NaviDnD.Migrations;

namespace NaviDnD.Tests;

public class NpcVoiceTests
{
    [Theory]
    [InlineData("ru","male")]
    [InlineData("ru","female")]
    [InlineData("en","male")]
    [InlineData("en","female")]
    public void AssignmentUsesGenderLanguageAndSurvivesSaveAndModelPatches(string language,string gender)
    {
        var s=new Storage();
        s.ApplyUpdateWorldState(JsonSerializer.Serialize(new {language,narrative=new { npcs=new[]{new {name="Alex",gender,notes="First"}}}}));
        var voice=s.WorldState.Narrative.Npcs[0].SpeechVoice!;
        var pool=(language,gender) switch {("ru","male")=>NpcVoices.RussianMale,("ru",_)=>NpcVoices.RussianFemale,("en","male")=>NpcVoices.EnglishMale,_=>NpcVoices.EnglishFemale};
        Assert.Contains(voice.Speaker,pool);
        Assert.Equal(language,voice.Language);
        s.ApplyUpdateWorldState("""{"narrative":{"npcs":[{"id":0,"notes":"Changed","speechVoice":{"speaker":"wrong","pitch":"x-high"}}]}}""");
        Assert.Equal(voice.Speaker,s.WorldState.Narrative.Npcs[0].SpeechVoice!.Speaker);
        Assert.Equal(voice.Pitch,s.WorldState.Narrative.Npcs[0].SpeechVoice!.Pitch);
        var copy=new Storage();
        copy.ApplySavedState(JsonSerializer.Serialize(s.WorldState,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        Assert.Equal(voice.Speaker,copy.WorldState.Narrative.Npcs[0].SpeechVoice!.Speaker);
        Assert.Equal(voice.Pitch,copy.WorldState.Narrative.Npcs[0].SpeechVoice!.Pitch);
        Assert.DoesNotContain("speechVoice",new AiContextBuilder(s.WorldState).Serialize(["narrative.npcs"]));
        Assert.DoesNotContain("speechVoice",new AiContextBuilder(s.WorldState).Serialize(["narrative"]));
        L.SetWorld(L.Russian);
    }

    [Fact]
    public void SameNpcNameKeepsAssignedVoiceAndPitchDespiteConflictingModelSsml()
    {
        var ws=new WorldState{Narrative=new(){Npcs=[new(){Name="Mira",Gender="female"},new(){Name=" mira ",Gender="male"}]}};
        NpcVoices.Assign(ws);
        var assigned=NpcVoices.Find(ws,"MIRA")!;
        Assert.Same(assigned,ws.Narrative.Npcs[1].SpeechVoice);
        var speech=typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.Speech")!;
        string ssml="<speak voice=\"male\"><prosody pitch=\"x-low\">Hello</prosody></speak>";
        Assert.Equal(assigned.Speaker,speech.GetMethod("SelectVoice",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,["Mira",ssml,"eugene",false,assigned]));
        string styled=(string)speech.GetMethod("ApplyVoiceStyle",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,["Hello",ssml,"Mira",assigned])!;
        Assert.Contains($"pitch=\"{assigned.Pitch}\"",styled);
        Assert.DoesNotContain("voice=",styled);
        Assert.DoesNotContain("x-low",styled);
    }

    [Fact]
    public void MigrationKeepsKnownGenderFromExistingNpcSpeech()
    {
        string dir=Path.Combine(Path.GetTempPath(),"navidnd-voice-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path=Path.Combine(dir,"game1.json");
            File.WriteAllText(path,"""{"formatVersion":2,"language":"ru","narrative":{"npcs":[{"name":"Анна"}]},"history":[{"author":"Анна","text":"Привет","speechText":"<speak voice=\"female\">Привет</speak>"}]}""");
            Assert.True(StorageMigration.Run(dir).Ok);
            var npc=JsonNode.Parse(File.ReadAllText(path))!["narrative"]!["npcs"]![0]!;
            Assert.Equal("female",(string?)npc["gender"]);
            Assert.Contains((string)npc["speechVoice"]!["speaker"]!,NpcVoices.RussianFemale);
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact]
    public void MigrationAssignsVoicesWithoutChangingNpcMemoryOrPersonalSettings()
    {
        string dir=Path.Combine(Path.GetTempPath(),"navidnd-voice-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path=Path.Combine(dir,"game1.json");
            File.WriteAllText(path,"""{"formatVersion":2,"language":"en","narrative":{"npcs":[{"name":"Anna","gender":"female","memory":["Promise"]}]}}""");
            var result=StorageMigration.Run(dir);
            Assert.True(result.Ok,string.Join(";",result.Errors));
            var root=JsonNode.Parse(File.ReadAllText(path))!;
            var npc=root["narrative"]!["npcs"]![0]!;
            Assert.Equal(3,(int)root["formatVersion"]!);
            Assert.Contains((string)npc["speechVoice"]!["speaker"]!,NpcVoices.EnglishFemale);
            Assert.Equal("Promise",(string)npc["memory"]![0]!);
            Assert.Equal(0,StorageMigration.Run(dir).Migrated);
        }
        finally { Directory.Delete(dir,true); }
    }
}
