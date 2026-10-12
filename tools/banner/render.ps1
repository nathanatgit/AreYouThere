# Shared by make-banner.ps1 and make-profile-banner.ps1: renders an HTML page to a
# transparent PNG with headless Edge at 2x (so a 1280px-wide page becomes 2560px).
function Render-Banner([string]$Html, [int]$Width, [int]$Height, [string]$Out) {
  $edge = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
            "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $edge) { throw 'Microsoft Edge not found.' }
  $page = Join-Path ([IO.Path]::GetTempPath()) "areyouthere-banner-$PID.html"
  Set-Content -Path $page -Value $Html -Encoding utf8
  New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
  $Out = [IO.Path]::GetFullPath($Out)
  Start-Process $edge -Wait -WindowStyle Hidden -ArgumentList @(
    '--headless=new', '--disable-gpu', '--hide-scrollbars', '--allow-file-access-from-files',
    '--force-device-scale-factor=2', '--default-background-color=00000000',
    "--window-size=$Width,$Height", "--screenshot=`"$Out`"", ([Uri]$page).AbsoluteUri)
  Remove-Item $page
  Get-Item $Out | Select-Object Name, Length
}

function File-Uri([string]$Path) { ([Uri](Resolve-Path $Path).Path).AbsoluteUri }
