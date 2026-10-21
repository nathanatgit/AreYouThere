<img src="docs/banner.png" width="100%" alt="在吗 AreYouThere: uses an ordinary webcam to tell whether you're at your PC, keeps the display on while you are, and locks it after you leave / 用普通摄像头判断你在不在电脑前，在的时候保持亮屏，离开后自动锁定">

[![Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-%E5%9C%A8%E5%90%97%20AreYouThere-0078D4)](https://apps.microsoft.com/detail/9N4T5SJXMJH3)
[![Release downloads](https://img.shields.io/github/downloads/nathanatgit/AreYouThere/total?label=release%20downloads&color=0078D4)](https://github.com/nathanatgit/AreYouThere/releases)

<a href="https://apps.microsoft.com/detail/9N4T5SJXMJH3"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Get it from Microsoft Store / 从 Microsoft Store 获取"></a>

A lightweight alternative to Microsoft's official Presence Sensing feature, which is only available on some newer devices with a dedicated presence sensor. **AreYouThere** uses an ordinary webcam to check at intervals whether you're at the PC. While you are, it keeps Windows from turning the display off; after you leave, it locks the PC.

微软官方的“存在感知”(Presence Sensing) 只在部分新款设备上提供（需要专用的存在传感器）。『**在吗**』是它的轻量替代方案：用普通摄像头按间隔判断你是否在电脑前，在的时候阻止 Windows 自动息屏，离开后锁定电脑。

**Contents / 目录:** [Why](#why--为什么做这个) · [Use cases](#use-cases--适用场景) · [Limitations](#limitations--不适合的情况) · [Features](#features--功能) · [Download](#download--下载) · [Build](#build-from-source--从源码构建) · [Privacy](#privacy--隐私) · [License](#license--许可证)

## Why / 为什么做这个

Windows can only guess whether you're present from how long it has been since you touched the keyboard or mouse. That causes problems:

Windows 只能靠“多久没碰键盘鼠标”来猜你在不在，这带来几个问题：

- **You're there, but the screen dims or locks.** Reading long documents, reading code, reviewing drawings, or listening to an online meeting, you stay at the PC without touching the keyboard or mouse.<br>**人在，屏幕却暗了、锁了。** 读长文档、看代码、审图纸、听线上会议时，你一直坐在电脑前，只是没动键鼠。
- **You've left, but the screen stays on.** To avoid interruptions, many people set very long screen-off/lock timeouts, so the screen stays visible to others when they walk away.<br>**人走了，屏幕还亮着。** 为了不被打断，很多人把息屏/锁屏时间调得很长，起身离开时屏幕内容就一直暴露在旁人眼前。
- **PowerToys Awake left on.** Sometimes you forget to press Win+L.<br>**PowerToys Awake 忘记关闭。** 有时候你忘了按 Win+L。

AreYouThere uses the camera to tell whether you're really at the PC: the screen stays on while you are, and the PC locks after the delay you set once you leave. **Its primary goal is screen privacy**: when you're not there, nobody else sees your screen.

『在吗』用摄像头判断你是否真的在电脑前：在的时候屏幕常亮，离开后在设定的延迟后锁定电脑。**它的首要目的是保护你的屏幕隐私**：人不在时，不让别人看到你的屏幕内容。

It must not become a privacy risk itself: it runs fully offline with no networking. Frames are analyzed in memory and discarded immediately; nothing is saved or uploaded.

同时，『在吗』本身也不应成为隐私隐患：完全离线，没有任何联网功能，画面只在本机内存中分析后立即丢弃，不保存、不上传。

## Use cases / 适用场景

- **Offices and open-plan desks**: auto-lock when you step away for water, a meeting, or a quick chat, so colleagues can't see your mail, chats, or files; the screen doesn't dim while you read.<br>**办公室、开放工位**：起身倒水、去开会、临时被叫走时自动锁屏，同事和路过的人看不到你的邮件、聊天和工作文件；坐着阅读时屏幕不会变暗
- **Home**: lock after you leave so family, roommates, or kids can't see your chats or work, or touch the PC by accident.<br>**家里**：离开后自动锁定，避免家人、室友或孩子看到聊天记录、工作内容，或误操作电脑
- **Long "look but don't touch" work**: reading, thinking through writing, code, drawings, dashboards, online courses or meetings, with no need to jiggle the mouse every few minutes.<br>**长时间“只看不动”的工作**：阅读、写作构思、看代码、审图、看监控面板、听网课或线上会议，不必隔几分钟晃一下鼠标
- **Public places** (cafés, libraries, coworking spaces): raise the face-area threshold so only you, close to the camera, are recognized; people beside or behind you are ignored, and the PC still locks when you leave. Follow the venue's rules, and note that the camera light may make others think you're recording.<br>**公共场所**（咖啡馆、图书馆、共享办公区等）：把人脸区域识别阈值调大，只认离摄像头很近的你，旁边和身后的人会被忽略，离开后照常锁定。请遵守场所规定，并注意，摄像头指示灯可能让周围的人误以为你在拍摄
- **PCs without a presence sensor**: desktops with an external webcam, or older laptops without Presence Sensing.<br>**没有存在传感器的电脑**：台式机外接摄像头，或不支持“存在感知”的旧笔记本

## Limitations / 不适合的情况

- **Not for situations needing reliable security.** AreYouThere only detects faces; it **does not verify it's you**. Any sufficiently close face (someone sitting in your seat, even a photo) makes it think you're present. It is a convenience tool for reducing screen exposure, not a security product, and doesn't replace locking manually, Windows Hello, or your organization's security policy.<br>**需要可靠安全保障的场景**：『在吗』只检测人脸，**不识别是不是你本人**。只要有一张足够近的人脸（包括别人坐到你的位置，甚至一张照片），它就会认为你在电脑前。它是减少屏幕暴露的便利工具，不是安全产品，不能代替离开时手动锁屏的习惯、Windows Hello 等身份验证或组织的安全策略
- **There's a delay between leaving and locking.** At most about "detection interval + lock delay" (default 30 s + 10 s; with Smart Detection on, counted from your last keyboard/mouse input). A shorter interval locks sooner but uses the camera more often.<br>**从你离开到锁定有一段延迟**：最长约为“检测间隔 + 锁定延迟”（默认 30 秒 + 10 秒；开启智能检测时从最后一次操作键盘鼠标算起）。缩短检测间隔可以更快锁定，但摄像头工作更频繁

## Features / 功能

**Fully offline**: no networking, and nothing is uploaded, collected, or saved. Frames are analyzed in memory and discarded immediately; preferences are stored only on this PC.

**完全离线**：程序没有任何联网功能，不上传、不收集、不保存任何画面或检测数据，画面只在本机内存中分析后立即丢弃；偏好设置仅保存在本机。

### Presence detection / 在场检测

- Uses Windows' built-in face detection, every 30 seconds by default; configurable in hours/minutes/seconds (minimum 5 s).<br>使用 Windows 自带的人脸检测，默认每 30 秒检测一次，可按时、分、秒自定义（最短 5 秒）
- **Smart Detection** (on by default): while you use the keyboard or mouse you're considered present and no detection runs; detection starts only after you've been idle for the set time. Saves power and the camera light is on less.<br>**智能检测**（默认开启）：使用键盘鼠标时视为你在电脑前，不做在场检测；停止操作达到设定时间后才开始检测，更省电、摄像头指示灯亮得更少
- **Face-area threshold**: a face counts only if it fills at least this percentage of the frame, so distant people or photos on the wall are ignored. Raise it in public places so passers-by aren't mistaken for you.<br>**人脸区域识别阈值**：人脸区域占画面的百分比达到阈值才算你在电脑前，远处的人或墙上的照片会被忽略；在公共场所请适当调大，避免把路人当成你
- Warns when multiple close faces are detected: as long as any one remains, you're considered present.<br>检测到多张近处人脸时提醒：只要其中一张还在，就会认为你在电脑前
- Pauses while the PC is locked and resumes after unlock.<br>电脑锁定时自动暂停检测，解锁后恢复
- When Battery Saver (Energy Saver since Windows 11 24H2) is on, detection pauses by default and defers to system power settings, resuming when it's off; you can choose to keep detecting. A notification is shown on both transitions.<br>节电模式（Windows 11 24H2 起为“节能模式”）开启时默认暂停检测，交给系统电源设置，关闭后自动恢复；也可以设置为节电模式下继续检测。开启、关闭时都会通知

### Posture detection / 坐姿检测

- Off by default; enable it under "Detection".<br>默认关闭，可在“检测”中开启
- Reminds you to "sit up straight" when the face is below the posture line set in the preview, and to "mind your viewing distance" when the face is too large (too close). If the problem recurs within 5 minutes of a reminder, it suggests taking a break.<br>人脸低于预览中设定的坐姿线时提醒“记得抬头挺腰”，人脸太大（离屏幕太近）时提醒“注意用眼距离”；提醒后 5 分钟内再次发现问题，会提醒你休息一下
- Posture-line height and distance thresholds are adjusted with sliders in the preview; the face box turns from orange to green when the posture is good.<br>坐姿线高度阈值和距离阈值在预览中用滑块调整，人脸区域框由橙变绿即为合适
- Can run together with presence detection, or on its own interval (default every 10 minutes). With a custom interval, detection still runs while you type even if Smart Detection is on.<br>可跟随在场检测一起检测，或自定义单独的检测间隔（默认每 10 分钟）；自定义间隔时，开着智能检测打字也会照常检测

### Lock policy / 锁定策略

- After you leave, AreYouThere locks the PC after the delay you set, so the screen isn't exposed. While you're there, the screen stays on regardless of the system's screen-off timeout.<br>离开后由『在吗』在设定延迟后锁定电脑，避免屏幕暴露给他人；在电脑前时保持屏幕常亮，不受系统息屏时间影响
- Lock delay is configurable (default 10 s; 0 s locks immediately). Returning to the camera during the countdown cancels it.<br>锁定时机按时长设置（默认 10 秒，设为 0 秒立即锁定）；倒计时期间回到摄像头前即可取消
- The screen dims during the countdown (on by default) to warn you that it's about to lock. Two methods:<br>倒计时期间压暗屏幕（默认开启），提醒你快要锁定了。可选两种方式：
  - **Black overlay**: works on all monitors; at 100% dimming the whole screen goes black, temporarily hiding the content.<br>**黑色遮罩**：所有显示器都有效，压暗强度调到 100% 时整屏全黑，临时挡住屏幕内容
  - **Lower monitor brightness**: adjusts brightness directly on laptop panels and DDC/CI-capable external monitors; unsupported monitors automatically fall back to the overlay. Original brightness is restored when the countdown ends, when you return, or after locking, and also on the next launch if the app exited unexpectedly.<br>**降低显示器亮度**：笔记本内置屏和支持 DDC/CI 的外接显示器直接调亮度；不支持的显示器自动改用遮罩。倒计时结束、回来或锁定后恢复原亮度，程序意外退出后下次启动也会恢复
- Dimming strength is set with a slider; hold and drag to preview, release to restore.<br>压暗强度用滑块调整，按住拖动即可预览，松开恢复

### Camera and preview / 摄像头与预览

- Choose which camera to use; the list refreshes when cameras are plugged or unplugged. You're notified if there's no camera, no permission, or another app has exclusive access.<br>可选择使用哪个摄像头；插拔摄像头自动刷新；没有摄像头、没有权限或摄像头被其他程序独占时给出提示
- The camera is read in shared mode and not held open, so video conferencing and recording software keep working.<br>以共享模式读取摄像头，不会占住摄像头，视频会议、录像软件仍可正常使用
- Frame rotation: some cameras deliver sideways or upside-down frames, and detection is only accurate once corrected; remembered per camera.<br>画面方向可旋转：个别摄像头送来的画面是横的或倒的，转正后检测才准确；每个摄像头单独记忆
- Live preview with face boxes showing the percentage of the frame each face covers, to help position the camera.<br>实时预览和人脸框，框上显示人脸占画面的面积百分比，便于调整摄像头位置
- Power saving: the camera is opened for only about 1–2 seconds per check and off otherwise; the status card indicates power usage by detection frequency.<br>省电：只在每次检测时打开摄像头约 1–2 秒，其余时间关闭；状态卡片按检测频率提示耗电程度

### Interface and settings / 界面与设置

- Windows 11-style settings cards and an immersive title bar (falls back automatically on Windows 10).<br>Windows 11 风格的设置卡片和沉浸式标题栏（Windows 10 场景自动回退）
- Theme: follow system, light, or dark. UI in Simplified Chinese and English, following the system language by default.<br>主题可选跟随系统、浅色或深色；界面支持简体中文和英文，默认跟随系统语言
- Notifications: by default none while a full-screen app (e.g. a game) is in the foreground; or choose never or always.<br>通知可设置：默认在全屏应用（如游戏）前台时不弹出提醒，也可选择永不通知或始终通知
- The offline/privacy notice at the top can be dismissed and shown again under "Other settings".<br>顶部的离线说明和隐私提醒可以关闭，之后可在“其他设置”中重新显示

### Tray and startup / 托盘与开机自启动

- Tray icon: click to open the window; right-click to enable/disable or exit. Closing the window keeps the app running in the tray.<br>托盘图标：单击打开窗口，右键启用/关闭或退出；关闭窗口后继续在托盘运行
- Pause detection: from the tray menu or window, pause for 15 minutes to 2 hours; it resumes automatically, or you can resume manually at any time.<br>暂停检测：在托盘右键菜单或窗口中暂停 15 分钟到 2 小时，到时自动恢复，也可随时手动恢复
- Optional start with Windows (off by default): enabling it requires explicitly acknowledging the camera and public-place privacy notice. After you sign in, a reminder appears; detection starts with your current configuration and the app stays in the tray only after you click "OK". Opening the app manually still leaves detection off by default.<br>可选开机自启动（默认关闭）：启用时需明确确认摄像头与公共场所隐私提醒；登录 Windows 后弹出提醒，点击“确定”后才按当前配置开启检测并驻留托盘。手动打开程序仍默认关闭检测
- Hidden action: press and hold the "OK" button of the startup reminder for 5 seconds to permanently dismiss future startup reminders. This is saved only after a second confirmation; afterwards, detection starts automatically at sign-in. You can restore the reminder in settings, and re-enabling start with Windows also restores it.<br>隐藏操作：在开机提醒的“确定”按钮上持续按住 5 秒，可选择永久关闭以后的开机提醒；只有二次确认后才保存，以后登录会直接自动检测。设置中可恢复提醒，重新启用开机自启动也会恢复提醒

## Download / 下载

**Microsoft Store** (recommended): the Store handles signing and automatic updates, avoids the "unknown publisher" prompt, and ARM devices automatically get the ARM64 build.

**Microsoft Store**（推荐）：商店负责签名和自动更新，安装时不会出现“未知发布者”提示，ARM 设备自动获取 ARM64 版本。

<a href="https://apps.microsoft.com/detail/9N4T5SJXMJH3"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Get it from Microsoft Store / 从 Microsoft Store 获取"></a>

Or download from [Releases](https://github.com/nathanatgit/AreYouThere/releases) (x64, x86, and ARM64):

也可以到 [Releases](https://github.com/nathanatgit/AreYouThere/releases) 下载，x64 / x86 / ARM64 都有：

- **MSI**: installer<br>**MSI**：安装版
- **Portable exe**: a single file, ready to run<br>**便携版 exe**：单个文件，下载即用
- **zip**: folder version; extract and run `AreYouThere.exe`<br>**zip**：文件夹版，解压后运行 `AreYouThere.exe`

Requires Windows 10 version 1809 or later.

需要 Windows 10 1809 或更高版本。

If you enable start with Windows for the portable version, keep it at a fixed path; after moving or renaming it, re-enable the option. Startup applies to the current Windows user only, and Windows' startup app settings or organization policies may block it. Turning off startup doesn't stop detection that is already running; turn detection off separately or exit from the tray.

便携版开启自启动后请保留在固定路径；移动或改名后需重新启用此选项。自启动仅适用于当前 Windows 用户，Windows 的启动应用设置或组织策略可能阻止运行。取消自启动不会停止当前正在进行的检测，可单独关闭检测或从托盘退出。

## Build from source / 从源码构建

Requires the .NET 8 SDK / 需要 .NET 8 SDK：

```powershell
dotnet build AreYouThere\AreYouThere.csproj -p:Platform=x64
```

## Privacy / 隐私

AreYouThere runs fully offline and collects no data. See the [Privacy Policy](PRIVACY.md).

『在吗』完全离线运行，不收集任何数据，详见 [隐私政策](PRIVACY.md)。

## License / 许可证

[MIT](LICENSE)
