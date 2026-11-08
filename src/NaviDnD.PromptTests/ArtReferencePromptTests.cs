using NaviDnD.Clients;
using NaviDnD.Helpers;
using Xunit.Abstractions;

namespace NaviDnD.PromptTests;

// One deliberate real-model turn, isolated from player saves. Never part of the ordinary test project.
public sealed class ArtReferencePromptTests(ITestOutputHelper output)
{
    [Fact]
    public async Task LootedUtilityItemsResolveToTheirOwnRenderablePictures()
    {
        var previous = ArtPictures.Current;
        string previousPack = CatalogSearch.IconPack;
        string[] requested = ["Spyglass", "Magnifying glass", "Hourglass", "Manacles", "Tent, two-person", "Hunting trap"];
        try
        {
            ArtPictures.Current = ArtPictures.Load("Dnd5e");
            var storage = PromptScenario.LoadUiTestFixture("opportunity_attack_game.json");
            storage.SetIconPack(CatalogSearch.DcssPack);
            storage.WorldState.Combat = null;
            storage.WorldState.Map.Entities = [];
            storage.WorldState.Hero!.Position = [3, 3];
            storage.WorldState.Hero.Inventory = [];
            storage.WorldState.Map.Objects = [new()
            {
                Name = "Открытый сундук со снаряжением", Symbol = "[C]", Position = [4, 3],
                AiInfo = ["Unlocked, already open, ordinary chest. The hero owns its contents and can safely take them. Contains exactly one of each standard D&D equipment entry: Spyglass; Magnifying glass; Hourglass; Manacles; Tent, two-person; Hunting trap. No hidden trap, no checks required, no NPCs or ownership dispute. All six objects are plainly visible and fit the hero's carrying capacity."]
            }];
            var context = new AiContextBuilder(storage.WorldState, [(3,3), (4,3)]);
            var result = await PromptScenario.CallRealAiWithAutoAnswersAsync("Dnd5e", "SendAction", storage,
                context.MinimalState("Забираю из своего открытого сундука подзорную трубу, увеличительное стекло, песочные часы, кандалы, двухместную палатку и охотничий капкан. Укладываю всё это в свой рюкзак."));
            output.WriteLine(result.RawResponse);
            // The running game resolves catalog references before applying history patches.
            var parsed = PromptScenario.ApplyAndParse(storage, CatalogReferences.Resolve(result.RawResponse));
            Assert.NotEmpty(parsed.HistoryTexts);
            foreach (string reference in requested)
            {
                var expected = EquipmentDatabase.Find(reference)!;
                var item = Assert.Single(storage.WorldState.Hero.Inventory,
                    item => EquipmentDatabase.Find(item.Name)?.NameEn.Equals(expected.NameEn, StringComparison.OrdinalIgnoreCase) == true);
                Assert.False(string.IsNullOrEmpty(item.Image), reference);
                Assert.Contains(item.Image!, ArtPictures.Current.Find(reference, "item"));
                Assert.NotNull(SvgToBrailleConverter.Convert(item.Image!, ""));
                output.WriteLine(reference + " -> " + item.Image);
            }
            Assert.Equal(6, storage.WorldState.Hero.Inventory.Count);
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = previousPack; }
    }
}
