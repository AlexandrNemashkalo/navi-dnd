using System.Security.Cryptography;
using System.Text.Json;

namespace NaviDnD.Installer;

// Описание обновлений релиза (releases/NaviDnD-updates.json, ссылка в GitLab Release): неизменная база (полная сборка
// частями — для первой установки и восстановления), установщик-помощник и цепочка патчей от базы до текущей версии.
// Каждый файл — со ссылкой на тег, где он опубликован, и SHA256: неизменные части не отправляются заново.
internal sealed record UpdateFile(string Name, string Url, string Sha256, long Size);
internal sealed record UpdatePatch(string From, string To, string Name, string Url, string Sha256, long Size);
internal sealed record UpdateBase(string Version, UpdateFile[] Files);
internal sealed record UpdateManifest(int Format, string Version, UpdateBase Base, UpdateFile Installer, UpdatePatch[] Patches)
{
    internal const string FileName = "NaviDnD-updates.json";
    internal const string InstallerName = "NaviDnD-Setup-win-x64.exe";
    // Помощник обновления в папке игры: копия установщика, запускается из временной папки (файлы игры не заняты).
    internal const string HelperPath = "Updater/" + InstallerName;
    private const string UrlPrefix = "https://gitlab.com/navitalevich/navi-dnd/-/raw/";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static UpdateManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, Json) ?? throw new InvalidDataException(L.T("Пустое описание обновлений."));
        manifest.Validate();
        return manifest;
    }

    private void Validate()
    {
        if (Format != 1) throw new InvalidDataException(L.T("Неизвестный формат описания обновлений."));
        if (Base?.Files is not { Length: > 0 } || Installer == null || Patches == null)
            throw new InvalidDataException(L.T("Неполное описание обновлений."));
        ParseVersion(Version);
        ParseVersion(Base.Version);
        for (int i = 0; i < Base.Files.Length; i++)
        {
            Check(Base.Files[i].Name, Base.Files[i].Url, Base.Files[i].Sha256);
            if (Base.Files[i].Name != $"NaviDnD-payload.{i + 1:D3}") throw new InvalidDataException(L.T("Части базы перечислены не по порядку."));
        }
        Check(Installer.Name, Installer.Url, Installer.Sha256);
        if (Installer.Name != InstallerName) throw new InvalidDataException(L.T("Некорректное имя установщика."));
        // Цепочка патчей без разрывов: база → … → текущая версия, каждая версия старше предыдущей.
        string at = Base.Version;
        foreach (var patch in Patches)
        {
            Check(patch.Name, patch.Url, patch.Sha256);
            if (patch.From != at || ParseVersion(patch.To) <= ParseVersion(patch.From))
                throw new InvalidDataException(L.T("Разрыв в цепочке патчей."));
            at = patch.To;
        }
        if (at != Version) throw new InvalidDataException(L.T("Цепочка патчей не доходит до версии релиза."));
    }

    private static void Check(string name, string url, string sha256)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name.StartsWith('.'))
            throw new InvalidDataException(L.T("Некорректное имя файла обновления."));
        if (!url.StartsWith(UrlPrefix, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != "gitlab.com")
            throw new InvalidDataException(L.T("Некорректный адрес обновления."));
        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit)) throw new InvalidDataException(L.T("Некорректная контрольная сумма."));
    }

    internal static Version ParseVersion(string text) =>
        System.Version.TryParse(text, out var version) && version.Build >= 0 && version.Revision < 0
            ? version : throw new InvalidDataException(L.T("Некорректная версия: ") + text);

    // Патчи от установленной версии до текущей; null — версии нет в цепочке (нужна полная установка базы).
    internal UpdatePatch[]? PatchesFrom(string installed)
    {
        if (installed == Version) return [];
        if (installed == Base.Version) return Patches;
        int index = Array.FindIndex(Patches, p => p.From == installed);
        return index < 0 ? null : Patches[index..];
    }

    internal static string Sha256(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
