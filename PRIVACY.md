# 隐私政策 / Privacy Policy

**在吗 AreYouThere** · 生效日期 / Effective date: 2026-10-12

## 中文

「在吗」是一个完全离线运行的 Windows 工具。

- **不联网。** 程序不包含任何网络功能，不会连接互联网或任何服务器。“关于”区域的 GitHub 链接只是交给系统浏览器打开，程序本身不发起网络请求。
- **摄像头画面只在本机内存中处理。** 启用后，程序读取摄像头画面，用 Windows 自带的人脸检测判断你是否在电脑前。画面不会被保存到磁盘、不会上传、不会发送给任何人，分析完立即丢弃。预览画面也只显示在本机窗口中。
- **不收集任何个人数据。** 没有账号、没有统计、没有遥测、没有广告。
- **本地设置。** 检测间隔、所选摄像头、预览开关、离开后的处理方式等设置保存在本机 `%LOCALAPPDATA%\AreYouThere\settings.json`，不包含图像或个人信息，只供程序自己读取。
- **读取的系统信息。** 为了提示“离开后多久息屏/锁屏”，程序会读取本机的电源计划和屏幕保护设置；为了提示冲突，会检查 PowerToys Awake 是否在运行并读取它的本地设置文件。这些信息只用于在界面上显示，不会保存或发送。
- **摄像头指示灯。** 摄像头工作时设备的指示灯会亮起。你可以随时关闭检测，或在 Windows 设置 › 隐私和安全性 › 摄像头 中撤销权限。

如有疑问，请在 GitHub 提交 issue：<https://github.com/nathanatgit/AreYouThere/issues>

## English

AreYouThere is a Windows utility that runs entirely offline.

- **No network access.** The app contains no networking code and never connects to the internet or any server. The GitHub link in the About section is handed to your default browser; the app itself makes no network requests.
- **Camera frames are processed only in local memory.** When enabled, the app reads camera frames and uses the face detector built into Windows to decide whether you are at the PC. Frames are never written to disk, uploaded, or shared, and are discarded right after analysis. The preview is shown only in the local window.
- **No personal data is collected.** No accounts, analytics, telemetry, or ads.
- **Local settings.** Settings such as the check interval, selected camera, preview toggle and away behaviour are stored on your device in `%LOCALAPPDATA%\AreYouThere\settings.json`. They contain no images or personal information and are read only by the app.
- **System information read locally.** To explain when the display will turn off or lock, the app reads the local power plan and screen saver settings. To warn about conflicts, it checks whether PowerToys Awake is running and reads its local settings file. This information is only displayed in the app and is never stored or sent.
- **Camera indicator.** The device's camera light turns on while the camera is in use. You can turn detection off at any time, or revoke access in Windows Settings › Privacy & security › Camera.

Questions: please open an issue at <https://github.com/nathanatgit/AreYouThere/issues>
