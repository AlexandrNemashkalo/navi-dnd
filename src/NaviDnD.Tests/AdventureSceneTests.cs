using NaviDnD.Data.Models;
using NaviDnD.Display;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NaviDnD.Tests;

public class AdventureSceneTests
{
    private static readonly Regex Ansi = new("\x1b\\[[0-9;]*m");

    [Fact]
    public void EveryBiomeAndPlaceHasTwoDifferentFullViewportImages()
    {
        var renderer = new AdventureSceneRenderer();
        foreach (var (atlas, keys) in new[] { ("nature", AdventureScene.NatureKeys), ("places", AdventureScene.PlaceKeys) })
        for (int tile = 0; tile < keys.Length; tile++)
        {
            var scene = new AdventureScene(keys[tile], atlas, tile, 0, PartsOfDay.Day, keys[tile]);
            var a = renderer.Render(scene, 87, 33, [18, 18, 22]);
            var b = renderer.Render(scene with { Variant = 1 }, 87, 33, [18, 18, 22]);
            Assert.Equal(33, a.Length);
            Assert.All(a.Concat(b), row => Assert.Equal(87, Ansi.Replace(row, "").Length));
            Assert.NotEqual(string.Join('\n', a), string.Join('\n', b));
            Assert.All(a.Concat(b), row => Assert.Contains("\x1b[38;2;", row));
        }
    }

    [Fact]
    public void SelectorCoversEveryWorldBiomeAndDoesNotExposeHiddenPlaces()
    {
        var state = new WorldState { World = new() { Id = "test", X = 0, Y = 0 } };
        foreach (char code in WorldBiomes.All.Keys)
        {
            var world = new WorldMap { Width = 1, Height = 1, Biomes = [code.ToString()] };
            var scene = AdventureScene.Select(state, world);
            Assert.Equal("nature", scene.Atlas);
            Assert.Equal(Array.IndexOf(WorldBiomes.All.Keys.ToArray(), code), scene.Tile);
            Assert.Equal(scene, AdventureScene.Select(state, world));
        }
        state.World.Place = "Secret";
        var hidden = new WorldMap { Biomes = ["f"], Places = [new() { Name = "Secret", Type = "dungeon", Hidden = true }] };
        Assert.Equal("forest", AdventureScene.Select(state, hidden).Key);
        hidden.Places[0].Hidden = false;
        Assert.Equal("dungeon", AdventureScene.Select(state, hidden).Key);
        hidden.Places[0].Type = "unfamiliar-place-type";
        Assert.Equal("forest", AdventureScene.Select(state, hidden).Key);
        Assert.Equal("plains", AdventureScene.Select(new WorldState(), null).Key);
    }

    [Fact]
    public void SelectionAndAnimationLeaveSavedStateUnchanged()
    {
        var state = new WorldState { World = new() { Id = "test", X = 0, Y = 0 } };
        var world = new WorldMap { Biomes = ["w"] };
        string saved = JsonSerializer.Serialize(state);
        var scene = AdventureScene.Select(state, world);
        var renderer = new AdventureSceneRenderer();
        var first = renderer.Render(scene, 87, 33, [18, 18, 22], 0);
        var next = renderer.Render(scene, 87, 33, [18, 18, 22], 2);
        Assert.NotEqual(string.Join('\n', first), string.Join('\n', next));
        Assert.Same(first, renderer.Render(scene, 87, 33, [18, 18, 22], AdventureSceneRenderer.FrameCount));
        Assert.Equal(saved, JsonSerializer.Serialize(state));
        var other = AdventureScene.Select(state, world, 1);
        Assert.NotEqual(scene.Variant, other.Variant);
        Assert.Equal(scene.Key, other.Key);
    }

    [Theory]
    [InlineData(AdventureSceneStyle.Braille)]
    [InlineData(AdventureSceneStyle.Mosaic)]
    [InlineData(AdventureSceneStyle.HalfBlock)]
    [InlineData(AdventureSceneStyle.Quadrants)]
    public void RenderingFitsDifferentViewportsAndNightChangesLighting(AdventureSceneStyle style)
    {
        var renderer = new AdventureSceneRenderer();
        var scene = new AdventureScene("forest", "nature", 6, 0, PartsOfDay.Day, "Forest");
        var day = renderer.Render(scene, 65, 25, [18, 18, 22], style: style);
        var night = renderer.Render(scene with { Light = PartsOfDay.Night }, 65, 25, [18, 18, 22], style: style);
        Assert.Equal(25, day.Length);
        Assert.All(day.Concat(night), row => Assert.Equal(65, Ansi.Replace(row, "").Length));
        Assert.NotEqual(string.Join('\n', day), string.Join('\n', night));
    }

    [Fact]
    public void ScenesWithoutSnowOrFireRemainCompletelyStill()
    {
        for (int i = 0; i < AdventureScene.GalleryCount; i++)
        {
            var scene = AdventureScene.Preview(i, PartsOfDay.Day);
            if (scene.Snow || scene.Fires.Count > 0) continue;
            using var first = AdventureSceneRenderer.Raster(scene, 174, 66, physicalAspect: 87f / 66);
            using var next = AdventureSceneRenderer.Raster(scene, 174, 66, AdventureSceneRenderer.FrameCount / 4, 87f / 66);
            for (int y = 0; y < 66; y++)
            for (int x = 0; x < 174; x++)
                Assert.Equal(first.GetPixel(x, y), next.GetPixel(x, y));
        }
    }

    [Theory]
    [InlineData("dungeon", "places", 7)]
    [InlineData("snow", "nature", 14)]
    [InlineData("camp", "places", 15)]
    [InlineData("tavern", "places", 10)]
    public void AnimationLoopWrapsWithSmallSteps(string key, string atlas, int tile)
    {
        var scene = new AdventureScene(key, atlas, tile, 0, PartsOfDay.Day, key);
        using var first = AdventureSceneRenderer.Raster(scene, 174, 66, physicalAspect: 87f / 66);
        using var last = AdventureSceneRenderer.Raster(scene, 174, 66, AdventureSceneRenderer.FrameCount - 1, 87f / 66);
        using var distant = AdventureSceneRenderer.Raster(scene, 174, 66, AdventureSceneRenderer.FrameCount / 4, 87f / 66);
        using var wrapped = AdventureSceneRenderer.Raster(scene, 174, 66, AdventureSceneRenderer.FrameCount, 87f / 66);
        long boundaryDifference = 0, distantDifference = 0;
        for (int y = 0; y < 66; y++)
        for (int x = 0; x < 174; x++)
        {
            var a = first.GetPixel(x, y); var b = last.GetPixel(x, y); var c = distant.GetPixel(x, y);
            Assert.Equal(a, wrapped.GetPixel(x, y));
            boundaryDifference += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
            distantDifference += Math.Abs(a.Red - c.Red) + Math.Abs(a.Green - c.Green) + Math.Abs(a.Blue - c.Blue);
        }
        Assert.True(distantDifference > 0);
        Assert.True(boundaryDifference < distantDifference / 2, $"{key}: loop jump {boundaryDifference}, distant frame {distantDifference}");
    }

    [Fact]
    public void AnimationPatchesOnlyChangedCellsAndKeepsTheirColors()
    {
        string fg = "\x1b[38;2;34;51;68m", bg = "\x1b[48;2;17;17;17m";
        string changed = "\x1b[38;2;85;102;119m";
        string patch = AdventureSceneRenderer.Patch([fg + bg + "▀▀▀"], [fg + bg + "▀" + changed + "▀" + fg + "▀"], 2, 3);
        Assert.Equal("\x1b[4;4H" + changed + bg + "▀", patch);
        Assert.Empty(AdventureSceneRenderer.Patch([fg + bg + "▀"], [fg + bg + "▀"], 2, 3));

        var renderer = new AdventureSceneRenderer();
        var scene = new AdventureScene("camp", "places", 15, 0, PartsOfDay.Day, "Camp");
        var first = renderer.Render(scene, 87, 33, [18, 18, 22], 0);
        var next = renderer.Render(scene, 87, 33, [18, 18, 22], 2);
        patch = AdventureSceneRenderer.Patch(first, next, 2, 3);
        Assert.NotEmpty(patch);
        Assert.True(patch.Length < next.Sum(r => r.Length) / 10, "A small step in firelight must not resend the whole illustration.");
    }

    [Theory]
    [InlineData(46)]
    [InlineData(47)]
    [InlineData(52)]
    [InlineData(53)]
    [InlineData(55)]
    [InlineData(58)]
    [InlineData(60)]
    [InlineData(62)]
    [InlineData(63)]
    public void FirelightKeepsEveryQuadrantInPlaceAndReversesWithoutFlicker(int galleryIndex)
    {
        var scene = AdventureScene.Preview(galleryIndex, PartsOfDay.Day);
        var renderer = new AdventureSceneRenderer();
        // Switching galleries or resuming animation can request an arbitrary phase first.
        renderer.Render(scene, 87, 33, [18, 18, 22], 31);
        var first = renderer.Render(scene, 87, 33, [18, 18, 22]);
        var shape = first.Select(row => Ansi.Replace(row, "")).ToArray();
        foreach (int phase in new[] { 1, 16, 32, 48, 64 })
        {
            var frame = renderer.Render(scene, 87, 33, [18, 18, 22], phase);
            Assert.Equal(shape, frame.Select(row => Ansi.Replace(row, "")).ToArray());
            Assert.Equal(frame, renderer.Render(scene, 87, 33, [18, 18, 22], AdventureSceneRenderer.FrameCount - phase));
        }
        Assert.NotEqual(string.Join('\n', first), string.Join('\n', renderer.Render(scene, 87, 33, [18, 18, 22], 64)));
        // Cached partitions must be replaced when switching to another image, then rebuilt deterministically.
        renderer.Render(AdventureScene.Preview((galleryIndex + 1) % AdventureScene.GalleryCount, PartsOfDay.Day), 87, 33, [18, 18, 22]);
        Assert.Equal(first, renderer.Render(scene, 87, 33, [18, 18, 22]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CampLightGraduallyRisesAndFallsWithoutChangingTheSky(int variant)
    {
        var scene = new AdventureScene("camp", "places", 15, variant, PartsOfDay.Day, "Camp");
        var levels = new List<long>();
        using var reference = AdventureSceneRenderer.Raster(scene, 174, 66, physicalAspect: 87f / 66);
        foreach (int phase in new[] { 0, 16, 32, 48, 64, 80, 96, 112, 128 })
        {
            using var frame = AdventureSceneRenderer.Raster(scene, 174, 66, phase, 87f / 66);
            long brightness = 0;
            for (int y = 0; y < 66; y++)
            for (int x = 0; x < 174; x++)
            {
                var p = frame.GetPixel(x, y);
                brightness += p.Red + p.Green + p.Blue;
                if (y < 20) Assert.Equal(reference.GetPixel(x, y), p);
            }
            levels.Add(brightness);
        }
        for (int i = 1; i <= 4; i++) Assert.True(levels[i] > levels[i - 1]);
        for (int i = 5; i <= 8; i++) Assert.True(levels[i] < levels[i - 1]);
        Assert.Equal(levels[0], levels[8]);
    }
}
