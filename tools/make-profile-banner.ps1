# Compact banner (2560x384, about 120px tall on GitHub) for the profile README at
# github.com/nathanatgit/nathanatgit, where it lives as assets/areyouthere.png.
param([string]$Out = "$PSScriptRoot\..\out\areyouthere-profile.png",
      [string]$Screenshot = "$PSScriptRoot\banner\screenshot.png")
. "$PSScriptRoot\banner\render.ps1"

$html = @"
<!doctype html><meta charset="utf-8"><style>
html,body{margin:0;background:transparent}
.b{position:relative;width:1280px;height:192px;border-radius:16px;overflow:hidden;color:#fff;
  background:linear-gradient(90deg,#0a1626 0%,#0a1626 40%,#0b3156 100%);
  font-family:"Segoe UI Variable Display","Segoe UI","Microsoft YaHei UI",sans-serif}
.glow{position:absolute;right:0;top:0;width:580px;height:192px;
  background:linear-gradient(90deg,transparent 0,#0b3a66 30%,#0e4f8c 100%)}
.txt{position:absolute;left:48px;top:0;bottom:0;width:640px;display:flex;flex-direction:column;justify-content:center}
.k{font-size:13px;letter-spacing:.06em;text-transform:uppercase;color:#4cc2ff;font-weight:600}
.t{font-size:44px;font-weight:700;line-height:1.1;margin:4px 0 6px;letter-spacing:-.01em}
.t span{font-weight:600;opacity:.92}
.g{font-size:16.5px;line-height:1.35;color:#c8ccd6;max-width:620px}
.shot{position:absolute;left:770px;top:20px;width:470px;clip-path:inset(0 round 10px 10px 0 0)}
</style><div class="b"><div class="glow"></div>
<div class="txt"><div class="k">C# · WinUI 3 · Windows</div><div class="t">在吗 <span>AreYouThere</span></div>
<div class="g">Webcam presence detection for Windows: keeps the screen on while you’re there, locks the PC when you leave. Fully offline.</div></div>
<img class="shot" src="$(File-Uri $Screenshot)"></div>
"@

Render-Banner $html 1280 192 $Out
