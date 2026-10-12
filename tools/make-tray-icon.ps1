# Monochrome tray icons from tools/tray-icon.svg: tray-white.ico for dark taskbars and
# tray-black.ico for light ones. Each size is rendered natively (not downscaled) so 16px stays crisp.
param([string]$OutDir = "$PSScriptRoot\..\AreYouThere\Assets")
. "$PSScriptRoot\banner\render.ps1"
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64
$colors = [ordered]@{ white = '#ffffff'; black = '#000000' }
$svg = (Get-Content "$PSScriptRoot\tray-icon.svg" -Raw) -replace '<!--.*?-->', ''

# One sprite page: a row per color, each size at its native pixel size, left to right
$cells = foreach ($row in 0..($colors.Count - 1)) {
  $x = 0
  foreach ($s in $sizes) {
    $color = @($colors.Values)[$row]
    "<div style=`"position:absolute;left:${x}px;top:$($row * 70)px;width:${s}px;height:${s}px;color:$color`">" +
      ($svg -replace '<svg ', "<svg width=`"$s`" height=`"$s`" ") + '</div>'
    $x += $s + 6
  } }
$sprite = Join-Path ([IO.Path]::GetTempPath()) "areyouthere-tray-$PID.png"
Render-Banner "<!doctype html><style>html,body{margin:0;background:transparent}svg{display:block}</style>$($cells -join '')" 400 140 $sprite 1 | Out-Null

$sheet = [System.Drawing.Bitmap]::new($sprite)
$row = 0
foreach ($name in $colors.Keys) {
  $frames = @(); $x = 0
  foreach ($s in $sizes) {
    $frame = $sheet.Clone([System.Drawing.Rectangle]::new($x, $row * 70, $s, $s), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $ms = [IO.MemoryStream]::new(); $frame.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $frames += , $ms.ToArray()
    $frame.Dispose(); $x += $s + 6
  }
  $fs = [IO.File]::Create((Join-Path $OutDir "tray-$name.ico")); $w = [IO.BinaryWriter]::new($fs)
  $w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
  $offset = 6 + 16 * $sizes.Count
  for ($i = 0; $i -lt $sizes.Count; $i++) {
    $w.Write([byte]$sizes[$i]); $w.Write([byte]$sizes[$i]); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$frames[$i].Length); $w.Write([int]$offset)
    $offset += $frames[$i].Length
  }
  foreach ($f in $frames) { $w.Write($f) }
  $w.Dispose(); $row++
}
$sheet.Dispose(); Remove-Item $sprite
Get-ChildItem $OutDir -Filter 'tray-*.ico' | Select-Object Name, Length
