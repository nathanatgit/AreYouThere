# Microsoft Store 一览用的图：应用磁贴图标 300/150/71、框图 2160x2160、招贴画 1440x2160、超级英雄画 3840x2160。
# 招贴画在 Windows 10/11 上用作主徽标（常以小尺寸显示），所以和框图一样只放图标和名称，不放截图。
# 最清晰的图标源是 icon.ico 里的 256 像素帧，大图里图标最多放大约 2 倍，避免发虚。
# 截图用 tools/banner/screenshot.png 的同样要求：预览关闭，不带摄像头画面。
# 超级英雄画不放文字：商店会在左下角叠加应用名称。
param([string]$OutDir = "$PSScriptRoot\..\docs\store",
      [string]$Screenshot = "$PSScriptRoot\banner\screenshot.png",
      [string]$Icon = "$PSScriptRoot\..\AreYouThere\Assets\icon.ico")
. "$PSScriptRoot\banner\render.ps1"
New-Item -ItemType Directory -Force $OutDir | Out-Null

# icon.ico 里 256 像素那一帧是 PNG，原样取出来当高清图标
$bytes = [IO.File]::ReadAllBytes((Resolve-Path $Icon))
$count = [BitConverter]::ToInt16($bytes, 4)
$iconPng = Join-Path ([IO.Path]::GetTempPath()) "areyouthere-icon-256.png"
for ($k = 0; $k -lt $count; $k++) {
  $e = 6 + 16 * $k
  if ($bytes[$e] -eq 0) {
    $size = [BitConverter]::ToInt32($bytes, $e + 8); $offset = [BitConverter]::ToInt32($bytes, $e + 12)
    [IO.File]::WriteAllBytes($iconPng, $bytes[$offset..($offset + $size - 1)])
  }
}
$iconUri = File-Uri $iconPng
$shotUri = File-Uri $Screenshot

$style = @"
html,body{margin:0;background:transparent}
.b{position:relative;overflow:hidden;color:#fff;
  background:radial-gradient(120% 90% at 85% 15%,#0e4f8c 0%,#0b3156 40%,#0a1626 75%);
  font-family:"Segoe UI Variable Display","Segoe UI","Microsoft YaHei UI",sans-serif}
.icon{display:block;border-radius:22%;box-shadow:0 18px 50px rgba(0,0,0,.45)}
.name{font-weight:700;letter-spacing:-.01em;line-height:1.1}
.tag{color:#c8ccd6;line-height:1.5}
.shot{position:absolute;border-radius:14px;box-shadow:0 24px 60px rgba(0,0,0,.55);object-fit:cover;object-position:left top}
"@

# 应用磁贴图标：只有图标，铺满（300 / 150 / 71）
foreach ($px in 300, 150, 71) {
  Render-Banner @"
<!doctype html><meta charset="utf-8"><style>html,body{margin:0;background:transparent}img{display:block;width:${px}px;height:${px}px}</style>
<img src="$iconUri">
"@ $px $px (Join-Path $OutDir "tile-icon-$px.png") -Scale 1
}

# 框图 1:1：图标、名称、一句话
Render-Banner @"
<!doctype html><meta charset="utf-8"><style>$style
.b{width:1080px;height:1080px;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center}
.icon{width:256px;height:256px}.name{font-size:96px;margin-top:64px}.tag{font-size:40px;margin-top:24px}
</style><div class="b"><img class="icon" src="$iconUri"><div class="name">在吗 · AreYouThere</div>
<div class="tag">人在时保持亮屏，人离开后自动锁定</div></div>
"@ 1080 1080 (Join-Path $OutDir 'box-art-2160.png')

# 招贴画 9:16：图标居中偏上，名称和一句话在下面
Render-Banner @"
<!doctype html><meta charset="utf-8"><style>$style
.b{width:720px;height:1080px;display:flex;flex-direction:column;align-items:center;justify-content:center;text-align:center}
.icon{width:256px;height:256px;margin-top:-80px}.name{font-size:64px;margin-top:64px}.tag{font-size:30px;margin-top:28px}
</style><div class="b"><img class="icon" src="$iconUri"><div class="name">在吗<br>AreYouThere</div>
<div class="tag">人在时保持亮屏<br>人离开后自动锁定</div></div>
"@ 720 1080 (Join-Path $OutDir 'poster-art-1440x2160.png')

# 超级英雄画 16:9：不放文字，截图放在右上，左下留给商店叠加的标题
Render-Banner @"
<!doctype html><meta charset="utf-8"><style>$style
.b{width:1920px;height:1080px}
.icon{position:absolute;left:216px;top:236px;width:256px;height:256px}
.shot{left:700px;top:150px;width:1120px;height:630px}
</style><div class="b"><img class="icon" src="$iconUri"><img class="shot" src="$shotUri"></div>
"@ 1920 1080 (Join-Path $OutDir 'super-hero-art-3840x2160.png')

Remove-Item $iconPng
