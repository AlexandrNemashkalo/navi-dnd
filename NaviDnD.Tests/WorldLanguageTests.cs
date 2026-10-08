using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

// Язык мира: названия генератора (места, королевства, природные области, комнаты) — на языке мира.
public class WorldLanguageTests
{
    private static bool HasCyrillic(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    private static IEnumerable<string> Names(WorldMap w) =>
        w.Places.Select(p => p.Name).Concat(w.Kingdoms.Select(k => k.Name)).Concat(w.Features.Select(f => f.Name)).Append(w.Name);

    [Fact]
    public void EnglishWorldHasEnglishNames()
    {
        var world = WorldGenerator.Generate(new WorldGenerator.Options(1234, Language: L.English));
        Assert.Equal(L.English, world.Language);
        var russian = Names(world).Where(HasCyrillic).ToList();
        Assert.True(russian.Count == 0, string.Join(", ", russian));
        Assert.False(HasCyrillic(WorldGenerator.DefaultName(1234, L.English)));
    }

    [Fact]
    public void RussianWorldKeepsRussianNames()
    {
        var world = WorldGenerator.Generate(new WorldGenerator.Options(1234));
        Assert.Equal(L.Russian, world.Language);
        Assert.Contains(Names(world), HasCyrillic);
    }

    [Fact]
    public void RoomNamesFollowWorldLanguage()
    {
        try
        {
            L.SetWorld(L.English);
            Assert.Equal("Corridor", L.W("Коридор"));
            Assert.Equal("Room 3", L.WF("Комната {0}", 3));
            Assert.Equal("floor 2", MapConfig.FloorName(1));
            L.SetWorld(L.Russian);
            Assert.Equal("2 этаж", MapConfig.FloorName(1));
        }
        finally { L.SetWorld(L.Russian); }
    }
}
