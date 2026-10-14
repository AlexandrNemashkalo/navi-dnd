# Build NaviDnD.app for macOS (Apple Silicon) - on Windows. Its own chain of updates in releases-macos/ (the files are not the same
# that of Windows), arranged in the same way as in build-release.ps1:
#   (no keys) - patch: the application is rebuilt and compared with the description of the previous version
#                  (releases-macos/manifests/<version>.json); in releases-macos/patches/ - ZIP only with changes.
#                  Patches are installed by the game itself (Updates in the menu) - along with the application label.
#   -Base - new database: archive for players artifacts/NaviDnD-macOS-arm64-<version>.zip and the same in parts
#                  releases-macos/NaviDnD-macOS-payload.* (file size limit); a chain of patches from scratch. GitLab CI on tag
#                  version of the database glues the parts together and uploads the archive to Package Registry - via the link “macOS” in the release.
#   -Test - only archive in artifacts/ for checking on Mac; releases-macos/ does not move.
# NaviDnD.app according to the rules of Apple: in Contents/MacOS - only programs (game and MCP-server in one file each, .NET
# inside; picture libraries; terminal Alacritty, in which the game is played: window without frame, icon NaviDnD in Dock), in
# Contents/Resources - game files, icon and terminal settings (macos/). Voice acting (own Python 3.12 + PyTorch) - archived
# Speech/silero.zip: folders Python (python3.12, *.dist-info) the label is taken for embedded applications; the game unpacks
# it in ~/Library/Application Support/NaviDnD. The application is signed here with a local label (rcodesign): without label macOS calls
# the downloaded application was corrupted, and the label on Mac would change files and break patch checking. The label is the same
# with each build, so the build is always complete (with voice acting) - only what has changed gets into the patch.
# Downloads (with pinned versions and hashes) are cached in artifacts/macos-cache.
# Needed: .NET SDK, git, Python 3 with pip (voice wheels and ZIP), internet.
param([switch]$Base, [switch]$Test, [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$gitRoot = $root.Replace('\', '/')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryRoot ('NaviDnD-macos-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'NaviDnD.app'
$programsDir = Join-Path $app 'Contents/MacOS'
$game = Join-Path $app 'Contents/Resources'
$resources = $game
$cache = Join-Path $root 'artifacts/macos-cache'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$release = Join-Path $root 'releases-macos'
$manifests = Join-Path $release 'manifests'
$patches = Join-Path $release 'patches'
$updatesPath = Join-Path $release 'NaviDnD-macOS-updates.json'
$versionFile = 'Contents/Resources/release-version.txt'
$rawBase = 'https://gitlab.com/navitalevich/navi-dnd/-/raw/'
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Pinned downloads: Python for macOS arm64 (astral-sh/python-build-standalone), Russian model Silero, terminal
# Alacritty and its license, rcodesign (indygreg/apple-platform-rs) - label on Windows, 7-Zip - unpack .dmg.
$downloads = [ordered]@{
    'python.tar.gz'     = @('https://github.com/astral-sh/python-build-standalone/releases/download/20260924/cpython-3.12.14%2B20260924-aarch64-apple-darwin-install_only.tar.gz', '9763f43db2481a6af36af82ec40302aab7a73632f880129d07a6e81aec846277')
    'v5_5_ru.pt'        = @('https://models.silero.ai/models/tts/ru/v5_5_ru.pt', '50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437')
    'alacritty.dmg'     = @('https://github.com/alacritty/alacritty/releases/download/v0.17.0/Alacritty-v0.17.0.dmg', 'ad8d7de35fb38e43184776cac6dfee05ca325caa0b6639a06a55e54e4b026620')
    'alacritty-LICENSE' = @('https://raw.githubusercontent.com/alacritty/alacritty/v0.17.0/LICENSE-APACHE', '8884289e3969c089cd556c53fa1d3f51d86b29e52a8c1d660a55ce8292986910')
    'rcodesign.zip'     = @('https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/0.29.0/apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip', '54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa')
    '7zr.exe'           = @('https://github.com/ip7z/7zip/releases/download/26.03/7zr.exe', 'ad4c82fadcbdf93c03b4fc440f300509c7d60c5c2f4d183e35d9d70d6957037d')
    '7z-x64.exe'        = @('https://github.com/ip7z/7zip/releases/download/26.03/7z2603-x64.exe', '0859c524b8a63551848f0c246abddcb1d0b7b656b0fbfe879f8d85e61a9e6edd')
}
# Rights 755 in ZIP: everything in Contents/MacOS and voiceover installation script; the rest - 644.

function RawUrl($tag, $relative) { $rawBase + $tag + '/' + ((($relative -split '/') | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/') }
function Sha256File($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 10), $utf8) }
# File from cache (downloaded if it is not there or the hash is incorrect).
function Cached($name) {
    $url, $sha256 = $downloads[$name]
    $path = Join-Path $cache $name
    if ((Test-Path -LiteralPath $path) -and (Sha256File $path) -eq $sha256) { return $path }
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    # The developer already has the Silero model (voice acting for the game from the source): the Silero site can be very slow.
    $local = Join-Path $root "NaviDnD/Storage/Speech/silero/$name"
    if ((Test-Path -LiteralPath $local) -and (Sha256File $local) -eq $sha256) { Copy-Item -LiteralPath $local -Destination $path -Force; return $path }
    & curl.exe --fail --location --silent --show-error --connect-timeout 20 --max-time 900 --retry 2 --output "$path.part" $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
    if ((Sha256File "$path.part") -ne $sha256) { Remove-Item -LiteralPath "$path.part" -Force; throw "Checksum mismatch: $url" }
    Move-Item -LiteralPath "$path.part" -Destination $path -Force
    $path
}
# External program: progress in stderr is not considered an error (Windows PowerShell with Stop) - exit code is important.
function Native($what) {
    $ErrorActionPreference = 'Continue'
    $output = & $args[0] @($args | Select-Object -Skip 1) 2>&1 | ForEach-Object { "$_" }
    $code = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($code -ne 0) { $output | Select-Object -Last 15 | Out-Host; throw "$what failed ($code)" }
}
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
function TreeManifest($directory) {
    $prefix = [IO.Path]::GetFullPath($directory).TrimEnd('\') + '\'
    $result = @{}
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File) {
        $relative = $file.FullName.Substring($prefix.Length).Replace('\', '/')
        if ($relative -cmatch '(^|/)(Storage|logs|Updater)/') { throw "Private or reserved file in the app: $relative" }
        $result[$relative] = Sha256File $file.FullName
    }
    $result
}
function UnixMode($relative) { if ($relative -like 'Contents/MacOS/*' -or $relative -eq 'Contents/Resources/Speech/setup-silero.sh') { 493 } else { 420 } }   # 0755 / 0644
# Mach-O (64-bit or universal) - by first bytes.
function IsMachO($path) {
    $stream = [IO.File]::OpenRead($path)
    try { $head = New-Object byte[] 4; $read = $stream.Read($head, 0, 4) } finally { $stream.Dispose() }
    $read -eq 4 -and ((($head[0] -eq 0xCF) -and ($head[1] -eq 0xFA) -and ($head[2] -eq 0xED) -and ($head[3] -eq 0xFE)) -or
        (($head[0] -eq 0xCA) -and ($head[1] -eq 0xFE) -and ($head[2] -eq 0xBA) -and ($head[3] -eq 0xBE)))
}

function BuildGame() {
    New-Item -ItemType Directory -Force -Path $programsDir, $game | Out-Null
    # One file: Contents/MacOS should not contain anything other than programs, and .NET assemblies should not be programs.
    $publish = Join-Path $work 'publish'
    foreach ($project in @('NaviDnD', 'NaviDnD.McpServer')) {
        & dotnet publish (Join-Path $root "$project/$project.csproj") -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o $publish -p:DebugType=None -p:DebugSymbols=false | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    Get-ChildItem -LiteralPath $publish -Filter '*.pdb' -File -Recurse | Remove-Item -Force
    $prefix = [IO.Path]::GetFullPath($publish).TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File) {
        $relative = $file.FullName.Substring($prefix.Length)
        $target = if ((IsMachO $file.FullName) -and -not $relative.Contains('\')) { Join-Path $programsDir $relative } else { Join-Path $game $relative }
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Move-Item -LiteralPath $file.FullName -Destination $target
    }
    # Only public files from git - as in build-release.ps1: without Storage, logs and local settings.
    foreach ($asset in @('Prompts', 'sound', 'Fonts', 'GameData', 'icons')) {
        $assetOutput = Join-Path $game $asset
        if (Test-Path -LiteralPath $assetOutput) { Remove-Item -LiteralPath $assetOutput -Recurse -Force }
        $prefix = if ($asset -in @('GameData', 'icons')) { '' } else { 'NaviDnD/' }
        $files = & git -c "safe.directory=$gitRoot" -C $root ls-files "$prefix$asset"
        if ($LASTEXITCODE -ne 0) { throw 'Cannot list public assets' }
        foreach ($file in $files) {
            $target = Join-Path $game $file.Substring($prefix.Length)
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
        }
    }
    if (Get-ChildItem $game -Recurse -Directory | Where-Object Name -in @('Storage', 'logs', 'Updater')) { throw 'Private data in the app' }
    # Scripts - with line breaks Unix (otherwise bash on Mac will not execute them), before hashes are calculated.
    foreach ($script in Get-ChildItem -LiteralPath $game -Recurse -File | Where-Object { $_.Extension -eq '.sh' }) {
        $text = [IO.File]::ReadAllText($script.FullName).Replace("`r`n", "`n")
        [IO.File]::WriteAllText($script.FullName, $text, $utf8)
    }
    Set-Content (Join-Path $game 'release-version.txt') $version -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $game 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Destination (Join-Path $game 'RELEASE_NOTES.md')

    # Terminal: alacritty from .dmg (7-Zip - from its installer, without installation into the system).
    $tools = Join-Path $work 'tools'
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    Native '7-Zip unpack' (Cached '7zr.exe') x -y "-o$tools\7z" (Cached '7z-x64.exe')
    Native 'Alacritty unpack' "$tools\7z\7z.exe" e -y "-o$tools\alacritty" (Cached 'alacritty.dmg') 'Alacritty\Alacritty.app\Contents\MacOS\alacritty'
    Copy-Item -LiteralPath "$tools\alacritty\alacritty" -Destination (Join-Path $programsDir 'alacritty')
    Copy-Item -LiteralPath (Cached 'alacritty-LICENSE') -Destination (Join-Path $resources 'alacritty-LICENSE')
    Copy-Item -LiteralPath (Join-Path $root 'macos/alacritty.toml') -Destination $resources
    Copy-Item -LiteralPath (Join-Path $root 'macos/NaviDnD.icns') -Destination $resources
    $plist = [IO.File]::ReadAllText((Join-Path $root 'macos/Info.plist')).Replace("`r`n", "`n").Replace('$(VERSION)', $version)
    [IO.File]::WriteAllText((Join-Path $app 'Contents/Info.plist'), $plist, $utf8)
}

# Voice acting: wheels strictly according to macos/silero-requirements.txt (versions and hashes).
function BuildSpeech() {
    $wheels = Join-Path $work 'wheels'
    New-Item -ItemType Directory -Force -Path $wheels | Out-Null
    & $Python -I -m pip download --disable-pip-version-check --quiet --no-deps --require-hashes --only-binary=:all: `
        --platform macosx_11_0_arm64 --python-version 3.12 --implementation cp --abi cp312 `
        -r (Join-Path $root 'macos/silero-requirements.txt') -d $wheels
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download speech packages.' }
    $silero = Join-Path $work 'silero'
    & $Python -I (Join-Path $root 'macos/package.py') silero (Cached 'python.tar.gz') $wheels (Cached 'v5_5_ru.pt') `
        (Join-Path $root 'NaviDnD/Speech/THIRD_PARTY_NOTICES.md') $silero
    if ($LASTEXITCODE -ne 0) { throw 'Cannot build the speech bundle.' }
    & $Python -I (Join-Path $root 'macos/package.py') zipdir $silero (Join-Path $game 'Speech/silero.zip')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot pack the speech bundle.' }
}

# Signature of the entire application (embedded programs and libraries too), after which the files do not change.
function SignApp() {
    $tools = Join-Path $work 'tools'
    Expand-Archive -LiteralPath (Cached 'rcodesign.zip') -DestinationPath "$tools\rcodesign" -Force
    $rcodesign = (Get-ChildItem -LiteralPath "$tools\rcodesign" -Recurse -Filter 'rcodesign.exe' | Select-Object -First 1).FullName
    Native 'Signing' $rcodesign sign $app
}

function MakeArchive($output) {
    New-Item -ItemType Directory -Force -Path (Split-Path $output -Parent) | Out-Null
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
    & $Python -I (Join-Path $root 'macos/package.py') zip $app $output
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the archive.' }
    Write-Host ("macOS archive: {0} ({1:N0} MiB)" -f $output, ((Get-Item -LiteralPath $output).Length / 1MB))
}

try {
    BuildGame; BuildSpeech; SignApp
    $archive = Join-Path $root "artifacts/NaviDnD-macOS-arm64-$version.zip"
    if ($Test) { MakeArchive $archive }
    elseif ($Base) {
        $tree = TreeManifest $app
        MakeArchive $archive
        New-Item -ItemType Directory -Force -Path $release, $manifests | Out-Null
        Get-ChildItem -LiteralPath $release -Filter 'NaviDnD-macOS-payload.*' -File | Remove-Item -Force
        if (Test-Path -LiteralPath $patches) { Get-ChildItem -LiteralPath $patches -Filter 'NaviDnD-macOS-patch-*.zip' -File | Remove-Item -Force }
        # Parts of 50 MB - like the Windows base; CI stitches them back into an archive for the players.
        $parts = @()
        $source = [IO.File]::OpenRead($archive)
        try {
            $buffer = New-Object byte[] (1MB)
            for ($number = 1; $source.Position -lt $source.Length; $number++) {
                $name = 'NaviDnD-macOS-payload.{0:D3}' -f $number
                $output = [IO.File]::Create((Join-Path $release $name))
                try {
                    $remaining = 50MB
                    while ($remaining -gt 0 -and ($count = $source.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))) -gt 0) {
                        $output.Write($buffer, 0, $count); $remaining -= $count
                    }
                } finally { $output.Dispose() }
                $part = Get-Item -LiteralPath (Join-Path $release $name)
                $parts += [ordered]@{ name = $name; url = (RawUrl "v$version" "releases-macos/$name"); sha256 = (Sha256File $part.FullName); size = $part.Length }
            }
        } finally { $source.Dispose() }
        WriteJson (Join-Path $manifests "$version.json") (Sorted $tree)
        WriteJson $updatesPath ([ordered]@{ format = 1; version = $version; base = [ordered]@{ version = $version; files = $parts }; installer = $null; patches = @() })
        Set-Content (Join-Path $release 'base-version.txt') $version -Encoding ASCII
        Write-Host "macOS base $version`: $($parts.Count) parts"
    }
    else {
        if (-not (Test-Path -LiteralPath $updatesPath)) { throw 'No releases-macos/NaviDnD-macOS-updates.json: build a base first (-Base).' }
        $previous = Get-Content -LiteralPath $updatesPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $previousVersion = $previous.version
        if ([version]$version -le [version]$previousVersion) { throw "Raise Version in Directory.Build.props above $previousVersion" }
        $previousFiles = ReadDictionary (Join-Path $manifests "$previousVersion.json")
        $tree = TreeManifest $app
        $changed = @([string[]]$tree.Keys | Where-Object { $previousFiles[$_] -ne $tree[$_] } | Sort-Object -CaseSensitive)
        $removed = @([string[]]$previousFiles.Keys | Where-Object { -not $tree.Contains($_) } | Sort-Object -CaseSensitive)
        $files = [ordered]@{}; foreach ($key in $changed) { $files[$key] = $tree[$key] }
        New-Item -ItemType Directory -Force -Path $patches | Out-Null
        $patchName = "NaviDnD-macOS-patch-$previousVersion-$version.zip"
        $patchPath = Join-Path $patches $patchName
        if (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }
        $zip = [IO.Compression.ZipFile]::Open($patchPath, 'Create')
        try {
            $description = [ordered]@{ baseVersion = $previousVersion; version = $version; versionFile = $versionFile; baseFiles = $previousFiles; files = $files; removed = $removed }
            $writer = New-Object IO.StreamWriter($zip.CreateEntry('patch.json').Open(), $utf8)
            try { $writer.Write(($description | ConvertTo-Json -Depth 10)) } finally { $writer.Dispose() }
            foreach ($key in $changed) {
                $entry = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $app $key), "files/$key", [IO.Compression.CompressionLevel]::Optimal)
                # The rights Unix: game on Mac unpacks with them - the programs remain executable.
                $entry.ExternalAttributes = ((0x8000 -bor (UnixMode $key)) -shl 16)
            }
        } finally { $zip.Dispose() }
        $patchFile = Get-Item -LiteralPath $patchPath
        if ($patchFile.Length -ge 100MB) { throw "Patch is too large ($($patchFile.Length) bytes): consider a new base (-Base)" }
        WriteJson (Join-Path $manifests "$version.json") (Sorted $tree)
        WriteJson $updatesPath ([ordered]@{
            format = 1; version = $version; base = $previous.base; installer = $null
            patches = @($previous.patches) + @([ordered]@{ from = $previousVersion; to = $version; name = $patchName; url = (RawUrl "v$version" "releases-macos/patches/$patchName"); sha256 = (Sha256File $patchPath); size = $patchFile.Length })
        })
        Write-Host ("macOS patch {0}: {1} changed, {2} removed, {3:N1} MiB" -f $patchName, $changed.Count, $removed.Count, ($patchFile.Length / 1MB))
    }
} finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if ((Split-Path $resolvedWork -Parent).TrimEnd('\', '/') -ne $temporaryRoot.TrimEnd('\', '/') -or
        (Split-Path $resolvedWork -Leaf) -notlike 'NaviDnD-macos-*') { throw 'Unsafe cleanup path' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
