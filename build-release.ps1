$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$gitRoot = $root.Replace('\', '/')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$work = Join-Path $temporaryRoot ('NaviDnD-release-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $work ('payload-' + [guid]::NewGuid().ToString('N'))
$release = Join-Path $root 'releases'
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
Write-Host "Release ready: $release"
} finally {
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if ((Split-Path $resolvedWork -Parent).TrimEnd('\', '/') -ne $temporaryRoot.TrimEnd('\', '/') -or
        (Split-Path $resolvedWork -Leaf) -notlike 'NaviDnD-release-*') { throw 'Unsafe cleanup path' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
