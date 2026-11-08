$ErrorActionPreference='Stop'
Add-Type 'using System; using System.Runtime.InteropServices; public class DllPath { [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool SetDllDirectory(string path); }'
[DllPath]::SetDllDirectory("$env:USERPROFILE/.nuget/packages/skiasharp.nativeassets.win32/4.148.0/runtimes/win-x64/native") | Out-Null
Add-Type -Path "$env:USERPROFILE/.nuget/packages/skiasharp/4.148.0/lib/net462/SkiaSharp.dll"
$b=[SkiaSharp.SKBitmap]::Decode((Join-Path $PSScriptRoot 'Lightning-Mage-Icons-32x32-Pixel-Art2.webp'))
$d=$b.Encode([SkiaSharp.SKEncodedImageFormat]::Png,100)
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'Lightning-Mage-Icons-32x32-Pixel-Art2.png'),$d.ToArray())
$b.Dispose();$d.Dispose()
