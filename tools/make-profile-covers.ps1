# 内置示例方案（办公室、公共场所、居家）的封面图，显示在方案卡片上（没有单独设置锁屏图片时）。
# 只是卡片缩略图，不会设成锁屏图片。输出到 AreYouThere/Assets/Profiles/，880x500（卡片 440 像素宽的 2 倍）。
param([string]$OutDir = "$PSScriptRoot\..\AreYouThere\Assets\Profiles")
. "$PSScriptRoot\banner\render.ps1"

$covers = @(
  @{ Name = 'office'; From = '#0a2a4f'; To = '#1c7ed6'; Glyph = '&#xE821;' },   # Work（公文包）
  @{ Name = 'public'; From = '#5c2400'; To = '#e8590c'; Glyph = '&#xEC32;' },   # Cafe（咖啡）
  @{ Name = 'home';   From = '#0f3d22'; To = '#2f9e44'; Glyph = '&#xE80F;' }    # Home（房子）
)
foreach ($c in $covers) {
  Render-Banner @"
<!doctype html><meta charset="utf-8"><style>
html,body{margin:0;background:transparent}
.b{position:relative;width:440px;height:250px;overflow:hidden;background:linear-gradient(135deg,$($c.From) 0%,$($c.To) 100%)}
.ring{position:absolute;border-radius:50%;border:2px solid rgba(255,255,255,.12)}
.dot{position:absolute;border-radius:50%;background:rgba(255,255,255,.08)}
.icon{position:absolute;left:0;right:0;top:0;bottom:0;display:flex;align-items:center;justify-content:center;
  font-family:"Segoe Fluent Icons","Segoe MDL2 Assets";font-size:96px;color:rgba(255,255,255,.92);text-shadow:0 8px 24px rgba(0,0,0,.25)}
</style><div class="b">
<div class="ring" style="width:300px;height:300px;right:-90px;top:-120px"></div>
<div class="ring" style="width:180px;height:180px;left:-60px;bottom:-80px"></div>
<div class="dot" style="width:120px;height:120px;right:40px;bottom:-50px"></div>
<div class="dot" style="width:60px;height:60px;left:70px;top:30px"></div>
<div class="icon">$($c.Glyph)</div></div>
"@ 440 250 (Join-Path $OutDir "$($c.Name).png")
}
