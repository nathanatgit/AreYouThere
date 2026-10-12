# 在吗 AreYouThere

微软官方的「存在感知」(Presence Sensing) 只在部分新款设备上提供（需要专用的存在传感器）。**「在吗」**是它的轻量替代方案：用普通摄像头按间隔判断你是否在电脑前，在的时候暂时阻止 Windows 自动息屏；离开后恢复正常电源策略。

A lightweight workaround for Microsoft's official Presence Sensing feature, which is only available on some modern devices: AreYouThere uses an ordinary webcam to check whether you're at the PC and keeps the display awake while you are.

## 功能

- **完全离线**：程序没有任何联网功能，不上传、不收集、不保存任何画面或数据，画面只在本机内存中分析后立即丢弃
- 默认每 60 秒检测一次（可选 15 秒 – 5 分钟），使用 Windows 自带的人脸检测
- 离开后可选：跟随系统电源策略（并显示当前息屏/睡眠/锁屏时间），或由「在吗」接管，在设定延迟后锁定电脑
- 省电：不显示预览时，只在每次检测时打开摄像头约 1–2 秒，其余时间关闭；界面底部按当前设置提示耗电程度
- 可选择使用哪个摄像头；插拔摄像头自动刷新；没有摄像头或没有权限时给出提示
- 实时摄像头预览和人脸框，便于调整摄像头位置
- 托盘图标：单击打开窗口，右键启用/关闭或退出；关闭窗口后继续在托盘运行
- 电脑锁定时自动暂停检测，解锁后恢复；设置自动保存
- 检测到 PowerToys Awake 处于保持唤醒模式时提示可能冲突

## 下载

到 [Releases](https://github.com/nathanatgit/AreYouThere/releases) 下载，x64 / x86 / ARM64 都有：

- **MSI**：安装版，推荐
- **便携版 exe**：单个文件，下载即用
- **zip**：文件夹版，解压后运行 `AreYouThere.exe`

需要 Windows 10 1809 或更高版本。

## 从源码构建

需要 .NET 8 SDK：

```powershell
dotnet build AreYouThere\AreYouThere.csproj -p:Platform=x64
```

## 隐私

「在吗」完全离线运行，不收集任何数据，详见 [隐私政策](PRIVACY.md)。

## 许可证

[MIT](LICENSE)
