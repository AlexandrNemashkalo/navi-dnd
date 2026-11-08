param([string]$Pack='elves')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -Path (Join-Path $PSScriptRoot 'NativeExport.cs')
$root=Split-Path $PSScriptRoot -Parent
[NativeExport]::Run((Join-Path $PSScriptRoot "$Pack.jpg"),(Join-Path $root "png/$Pack"),(Join-Path $root 'previews'),$Pack)
Write-Output "Extracted native grid: $Pack"
