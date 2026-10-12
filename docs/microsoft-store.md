# 上架 Microsoft Store

| 项目 | 值 |
|---|---|
| 商店页面 | <https://apps.microsoft.com/detail/9N4T5SJXMJH3> |
| Store ID | `9N4T5SJXMJH3` |
| 商店协议链接 | `ms-windows-store://pdp/?productid=9N4T5SJXMJH3` |
| Package/Identity/Name | `NathanHan.AreYouThere` |
| Package/Identity/Publisher | `CN=65060FB2-9417-4C15-9ABF-D5188EB4DF3A` |
| PublisherDisplayName | `NathanHan` |
| 包系列名 (PFN) | `NathanHan.AreYouThere_1qae44mz451pg`（由前两项计算得出，构建时无需配置） |

Microsoft Store 会替你给 MSIX 签名，用户从商店安装不会看到任何“未知发布者”警告，ARM 设备也会自动拿到 ARM64 版本。商店只覆盖 MSIX；GitHub Release 里的 MSI / 便携版 / zip 不受影响。

## 1. 注册开发者账户（只需一次）

1. 打开 <https://storedeveloper.microsoft.com>，用你的 Microsoft 账户登录，选择**个人（Individual）**账户。费用以注册页面显示为准。
2. 按提示完成资料填写和身份验证。

## 2. 预留应用名称

1. 进入 [Partner Center](https://partner.microsoft.com/dashboard/apps-and-games/overview) › **应用和游戏** › **新建产品** › **MSIX 或 PWA 应用**。
2. 输入名称，建议 `在吗 AreYouThere`。如果被占用，可以试 `在吗 - AreYouThere` 或 `AreYouThere 在吗`。

## 3. 把产品标识交给构建流程

在产品页面 › **产品管理** › **产品标识**，找到这三个值：

| Partner Center 字段 | GitHub 变量 |
|---|---|
| Package/Identity/Name | `MSIX_IDENTITY_NAME` |
| Package/Identity/Publisher | `MSIX_PUBLISHER` |
| Package/Properties/PublisherDisplayName | `MSIX_PUBLISHER_DISPLAY_NAME` |

设置到仓库（Settings › Secrets and variables › Actions › **Variables**），或用命令行：

```powershell
gh variable set MSIX_IDENTITY_NAME --body "<Package/Identity/Name>"
gh variable set MSIX_PUBLISHER --body "<Package/Identity/Publisher>"
gh variable set MSIX_PUBLISHER_DISPLAY_NAME --body "<PublisherDisplayName>"
```

这些值不是机密，可以公开。

## 4. 生成商店包

推送版本标签，等 GitHub Actions 完成后，从 Release 下载 `AreYouThere-<版本>.msixbundle`：

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## 5. 创建提交

在产品页面点 **开始提交**，逐项填写：

- **定价和可用性**：免费；市场按需选择。
- **属性**：类别选「实用工具和工具」；**隐私政策 URL** 填
  `https://github.com/nathanatgit/AreYouThere/blob/main/PRIVACY.md`（使用摄像头的应用必须提供）；勾选需要摄像头。
- **年龄分级**：完成问卷（无用户互动、无数据共享、无内购）。
- **程序包**：上传 `.msixbundle`，设备系列勾选 Desktop。
- **应用商店一览**：见下方文案，至少上传 1 张截图（建议 1920×1080 或 1366×768）。
- **提交选项 › 受限功能**：程序声明了 `runFullTrust`，需要填写理由（见下方）。
- **认证说明**：见下方，给审核人员看的测试说明。

提交后一般 1–3 个工作日完成认证。以后每次发布新版本，推送新标签，然后在 Partner Center 新建提交、上传新的 `.msixbundle` 即可。

## 商店文案（可直接粘贴）

**简短说明**

> 你在电脑前时，屏幕就不会自动关闭。完全离线，用普通摄像头实现微软「存在感知」的效果。

**说明**

> 微软官方的「存在感知」(Presence Sensing) 只在部分配备专用传感器的新款设备上提供。「在吗」是它的轻量替代方案：用电脑上普通的摄像头，按设定的间隔判断你是否在电脑前。你在的时候，暂时阻止 Windows 自动息屏；你离开后，可以交还系统电源策略，或在设定的延迟后自动锁定电脑。
>
> • 完全离线：没有任何联网功能，不上传、不收集、不保存任何画面或数据
> • 省电：不显示预览时，只在每次检测时短暂打开摄像头，界面会提示当前设置的耗电程度
> • 离开后可选：跟随系统电源策略，或由「在吗」接管，立即或延迟锁定电脑
> • 支持选择摄像头，电脑锁定时自动暂停检测
> • 常驻托盘，开源免费（MIT 许可证）

**功能（每行一条）**

> 检测到你在电脑前时阻止自动息屏
> 离开后可按设定延迟自动锁定电脑
> 完全离线，画面只在本机内存中处理
> 后台省电模式，耗电程度一目了然
> 支持多个摄像头选择
> 检测 PowerToys Awake 冲突并提示

**runFullTrust 理由（英文，给审核人员）**

> AreYouThere is a WinUI 3 desktop app. It needs full trust to call Win32 APIs that are not available to sandboxed apps: SetThreadExecutionState (keep the display on while the user is present), LockWorkStation (optional lock when the user leaves), Shell_NotifyIcon (tray icon), and WTSRegisterSessionNotification (pause camera use while the session is locked). It has no network access and does not collect any data.

**认证说明（英文，给审核人员）**

> The app requires a webcam. Turn on "启用在场检测" (Enable presence detection) and sit in front of the camera; the status line shows that the display is kept awake. Under "离开电脑后" (After leaving) you can choose to follow the system power plan or to let the app lock the PC after a delay. The app is fully offline and stores only its settings locally. Closing the window keeps it running in the tray; use the tray menu's "退出" to exit.
