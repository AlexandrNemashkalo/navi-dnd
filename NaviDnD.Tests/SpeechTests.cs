using System.Reflection;
using System.Text.Json;
using NaviDnD.Clients;
using NaviDnD.Data.Models;

namespace NaviDnD.Tests;

public class SpeechTests
{
    [Theory]
    [InlineData("DM", "<speak voice=\"female\">Текст.</speak>", "eugene", false)]
    [InlineData("Анна", "<speak voice=\"female\">Текст.</speak>", "baya", false)]
    [InlineData("Борис", "<speak voice=\"male\">Текст.</speak>", "eugene", false)]
    // Английская игра — голоса английской модели: рассказчик — выбранный, персонажи — по полю voice.
    [InlineData("DM", "<speak voice=\"female\">Text.</speak>", "eugene", true)]
    [InlineData("Anna", "<speak voice=\"female\">Text.</speak>", "en_6", true)]
    [InlineData("Boris", "<speak voice=\"male\">Text.</speak>", "en_23", true)]
    public void NarratorKeepsSelectedVoiceAndCharactersUseTheirVoiceMarker(string author, string ssml, string expected, bool english)
    {
        var speech = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.Speech")!;
        Assert.Equal(expected, speech.GetMethod("SelectVoice", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [author, ssml, "eugene", english]));
    }

    [Fact]
    public void NarratorPitchIsLoweredAndVoiceMarkerIsRemovedBeforeSilero()
    {
        var speech = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.Speech")!;
        var style = speech.GetMethod("ApplyVoiceStyle", BindingFlags.NonPublic | BindingFlags.Static)!;
        string result = (string)style.Invoke(null, ["Текст.", "<speak voice=\"male\"><prosody pitch=\"high\" rate=\"slow\">Текст.</prosody></speak>", "DM"])!;
        Assert.Contains("pitch=\"low\"", result);
        Assert.Contains("rate=\"slow\"", result);
        Assert.DoesNotContain("pitch=\"high\"", result);
        Assert.DoesNotContain("voice=", result);
    }
    [Fact]
    public async Task SpeechFlagFollowsCurrentSettingWithoutChangingSystemBlocks()
    {
        var storage = new Storage();
        var config = new AppConfig { SpeechEnabled = false };
        var provider = new CapturingProvider();
        var client = new GameAiClient(storage.WorldState, storage, provider, config);
        var complete = typeof(GameAiClient).GetMethod("CompleteConfigured", BindingFlags.NonPublic | BindingFlags.Instance)!;
        string[] blocks = ["instructions"];
        await (Task<string>)complete.Invoke(client, [blocks, "action", "path", "model"])!;
        Assert.Equal("lang:ru\nspeechEnabled:false\naction", provider.Message);
        Assert.Same(blocks, provider.Blocks);
        config.SpeechEnabled = true;
        await (Task<string>)complete.Invoke(client, [blocks, "action", "path", "model"])!;
        Assert.Equal("lang:ru\nspeechEnabled:true\naction", provider.Message);
        // Язык игры — язык мира загруженного сохранения.
        try
        {
            storage.ApplyUpdateWorldState("""{"language":"en"}""");
            await (Task<string>)complete.Invoke(client, [blocks, "action", "path", "model"])!;
            Assert.Equal("lang:en\nspeechEnabled:true\naction", provider.Message);
        }
        finally { L.SetWorld(L.Russian); }
    }

    private sealed class CapturingProvider : IAiProvider
    {
        public string? Message;
        public IReadOnlyList<string>? Blocks;
        public Task<string> Complete(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model)
        {
            Blocks = systemBlocks;
            Message = userMessage;
            return Task.FromResult("{}");
        }
        public Task<string> CompleteWithStreaming(IReadOnlyList<string> systemBlocks, string userMessage, string actionPath, string model, Action<string>? onChunk)
            => Complete(systemBlocks, userMessage, actionPath, model);
    }
    [Theory]
    [InlineData("<speak><prosody rate=\"slow\">Тихо.</prosody><break time=\"500ms\"/>*Шаги*.</speak>", true)]
    [InlineData("<speak><break time=\"999999ms\"/></speak>", false)]
    [InlineData("<speak><audio src=\"https://example.com\"/></speak>", false)]
    [InlineData("<speak>сломанный XML", false)]
    [InlineData("<!DOCTYPE speak [<!ENTITY x SYSTEM 'file:///secret'>]><speak>&x;</speak>", false)]
    public void SpeechAcceptsSupportedMarkupAndRejectsInvalidOrExternalContent(string markup, bool valid)
    {
        var speech = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.Speech")!;
        var validate = speech.GetMethod("ValidateSsml", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(valid, validate.Invoke(null, [markup]) is string);
    }

    [Fact]
    public void StoredSpeechMarkupIsOmittedFromAiHistory()
    {
        var storage = new Storage();
        storage.WorldState.History = [];
        storage.ApplyUpdateWorldState("""{"history":[{"author":"DM","text":"<speak>Тихо.</speak>"}]}""");
        Assert.Equal("<speak>Тихо.</speak>", storage.WorldState.History.Single().SpeechText);
        string context = new AiContextBuilder(storage.WorldState).Serialize(["history"]);
        Assert.DoesNotContain("speechText", context);
        Assert.DoesNotContain("speak", context);
        Assert.Contains("Тихо.", context);
    }

    [Fact]
    public async Task HistoryPlayerDisplaysOnlyPlainTextAndKeepsSpeechMarkup()
    {
        var storage = new Storage();
        storage.WorldState.History = [];
        string shown = "";
        var playerType = typeof(AppConfig).Assembly.GetType("NaviDnD.Clients.SequentialHistoryPlayer")!;
        var player = Activator.CreateInstance(playerType, storage.WorldState, storage, new JsonSerializerOptions(),
            (Action<string>)(chunk => shown += chunk), null, null, null)!;
        var task = (Task<List<DialogMessage>>)playerType.GetMethod("PlayAsync")!.Invoke(player,
            ["""{"history":[{"text":"<speak><prosody rate=\"slow\">Тихо.</prosody></speak>"}]}"""])!;
        var result = await task;
        Assert.Equal("Тихо.", shown);
        Assert.Contains("prosody", result.Single().SpeechText);
    }

    [Fact]
    public void DisplayKeepsMechanicsWhileSpeechKeepsStressAndOmitsMechanics()
    {
        var speech = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.Speech")!;
        string raw = "<speak>Впереди з+амок и *стражник*. [HP 10→7]</speak>";
        string visible = (string)speech.GetMethod("DisplayText", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [raw])!;
        string ssml = (string)speech.GetMethod("ValidateSsml", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [raw])!;
        Assert.Equal("Впереди замок и стражник. [HP 10→7]", visible);
        Assert.Contains("з+амок", ssml);
        Assert.Contains("*стражник*", ssml);
        Assert.DoesNotContain("HP", ssml);
    }
    [Theory]
    [InlineData("**Гоблин** атакует! [Атака 15, урон 4]", "Гоблин атакует!")]
    [InlineData("Впереди\n\rтёмный   лес.", "Впереди тёмный лес.")]
    [InlineData("[HP 10→7]", "")]
    public void SpeechOmitsMechanicsButPreservesNarration(string input, string expected)
    {
        var speech = typeof(AppConfig).Assembly.GetType("NaviDnD.Helpers.Speech")!;
        var prepare = speech.GetMethod("PrepareText", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, prepare.Invoke(null, [input]));
    }
}
