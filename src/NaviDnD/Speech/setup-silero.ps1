param([string]$Python = 'python', [string]$Destination = '')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot '../Storage/Speech/silero' }
$Destination = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$venv = Join-Path $Destination 'venv'
if (-not (Test-Path (Join-Path $venv 'Scripts/python.exe'))) {
    & $Python -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.10-3.12 x64 is required. Check the configured python.exe path.' }
}
$localPython = Join-Path $venv 'Scripts/python.exe'
$pythonTag = (& $localPython -c "import sys; print('cp%d%d' % sys.version_info[:2])").Trim()
if ($pythonTag -notin @('cp310', 'cp311', 'cp312')) { throw 'Python 3.10-3.12 x64 is required.' }
$torchUrl = "https://download.pytorch.org/whl/cpu/torch-2.6.0%2Bcpu-$pythonTag-$pythonTag-win_amd64.whl"
$installedTorch = & $localPython -c "import importlib.metadata; print(any(d.metadata['Name'] == 'torch' and d.version == '2.6.0+cpu' for d in importlib.metadata.distributions()))"
if ($installedTorch -ne 'True') {
    $wheel = Join-Path $Destination "torch-2.6.0+cpu-$pythonTag-$pythonTag-win_amd64.whl"
    & curl.exe --fail --location --silent --show-error --connect-timeout 20 --max-time 600 --retry 2 --continue-at - --output ($wheel + '.part') $torchUrl
    if ($LASTEXITCODE -ne 0) { throw 'PyTorch download failed. Enable speech again to resume.' }
    Move-Item -LiteralPath ($wheel + '.part') -Destination $wheel -Force
    & $localPython -m pip install --disable-pip-version-check --timeout 30 --retries 2 $wheel
    if ($LASTEXITCODE -ne 0) { throw 'Failed to install CPU PyTorch.' }
    Remove-Item -LiteralPath $wheel
}
& $localPython -m pip install --disable-pip-version-check 'numpy==1.26.4'
if ($LASTEXITCODE -ne 0) { throw 'Failed to install numpy.' }
$model = Join-Path $Destination 'v5_5_ru.pt'
if (-not (Test-Path $model)) {
    try {
        Invoke-WebRequest 'https://models.silero.ai/models/tts/ru/v5_5_ru.pt' -OutFile ($model + '.part')
        Move-Item -LiteralPath ($model + '.part') -Destination $model -Force
    } finally { if (Test-Path ($model + '.part')) { Remove-Item -LiteralPath ($model + '.part') } }
}
& $localPython -c 'import torch, numpy'
if ($LASTEXITCODE -ne 0) { throw 'Python dependency validation failed.' }
Set-Content -LiteralPath (Join-Path $Destination '.ready') -Value 'torch2.6.0 / silero-v5_5_ru'
