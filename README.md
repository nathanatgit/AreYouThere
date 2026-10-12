# 在吗 AreYouThere

微软官方的「存在感知」(Presence Sensing) 只在部分新款设备上提供（需要专用的存在传感器）。**「在吗」**是它的轻量替代方案：用普通摄像头按间隔判断你是否在电脑前，在的时候暂时阻止 Windows 自动息屏；离开后恢复正常电源策略。

A lightweight workaround for Microsoft's official Presence Sensing feature, which is only available on some modern devices: AreYouThere uses an ordinary webcam to check whether you're at the PC and keeps the display awake while you are.

## 功能

- 默认每 60 秒检测一次（可选 15 秒 – 5 分钟），使用 Windows 原生 `FaceDetector`，不依赖 OpenCV
- 实时摄像头预览和人脸框，便于调整摄像头位置
- 托盘图标：单击打开窗口，右键启用/关闭或退出；关闭窗口后继续在托盘运行
- 离开后可选：跟随系统电源策略（并显示当前息屏/睡眠/锁屏时间），或由「在吗」接管，在设定延迟后锁定电脑
- 检测到 PowerToys Awake 处于保持唤醒模式时提示可能冲突
- 电脑锁定时自动暂停检测，解锁后恢复；设置自动保存
- 只允许单实例运行，重复启动会唤出已运行的窗口
- **完全离线**：程序没有任何联网功能，不上传、不收集、不保存任何画面或数据，画面只在本机内存中分析后立即丢弃
- 可选择使用哪个摄像头（显示编号、名称和前置/后置）；插拔摄像头自动刷新；没有摄像头或没有权限时给出提示
- 省电：不显示预览时，只在每次检测时打开摄像头约 1–2 秒，其余时间关闭；界面底部按当前设置提示耗电程度（几乎无影响 / 轻微 / 中等 / 非常耗电）

## 下载

到 [Releases](https://github.com/nathanatgit/AreYouThere/releases) 下载：

- **MSI**（x64 / x86）：安装版，推荐
- **便携版 exe**（x64 / x86）：单个文件，下载即用
- **zip**（x64 / x86）：文件夹版
- **msixbundle**：x86 / x64 / ARM64，ARM 设备请用这个

需要 Windows 10 1809 或更高版本。

## 发布

推送 `vX.Y.Z` 标签即可由 GitHub Actions（`.github/workflows/release.yml`）自动构建并发布全部产物。本地构建同样的产物：

```powershell
./build/build-release.ps1 -Version 0.1.0 -Arch x64            # zip、便携版、msi、msix
./build/build-release.ps1 -Version 0.1.0 -Arch arm64 -Kinds msix
./build/build-release.ps1 -Version 0.1.0 -Bundle              # 合并 out/msix 为 msixbundle
```

MSI 需要 WiX 5（`dotnet tool install --global wix --version 5.0.2`）。

## 构建

需要 .NET 8 SDK（或 Visual Studio 2022 + Windows App SDK 工作负载）：

```powershell
dotnet build AreYouThere\AreYouThere.csproj -p:Platform=x64
```

输出：`AreYouThere\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AreYouThere.exe`。ARM64 设备可改用 `-p:Platform=ARM64`。

## 图标

`AreYouThere\Assets\icon.ico` 和 `AreYouThere\Assets\logo.png` 由仓库根目录的 `icon.png` 生成，见 `tools\make-icon.ps1`。

## 隐私

「在吗」完全离线运行，不收集任何数据，详见 [隐私政策](PRIVACY.md)。上架 Microsoft Store 的步骤见 [docs/microsoft-store.md](docs/microsoft-store.md)。

## 许可证

[MIT](LICENSE)
