using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AreYouThere.Services;

namespace AreYouThere;

// 登录 Windows 后自动运行：知情同意、开机提醒和恢复提醒。
public sealed partial class MainWindow
{
    private string DefaultStartupStatus() => _settings.SkipStartupReminder
        ? L.T("开机运行提醒已永久关闭；以后登录后会自动检测。可点击“恢复每次开机运行提醒”重新开启。",
            "Startup reminders are off. If startup is enabled, detection begins automatically when you sign in. Click \"Restore startup reminders\" to show the reminder again.")
        : L.T("启用前需确认隐私提醒；每次登录后点击“确定”才开始检测。",
            "To enable startup, confirm the privacy notice. At each sign-in, click OK on the reminder to start detection.");

    private void ShowRestoreButton(bool show) =>
        RestoreStartupReminderButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    private async Task ShowStartupReminderAsync(bool alreadyRunning = false)
    {
        _showingStartupReminder = true;
        try { await StartupService.ShowLaunchWarningAsync(RootScroll.XamlRoot, _settings, alreadyRunning); }
        finally
        {
            _showingStartupReminder = false;
            ShowRestoreButton(_settings.SkipStartupReminder);
            if (_settings.SkipStartupReminder) StartupStatusText.Text = DefaultStartupStatus();
        }
    }

    private void RestoreStartupReminder_Click(object sender, RoutedEventArgs e)
    {
        _settings.SkipStartupReminder = false;
        try
        {
            _settings.SaveOrThrow();
            ShowRestoreButton(false);
            StartupStatusText.Text = L.T("已恢复提醒：每次登录后需点击“确定”，之后才开始检测。",
                "Reminders restored: at each sign-in you must click OK before detection starts.");
        }
        catch (Exception ex)
        {
            _settings.SkipStartupReminder = true;
            StartupStatusText.Text = L.T($"无法保存提醒设置：{ex.Message}", $"Couldn't save the reminder setting: {ex.Message}");
        }
    }

    private async Task RefreshStartupStateAsync()
    {
        try
        {
            SetStartupSwitch(await StartupService.IsEnabledAsync() && _settings.StartupConsent);
            StartupSwitch.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StartupSwitch.IsEnabled = false;
            StartupStatusText.Text = L.T($"无法读取开机自启动状态：{ex.Message}", $"Couldn't check the startup setting: {ex.Message}");
        }
    }

    private void SetStartupSwitch(bool enabled)
    {
        _suppressStartupToggle = true;
        StartupSwitch.IsOn = enabled;
        _suppressStartupToggle = false;
    }

    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppressStartupToggle || _startupBusy) return;
        _startupBusy = true;
        StartupSwitch.IsEnabled = false;
        var enable = StartupSwitch.IsOn;
        var previousConsent = _settings.StartupConsent;
        var previousSkipReminder = _settings.SkipStartupReminder;
        try
        {
            if (enable)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = RootScroll.XamlRoot,
                    Title = L.T("确认开机自启动与摄像头使用", "Enable startup and automatic camera use?"),
                    Content = L.T("启用后，『在吗』将在你登录 Windows 时自动运行。每次启动都会弹出提醒，需点击“确定”；确认后自动开启在场检测并驻留托盘。\n\n"
                        + $"按当前设置，每 {PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds))}会打开摄像头检测，指示灯会亮起。显示预览时摄像头持续工作；离开后的锁定倒计时也可能更频繁地检测。检测间隔等设置修改后会用于下次启动。\n\n"
                        + "即使完全离线、不保存或上传画面，摄像头指示灯仍可能让他人以为正在录像。请勿在隐私敏感场所使用，以免引起误会或纠纷。在公共场所使用时，请遵守场所规定，并调大人脸面积阈值，避免路人入镜被当成你，导致离开后不自动锁屏。\n\n"
                        + "你可以随时关闭此选项取消自启动，关闭检测停用摄像头，或通过托盘菜单退出。确认表示你已了解并同意上述行为。",
                        "AreYouThere will run automatically when you sign in to Windows. Each time, a startup reminder asks you to click OK before presence detection begins. The app then runs in the system tray.\n\n"
                        + $"Your presence check interval is {PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds))}. The camera light comes on whenever the camera is in use. If smart detection is enabled, presence checks pause while you're using the keyboard or mouse. The preview, separate posture checks, or a lock countdown may keep the camera on or use it more often. Future startup launches use your latest settings.\n\n"
                        + "Even though the app works offline and never saves or uploads images, the camera light may make others think you are recording. Avoid using the camera where privacy is expected. In public places, follow the venue's rules and increase the minimum face size to help filter out passersby. The app detects faces, not identities: another person's face may keep the PC unlocked after you leave.\n\n"
                        + "You can disable startup at any time, turn off detection to stop camera use, or exit using the tray menu. By enabling startup, you confirm that you understand and agree to this automatic camera use."),
                    PrimaryButtonText = L.T("我已了解，启用开机自启动", "I understand — enable startup"),
                    CloseButtonText = L.T("取消", "Cancel"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await Themed(dialog, RootScroll.XamlRoot).ShowAsync() != ContentDialogResult.Primary)
                {
                    SetStartupSwitch(false);
                    return;
                }
            }
            // 启动检测必须以成功保存的知情同意为前提。
            _settings.StartupConsent = enable;
            if (enable)
            {
                _settings.SkipStartupReminder = false;
                ShowRestoreButton(false);
            }
            _settings.SaveOrThrow();
            await StartupService.SetEnabledAsync(enable);
            SetStartupSwitch(enable);
            StartupStatusText.Text = enable
                ? L.T("已启用：每次登录后弹出提醒，点击“确定”后按当前设置检测并驻留托盘。便携版请保留在固定路径。",
                    "Startup enabled. At each sign-in, click OK on the reminder to begin detection using your current settings. The app then runs in the system tray. If you use the portable version, keep it in the same folder with the same filename.")
                : L.T("已关闭开机自启动。当前检测状态不变。", "Startup disabled. This does not turn detection on or off.");
        }
        catch (Exception ex)
        {
            _settings.StartupConsent = previousConsent;
            _settings.SkipStartupReminder = previousSkipReminder;
            ShowRestoreButton(previousSkipReminder);
            _settings.Save();
            SetStartupSwitch(previousConsent && !enable);
            StartupStatusText.Text = L.T($"更改开机自启动失败：{ex.Message}", $"Couldn't change the startup setting: {ex.Message}");
        }
        finally
        {
            _startupBusy = false;
            StartupSwitch.IsEnabled = true;
        }
    }
}
