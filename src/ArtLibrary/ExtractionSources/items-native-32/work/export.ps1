param([string[]]$Only=@())
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -Path (Join-Path $PSScriptRoot 'NativeExport.cs')
$root=Split-Path $PSScriptRoot -Parent
$packs=Get-Content (Join-Path $PSScriptRoot 'packs.json') -Raw | ConvertFrom-Json
foreach($p in $packs){
 if($Only.Count -gt 0 -and $p.id -notin $Only){continue}
 [NativeExport]::Run((Join-Path $PSScriptRoot $p.source),(Join-Path $root "png/$($p.id)"),(Join-Path $root 'previews'),$p.id,($p.kind -eq 'skill'))
 Write-Output $p.id
}
