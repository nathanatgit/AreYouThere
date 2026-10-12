# Large banner (2560x800) for this repo's README.
# The window image is tools/banner/screenshot.png: capture the app with camera preview off
# and crop it above the camera picker so no webcam image or device name ends up in it.
param([string]$Out = "$PSScriptRoot\..\docs\banner.png",
      [string]$Screenshot = "$PSScriptRoot\banner\screenshot.png")
. "$PSScriptRoot\banner\render.ps1"

$html = @"
<!doctype html><meta charset="utf-8"><style>
html,body{margin:0;background:transparent}
.b{position:relative;width:1280px;height:400px;border-radius:24px;overflow:hidden;color:#fff;
  background:linear-gradient(90deg,#0a1626 0%,#0a1626 42%,#0b3156 100%);
  font-family:"Segoe UI Variable Display","Segoe UI","Microsoft YaHei UI",sans-serif}
.glow{position:absolute;right:0;top:0;width:620px;height:400px;
  background:linear-gradient(90deg,transparent 0,#0b3a66 35%,#0e4f8c 100%)}
.txt{position:absolute;left:64px;top:0;bottom:0;width:560px;display:flex;flex-direction:column;justify-content:center}
.k{font-size:16px;letter-spacing:.06em;text-transform:uppercase;color:#4cc2ff;font-weight:600}
.t{font-size:56px;font-weight:700;line-height:1.1;margin:8px 0 10px;letter-spacing:-.01em}
.t span{font-weight:600;opacity:.92}
.g{font-size:20px;white-space:nowrap;line-height:1.45;color:#c8ccd6}
.ns{display:flex;flex-direction:column;gap:6px;margin-top:12px}
.n{display:flex;align-items:center;gap:12px;font-size:18px;color:#8fd8ff}
.i{flex:none;width:8px;height:8px;margin-left:4px;border-radius:50%;background:#4ade80;box-shadow:0 0 0 4px rgba(74,222,128,.18)}
.chips{display:flex;gap:10px;margin-top:20px}
.chips span{font-size:16px;font-weight:600;padding:6px 16px;border-radius:999px;
  border:1px solid rgba(255,255,255,.22);background:rgba(255,255,255,.06)}
.shot{position:absolute;left:650px;top:132px;width:580px;clip-path:inset(0 round 12px 12px 0 0);
  filter:drop-shadow(0 16px 40px rgba(0,0,0,.5))}
</style><div class="b"><div class="glow"></div>
<div class="txt"><div class="k">C# · WinUI 3 · Windows</div><div class="t">在吗 · AreYouThere</div>
<div class="g">基于普通摄像头的存在感知（Presence Sensing）轻量化实现：<br>人离开后自动锁定。</div>
<div class="ns"><div class="n"><span class="i"></span>摄像头按需开启：检测时短暂亮起，操作键鼠时不检测。</div>
<div class="n"><span class="i"></span>兼容 PowerToys Awake：开着 Awake，离开后也能自动锁定。</div>
<div class="n"><span class="i"></span>可选坐姿提醒：弯腰低头、离屏幕太近时提醒你。</div></div>
<div class="chips"><span>Native Windows</span><span>Offline</span><span>MIT Open Source</span></div></div>
<img class="shot" src="$(File-Uri $Screenshot)"></div>
"@

Render-Banner $html 1280 400 $Out
