using System.Reflection;
using System.Text.Json;
using NaviDnD.Data.Models;
using NaviDnD.Data;
using NaviDnD.Helpers;
using NaviDnD.McpServer;

namespace NaviDnD.Tests;

[Collection(IconPackCollection.Name)]
public class InventoryPictureRegressionTests
{
    [Fact]
    public async Task CoinsClothesAndIncenseUseNamedPixelPicturesInExistingInventory()
    {
        var previous = ArtPictures.Current;
        string pack = CatalogSearch.IconPack;
        try
        {
            var art = ArtPictures.Current = ArtPictures.Load("Dnd5e");
            CatalogSearch.IconPack = CatalogSearch.DcssPack;
            var tools = new IconTools(new McpServerConfig(WorldStatePath: "unused", LogPath: null));
            string[] english = ["Copper coin", "Silver coin", "Electrum coin", "Gold coin", "Platinum coin", "Vestments", "Incense stick"];
            string[] russian = ["Медная монета", "Серебряная монета", "Электрумовая монета", "Золотая монета", "Платиновая монета", "Облачение", "Палочка благовоний"];
            var currencies = new HashSet<string>();
            var colors = new HashSet<string>();
            var select = typeof(SpellArt).Assembly.GetType("NaviDnD.MouseUiHelper")!
                .GetMethod("SetSelectedInventoryItem", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int i = 0; i < english.Length; i++)
            {
                string expected = Assert.Single(art.Find(english[i], "item"));
                Assert.Equal(expected, Assert.Single(art.Find(russian[i], "item")));
                Assert.Equal(expected, tools.FindIcon("item: " + english[i]));
                Assert.NotNull(art.ResolvePath(expected));
                var lines = SvgToBrailleConverter.Convert(expected, "")!;
                Assert.NotNull(lines);
                Assert.DoesNotContain(lines, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
                string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/inventory-picture-fixes"));
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, english[i].Replace(' ', '-') + ".json"),
                    JsonSerializer.Serialize(lines.Select(line =>
                    {
                        var cells = PixelArt.Colors(line)!;
                        return string.Concat(line.Select((c, index) =>
                        {
                            var fg = cells[index].Fg ?? [18,20,24];
                            var bg = PixelArt.Background(cells[index], [18,20,24])!;
                            return $"\u001b[38;2;{fg[0]};{fg[1]};{fg[2]}m\u001b[48;2;{bg[0]};{bg[1]};{bg[2]}m{c}";
                        }));
                    })));
                if (i < 5)
                {
                    currencies.Add(expected);
                    colors.Add(string.Join(';', lines.SelectMany(line => PixelArt.Colors(line) ?? [])
                        .Select(c => string.Join(',', c.Fg ?? []) + "/" + string.Join(',', c.Bg ?? []))));
                }
                // A previously misassigned reviewed book must not override a corrected named binding.
                var item = new HeroInventory { Name = russian[i], Image = art.Find("Magnifying glass", "item").Single() };
                string saved = JsonSerializer.Serialize(item);
                var display = new DisplayConfig();
                await SvgToBrailleConverter.PrepareAsync(expected);
                select.Invoke(null, [display, item, 0]);
                Assert.NotNull(display.SelectedImageLines);
                Assert.Equal(lines, display.SelectedImageLines!.Take(lines.Length));
                Assert.DoesNotContain(display.SelectedImageLines!, line => line.Any(c => c is >= '\u2800' and <= '\u28ff'));
                Assert.Equal(saved, JsonSerializer.Serialize(item));
            }
            Assert.Equal(5, currencies.Count);
            Assert.Equal(5, colors.Count);
        }
        finally { ArtPictures.Current = previous; CatalogSearch.IconPack = pack; }
    }
}
