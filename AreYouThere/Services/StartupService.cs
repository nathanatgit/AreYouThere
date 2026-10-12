using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel;

namespace AreYouThere.Services;

internal static class StartupService
{
    private const string TaskId = "AreYouThereStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string StartupArgument = "--startup";

    private static bool IsPackaged
    {
        get
        {
            try { _ = Package.Current.Id; return true; }
            catch (InvalidOperationException) { return false; }
        }
    }

    public static async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged)
            return (await StartupTask.GetAsync(TaskId)).State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("AreYouThere") is string command && command == GetCommand();
    }

    public static async Task SetEnabledAsync(bool enabled)
    {
        if (IsPackaged)
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!enabled)
            {
                task.Disable();
                if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
                    throw new InvalidOperationException(L.T("Windows 未允许关闭开机自启动，请检查启动应用设置或组织策略。",
                        "Windows blocked the change to your startup setting. Check Startup apps in Windows Settings or contact your organization's administrator."));
                return;
            }
            var state = await task.RequestEnableAsync();
            if (state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
                throw new InvalidOperationException(L.T("Windows 未允许开机自启动。若已在任务管理器中禁用，请在 Windows 设置的“启动应用”中重新启用；组织策略也可能禁止此功能。",
                    "Windows blocked startup. If you disabled AreYouThere in Task Manager, enable it again under Apps › Startup in Windows Settings. Your organization's policies may also prevent startup."));
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var command = GetCommand();
            if (command.Length > 260) throw new InvalidOperationException(L.T("程序路径过长，无法注册开机自启动。请将程序移到较短的固定路径后重试。",
                "The app's file path is too long to register for startup. Move it to a folder with a shorter path and try again. Keep the app at that location."));
            key.SetValue("AreYouThere", command, RegistryValueKind.String);
        }
        else key.DeleteValue("AreYouThere", throwOnMissingValue: false);
    }

    private static string GetCommand() => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException(L.T("无法确定程序路径。请直接运行 AreYouThere.exe。", "Can't determine the app's path. Run AreYouThere.exe directly.")) }\" {StartupArgument}";

    // 普通点击确认；持续按住五秒后另行确认是否永久停用提醒。
    public static async Task ShowLaunchWarningAsync(XamlRoot root, AppSettings settings, bool alreadyRunning = false)
    {
        if (settings.SkipStartupReminder) return;
        var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(settings.IntervalSeconds));
        var action = alreadyRunning
            ? L.T("已有一个 AreYouThere 实例正在运行。本次启动不会重复开启检测，请在已打开的窗口中检查检测状态；摄像头工作时指示灯会亮起。",
                "AreYouThere is already running. No second instance will be started. Check the detection status in the app window. The camera light turns on whenever the camera is in use.")
            : settings.StartupConsent
            ? L.T($"点击“确定”后将按当前设置每 {interval}检测一次，摄像头指示灯会在检测时亮起。显示预览时摄像头会持续工作。",
                $"Click OK to begin presence detection using your current settings. Background checks run every {interval}, and the camera light turns on during each check. The camera stays on while the preview is visible.")
            : L.T("尚未在应用内确认开机自启动，摄像头检测保持关闭。请在应用中启用并确认此选项。",
                "You have not agreed to automatic startup in the app, so detection remains off. To enable it, turn on \"Start when I sign in to Windows\" and confirm the privacy notice.");
        var text = L.T("『在吗』AreYouThere 已随 Windows 登录自动启动，正在运行。", "AreYouThere is running. It started automatically when you signed in to Windows.")
            + "\n\n" + action + "\n\n"
            + L.T("请确认当前场所适合使用摄像头。请勿在隐私敏感场所使用，以免引起误会或纠纷。在公共场所使用时，请遵守场所规定，并调大人脸面积阈值，避免路人入镜被当成你，导致离开后不自动锁屏。画面仅在本机内存中处理，不保存、不上传。",
                "Make sure camera use is appropriate here. Avoid using the camera where privacy is expected, as this may cause concern or disputes. In public places, follow the venue's rules and increase the minimum face size to help filter out passersby. The app detects faces, not identities: another person's face may keep the PC unlocked after you leave. Images are processed only in memory on this PC and are never saved or uploaded.")
            + "\n\n"
            + L.T("可在应用中关闭检测或开机自启动，右键托盘图标可退出程序。请点击“确定”确认已知晓。",
                "You can turn off detection or disable startup in the app. To exit, right-click the system tray icon and choose Exit. Click OK to acknowledge this notice.");
        var ok = new Button { Content = L.T("确定", "OK"), HorizontalAlignment = HorizontalAlignment.Right };
        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(ok);
        var dialog = new ContentDialog { XamlRoot = root, Title = L.T("『在吗』开机运行提醒", "AreYouThere startup reminder"), Content = content };
        var acknowledged = false;
        var held = false;
        uint? pointerId = null;
        var holdTimer = ok.DispatcherQueue.CreateTimer();
        holdTimer.Interval = TimeSpan.FromSeconds(5);
        holdTimer.IsRepeating = false;
        holdTimer.Tick += (_, _) =>
        {
            if (pointerId is null || !ok.IsPointerOver) return;
            held = true;
            acknowledged = true;
            dialog.Hide();
        };
        void CancelHold() { holdTimer.Stop(); pointerId = null; }
        ok.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(ok);
            if (!point.IsInContact || !point.Properties.IsLeftButtonPressed || pointerId is not null) return;
            pointerId = e.Pointer.PointerId;
            holdTimer.Start();
        }), handledEventsToo: true);
        ok.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (pointerId == e.Pointer.PointerId) CancelHold();
        }), handledEventsToo: true);
        ok.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => CancelHold()), handledEventsToo: true);
        ok.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => CancelHold()), handledEventsToo: true);
        ok.PointerExited += (_, _) => CancelHold();
        ok.Click += (_, _) => { CancelHold(); acknowledged = true; dialog.Hide(); };
        dialog.Closing += (_, e) => e.Cancel = !acknowledged;
        dialog.Opened += (_, _) => ok.Focus(FocusState.Programmatic);
        try { await dialog.ShowAsync(); }
        finally { CancelHold(); }
        if (!held) return;

        var confirm = new ContentDialog
        {
            XamlRoot = root,
            Title = L.T("永久关闭开机运行提醒？", "Turn off startup reminders permanently?"),
            Content = L.T("关闭后，『在吗』将在以后登录 Windows 时直接开始检测，不再弹出此提醒，也无需点击“确定”。摄像头会按当前配置自动工作，指示灯会亮起。\n\n请确认你已了解在公共或隐私敏感场所使用的风险。此选择会保存在本机；你可以在设置中点击“恢复每次开机运行提醒”。",
                "When AreYouThere starts automatically at sign-in, it will begin presence detection without showing this reminder or waiting for you to click OK. The camera will turn on according to your current settings, and its indicator light will come on.\n\nPlease confirm that you understand the risks of camera use in public places or where privacy is expected. This choice is saved on this PC and remains in effect until you restore reminders. Click \"Restore startup reminders\" in settings to show them again."),
            PrimaryButtonText = L.T("已了解，永久关闭提醒", "I understand — turn off reminders"),
            CloseButtonText = L.T("保留每次提醒", "Keep reminders"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        settings.SkipStartupReminder = true;
        try { settings.SaveOrThrow(); }
        catch (Exception ex)
        {
            settings.SkipStartupReminder = false;
            await new ContentDialog
            {
                XamlRoot = root, Title = L.T("无法保存提醒设置", "Couldn't save the reminder setting"),
                Content = L.T($"仍会在下次启动时提醒。\n{ex.Message}", $"You'll still be reminded at the next startup.\n{ex.Message}"), CloseButtonText = L.T("确定", "OK")
            }.ShowAsync();
        }
    }
}
