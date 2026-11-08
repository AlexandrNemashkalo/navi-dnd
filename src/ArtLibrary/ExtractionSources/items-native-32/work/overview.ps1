Add-Type -AssemblyName System.Drawing
$root=Join-Path (Get-Location) 'artifacts/items-native-32'
$b=New-Object Drawing.Bitmap 1280,800
$g=[Drawing.Graphics]::FromImage($b);$g.Clear([Drawing.Color]::White)
$g.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode=[Drawing.Drawing2D.PixelOffsetMode]::Half
$packs=Get-Content "$root/work/packs.json" -Raw|ConvertFrom-Json
for($j=0;$j -lt 40;$j++){ $x=($j%5)*256;$y=[math]::Floor($j/5)*100;$g.DrawString($packs[$j].id,(New-Object Drawing.Font 'Arial',9),[Drawing.Brushes]::Black,$x,$y);for($i=1;$i -le 4;$i++){ $f=Join-Path $root ('png/'+$packs[$j].id+'/'+$packs[$j].id+('-{0:00}.png' -f $i));$im=[Drawing.Image]::FromFile($f);$g.DrawImage($im,[Drawing.Rectangle]::new(($x+($i-1)*64),($y+20),64,64));$im.Dispose() }}
$b.Save("$root/previews/overview.png");$g.Dispose();$b.Dispose()
