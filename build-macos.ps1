# Сборка для macOS (Apple Silicon) — на Windows. Своя цепочка обновлений в releases-macos/ (файлы игры не те, что у
# Windows), устроенная так же, как в build-release.ps1:
#   (без ключей) — патч: игра и MCP собираются заново и сравниваются с описанием предыдущей версии
#                  (releases-macos/manifests/<версия>.json); в releases-macos/patches/ — ZIP только с изменениями.
#                  Озвучка не пересобирается — она из базы. Патчи ставит сама игра (Обновления в меню).
#   -Base        — новая база: полная сборка с озвучкой, архив для игроков artifacts/NaviDnD-macOS-arm64-<версия>.zip
#                  и он же частями releases-macos/NaviDnD-macOS-payload.* (лимит размера файлов); цепочка патчей с нуля.
#                  GitLab CI на теге версии базы склеивает части и выкладывает архив в Package Registry — по ссылке
#                  «macOS» в релизе. Игроки с ним доходят до текущей версии патчами.
#   -Test        — только архив в artifacts/ для проверки на Mac; releases-macos/ не трогается.
# Внутри архива всё готовое: .NET (игра и MCP-сервер — по одному файлу, среда .NET встроена: macOS при запуске не
# проверяет отдельных библиотек), озвучка (свой Python 3.12 + PyTorch). Игрок запускает NaviDnD двойным щелчком и один
# раз разрешает его в настройках; карантин загрузки с остальной папки игра снимает сама. Программы подписываются здесь
# же локальной подписью (rcodesign): Apple Silicon не запускает неподписанное, у неподписанного macOS не предлагает
# «Всё равно открыть», а подпись на Mac изменила бы файлы и сломала проверку патчей.
# Нужны: .NET SDK, git, Python 3 с pip (колёса озвучки и ZIP базы), интернет.
param([switch]$Base, [switch]$Test, [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = $PSScriptRoot
$gitRoot = $root.Replace('\', '/')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryRoot ('NaviDnD-macos-' + [guid]::NewGuid().ToString('N'))
$game = Join-Path $work 'NaviDnD'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$release = Join-Path $root 'releases-macos'
$manifests = Join-Path $release 'manifests'
$patches = Join-Path $release 'patches'
$updatesPath = Join-Path $release 'NaviDnD-macOS-updates.json'
$rawBase = 'https://gitlab.com/navitalevich/navi-dnd/-/raw/'
$utf8 = New-Object Text.UTF8Encoding($false)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Закреплённые загрузки с хешами: Python для macOS arm64 (astral-sh/python-build-standalone), русская модель Silero,
# rcodesign (indygreg/apple-platform-rs) — подпись Mach-O на Windows.
$pythonUrl = 'https://github.com/astral-sh/python-build-standalone/releases/download/20260924/cpython-3.12.14%2B20260924-aarch64-apple-darwin-install_only.tar.gz'
$pythonSha256 = '9763f43db2481a6af36af82ec40302aab7a73632f880129d07a6e81aec846277'
$modelUrl = 'https://models.silero.ai/models/tts/ru/v5_5_ru.pt'
$modelSha256 = '50081637b602126ee06cb3bc8a744d25651d2da149ee8864b9a379bfdd934437'
$rcodesignUrl = 'https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign/0.29.0/apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip'
$rcodesignSha256 = '54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa'
# Программы (права 755 в ZIP); остальное — 644.
$programs = @('NaviDnD', 'NaviDnD.McpServer', 'Speech/setup-silero.sh')

function RawUrl($tag, $relative) { $rawBase + $tag + '/' + ((($relative -split '/') | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/') }
function Sha256File($path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson($path, $value) { [IO.File]::WriteAllText($path, ($value | ConvertTo-Json -Depth 10), $utf8) }
function Download($url, $path, $sha256) {
    & curl.exe --fail --location --silent --show-error --connect-timeout 20 --max-time 900 --retry 2 --output $path $url
    if ($LASTEXITCODE -ne 0) { throw "Download failed: $url" }
    if ((Sha256File $path) -ne $sha256) { throw "Checksum mismatch: $url" }
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
        if ($relative -match '^(Storage|logs|Updater)/') { throw "Private or reserved file in game folder: $relative" }
        $result[$relative] = Sha256File $file.FullName
    }
    $result
}
function UnixMode($relative) { if ($programs -contains $relative -or $relative -like 'Speech/silero/python/bin/*') { 493 } else { 420 } }   # 0755 / 0644

function BuildGame() {
    New-Item -ItemType Directory -Force -Path $game | Out-Null
    foreach ($project in @('NaviDnD', 'NaviDnD.McpServer')) {
        & dotnet publish (Join-Path $root "$project/$project.csproj") -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -o $game -p:DebugType=None -p:DebugSymbols=false | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
    }
    Get-ChildItem -LiteralPath $game -Filter '*.pdb' -File -Recurse | Remove-Item -Force
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
    if (Get-ChildItem $game -Recurse -Directory | Where-Object Name -in @('Storage', 'logs', 'Updater')) { throw 'Private data in game folder' }
    # Скрипты — с переводами строк Unix (иначе bash на Mac их не выполнит), до подсчёта хешей.
    foreach ($script in Get-ChildItem -LiteralPath $game -Recurse -File | Where-Object { $_.Extension -eq '.sh' }) {
        $bytes = [IO.File]::ReadAllBytes($script.FullName)
        $text = [Text.Encoding]::UTF8.GetString($bytes).Replace("`r`n", "`n")
        [IO.File]::WriteAllText($script.FullName, $text, $utf8)
    }
    Set-Content (Join-Path $game 'release-version.txt') $version -Encoding ASCII
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $game 'LICENSE')
    Copy-Item -LiteralPath (Join-Path $root 'RELEASE_NOTES.md') -Destination (Join-Path $game 'RELEASE_NOTES.md')

    # Локальная (ad-hoc) подпись программ: результат один и тот же при каждой сборке — патчи их не тянут без нужды.
    $tools = Join-Path $work 'tools'
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    Download $rcodesignUrl (Join-Path $tools 'rcodesign.zip') $rcodesignSha256
    Expand-Archive -LiteralPath (Join-Path $tools 'rcodesign.zip') -DestinationPath $tools -Force
    $rcodesign = (Get-ChildItem -LiteralPath $tools -Recurse -Filter 'rcodesign.exe' | Select-Object -First 1).FullName
    foreach ($program in @('NaviDnD', 'NaviDnD.McpServer')) {
        # rcodesign пишет ход работы в stderr: Windows PowerShell со Stop счёл бы это ошибкой — важен только код выхода.
        $ErrorActionPreference = 'Continue'
        & $rcodesign sign (Join-Path $game $program) *> $null
        $ErrorActionPreference = 'Stop'
        if ($LASTEXITCODE -ne 0) { throw "Signing failed: $program" }
    }
}

# Озвучка: колёса строго по macos/silero-requirements.txt (версии и хеши), модель — из кеша разработчика или с сайта Silero.
function BuildSpeech() {
    $downloads = Join-Path $work 'downloads'
    $wheels = Join-Path $downloads 'wheels'
    New-Item -ItemType Directory -Force -Path $wheels | Out-Null
    Download $pythonUrl (Join-Path $downloads 'python.tar.gz') $pythonSha256
    & $Python -I -m pip download --disable-pip-version-check --quiet --no-deps --require-hashes --only-binary=:all: `
        --platform macosx_11_0_arm64 --python-version 3.12 --implementation cp --abi cp312 `
        -r (Join-Path $root 'macos/silero-requirements.txt') -d $wheels
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download speech packages.' }
    $model = Join-Path $root 'NaviDnD/Storage/Speech/silero/v5_5_ru.pt'
    if (-not (Test-Path -LiteralPath $model) -or (Sha256File $model) -ne $modelSha256) {
        $model = Join-Path $downloads 'v5_5_ru.pt'
        Download $modelUrl $model $modelSha256
    }
    & $Python -I (Join-Path $root 'macos/package.py') silero (Join-Path $downloads 'python.tar.gz') $wheels $model `
        (Join-Path $root 'NaviDnD/Speech/THIRD_PARTY_NOTICES.md') (Join-Path $game 'Speech/silero')
    if ($LASTEXITCODE -ne 0) { throw 'Cannot build the speech bundle.' }
}

function MakeArchive($output) {
    New-Item -ItemType Directory -Force -Path (Split-Path $output -Parent) | Out-Null
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
    & $Python -I (Join-Path $root 'macos/package.py') zip $game $output
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create the archive.' }
    Write-Host ("macOS archive: {0} ({1:N0} MiB)" -f $output, ((Get-Item -LiteralPath $output).Length / 1MB))
}

try {
    if ($Test) {
        BuildGame; BuildSpeech
        MakeArchive (Join-Path $root "artifacts/NaviDnD-macOS-arm64-$version.zip")
    }
    elseif ($Base) {
        BuildGame; BuildSpeech
        $tree = TreeManifest $game
        $archive = Join-Path $root "artifacts/NaviDnD-macOS-arm64-$version.zip"
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
        BuildGame
        $tree = TreeManifest $game
        # Озвучка не пересобирается — она из базы, без изменений.
        foreach ($key in $previousFiles.Keys) { if ($key.StartsWith('Speech/silero/')) { $tree[$key] = $previousFiles[$key] } }
        $changed = @([string[]]$tree.Keys | Where-Object { $previousFiles[$_] -ne $tree[$_] } | Sort-Object -CaseSensitive)
        $removed = @([string[]]$previousFiles.Keys | Where-Object { -not $tree.Contains($_) } | Sort-Object -CaseSensitive)
        $files = [ordered]@{}; foreach ($key in $changed) { $files[$key] = $tree[$key] }
        New-Item -ItemType Directory -Force -Path $patches | Out-Null
        $patchName = "NaviDnD-macOS-patch-$previousVersion-$version.zip"
        $patchPath = Join-Path $patches $patchName
        if (Test-Path -LiteralPath $patchPath) { Remove-Item -LiteralPath $patchPath -Force }
        $zip = [IO.Compression.ZipFile]::Open($patchPath, 'Create')
        try {
            $description = [ordered]@{ baseVersion = $previousVersion; version = $version; baseFiles = $previousFiles; files = $files; removed = $removed }
            $writer = New-Object IO.StreamWriter($zip.CreateEntry('patch.json').Open(), $utf8)
            try { $writer.Write(($description | ConvertTo-Json -Depth 10)) } finally { $writer.Dispose() }
            foreach ($key in $changed) {
                $entry = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $game $key), "files/$key", [IO.Compression.CompressionLevel]::Optimal)
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
