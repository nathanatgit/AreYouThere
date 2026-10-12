<#
.SYNOPSIS
  构建「在吗」的发布产物。GitHub Actions 和本地都用这个脚本。

.EXAMPLE
  # 构建 x64 的全部产物（zip、单文件便携版、msi、msix）
  ./build/build-release.ps1 -Version 0.1.0 -Arch x64

.EXAMPLE
  # 把 out/msix 下各架构的 .msix 合并为 .msixbundle
  ./build/build-release.ps1 -Version 0.1.0 -Bundle

.NOTES
  产物命名：
    AreYouThere-<ver>-win-<arch>.zip            含 exe 和所有依赖文件的文件夹
    AreYouThere-<ver>-win-<arch>-portable.exe   单文件便携版（首次运行解压到临时目录）
    AreYouThere-<ver>-win-<arch>.msi            安装到 Program Files，带开始菜单快捷方式
    AreYouThere-<ver>.msixbundle                x86/x64/arm64 合一的 MSIX 包
  提供 -CertPath 时对 exe/msi/msix/msixbundle 签名；MSIX 必须签名才能安装，Publisher 必须与证书主题一致。
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [ValidateSet('x86', 'x64', 'arm64')] [string] $Arch,
    [string[]] $Kinds = @('zip', 'portable', 'msi', 'msix'),
    [switch] $Bundle,
    [string] $OutDir = (Join-Path $PSScriptRoot '..\out'),
    [string] $Publisher = 'CN=HGY',
    [string] $CertPath,
    [string] $CertPassword,
    [string] $Wix = 'wix'
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'AreYouThere\AreYouThere.csproj'
$manifest = Join-Path $root 'AreYouThere\Package.appxmanifest'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "版本号必须是 x.y.z 形式：$Version" }
$msixVersion = "$Version.0"
New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = Resolve-Path $OutDir

function Invoke-Checked([scriptblock] $Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$Command" }
}

# makeappx / signtool 优先取项目已引用的 Microsoft.Windows.SDK.BuildTools 包，其次取本机 Windows SDK
function Find-SdkTool([string] $Name) {
    $roots = @()
    $nugetRoot = (dotnet nuget locals global-packages -l) -replace '^global-packages:\s*', ''
    if ($nugetRoot) { $roots += Join-Path $nugetRoot.Trim() 'microsoft.windows.sdk.buildtools' }
    $roots += 'C:\Program Files (x86)\Windows Kits\10\bin'
    foreach ($r in $roots) {
        $tool = Get-ChildItem $r -Recurse -Filter $Name -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($tool) { return $tool.FullName }
    }
    throw "找不到 $Name"
}

function Sign-File([string] $Path) {
    if (-not $CertPath) { return }
    $signtool = Find-SdkTool 'signtool.exe'
    Invoke-Checked { & $signtool sign /fd SHA256 /f $CertPath /p $CertPassword /tr http://timestamp.digicert.com /td SHA256 $Path }
}

if ($Bundle) {
    $msixDir = Join-Path $OutDir 'msix'
    $packages = Get-ChildItem $msixDir -Filter *.msix
    if (-not $packages) { throw "$msixDir 下没有 .msix" }
    $bundlePath = Join-Path $OutDir "AreYouThere-$Version.msixbundle"
    $makeappx = Find-SdkTool 'makeappx.exe'
    Invoke-Checked { & $makeappx bundle /d $msixDir /p $bundlePath /bv $msixVersion /o }
    Sign-File $bundlePath
    Write-Host "已生成 $bundlePath（包含：$($packages.Name -join ', ')）"
    return
}

if (-not $Arch) { throw '需要 -Arch 或 -Bundle' }
$platform = @{ x86 = 'x86'; x64 = 'x64'; arm64 = 'ARM64' }[$Arch]
$rid = "win-$Arch"
$work = Join-Path $OutDir "work\$Arch"
$common = @("-c", "Release", "-r", $rid, "-p:Platform=$platform", "-p:Version=$Version", "-p:PublishTrimmed=true")

# 文件夹版（zip 和 msi 共用）
if ($Kinds -contains 'zip' -or $Kinds -contains 'msi') {
    $appDir = Join-Path $work 'AreYouThere'
    Remove-Item $appDir -Recurse -Force -ErrorAction SilentlyContinue
    Invoke-Checked { dotnet publish $project @common -o $appDir }
    Get-ChildItem $appDir -Filter *.pdb | Remove-Item
    Sign-File (Join-Path $appDir 'AreYouThere.exe')

    if ($Kinds -contains 'zip') {
        $zip = Join-Path $OutDir "AreYouThere-$Version-win-$Arch.zip"
        Remove-Item $zip -ErrorAction SilentlyContinue
        Compress-Archive -Path $appDir -DestinationPath $zip
        Write-Host "已生成 $zip"
    }
    if ($Kinds -contains 'msi') {
        $msi = Join-Path $OutDir "AreYouThere-$Version-win-$Arch.msi"
        Invoke-Checked { & $Wix build (Join-Path $PSScriptRoot 'AreYouThere.wxs') -arch $Arch `
            -d "Version=$Version" -d "SourceDir=$appDir" -d "IconFile=$(Join-Path $root 'AreYouThere\Assets\icon.ico')" -o $msi }
        Remove-Item ([IO.Path]::ChangeExtension($msi, '.wixpdb')) -ErrorAction SilentlyContinue
        Sign-File $msi
        Write-Host "已生成 $msi"
    }
}

# 单文件便携版：WinUI 的 .pri/.xbf 等资源也打进 exe，首次运行时解压
if ($Kinds -contains 'portable') {
    $singleDir = Join-Path $work 'single'
    Remove-Item $singleDir -Recurse -Force -ErrorAction SilentlyContinue
    Invoke-Checked { dotnet publish $project @common -o $singleDir -p:PublishSingleFile=true `
        -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true }
    $portable = Join-Path $OutDir "AreYouThere-$Version-win-$Arch-portable.exe"
    Copy-Item (Join-Path $singleDir 'AreYouThere.exe') $portable -Force
    Sign-File $portable
    Write-Host "已生成 $portable"
}

# MSIX：用 SDK 自带的单项目打包，资源索引（resources.pri）由工具链正确合并
if ($Kinds -contains 'msix') {
    $original = [IO.File]::ReadAllText($manifest)
    try {
        $patched = $original -replace '(<Identity [^>]*Version=")[^"]*', "`${1}$msixVersion" `
                             -replace '(<Identity [^>]*Publisher=")[^"]*', "`${1}$Publisher"
        [IO.File]::WriteAllText($manifest, $patched, [Text.UTF8Encoding]::new($false))
        $pkgDir = Join-Path $work 'msixpkg\'
        Remove-Item $pkgDir -Recurse -Force -ErrorAction SilentlyContinue
        Invoke-Checked { dotnet publish $project @common -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=false -p:AppxSymbolPackageEnabled=false -p:AppxPackageDir=$pkgDir }
    }
    finally {
        [IO.File]::WriteAllText($manifest, $original, [Text.UTF8Encoding]::new($false))
    }
    $msixOut = Join-Path $OutDir 'msix'
    New-Item -ItemType Directory -Force $msixOut | Out-Null
    $built = Get-ChildItem $pkgDir -Recurse -Filter *.msix | Select-Object -First 1
    if (-not $built) { throw '没有生成 .msix' }
    $msix = Join-Path $msixOut "AreYouThere-$Version-$Arch.msix"
    Copy-Item $built.FullName $msix -Force
    Sign-File $msix
    Write-Host "已生成 $msix"
}
