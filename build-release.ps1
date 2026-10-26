# Build release.
#   (without keys) - patch release: the game and MCP are reassembled, compared with the description of the previous version
#                  (releases/manifests/<version>.json), in releases/patches/ - ZIP only with changes and deletions.
#                  The database (parts payload + Silero) and the installer are not rebuilt or sent again: links
#                  lead to the tags where they are published. The installer is rebuilt only if its sources have changed.
#   -Base - new complete database (parts payload with Silero, installer, file description), chain of patches from scratch.
#   -AdoptBase — declare already assembled releases/NaviDnD-payload.* as a base (without rebuilding): description of its files and
#                  NaviDnD-updates.json without patches.
# Assembly for macOS - its own chain in releases-macos/ (build-macos.ps1): patch for it is assembled along with the release, and if
# it doesn’t exist yet - the base. -NoMac - without it (links to the previous one will remain in the release).
param([switch]$Base, [switch]$AdoptBase, [switch]$NoMac)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$gitRoot = $root.Replace('\', '/')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryRoot ('NaviDnD-release-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'p'
$release = Join-Path $root 'releases'
$manifests = Join-Path $release 'manifests'
$patches = Join-Path $release 'patches'
$updatesPath = Join-Path $release 'NaviDnD-updates.json'
$macUpdatesPath = Join-Path $root 'releases-macos/NaviDnD-macOS-updates.json'
$installerName = 'NaviDnD-Setup-win-x64.exe'
$rawBase = 'https://gitlab.com/navitalevich/navi-dnd/-/raw/'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

function RawUrl($tag, $relative) { $rawBase + $tag + '/' + ((($relative -split '/') | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/') }
function Sha256File($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Sha256Stream($stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}
function WriteJson($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 10), $utf8) }
function ReadDictionary($path) {
    $result = [ordered]@{}
    foreach ($p in (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json).PSObject.Properties) { $result[$p.Name] = $p.Value }
    $result
}
function Sorted($dictionary) {
    $result = [ordered]@{}
    foreach ($key in ([string[]]$dictionary.Keys | Sort-Object -CaseSensitive)) { $result[$key] = $dictionary[$key] }
    $result
}
# Installed game files: path via “/” → SHA256. User Storage/, logs/ and assistant Updater/ are not a game.
function TreeManifest($directory) {
    $prefix = [IO.Path]::GetFullPath($directory).TrimEnd('\') + '\'
    $result = @{}
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        if ($relative -match '^(Storage|logs|Updater)/') { throw "Private or reserved file in payload: $relative" }
        $result[$relative] = Sha256File $file.FullName
    }
    $result
}
# Description of database files directly from its parts (ZIP): without unpacking to disk.
function PayloadManifest() {
    $parts = Get-ChildItem -LiteralPath $release -Filter 'NaviDnD-payload.*' -File | Sort-Object Name
    if (-not $parts) { throw 'No payload parts in releases/' }
    $zip = Join-Path $work 'base.zip'
    $output = [IO.File]::Create($zip)
    try { foreach ($part in $parts) { $input = [IO.File]::OpenRead($part.FullName); try { $input.CopyTo($output) } finally { $input.Dispose() } } }
    finally { $output.Dispose() }
    $result = @{}; $baseVersion = $null
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\', '/')
            if ($name.EndsWith('/')) { continue }
            $stream = $entry.Open()
            try { $result[$name] = Sha256Stream $stream } finally { $stream.Dispose() }
            if ($name -eq 'release-version.txt') {
                $reader = New-Object IO.StreamReader($entry.Open()); try { $baseVersion = $reader.ReadToEnd().Trim() } finally { $reader.Dispose() }
            }
        }
    } finally { $archive.Dispose() }
    if (-not $baseVersion) { throw 'Payload has no release-version.txt' }
    @{ Version = $baseVersion; Files = $result }
}
function BaseParts($tag) {
    @(Get-ChildItem -LiteralPath $release -Filter 'NaviDnD-payload.*' -File | Sort-Object Name | ForEach-Object {
        [ordered]@{ name = $_.Name; url = (RawUrl $tag "releases/$($_.Name)"); sha256 = (Sha256File $_.FullName); size = $_.Length }
    })
}
# Installer source fingerprint (no version number): unchanged - installer is not rebuilt.
function InstallerSource() {
    $files = & git -c "safe.directory=$gitRoot" -C $root ls-files -s -- 'NaviDnD.Installer' 'NaviDnD/dnd.ico' 'NaviDnD.Data/L.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot fingerprint installer sources' }
    $untracked = & git -c "safe.directory=$gitRoot" -C $root status --porcelain -- 'NaviDnD.Installer' 'NaviDnD.Data/L.cs'
    if ($untracked) { throw 'Commit installer sources before building a release' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes(($files -join "`n"))))).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
}
function PublishInstaller() {
    # Build output is on screen: otherwise it would end up in the function result along with the installer description.
    & dotnet publish "$root/NaviDnD.Installer/NaviDnD.Installer.csproj" -c Release -r win-x64 --self-contained true -o "$work/installer" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
    Copy-Item "$work/installer/NaviDnD.Installer.exe" "$release/$installerName" -Force
    $file = Get-Item -LiteralPath "$release/$installerName"
    [ordered]@{ name = $installerName; url = (RawUrl "v$version" "releases/$installerName"); sha256 = (Sha256File $file.FullName); size = $file.Length; source = (InstallerSource) }
}
# Game without Silero: published projects, public tracked resources, version, license, notes.
function BundleWindowsTerminal() {
    $cache = Join-Path $root 'artifacts/alacritty'
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $downloads = @{
        'alacritty.exe' = @('https://github.com/alacritty/alacritty/releases/download/v0.17.0/Alacritty-v0.17.0-portable.exe', '5ad70ddc5c2a2bfe84084a2c4c73558e9360914f035a902d7a69277be7249f1f')
        'alacritty-LICENSE' = @('https://raw.githubusercontent.com/alacritty/alacritty/v0.17.0/LICENSE-APACHE', '8884289e3969c089cd556c53fa1d3f51d86b29e52a8c1d660a55ce8292986910')
    }
    foreach ($name in $downloads.Keys) {
        $file = Join-Path $cache $name
        $source = $downloads[$name]
        if (!(Test-Path -LiteralPath $file) -or (Sha256File $file) -ne $source[1]) {
            Invoke-WebRequest -UseBasicParsing -Uri $source[0] -OutFile $file
        }
        if ((Sha256File $file) -ne $source[1]) { throw "Windows terminal checksum mismatch: $name" }
        Copy-Item -LiteralPath $file -Destination (Join-Path $payload $name)
    }
}

function BuildGame() {
    New-Item -ItemType Directory -Force -Path $payload | Out-Null
    foreach ($project in @('NaviDnD', 'NaviDnD.McpServer')) {
        & dotnet publish (Join-Path $root "$project/$project.csproj") -c Release -r win-x64 --self-contained true -o $payload -p:DebugType=None -p:DebugSymbols=false | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    # DebugType=None covers managed symbols only; native packages (SkiaSharp, HarfBuzzSharp) ship their own ~100 MB of .pdb.
    Get-ChildItem -LiteralPath $payload -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    # Only public tracked assets: never package Storage, logs, dev mocks or local settings.
    foreach ($asset in @('Prompts', 'sound', 'Fonts', 'GameData', 'icons')) {
        $assetOutput = Join-Path $payload $asset
        if (Test-Path -LiteralPath $assetOutput) {
            if (-not ([IO.Path]::GetFullPath($assetOutput).StartsWith([IO.Path]::GetFullPath($payload) + [IO.Path]::DirectorySeparatorChar))) { throw 'Unsafe asset path' }
            Remove-Item -LiteralPath $assetOutput -Recurse -Force
        }
        $prefix = if ($asset -in @('GameData', 'icons')) { '' } else { 'NaviDnD/' }
        $files = & git -c "safe.directory=$gitRoot" -C $root ls-files "$prefix$asset"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot list public assets' }
        foreach ($file in $files) {
            $target = Join-Path $payload $file.Substring($prefix.Length)
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
        }
    }
    if (Get-ChildItem $payload -Recurse -Directory | Where-Object Name -in @('Storage', 'logs', 'Updater')) { throw 'Private data in payload' }
    if (Test-Path -LiteralPath (Join-Path $payload 'Speech/silero')) { throw 'Speech bundle must come from the base' }
    BundleWindowsTerminal
    Set-Content (Join-Path $payload 'release-version.txt') $version -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $payload 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Destination (Join-Path $payload 'RELEASE_NOTES.md')
}
# Old format for clients ≤1.1.4 (they only understand this): installer + database parts. New installer himself
# will download patches after the database.
function WriteLegacySums($updates) {
    $lines = @("$($updates.installer.sha256)  $installerName") + @($updates.base.files | ForEach-Object { "$($_.sha256)  $($_.name)" })
    [IO.File]::WriteAllText("$release/SHA256SUMS.txt", (($lines -join "`n") + "`n"), $utf8)
}
function BuildMac() {
    if ($NoMac) { return }
    $macArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'build-macos.ps1'))
    if (-not (Test-Path -LiteralPath $macUpdatesPath)) { $macArguments += '-Base' }
    & powershell.exe @macArguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'macOS build failed' }
}
function WriteReleaseMetadata($updates) {
    # The first link is for people: “📦 Download NaviDnD (all installation files)” in the “Packages” group above the rest (archive folder releases/, GitLab collects it on the fly); further - official ones,
    # the game searches for them by name. The name in the \u-escape: script without BOM, Windows PowerShell 5.1 reads it as ANSI.
    $links = @(
        [ordered]@{ name = [regex]::Unescape('\ud83d\udce6 \u0421\u043a\u0430\u0447\u0430\u0442\u044c NaviDnD (\u0432\u0441\u0435 \u0444\u0430\u0439\u043b\u044b \u0443\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0438)'); url = "https://gitlab.com/navitalevich/navi-dnd/-/archive/v$version/navi-dnd-v$version-releases.zip?path=releases"; link_type = 'package' }
        [ordered]@{ name = 'SHA256SUMS.txt'; url = (RawUrl "v$version" 'releases/SHA256SUMS.txt') }
        [ordered]@{ name = 'NaviDnD-updates.json'; url = (RawUrl "v$version" 'releases/NaviDnD-updates.json') }
        [ordered]@{ name = $installerName; url = $updates.installer.url }
    ) + @($updates.base.files | ForEach-Object { [ordered]@{ name = $_.name; url = $_.url } }) +
        @($updates.patches | ForEach-Object { [ordered]@{ name = $_.name; url = $_.url } })
    # macOS: archive of the database for players (CI posts it in Package Registry on the base tag, then - patches from the game) and
    # description of the chain by which the game on Mac finds patches. The name is “Download for macOS (Apple Silicon)” with apple.
    if (Test-Path -LiteralPath $macUpdatesPath) {
        $macBase = (Get-Content -LiteralPath $macUpdatesPath -Raw -Encoding UTF8 | ConvertFrom-Json).base.version
        $links = @($links[0]) + @(
            [ordered]@{ name = [regex]::Unescape('\ud83c\udf4e \u0421\u043a\u0430\u0447\u0430\u0442\u044c \u0434\u043b\u044f macOS (Apple Silicon)'); url = "https://gitlab.com/api/v4/projects/navitalevich%2Fnavi-dnd/packages/generic/NaviDnD-macOS/$macBase/NaviDnD-macOS-arm64-$macBase.zip"; link_type = 'package' }
            [ordered]@{ name = 'NaviDnD-macOS-updates.json'; url = (RawUrl "v$version" 'releases-macos/NaviDnD-macOS-updates.json') }
        ) + @($links | Select-Object -Skip 1)
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $root '.gitlab') | Out-Null
    WriteJson (Join-Path $root '.gitlab/release.json') ([ordered]@{
        name = "NaviDnD $version"; tag_name = "v$version"
        description = [IO.File]::ReadAllText((Join-Path $root 'RELEASE_NOTES.md'))
        assets = @{ links = $links }
    })
}

try {
New-Item -ItemType Directory -Force -Path $work, $release, $manifests | Out-Null

if ($AdoptBase) {
    $baseInfo = PayloadManifest
    WriteJson (Join-Path $manifests "$($baseInfo.Version).json") (Sorted $baseInfo.Files)
    $setup = Get-Item -LiteralPath "$release/$installerName"
    $updates = [ordered]@{
        format = 1; version = $baseInfo.Version
        base = [ordered]@{ version = $baseInfo.Version; files = (BaseParts "v$($baseInfo.Version)") }
        # The patch database installer does not understand: source 'legacy' - the next release will compile a new one.
        installer = [ordered]@{ name = $installerName; url = (RawUrl "v$($baseInfo.Version)" "releases/$installerName"); sha256 = (Sha256File $setup.FullName); size = $setup.Length; source = 'legacy' }
        patches = @()
    }
    WriteJson $updatesPath $updates
    Write-Host "Base adopted: $($baseInfo.Version), $($baseInfo.Files.Count) files"
}
elseif ($Base) {
    BuildGame
    & (Join-Path $root 'prepare-silero-bundle.ps1') -Destination (Join-Path $payload 'Speech/silero')
    $tree = TreeManifest $payload
    Compress-Archive -Path "$payload/*" -DestinationPath "$work/payload.zip" -Force
    Get-ChildItem -LiteralPath $release -Filter 'NaviDnD-payload.*' -File | Remove-Item -Force
    $inputStream = [IO.File]::OpenRead("$work/payload.zip")
    try {
        $buffer = New-Object byte[] (1MB)
        $partNumber = 1
        while ($inputStream.Position -lt $inputStream.Length) {
            $outputStream = [IO.File]::Create((Join-Path $release ('NaviDnD-payload.{0:D3}' -f $partNumber)))
            try {
                $remaining = 50MB
                while ($remaining -gt 0 -and ($count = $inputStream.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))) -gt 0) {
                    $outputStream.Write($buffer, 0, $count); $remaining -= $count
                }
            } finally { $outputStream.Dispose() }
            $partNumber++
        }
    } finally { $inputStream.Dispose() }
    if (Test-Path -LiteralPath $patches) { Get-ChildItem -LiteralPath $patches -Filter 'NaviDnD-patch-*.zip' -File | Remove-Item -Force }
    WriteJson (Join-Path $manifests "$version.json") (Sorted $tree)
    $updates = [ordered]@{ format = 1; version = $version; base = [ordered]@{ version = $version; files = (BaseParts "v$version") }; installer = (PublishInstaller); patches = @() }
    WriteJson $updatesPath $updates
    WriteLegacySums $updates
    BuildMac
    WriteReleaseMetadata $updates
}
else {
    if (-not (Test-Path -LiteralPath $updatesPath)) { throw 'No releases/NaviDnD-updates.json: build a base first (-Base or -AdoptBase).' }
    $previous = Get-Content -LiteralPath $updatesPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $previousVersion = $previous.version
    if ([version]$version -le [version]$previousVersion) { throw "Raise Version in Directory.Build.props above $previousVersion" }
    $previousFiles = ReadDictionary (Join-Path $manifests "$previousVersion.json")
    BuildGame
    $tree = TreeManifest $payload
    # The voice acting set is not rebuilt - it is from the database, without changes.
    foreach ($key in $previousFiles.Keys) { if ($key.StartsWith('Speech/silero/')) { $tree[$key] = $previousFiles[$key] } }
    $changed = @([string[]]$tree.Keys | Where-Object { $previousFiles[$_] -ne $tree[$_] } | Sort-Object -CaseSensitive)
    $removed = @([string[]]$previousFiles.Keys | Where-Object { -not $tree.Contains($_) } | Sort-Object -CaseSensitive)
    $files = [ordered]@{}; foreach ($key in $changed) { $files[$key] = $tree[$key] }
    New-Item -ItemType Directory -Force -Path $patches | Out-Null
    $patchName = "NaviDnD-patch-$previousVersion-$version.zip"
    $patchPath = Join-Path $patches $patchName
    if (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }
    $archive = [IO.Compression.ZipFile]::Open($patchPath, 'Create')
    try {
        $description = [ordered]@{ baseVersion = $previousVersion; version = $version; baseFiles = $previousFiles; files = $files; removed = $removed }
        $writer = New-Object IO.StreamWriter($archive.CreateEntry('patch.json').Open(), $utf8)
        try { $writer.Write(($description | ConvertTo-Json -Depth 10)) } finally { $writer.Dispose() }
        foreach ($key in $changed) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $payload $key), "files/$key", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose() }
    $patchFile = Get-Item -LiteralPath $patchPath
    if ($patchFile.Length -ge 100MB) { throw "Patch is too large ($($patchFile.Length) bytes): consider a new base (-Base)" }
    WriteJson (Join-Path $manifests "$version.json") (Sorted $tree)
    # Assistant installer: rebuilds only when its sources change.
    $installer = $previous.installer
    if ($previous.installer.source -ne (InstallerSource)) { $installer = PublishInstaller }
    $updates = [ordered]@{
        format = 1; version = $version; base = $previous.base; installer = $installer
        patches = @($previous.patches) + @([ordered]@{ from = $previousVersion; to = $version; name = $patchName; url = (RawUrl "v$version" "releases/patches/$patchName"); sha256 = (Sha256File $patchPath); size = $patchFile.Length })
    }
    WriteJson $updatesPath $updates
    WriteLegacySums $updates
    BuildMac
    WriteReleaseMetadata $updates
    Write-Host ("Patch {0}: {1} changed, {2} removed, {3:N1} MiB" -f $patchName, $changed.Count, $removed.Count, ($patchFile.Length / 1MB))
}
Get-ChildItem $release -File -Recurse | ForEach-Object { if ($_.Length -ge 100MB) { throw "File size limit exceeded: $($_.Name)" } }
Write-Host "Release ready: $release"
} finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if ((Split-Path $resolvedWork -Parent).TrimEnd('\', '/') -ne $temporaryRoot.TrimEnd('\', '/') -or
        (Split-Path $resolvedWork -Leaf) -notlike 'NaviDnD-release-*') { throw 'Unsafe cleanup path' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
