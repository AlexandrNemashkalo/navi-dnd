using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NaviDnD.Installer;

namespace NaviDnD.Tests;

// Обновление патчами: база → цепочка патчей с проверкой исходных файлов, SHA256 и откатом.
public class PatchUpdateTests
{
    private static readonly Dictionary<string, string> Base = new()
    {
        ["release-version.txt"] = "1.0.0", ["NaviDnD.dll"] = "a1", ["Prompts/x.md"] = "prompt", ["old/legacy.txt"] = "old",
    };

    [Fact]
    public void PatchReplacesAddsRemovesAndKeepsStorage()
    {
        WithGame(Base, (root, temp) =>
        {
            Directory.CreateDirectory(Path.Combine(root, "Storage"));
            File.WriteAllText(Path.Combine(root, "Storage", "settings.json"), "private");
            string zip = MakePatch(temp, "1.0.0", "1.0.1", Base,
                [("release-version.txt", "1.0.1"), ("NaviDnD.dll", "a2"), ("Speech/new.py", "new")], ["old/legacy.txt"]);
            Assert.Equal("1.0.1", PatchInstaller.ApplyChain(root, [zip]));
            Assert.Equal("a2", Read(root, "NaviDnD.dll"));
            Assert.Equal("new", Read(root, "Speech/new.py"));
            Assert.Equal("prompt", Read(root, "Prompts/x.md"));
            Assert.False(File.Exists(Path.Combine(root, "old", "legacy.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "old")));   // опустевшая папка убрана
            Assert.Equal("private", Read(root, "Storage/settings.json"));
            Assert.Empty(Directory.GetDirectories(root, ".update-*"));
        });
    }

    [Fact]
    public void PatchForOtherVersionIsRejectedWithoutChanges()
    {
        var installed = new Dictionary<string, string>(Base) { ["release-version.txt"] = "0.9.0" };
        WithGame(installed, (root, temp) =>
        {
            string zip = MakePatch(temp, "1.0.0", "1.0.1", Base, [("release-version.txt", "1.0.1"), ("NaviDnD.dll", "a2")], []);
            Assert.Throws<InvalidDataException>(() => PatchInstaller.ApplyChain(root, [zip]));
            Assert.Equal("a1", Read(root, "NaviDnD.dll"));
            Assert.Equal("0.9.0", Read(root, "release-version.txt"));
        });
    }

    [Fact]
    public void ModifiedBaseFileIsRejectedWithoutChanges()
    {
        var installed = new Dictionary<string, string>(Base) { ["Prompts/x.md"] = "edited" };
        WithGame(installed, (root, temp) =>
        {
            // Патч не трогает Prompts/x.md, но полная проверка базы ловит повреждённую установку.
            string zip = MakePatch(temp, "1.0.0", "1.0.1", Base, [("release-version.txt", "1.0.1"), ("NaviDnD.dll", "a2")], []);
            var error = Assert.Throws<InvalidDataException>(() => PatchInstaller.ApplyChain(root, [zip]));
            Assert.Contains("Prompts/x.md", error.Message);
            Assert.Equal("a1", Read(root, "NaviDnD.dll"));
        });
    }

    [Fact]
    public void DamagedPatchFileIsRejectedBeforeAnyReplacement()
    {
        WithGame(Base, (root, temp) =>
        {
            string zip = MakePatch(temp, "1.0.0", "1.0.1", Base, [("release-version.txt", "1.0.1"), ("NaviDnD.dll", "a2")], [],
                corrupt: "NaviDnD.dll");
            Assert.Throws<InvalidDataException>(() => PatchInstaller.ApplyChain(root, [zip]));
            Assert.Equal("a1", Read(root, "NaviDnD.dll"));
            Assert.Equal("1.0.0", Read(root, "release-version.txt"));
            Assert.Empty(Directory.GetDirectories(root, ".update-*"));
        });
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("Storage/settings.json")]
    [InlineData("logs/ai.log")]
    [InlineData("Updater/NaviDnD-Setup-win-x64.exe")]
    [InlineData("C:/Windows/evil.dll")]
    [InlineData("dir\\file.dll")]
    [InlineData("/rooted.dll")]
    [InlineData(".update-x/file")]
    public void UnsafePatchPathsAreRejected(string path)
    {
        WithGame(Base, (root, temp) =>
        {
            string zip = MakePatch(temp, "1.0.0", "1.0.1", Base, [("release-version.txt", "1.0.1"), (path, "bad")], []);
            Assert.Throws<InvalidDataException>(() => PatchInstaller.ApplyChain(root, [zip]));
            Assert.Equal("1.0.0", Read(root, "release-version.txt"));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(root)!, "outside.txt")));
        });
    }

    [Fact]
    public void FailureInLaterPatchRollsBackWholeChain()
    {
        WithGame(Base, (root, temp) =>
        {
            Directory.CreateDirectory(Path.Combine(root, "blocked"));   // на месте нового файла — папка: замена невозможна
            File.WriteAllText(Path.Combine(root, "blocked", "keep.txt"), "keep");
            string first = MakePatch(temp, "1.0.0", "1.0.1", Base, [("release-version.txt", "1.0.1"), ("NaviDnD.dll", "a2")], ["old/legacy.txt"]);
            var mid = new Dictionary<string, string>(Base) { ["release-version.txt"] = "1.0.1", ["NaviDnD.dll"] = "a2" };
            mid.Remove("old/legacy.txt");
            string second = MakePatch(temp, "1.0.1", "1.0.2", mid, [("release-version.txt", "1.0.2"), ("blocked", "file")], []);
            Assert.ThrowsAny<IOException>(() => PatchInstaller.ApplyChain(root, [first, second]));
            Assert.Equal("1.0.0", Read(root, "release-version.txt"));
            Assert.Equal("a1", Read(root, "NaviDnD.dll"));
            Assert.Equal("old", Read(root, "old/legacy.txt"));
            Assert.Equal("keep", Read(root, "blocked/keep.txt"));
            Assert.Empty(Directory.GetDirectories(root, ".update-*"));
        });
    }

    [Fact]
    public void ChainIsOrderedFromInstalledVersionAndApplied()
    {
        WithGame(Base, (root, temp) =>
        {
            string first = MakePatch(temp, "1.0.0", "1.0.1", Base, [("release-version.txt", "1.0.1"), ("NaviDnD.dll", "a2")], []);
            var mid = new Dictionary<string, string>(Base) { ["release-version.txt"] = "1.0.1", ["NaviDnD.dll"] = "a2" };
            string second = MakePatch(temp, "1.0.1", "1.0.2", mid, [("release-version.txt", "1.0.2"), ("NaviDnD.dll", "a3")], []);
            var chain = UpdateClient.OrderChain("1.0.0", [second, first]);
            Assert.Equal([first, second], chain);
            Assert.Equal("1.0.2", PatchInstaller.ApplyChain(root, chain));
            Assert.Equal("a3", Read(root, "NaviDnD.dll"));
            Assert.Equal([second], UpdateClient.OrderChain("1.0.1", [first, second]));
        });
    }

    [Fact]
    public void ManifestSelectsPatchesAndRejectsBrokenChains()
    {
        var manifest = UpdateManifest.Parse(Manifest("1.1.6", ("1.1.4", "1.1.5"), ("1.1.5", "1.1.6")));
        Assert.Equal(2, manifest.PatchesFrom("1.1.4")!.Length);
        Assert.Equal("1.1.6", manifest.PatchesFrom("1.1.5")!.Single().To);
        Assert.Empty(manifest.PatchesFrom("1.1.6")!);
        Assert.Null(manifest.PatchesFrom("1.1.3"));   // старше базы — полная установка
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(Manifest("1.1.6", ("1.1.4", "1.1.5"), ("1.1.4", "1.1.6"))));
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(Manifest("1.1.7", ("1.1.4", "1.1.5"))));
        Assert.Throws<InvalidDataException>(() => UpdateManifest.Parse(
            Manifest("1.1.5", ("1.1.4", "1.1.5")).Replace("https://gitlab.com/navitalevich/navi-dnd/-/raw/v1.1.5/", "https://evil.example/")));
    }

    private static string Manifest(string version, params (string From, string To)[] chain)
    {
        string sha = new('a', 64);
        string File(string name, string tag) => $$"""{"name":"{{name}}","url":"https://gitlab.com/navitalevich/navi-dnd/-/raw/{{tag}}/releases/{{name}}","sha256":"{{sha}}","size":1}""";
        string patches = string.Join(",", chain.Select(p => $$"""{"from":"{{p.From}}","to":"{{p.To}}","name":"NaviDnD-patch-{{p.From}}-{{p.To}}.zip","url":"https://gitlab.com/navitalevich/navi-dnd/-/raw/v{{p.To}}/releases/patches/NaviDnD-patch-{{p.From}}-{{p.To}}.zip","sha256":"{{sha}}","size":1}"""));
        return $$"""{"format":1,"version":"{{version}}","base":{"version":"1.1.4","files":[{{File("NaviDnD-payload.001", "v1.1.4")}}]},"installer":{{File("NaviDnD-Setup-win-x64.exe", "v1.1.5")}},"patches":[{{patches}}]}""";
    }

    private static string MakePatch(string dir, string from, string to, Dictionary<string, string> baseFiles,
        (string Name, string Text)[] files, string[] removed, string? corrupt = null)
    {
        string path = Path.Combine(dir, $"NaviDnD-patch-{from}-{to}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var description = new
        {
            baseVersion = from, version = to,
            baseFiles = baseFiles.ToDictionary(f => f.Key, f => Hash(f.Value)),
            files = files.ToDictionary(f => f.Name, f => Hash(f.Text)),
            removed,
        };
        using (var writer = new StreamWriter(archive.CreateEntry("patch.json").Open()))
            writer.Write(JsonSerializer.Serialize(description));
        foreach (var (name, text) in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry("files/" + name).Open());
            writer.Write(name == corrupt ? text + "!" : text);
        }
        return path;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string Read(string root, string name) => File.ReadAllText(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));

    private static void WithGame(Dictionary<string, string> files, Action<string, string> action)
    {
        string temp = Path.Combine(Path.GetTempPath(), "NaviDnD-patch-test-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(temp, "game"), work = Path.Combine(temp, "work");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(work);
        foreach (var (name, text) in files)
        {
            string path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);   // UTF-8 без BOM — те же байты, что и Hash(text)
        }
        try { action(root, work); } finally { Directory.Delete(temp, true); }
    }
}
