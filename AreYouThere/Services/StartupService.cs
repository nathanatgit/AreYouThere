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
                    throw new InvalidOperationException("Windows 未允许关闭开机自启动，请检查启动应用设置或组织策略。");
                return;
            }
            var state = await task.RequestEnableAsync();
            if (state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
                throw new InvalidOperationException("Windows 未允许开机自启动。若已在任务管理器中禁用，请在 Windows 设置的“启动应用”中重新启用；组织策略也可能禁止此功能。");
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var command = GetCommand();
            if (command.Length > 260) throw new InvalidOperationException("程序路径过长，无法注册开机自启动。请将程序移到较短的固定路径后重试。");
            key.SetValue("AreYouThere", command, RegistryValueKind.String);
        }
        else key.DeleteValue("AreYouThere", throwOnMissingValue: false);
    }

    private static string GetCommand() => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。请直接运行 AreYouThere.exe。") }\" {StartupArgument}";

    // 普通点击确认；持续按住五秒后另行确认是否永久停用提醒。
    public static async Task ShowLaunchWarningAsync(XamlRoot root, AppSettings settings, bool alreadyRunning = false)
    {
        if (settings.SkipStartupReminder) return;
        var action = alreadyRunning ? "已有一个 AreYouThere 实例正在运行。本次启动不会重复开启检测，请在已打开的窗口中检查检测状态；摄像头工作时指示灯会亮起。"
            : settings.StartupConsent
            ? $"点击“确定”后将按当前设置每 {PowerPolicy.Describe(TimeSpan.FromSeconds(settings.IntervalSeconds))}检测一次，摄像头指示灯会在检测时亮起。显示预览时摄像头会持续工作。"
            : "尚未在应用内确认开机自启动，摄像头检测保持关闭。请在应用中启用并确认此选项。";
        var text = "AreYouThere（在吗）已随 Windows 登录自动启动，正在运行。\n\n" + action
            + "\n\n请确认当前场所适合使用摄像头；在公共或隐私敏感场所使用可能引起误会或纠纷。画面仅在本机内存中处理，不保存、不上传。"
            + "\n\n可在应用中关闭检测或开机自启动，右键托盘图标可退出程序。请点击“确定”确认已知晓。";
        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        var content = new StackPanel { Spacing = 20 };
        content.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(ok);
        var dialog = new ContentDialog { XamlRoot = root, Title = "AreYouThere 开机运行提醒", Content = content };
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
            Title = "永久关闭开机运行提醒？",
            Content = "关闭后，AreYouThere 将在以后登录 Windows 时直接开始检测，不再弹出此提醒，也无需点击“确定”。摄像头会按当前配置自动工作，指示灯会亮起。\n\n请确认你已了解在公共或隐私敏感场所使用的风险。此选择会保存在本机；你可以在设置中点击“恢复每次开机运行提醒”。",
            PrimaryButtonText = "已了解，永久关闭提醒",
            CloseButtonText = "保留每次提醒",
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
                XamlRoot = root, Title = "无法保存提醒设置", Content = $"仍会在下次启动时提醒。\n{ex.Message}", CloseButtonText = "确定"
            }.ShowAsync();
        }
    }
}
