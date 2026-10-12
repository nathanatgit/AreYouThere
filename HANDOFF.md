# 在吗 AreYouThere 交接文档

## 项目位置

```text
C:\Users\Nathan\Projects\AreYouThere\AreYouThere
```

## 项目目标

在吗（AreYouThere）是一个轻量 Windows WinUI 3 工具，作为微软官方「存在感知」(Presence Sensing) 的替代方案，面向没有存在传感器的设备：使用普通摄像头检测用户是否在电脑前，检测到人脸时暂时阻止 Windows 自动息屏。

摄像头画面只在本地处理，不保存、不上传。启用摄像头时设备的摄像头指示灯会亮起；界面有隐私提示。

## 当前工具链

- .NET SDK 8.0.425
- Windows App SDK 1.8.250907003
- 目标框架：`net8.0-windows10.0.19041.0`
- 平台：x64；项目文件也保留 ARM64 配置
- 未打包（`WindowsPackageType=None`）+ 自包含 Windows App SDK

## 文件结构

- `AreYouThere.csproj`：项目配置，含 exe 图标与版本信息
- `App.xaml.cs`：启动、单实例（命名 Mutex + 广播 `AreYouThere.ShowWindow` 消息）
- `MainWindow.xaml(.cs)`：界面、预览显示、关闭到托盘
- `Services/PresenceService.cs`：摄像头读帧（限 ~15fps）、`FaceDetector`（Gray8）、`SetThreadExecutionState`
- `Services/TrayService.cs`：Win32 `Shell_NotifyIcon` 托盘（图标、菜单、提示气泡、资源管理器重启后恢复），以及会话锁定/解锁通知
- `Services/PowerPolicy.cs`：读取当前电源计划的息屏/睡眠/屏保锁定超时；检测 PowerToys Awake 冲突
- `Services/CameraCatalog.cs`：枚举彩色摄像头（名称不可靠，标签带编号和前置/后置），DeviceWatcher 监听插拔
- `Services/AppSettings.cs`：设置保存在 `%LOCALAPPDATA%\AreYouThere\settings.json`（不保存“是否启用”）
- `Assets/icon.ico`、`Assets/logo.png`：由根目录 `icon.png` 经 `tools/make-icon.ps1` 生成

## 构建

```powershell
$env:DOTNET_CLI_HOME='C:\Users\Nathan\Projects\AreYouThere\.dotnet-cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
dotnet build AreYouThere\AreYouThere.csproj -p:Platform=x64
```

运行 `AreYouThere\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\AreYouThere.exe`（必须用 `win-x64` 自包含目录里的 exe）。

## 注意事项

1. `MediaFrameReader` 的 `FrameArrived` 在后台线程触发，任何 XAML 操作必须经 `DispatcherQueue` 回到 UI 线程（曾因此闪退，`RPC_E_WRONG_THREAD`）。
2. `FaceDetector` 只接受 Gray8 / Nv12。
3. 关闭预览不能停止读帧器，否则检测会一直使用旧帧。
4. `SetThreadExecutionState` 是按线程生效的；检测循环从 UI 线程启动，续体也在 UI 线程。
5. 托盘使用 Win32 原生 API，不要加入会改变 XAML 编译链的第三方托盘 NuGet 包。
6. 需要在 Windows 隐私设置中允许桌面应用访问摄像头。
7. 离开后两种模式：跟随系统（释放唤醒，由电源计划决定）；接管（离开期间仍保持唤醒，延迟到期后 `LockWorkStation`，倒计时内每 2 秒复查一次，看到人即取消）。
8. 会话锁定时暂停摄像头并释放唤醒，解锁后自动恢复。
9. 省电：预览不可见（预览关闭、窗口隐藏或最小化）时，距下次检测超过 8 秒就停止 MediaFrameReader，到点再开流；开流后丢弃前 8 帧等自动曝光稳定。已用注册表 CapabilityAccessManager 的 LastUsedTimeStop 验证：15 秒间隔下每次只亮约 1 秒。
10. 摄像头故障：MediaCapture.Failed、开流 10 秒无画面、帧超过 3 秒未更新都会停止检测并释放唤醒，避免用旧画面一直保持常亮。
11. 程序不含任何网络代码，界面和 README 都以此为卖点；以后加功能（如检查更新）要谨慎，会破坏这个承诺。
12. 发布：自包含 + `PublishTrimmed=true` 约 82 MB（zip 约 34 MB），不裁剪约 168 MB。设置序列化用了源生成，裁剪安全。

## 后续建议

- 摄像头选择下拉框
- 开机自启动选项（启动时直接驻留托盘）
- 发布版本：`dotnet publish` 生成压缩包或 MSIX，并在无开发环境的机器上验证
