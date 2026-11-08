using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using NaviDnD.Data.Art;
using SkiaSharp;

namespace NaviDnD.Tests;

public class ArtLibraryTests
{
    private static string LibraryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "ArtLibrary"));

    [Fact]
    public void DraftCatalogResolvesToActualNative32PixelSourcesWithMatchingHashes()
    {
        string root = LibraryRoot();
        var catalog = ArtCatalog.Parse(File.ReadAllText(Path.Combine(root, "catalog.draft.json")));
        Assert.NotEmpty(catalog.Assets);
        foreach (var asset in catalog.Assets.Values)
        {
            string file = Path.Combine(root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), asset.Id);
            Assert.Equal(asset.ContentHash, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
            using var bitmap = SKBitmap.Decode(file);
            Assert.NotNull(bitmap);
            Assert.Equal(32, bitmap.Width);
            Assert.Equal(32, bitmap.Height);
            if (asset.Usages.Contains("portrait"))
                Assert.Contains(Enumerable.Range(0, 32).SelectMany(y => Enumerable.Range(0, 32).Select(x => bitmap.GetPixel(x, y).Alpha)), alpha => alpha == 0);
        }
    }
}
