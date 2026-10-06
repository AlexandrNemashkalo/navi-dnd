param([string]$Destination, [string]$SourceCache = '', [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not $Destination) { throw 'Destination is required.' }
$Destination = [IO.Path]::GetFullPath($Destination)
if (-not $SourceCache) { $SourceCache = Join-Path $PSScriptRoot 'NaviDnD/Storage/Speech/silero' }
$sourcePython = Join-Path $SourceCache 'venv/Scripts/python.exe'
if (-not (Test-Path (Join-Path $SourceCache '.ready'))) {
    & (Join-Path $PSScriptRoot 'NaviDnD/Speech/setup-silero.ps1') -Python $Python -Destination $SourceCache
}
& $sourcePython -c "import sys, torch, numpy; assert sys.version_info[:2] == (3,12); assert torch.__version__ == '2.6.0+cpu'; assert numpy.__version__ == '1.26.4'"
if ($LASTEXITCODE -ne 0) { throw 'Build cache needs Python 3.12, CPU torch 2.6.0 and numpy 1.26.4.' }
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$zip = Join-Path $Destination 'python-embed.zip'
& curl.exe --fail --location --silent --show-error --connect-timeout 20 --max-time 180 --retry 2 --output $zip 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip'
if ($LASTEXITCODE -ne 0) { throw 'Cannot download embedded Python.' }
$runtime = Join-Path $Destination 'python'
Expand-Archive -LiteralPath $zip -DestinationPath $runtime -Force
Remove-Item -LiteralPath $zip
Set-Content (Join-Path $runtime 'python312._pth') @('python312.zip', '.', 'Lib/site-packages', 'import site') -Encoding ASCII
$packages = Join-Path $SourceCache 'venv/Lib/site-packages'
$target = Join-Path $runtime 'Lib/site-packages'
New-Item -ItemType Directory -Force -Path $target | Out-Null
# Explicit public dependencies only. Never copy the cache directory itself.
& $sourcePython (Join-Path $PSScriptRoot 'NaviDnD/Speech/copy_bundle_dependencies.py') $packages $target
if ($LASTEXITCODE -ne 0) { throw 'Cannot copy speech dependencies.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NaviDnD/Speech/THIRD_PARTY_NOTICES.md') -Destination (Join-Path $Destination 'THIRD_PARTY_NOTICES.md')
Copy-Item -LiteralPath (Join-Path $SourceCache 'v5_5_ru.pt') -Destination (Join-Path $Destination 'v5_5_ru.pt')
& (Join-Path $runtime 'python.exe') -W ignore::SyntaxWarning -c "import sys, torch, numpy; torch.set_num_threads(4); m=torch.package.PackageImporter(sys.argv[1]).load_pickle('tts_models','model'); a=m.apply_tts(text='\u041F\u0440\u043E\u0432\u0435\u0440\u043A\u0430.',speaker='aidar',sample_rate=48000); assert a.numel()>0 and torch.isfinite(a).all(); print('Offline Silero synthesis OK')" (Join-Path $Destination 'v5_5_ru.pt')
if ($LASTEXITCODE -ne 0) { throw 'Embedded speech validation failed.' }
foreach ($cache in Get-ChildItem -LiteralPath $target -Directory -Recurse -Filter '__pycache__') {
    if (-not $cache.FullName.StartsWith([IO.Path]::GetFullPath($target) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe bytecode cache path.' }
    Remove-Item -LiteralPath $cache.FullName -Recurse -Force
}
Set-Content (Join-Path $Destination '.ready') 'python3.12.10 / torch2.6.0 / silero-v5_5_ru' -Encoding ASCII
