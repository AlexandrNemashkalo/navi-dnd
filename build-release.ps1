$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$gitRoot = $root.Replace('\', '/')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryRoot ('NaviDnD-release-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work ('payload-' + [guid]::NewGuid().ToString('N'))
$release = Join-Path $root 'releases'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
try {
New-Item -ItemType Directory -Force -Path $payload, $release | Out-Null
foreach ($project in @('NaviDnD', 'NaviDnD.McpServer')) {
    & dotnet publish (Join-Path $root "$project/$project.csproj") -c Release -r win-x64 --self-contained true -o $payload -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
}
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
        $relative = $file.Substring($prefix.Length)
        $target = Join-Path $payload $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $target
    }
}
if (Get-ChildItem $payload -Recurse -Directory | Where-Object Name -in @('Storage', 'logs')) { throw 'Private data in payload' }
Set-Content (Join-Path $payload 'release-version.txt') $version -Encoding ASCII
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $payload 'LICENSE')
Compress-Archive -Path "$payload/*" -DestinationPath "$work/payload.zip" -Force
$oldParts = Get-ChildItem -LiteralPath $release -Filter 'NaviDnD-payload.*' -File
$oldParts | Remove-Item -Force
$oldZip = Join-Path $release 'NaviDnD-win-x64.zip'
if (Test-Path -LiteralPath $oldZip) { Remove-Item -LiteralPath $oldZip -Force }
$inputStream = [IO.File]::OpenRead("$work/payload.zip")
try {
    $buffer = New-Object byte[] (1MB)
    $partNumber = 1
    while ($inputStream.Position -lt $inputStream.Length) {
        $partPath = Join-Path $release ('NaviDnD-payload.{0:D3}' -f $partNumber)
        $outputStream = [IO.File]::Create($partPath)
        try {
            $remaining = 50MB
            while ($remaining -gt 0 -and ($count = $inputStream.Read($buffer, 0, [Math]::Min($buffer.Length, $remaining))) -gt 0) {
                $outputStream.Write($buffer, 0, $count)
                $remaining -= $count
            }
        } finally { $outputStream.Dispose() }
        $partNumber++
    }
} finally { $inputStream.Dispose() }
& dotnet publish "$root/NaviDnD.Installer/NaviDnD.Installer.csproj" -c Release -r win-x64 --self-contained true -o "$work/installer" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed' }
Copy-Item "$work/installer/NaviDnD.Installer.exe" "$release/NaviDnD-Setup-win-x64.exe" -Force
Get-ChildItem $release -File | ForEach-Object {
    if ($_.Length -ge 100MB) { throw "GitHub file size limit exceeded: $($_.Name)" }
}
Get-ChildItem $release -File | Where-Object Name -ne 'SHA256SUMS.txt' | Get-FileHash | ForEach-Object {
    "$($_.Hash.ToLowerInvariant())  $(Split-Path $_.Path -Leaf)"
} | Set-Content "$release/SHA256SUMS.txt" -Encoding ASCII
$releaseLinks = @(Get-ChildItem $release -File | ForEach-Object {
    $file = [uri]::EscapeDataString('releases/' + $_.Name)
    @{ name = $_.Name; url = "https://gitlab.com/api/v4/projects/navitalevich%2Fnavi-dnd/repository/files/$file/raw?ref=v$version" }
})
New-Item -ItemType Directory -Force -Path (Join-Path $root '.gitlab') | Out-Null
$releaseMetadata = @{
    name = "NaviDnD $version"; tag_name = "v$version"
    description = 'Silero speech with five narrator voices, SSML and background warmup. In-game updates with SHA256 checks and rollback. Saves and settings are preserved. Author-owned materials are licensed for non-commercial use; see LICENSE.'
    assets = @{ links = $releaseLinks }
} | ConvertTo-Json -Depth 6
[IO.File]::WriteAllText((Join-Path $root '.gitlab/release.json'), $releaseMetadata, (New-Object Text.UTF8Encoding($false)))
Write-Host "Release ready: $release"
} finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if ((Split-Path $resolvedWork -Parent).TrimEnd('\', '/') -ne $temporaryRoot.TrimEnd('\', '/') -or
        (Split-Path $resolvedWork -Leaf) -notlike 'NaviDnD-release-*') { throw 'Unsafe cleanup path' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
