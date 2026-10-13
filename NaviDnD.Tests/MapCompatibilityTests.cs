using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using NaviDnD.Data.Models;
using NaviDnD.MapGen.Generators;

namespace NaviDnD.Tests;

public class MapCompatibilityTests
{
    // Fingerprints verified against the generator at e366b68 before adding options.
    private static readonly string[] Expected =
    [
        "19AB0F050796A18F3261F495F008674CE5EEE41630F108D1600D5B230EDC24AD",
        "CD46B3F2DC234E66835575DAFB75760C35935520AAED6BF513F3C1C55A4E4A89",
        "6F29CE5665BA632F741723165B92C6F2FB0533980E83E102C62C939B0B070C56",
        "C934AEA8D71A18011562DE276415F2427881F4C6FE6BB53A268E9C61FB8107B9",
        "612643AEDE768362343AE0F59D3D88136484978D89EE1F9D06389DA45FD61C9E",
        "651749295ACCA094FD3A126E8F9CA939366EFCFFC442A5EE8561CB9632814377",
        "B195DD2D9CF7E4A362A350AAB52947F18FC0C081AA1814D011611EEAF65D51D6",
        "AE448FC50587C25C8CB3BD3D54B8469846C89E5F5F58735F3824A004BDD71A47",
        "50FA4F77DEC8496F77574CA107270010C9C7D644A271B99180EEA8454F0AB856",
        "9602989E28D2C5ED89099688ECD417A5B6F863D884107719CED3CB8B6093A46F",
    ];

    [Fact]
    public void ExistingSeedsKeepTheirGeometry()
    {
        for (int seed = 1; seed <= 5; seed++)
        foreach (bool village in new[] { false, true })
        {
            var chunk = new MapChunk { Seed = seed, Theme = village ? "village" : "building", Style = "tavern" };
            var bands = new Dictionary<ChunkSide, char>();
            var current = village ? BuildingGenerator.GenerateVillage(chunk, MapSampleTests.Exits, bands)
                : BuildingGenerator.Generate(chunk, MapSampleTests.Exits, seed, true, bands, []);
            string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(current))));
            Assert.Equal(Expected[(seed - 1) * 2 + (village ? 1 : 0)], actual);
        }
    }
}
