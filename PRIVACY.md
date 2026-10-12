# 隐私政策 / Privacy Policy

**在吗 AreYouThere** · 生效日期 / Effective date: 2026-10-12

## 中文

『在吗』是一个完全离线运行的 Windows 工具。

- **不联网。** 程序不包含任何网络功能，不会连接互联网或任何服务器。“关于”区域的 GitHub 链接只是交给系统浏览器打开，程序本身不发起网络请求。
- **摄像头画面只在本机内存中处理。** 启用后，程序读取摄像头画面，用 Windows 自带的人脸检测判断你是否在电脑前。画面不会被保存到磁盘、不会上传、不会发送给任何人，分析完立即丢弃。预览画面也只显示在本机窗口中。
- **不收集任何个人数据。** 没有账号、没有统计、没有遥测、没有广告。
- **本地设置。** 检测间隔、所选摄像头、预览开关、离开后的处理方式等设置保存在本机 `%LOCALAPPDATA%\AreYouThere\settings.json`，不包含图像或个人信息，只供程序自己读取。
- **开机自启动与提醒。** 默认关闭，启用前需要明确确认隐私提醒。启用后会为当前用户注册 Windows 登录启动项（普通版使用当前用户的注册表 Run 项，MSIX 使用 Windows 启动任务）。每次登录启动都会显示提醒，确认前不打开摄像头；按住“确定”5 秒并二次确认可永久停用提醒，此后会直接按当前设置开启检测。该选择保存在本机，可在应用设置中恢复提醒。取消自启动会移除或禁用启动项。
- **读取的系统信息。** 为了提示“离开后多久息屏/锁屏”，程序会读取本机的电源计划和屏幕保护设置；为了提示冲突，会检查 PowerToys Awake 是否在运行并读取它的本地设置文件。开启智能检测时，程序会读取距离最后一次键盘鼠标操作的时间，只读取时间，不读取按下的键或鼠标位置；为了在全屏应用（如游戏）前台时不弹出提醒，会询问 Windows 当前是否有全屏应用在前台。这些信息只用于在本机判断和在界面上显示，不会保存或发送。
- **摄像头指示灯。** 摄像头工作时设备的指示灯会亮起。你可以随时关闭检测，或在 Windows 设置 › 隐私和安全性 › 摄像头 中撤销权限。

如有疑问，请在 GitHub 提交 issue：<https://github.com/nathanatgit/AreYouThere/issues>

## English

AreYouThere is a Windows utility that runs entirely offline.

- **No network access.** The app contains no networking code and never connects to the internet or any server. The GitHub link in the About section is handed to your default browser; the app itself makes no network requests.
- **Camera frames are processed only in local memory.** When enabled, the app reads camera frames and uses the face detector built into Windows to decide whether you are at the PC. Frames are never written to disk, uploaded, or shared, and are discarded right after analysis. The preview is shown only in the local window.
- **No personal data is collected.** No accounts, analytics, telemetry, or ads.
- **Local settings.** Settings such as the check interval, selected camera, preview toggle and away behaviour are stored on your device in `%LOCALAPPDATA%\AreYouThere\settings.json`. They contain no images or personal information and are read only by the app.
- **Startup launch and reminders.** Off by default and requires explicit privacy consent. Enabling registers a Windows sign-in startup entry for the current user (a registry Run entry for regular builds, a Windows startup task for MSIX). Each startup launch shows a reminder before opening the camera. Holding OK for five seconds and confirming a separate warning permanently suppresses future reminders; subsequent sign-ins start detection automatically using your current settings. This preference is saved locally and reminders can be restored in settings. Turning startup launch off removes or disables the startup entry.
- **System information read locally.** To explain when the display will turn off or lock, the app reads the local power plan and screen saver settings. To warn about conflicts, it checks whether PowerToys Awake is running and reads its local settings file. When smart detection is on, the app reads how long it has been since the last keyboard or mouse input; it reads only that time, never which keys were pressed or where the mouse is. To avoid notifications while a fullscreen app (such as a game) is in front, it asks Windows whether a fullscreen app is in front. This information is used only on this PC and shown in the app, and is never stored or sent.
- **Camera indicator.** The device's camera light turns on while the camera is in use. You can turn detection off at any time, or revoke access in Windows Settings › Privacy & security › Camera.

Questions: please open an issue at <https://github.com/nathanatgit/AreYouThere/issues>
