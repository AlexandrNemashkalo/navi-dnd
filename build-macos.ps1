# Сборка NaviDnD.app для macOS (Apple Silicon) — на Windows. Своя цепочка обновлений в releases-macos/ (файлы не те,
# что у Windows), устроенная так же, как в build-release.ps1:
#   (без ключей) — патч: приложение собирается заново и сравнивается с описанием предыдущей версии
#                  (releases-macos/manifests/<версия>.json); в releases-macos/patches/ — ZIP только с изменениями.
#                  Патчи ставит сама игра (Обновления в меню) — вместе с подписью приложения.
#   -Base        — новая база: архив для игроков artifacts/NaviDnD-macOS-arm64-<версия>.zip и он же частями
#                  releases-macos/NaviDnD-macOS-payload.* (лимит размера файлов); цепочка патчей с нуля. GitLab CI на теге
#                  версии базы склеивает части и выкладывает архив в Package Registry — по ссылке «macOS» в релизе.
#   -Test        — только архив в artifacts/ для проверки на Mac; releases-macos/ не трогается.
# NaviDnD.app по правилам Apple: в Contents/MacOS — только программы (игра и MCP-сервер одним файлом каждый, .NET
# внутри; библиотеки картинок; терминал Alacritty, в котором игра идёт: окно без рамки, значок NaviDnD в Dock), в
# Contents/Resources — файлы игры, значок и настройки терминала (macos/). Озвучка (свой Python 3.12 + PyTorch) — архивом
# Speech/silero.zip: папки Python (python3.12, *.dist-info) подпись принимает за вложенные приложения; игра распаковывает
# его в ~/Library/Application Support/NaviDnD. Приложение подписывается здесь же локальной подписью (rcodesign): без подписи macOS называет
# скачанное приложение повреждённым, а подпись на Mac изменила бы файлы и сломала проверку патчей. Подпись одинакова
# при каждой сборке, поэтому сборка всегда полная (с озвучкой) — в патч попадает только изменившееся.
# Загрузки (с закреплёнными версиями и хешами) кешируются в artifacts/macos-cache.
# Нужны: .NET SDK, git, Python 3 с pip (колёса озвучки и ZIP), интернет.
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

# Закреплённые загрузки: Python для macOS arm64 (astral-sh/python-build-standalone), русская модель Silero, терминал
# Alacritty и его лицензия, rcodesign (indygreg/apple-platform-rs) — подпись на Windows, 7-Zip — распаковать .dmg.
$downloads = [ordered]@{
    'python.tar.gz'     = @('https://github.com/astral-sh/python-build-standalone/releases/download/20260924/cpython-3.12.14%2B20260924-aarch64-apple-darwin-install_only.tar.gz', '9763f43db2481a6af36af82ec40302aab7a73632f880129d07a6e81aec846277')
    'v5_5_ru.pt'        = @('https://models.silero.ai/models/tts/ru/v5_5_ru.pt', '50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437')
    'alacritty.dmg'     = @('https://github.com/alacritty/alacritty/releases/download/v0.17.0/Alacritty-v0.17.0.dmg', 'ad8d7de35fb38e43184776cac6dfee05ca325caa0b6639a06a55e54e4b026620')
    'alacritty-LICENSE' = @('https://raw.githubusercontent.com/alacritty/alacritty/v0.17.0/LICENSE-APACHE', '8884289e3969c089cd556c53fa1d3f51d86b29e52a8c1d660a55ce8292986910')
    'rcodesign.zip'     = @('https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/0.29.0/apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip', '54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa')
    '7zr.exe'           = @('https://github.com/ip7z/7zip/releases/download/26.03/7zr.exe', 'ad4c82fadcbdf93c03b4fc440f300509c7d60c5c2f4d183e35d9d70d6957037d')
    '7z-x64.exe'        = @('https://github.com/ip7z/7zip/releases/download/26.03/7z2603-x64.exe', '0859c524b8a63551848f0c246abddcb1d0b7b656b0fbfe879f8d85e61a9e6edd')
}
# Права 755 в ZIP: всё в Contents/MacOS и скрипт установки озвучки; остальное — 644.

function RawUrl($tag, $relative) { $rawBase + $tag + '/' + ((($relative -split '/') | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/') }
function Sha256File($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 10), $utf8) }
# Файл из кеша (скачивается, если его нет или хеш не тот).
function Cached($name) {
    $url, $sha256 = $downloads[$name]
    $path = Join-Path $cache $name
    if ((Test-Path -LiteralPath $path) -and (Sha256File $path) -eq $sha256) { return $path }
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    # Модель Silero уже есть у разработчика (озвучка игры из исходников): сайт Silero бывает очень медленным.
    $local = Join-Path $root "NaviDnD/Storage/Speech/silero/$name"
    if ((Test-Path -LiteralPath $local) -and (Sha256File $local) -eq $sha256) { Copy-Item -LiteralPath $local -Destination $path -Force; return $path }
    & curl.exe --fail --location --silent --show-error --connect-timeout 20 --max-time 900 --retry 2 --output "$path.part" $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
    if ((Sha256File "$path.part") -ne $sha256) { Remove-Item -LiteralPath "$path.part" -Force; throw "Checksum mismatch: $url" }
    Move-Item -LiteralPath "$path.part" -Destination $path -Force
    $path
}
# Внешняя программа: ход работы в stderr не считается ошибкой (Windows PowerShell со Stop) — важен код выхода.
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
# Mach-O (64-бит или универсальный) — по первым байтам.
function IsMachO($path) {
    $stream = [IO.File]::OpenRead($path)
    try { $head = New-Object byte[] 4; $read = $stream.Read($head, 0, 4) } finally { $stream.Dispose() }
    $read -eq 4 -and ((($head[0] -eq 0xCF) -and ($head[1] -eq 0xFA) -and ($head[2] -eq 0xED) -and ($head[3] -eq 0xFE)) -or
        (($head[0] -eq 0xCA) -and ($head[1] -eq 0xFE) -and ($head[2] -eq 0xBA) -and ($head[3] -eq 0xBE)))
}

function BuildGame() {
    New-Item -ItemType Directory -Force -Path $programsDir, $game | Out-Null
    # Одним файлом: в Contents/MacOS не должно быть ничего, кроме программ, а сборки .NET — не программы.
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
    # Только публичные файлы из git — как в build-release.ps1: без Storage, логов и локальных настроек.
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
    # Скрипты — с переводами строк Unix (иначе bash на Mac их не выполнит), до подсчёта хешей.
    foreach ($script in Get-ChildItem -LiteralPath $game -Recurse -File | Where-Object { $_.Extension -eq '.sh' }) {
        $text = [IO.File]::ReadAllText($script.FullName).Replace("`r`n", "`n")
        [IO.File]::WriteAllText($script.FullName, $text, $utf8)
    }
    Set-Content (Join-Path $game 'release-version.txt') $version -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $game 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Destination (Join-Path $game 'RELEASE_NOTES.md')

    # Терминал: alacritty из .dmg (7-Zip — из своего установщика, без установки в систему).
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

# Озвучка: колёса строго по macos/silero-requirements.txt (версии и хеши).
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

# Подпись всего приложения (вложенные программы и библиотеки — тоже), после неё файлы не меняются.
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
        # Части по 50 МБ — как у базы Windows; CI склеивает их обратно в архив для игроков.
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
                # Права Unix: игра на Mac распаковывает с ними — программы остаются исполняемыми.
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
