using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using AreYouThere.Services;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace AreYouThere;

public sealed partial class MainWindow : Window
{
    private readonly PresenceService _presence = new();
    private readonly TrayService _tray;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherQueueTimer _policyTimer;
    private readonly CameraCatalog _cameraCatalog = new();
    private IReadOnlyList<CameraInfo> _cameras = [];
    private int _previewBusy, _refreshingCameras;
    private bool _ready, _exiting, _trayHintShown, _suppressToggle, _pausedByLock, _starting, _suppressCameraChange;
    private bool _startupPending, _startupInitialized, _suppressStartupToggle, _startupBusy;
    private bool _showingStartupReminder;

    public MainWindow(bool startupLaunch = false)
    {
        _startupPending = startupLaunch;
        L.SetLanguage(_settings.Language); // 托盘、对话框等都按这里选定的语言
        _trayStatus = L.T("未启用", "Off");
        InitializeComponent();
        Localize();
        StatusText.Text = L.Status(_trayStatus);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.Closing += AppWindow_Closing;
        _tray = new TrayService(this, iconPath, () => EnabledSwitch.IsOn, ToggleFromTray, ShowFromTray, ExitApp);
        _tray.SessionLockChanged += Tray_SessionLockChanged;
        _tray.StartupLaunchRequested += () => DispatcherQueue.TryEnqueue(async () =>
        {
            if (_settings.SkipStartupReminder || _showingStartupReminder || !_startupInitialized || _startupBusy) return;
            ShowFromTray();
            await ShowStartupReminderAsync(alreadyRunning: true);
        });
        _presence.StatusChanged += Presence_StatusChanged;
        _presence.FrameReady += Presence_FrameReady;
        _presence.LockCountdownStarted += Presence_LockCountdownStarted;
        _presence.CameraFailed += Presence_CameraFailed;
        _presence.MultipleFacesDetected += Presence_MultipleFacesDetected;
        _cameraCatalog.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => _ = RefreshCamerasAsync());

        // 预览只在窗口可见时有意义；窗口隐藏/最小化后让摄像头进入省电的间歇工作模式
        AppWindow.Changed += (_, e) => { if (e.DidVisibilityChange || e.DidPresenterChange || e.DidSizeChange) UpdatePreviewWanted(); };
        VisibilityChanged += (_, _) => UpdatePreviewWanted();

        RootScroll.Loaded += async (_, _) =>
        {
            RootScroll.Focus(FocusState.Programmatic);
            if (_startupInitialized) return;
            _startupInitialized = true;
            if (startupLaunch)
                await ShowStartupReminderAsync();
            await RefreshStartupStateAsync();
            await RefreshCamerasAsync();
            _startupPending = false;
            UpdateSwitchEnabled();
            if (startupLaunch && _settings.StartupConsent && StartupSwitch.IsOn && EnabledSwitch.IsEnabled && !_exiting)
            {
                // 确认后先收起窗口，按检测间隔工作，避免预览导致指示灯常亮。
                AppWindow.Hide();
                EnabledSwitch.IsOn = true;
            }
        };
        EnabledSwitch.IsEnabled = false; // 找到摄像头之前不能启用
        ApplySettings();
        _ready = true;
        RefreshPolicyInfo();

        // 电源来源（插拔电源）、系统设置和 Awake 状态都可能随时变化，定期刷新提示
        _policyTimer = DispatcherQueue.CreateTimer();
        _policyTimer.Interval = TimeSpan.FromSeconds(30);
        _policyTimer.Tick += (_, _) => RefreshPolicyInfo();
        _policyTimer.Start();
        Activated += async (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            RefreshPolicyInfo();
            if (_startupInitialized && !_startupBusy) await RefreshStartupStateAsync();
        };

        Closed += (_, _) => { _policyTimer.Stop(); _presence.Dispose(); _cameraCatalog.Dispose(); _tray.Dispose(); };
    }

    public void SetInitialSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var width = Math.Min((int)(960 * scale), work.Width);
        var height = Math.Min((int)(720 * scale), work.Height - (int)(40 * scale));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }

    private bool? _wideLayout;

    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 820;
        if (wide == _wideLayout) return;
        _wideLayout = wide;
        Grid.SetRow(PreviewCard, wide ? 1 : 2);
        Grid.SetColumn(PreviewCard, wide ? 1 : 0);
        Grid.SetColumnSpan(SettingsPanel, wide ? 1 : 2);
        Grid.SetColumnSpan(PreviewCard, wide ? 1 : 2);
        PreviewCard.Width = wide ? 320 : double.NaN;
        PreviewArea.Height = wide ? 180 : 260;
    }

    #region 语言

    /// <summary>按当前语言设置所有固定文字。启动时调用一次，切换语言时再调用。</summary>
    private void Localize()
    {
        Title = L.T("在吗 AreYouThere", "AreYouThere");
        AppTitleText.Text = L.T("在吗", "AreYouThere");
        AppSubtitleText.Visibility = L.Zh ? Visibility.Visible : Visibility.Collapsed;
        IntroText.Text = L.T("微软官方的“存在感知”(Presence Sensing) 只在部分新款设备上提供。『在吗』是它的轻量替代方案：用普通摄像头判断你是否在电脑前，在的时候阻止 Windows 自动息屏，离开后锁定电脑。",
            "Microsoft's Presence Sensing is available only on certain newer devices. AreYouThere is a lightweight alternative that uses a standard webcam to detect whether someone is at the PC. It keeps the screen on while someone is present and can lock the PC when no one is detected.");
        OfflineBar.Title = L.T("完全离线，只在本机处理", "Works entirely offline");
        OfflineBar.Message = L.T("『在吗』没有任何联网功能：不上传、不收集、不保存任何画面或检测数据。摄像头画面只在本机内存中分析，分析完立即丢弃；偏好设置仅保存在本机。",
            "AreYouThere never connects to the internet. Camera images are processed only in memory on this PC, then discarded. No images or detection results are saved or uploaded. Only your preferences are saved locally.");
        PrivacyBar.Message = L.T("隐私提醒：启用后摄像头指示灯会亮起（显示预览时常亮，后台运行时每次检测短暂亮起）。请勿在隐私敏感场所使用，以免引起误会或纠纷。在公共场所使用时，请遵守场所规定，并调大人脸面积阈值，避免路人入镜被当成你，导致离开后不自动锁屏。",
            "Privacy notice: the camera light stays on while the preview is visible and lights up briefly during each background check. Avoid using the camera where privacy is expected, as this may cause concern or disputes. In public places, follow the venue's rules and increase the minimum face size to help filter out passersby. The app detects faces, not identities: another person's face may keep the PC unlocked after you leave.");
        AwakeWarning.Title = L.T("可能存在冲突", "Possible conflict");
        CameraPrivacyButton.Content = L.T("打开摄像头隐私设置", "Open camera privacy settings");
        CameraLabel.Text = L.T("摄像头", "Camera");
        if (_cameras.Count == 0) CameraBox.PlaceholderText = L.T("正在查找摄像头…", "Looking for cameras…");
        RefreshCamerasButton.Content = L.T("刷新", "Refresh");
        CameraHintText.Text = L.T("有些电脑上摄像头名称显示不准确。不确定是哪一个时，可以开启检测并查看预览来确认。",
            "Camera names aren't always accurate. If you're not sure which one is which, turn on detection and check the preview.");
        foreach (var toggle in new[] { EnabledSwitch, StartupSwitch, PreviewSwitch })
        {
            toggle.OnContent = L.T("开", "On");
            toggle.OffContent = L.T("关", "Off");
        }
        EnabledSwitch.Header = L.T("启用在场检测", "Presence detection");
        StartupSwitch.Header = L.T("开机自启动（登录 Windows 后）", "Start when I sign in to Windows");
        StartupStatusText.Text = DefaultStartupStatus();
        RestoreStartupReminderButton.Content = L.T("恢复每次开机运行提醒", "Restore startup reminders");
        PreviewSwitch.Header = L.T("显示摄像头预览", "Show camera preview");
        IntervalLabel.Text = L.T("检测间隔", "Check interval");
        string[] intervals = L.Zh
            ? ["15 秒", "30 秒", "1 分钟（默认）", "2 分钟", "5 分钟"]
            : ["15 seconds", "30 seconds", "1 minute (default)", "2 minutes", "5 minutes"];
        SetItemTexts(IntervalBox, intervals);
        AwayModeButtons.Header = L.T("离开电脑后", "When you leave");
        AwayFollowOption.Content = L.T("跟随系统电源策略", "Follow Windows power settings");
        AwayLockOption.Content = L.T("由『在吗』接管：检测到离开后锁定电脑", "Let AreYouThere lock the PC when you leave");
        LockDelayBox.Header = L.T("锁定时机", "Lock delay after absence is detected");
        string[] delays = L.Zh
            ? ["立即锁定", "延迟 15 秒后锁定", "延迟 30 秒后锁定", "延迟 1 分钟后锁定", "延迟 2 分钟后锁定", "延迟一个检测间隔后锁定"]
            : ["Immediately", "15 seconds", "30 seconds", "1 minute", "2 minutes", "Same as check interval"];
        SetItemTexts(LockDelayBox, delays);
        TrayHintText.Text = L.T("关闭此窗口后程序会继续在右下角托盘运行。单击托盘图标打开窗口，右键可启用/关闭或退出。",
            "Closing this window keeps the app running in the system tray. Click the tray icon to open this window; right-click it to turn detection on or off, or to exit.");
        LanguageBox.Header = L.T("语言", "Language");
        ((ComboBoxItem)LanguageBox.Items[0]).Content = L.T("跟随系统", "System default");
        AboutHeader.Text = L.T("关于", "About");
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        AboutText.Text = L.T($"在吗 AreYouThere v{version} · MIT 许可证 · 开源免费", $"AreYouThere v{version} · MIT License · Free and open source");
        ToolTipService.SetToolTip(RepoLink, L.T("在浏览器中打开", "Open in browser"));
        RepoLinkText.Text = L.T("github.com/nathanatgit/AreYouThere（在浏览器中打开）", "github.com/nathanatgit/AreYouThere (opens in browser)");
        PreviewTitle.Text = L.T("摄像头预览", "Camera preview");
        PreviewPlaceholder.Text = L.T("预览未开启", "Preview is off");
        CameraStatusText.Text = L.T("摄像头状态：尚未打开", "Camera: not started");
        FaceStatusText.Text = L.T("人脸状态：等待检测", "Face detection: waiting for a check");
        PreviewNoteText.Text = L.T("预览仅用于本地调整位置，不保存或上传图像。", "The preview is only for positioning the camera. No images are saved or uploaded.");
        FaceAreaSlider.Header = L.T("人脸最小面积（占画面）", "Minimum face size (% of image area)");
        FaceAreaHintText.Text = L.T("只有面积不小于此阈值的人脸（绿框）才算你在电脑前；远处的人、路过的人或墙上的照片通常较小（灰框），会被忽略。请先调整摄像头，坐在平时的位置查看绿框上的百分比，把阈值设为略低于它（例如显示 8% 时设为 4–5%）。阈值太小时，在公共场所可能把附近的其他人当成你，你离开后电脑不会锁定；太大时你稍微坐远就会被当成离开。",
            "A face must cover at least this percentage of the image to count as someone being present (green box). Smaller faces are ignored (gray box). The app does not identify you or distinguish a real face from a face in a photo. Position the camera, sit in your usual spot, and set the minimum slightly below the percentage shown above your face. For example, if it shows 8%, try 4–5%. If the minimum is too low, other people's faces may keep the PC unlocked after you leave. If it is too high, leaning back may make the app think you have left.");
    }

    private static void SetItemTexts(ComboBox box, string[] texts)
    {
        for (var i = 0; i < texts.Length && i < box.Items.Count; i++) ((ComboBoxItem)box.Items[i]).Content = texts[i];
    }

    private string DefaultStartupStatus() => _settings.SkipStartupReminder
        ? L.T("开机运行提醒已永久关闭；以后登录后会自动检测。可点击“恢复每次开机运行提醒”重新开启。",
            "Startup reminders are off. If startup is enabled, detection begins automatically when you sign in. Click \"Restore startup reminders\" to show the reminder again.")
        : L.T("默认关闭。启用前需确认隐私提醒；每次随开机启动都需点击“确定”，之后才开启检测并驻留托盘。",
            "Startup is off by default. Enabling it requires your agreement to the privacy notice. At each sign-in, click OK on the startup reminder to begin detection. The app then runs in the system tray.");

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        var language = item.Tag as string;
        _settings.Language = language is "zh" or "en" ? language : null;
        _settings.Save();
        L.SetLanguage(_settings.Language);
        Localize();
        ApplyFaceArea();
        RefreshPolicyInfo();
        HideCameraProblem(); // 旧语言的错误说明；问题仍在时（如没有摄像头）下面刷新会用新语言重新显示
        _presence.RefreshLanguage();
        Report(EnabledSwitch.IsOn ? RunningStatus() : L.T("未启用", "Off"));
        _ = RefreshCamerasAsync();
    }

    #endregion

    #region 设置

    private uint IntervalSeconds => _settings.IntervalSeconds;

    private string RunningStatus()
    {
        var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds));
        return L.T($"运行中，每 {interval}检测一次", $"Running, checking every {interval}");
    }

    private async Task ShowStartupReminderAsync(bool alreadyRunning = false)
    {
        _showingStartupReminder = true;
        try { await StartupService.ShowLaunchWarningAsync(RootScroll.XamlRoot, _settings, alreadyRunning); }
        finally
        {
            _showingStartupReminder = false;
            RestoreStartupReminderButton.IsEnabled = _settings.SkipStartupReminder;
            if (_settings.SkipStartupReminder) StartupStatusText.Text = DefaultStartupStatus();
        }
    }

    private void RestoreStartupReminder_Click(object sender, RoutedEventArgs e)
    {
        _settings.SkipStartupReminder = false;
        try
        {
            _settings.SaveOrThrow();
            RestoreStartupReminderButton.IsEnabled = false;
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
                        + $"With your current settings, the camera turns on every {PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds))} for a check, and its indicator light comes on. The camera stays on while the preview is visible. During the lock countdown, the app may check more frequently. Future startup launches use your latest settings.\n\n"
                        + "Even though the app works offline and never saves or uploads images, the camera light may make others think you are recording. Avoid using the camera where privacy is expected. In public places, follow the venue's rules and increase the minimum face size to help filter out passersby. The app detects faces, not identities: another person's face may keep the PC unlocked after you leave.\n\n"
                        + "You can disable startup at any time, turn off detection to stop camera use, or exit using the tray menu. By enabling startup, you confirm that you understand and agree to this automatic camera use."),
                    PrimaryButtonText = L.T("我已了解，启用开机自启动", "I understand — enable startup"),
                    CloseButtonText = L.T("取消", "Cancel"),
                    DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
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
                RestoreStartupReminderButton.IsEnabled = false;
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
            RestoreStartupReminderButton.IsEnabled = previousSkipReminder;
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

    private void ApplySettings()
    {
        SelectByTag(IntervalBox, _settings.IntervalSeconds.ToString());
        PreviewSwitch.IsOn = _settings.PreviewEnabled;
        RestoreStartupReminderButton.IsEnabled = _settings.SkipStartupReminder;
        SelectByTag(LanguageBox, _settings.Language ?? "system");
        AwayModeButtons.SelectedIndex = _settings.LockWhenAway ? 1 : 0;
        SelectByTag(LockDelayBox, _settings.LockDelaySeconds.ToString());
        LockDelayBox.Visibility = _settings.LockWhenAway ? Visibility.Visible : Visibility.Collapsed;
        _presence.LockWhenAway = _settings.LockWhenAway;
        _presence.LockDelay = _settings.LockDelaySeconds < 0 ? null : TimeSpan.FromSeconds(_settings.LockDelaySeconds);
        FaceAreaSlider.Value = Math.Clamp(_settings.MinFaceAreaPercent, FaceAreaSlider.Minimum, FaceAreaSlider.Maximum);
        ApplyFaceArea();
    }

    private void FaceAreaSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyFaceArea();
        _settings.Save();
    }

    private void ApplyFaceArea()
    {
        _settings.MinFaceAreaPercent = FaceAreaSlider.Value;
        _presence.MinFaceAreaPercent = FaceAreaSlider.Value;
        FaceAreaText.Text = FaceAreaSlider.Value == 0
            ? L.T("当前阈值：0%（任何大小的人脸都算你在，不推荐）", "Threshold: 0% (any detected face counts as someone being present; not recommended)")
            : L.T($"当前阈值：{FaceAreaSlider.Value:0.#}%", $"Threshold: {FaceAreaSlider.Value:0.#}%");
    }

    private void Presence_MultipleFacesDetected(object? sender, int count)
    {
        DispatcherQueue.TryEnqueue(() => _tray.ShowBalloon(L.T("『在吗』检测到多张近处人脸", "Multiple faces detected"),
            L.T($"画面中有 {count} 张人脸达到面积阈值。只要其中一张还在，『在吗』就会认为你在电脑前，你离开后电脑可能不会锁定。请调整摄像头角度或提高人脸面积阈值。",
                $"{count} faces meet the minimum size. Detection of any one of them will keep the PC awake and may prevent it from locking after you leave. Adjust the camera angle or increase the minimum face size.")));
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
        if (item is not null) box.SelectedItem = item;
    }

    private void IntervalBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (IntervalBox.SelectedItem is ComboBoxItem item && uint.TryParse(item.Tag?.ToString(), out var seconds))
        {
            _settings.IntervalSeconds = seconds;
            _settings.Save();
            if (EnabledSwitch.IsOn) _presence.SetInterval(seconds);
            RefreshPolicyInfo();
        }
    }

    private void AwayModeButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.LockWhenAway = AwayModeButtons.SelectedIndex == 1;
        _settings.Save();
        _presence.LockWhenAway = _settings.LockWhenAway;
        LockDelayBox.Visibility = _settings.LockWhenAway ? Visibility.Visible : Visibility.Collapsed;
        RefreshPolicyInfo();
    }

    private void LockDelayBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        if (LockDelayBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var seconds))
        {
            _settings.LockDelaySeconds = seconds;
            _settings.Save();
            _presence.LockDelay = seconds < 0 ? null : TimeSpan.FromSeconds(seconds);
            RefreshPolicyInfo();
        }
    }

    /// <summary>刷新“离开后会发生什么”的说明，以及 PowerToys Awake 冲突提示。</summary>
    private void RefreshPolicyInfo()
    {
        if (!_ready) return;
        var interval = TimeSpan.FromSeconds(IntervalSeconds);
        if (_settings.LockWhenAway)
        {
            var delay = _settings.LockDelaySeconds < 0 ? interval : TimeSpan.FromSeconds(_settings.LockDelaySeconds);
            var total = PowerPolicy.Describe(interval + delay);
            PolicyText.Text = delay == TimeSpan.Zero
                ? L.T($"在电脑前时保持屏幕常亮，不受系统超时影响。检测到离开后立即锁定电脑；回到摄像头前会取消锁定。\n从你离开到锁定，最长约 {total}（检测间隔 + 锁定延迟）。",
                    $"The screen stays on while a qualifying face is detected, overriding Windows timeouts. The PC locks as soon as a check detects no qualifying face. Returning after the PC has locked does not unlock it.\nThe estimated maximum time from leaving to locking is {total} (check interval + lock delay).")
                : L.T($"在电脑前时保持屏幕常亮，不受系统超时影响。检测到离开后再等 {PowerPolicy.Describe(delay)}，期间仍未回来就锁定电脑；回到摄像头前会取消锁定。\n从你离开到锁定，最长约 {total}（检测间隔 + 锁定延迟）。",
                    $"The screen stays on while a qualifying face is detected, overriding Windows timeouts. If a check detects no qualifying face, a {PowerPolicy.Describe(delay)} lock countdown begins. Detecting a qualifying face before the countdown ends cancels the pending lock.\nThe estimated maximum time from leaving to locking is {total} (check interval + lock delay).");
        }
        else
        {
            var p = PowerPolicy.Read();
            var source = p.OnBattery ? L.T("使用电池", "on battery") : L.T("接通电源", "plugged in");
            var lockText = p.ScreenSaverLockSeconds is uint s
                ? L.T($"屏幕保护程序 {PowerPolicy.Describe(TimeSpan.FromSeconds(s))}后启动并锁定", $"the screen saver starts after {PowerPolicy.Describe(TimeSpan.FromSeconds(s))} of inactivity and requires sign-in when you return")
                : L.T("未设置屏保锁定，通常在电脑睡眠后需要重新登录", "the screen saver is not set to require sign-in; whether sign-in is required after sleep depends on Windows settings");
            var after = p.DisplayOffSeconds == 0
                ? L.T("当前电源计划不会自动关闭屏幕。", "Your power plan never turns off the screen.")
                : L.T($"离开后最长约 {PowerPolicy.Describe(interval + TimeSpan.FromSeconds(p.DisplayOffSeconds))}息屏（检测间隔 + 系统息屏时间）。",
                    $"After you leave, the screen turns off within about {PowerPolicy.Describe(interval + TimeSpan.FromSeconds(p.DisplayOffSeconds))} (check interval + Windows screen timeout).");
            var screenTimeout = p.DisplayOffSeconds == 0 ? "never" : $"after {PowerPolicy.Describe(TimeSpan.FromSeconds(p.DisplayOffSeconds))} of inactivity";
            var sleepTimeout = p.SleepSeconds == 0 ? "never" : $"after {PowerPolicy.Describe(TimeSpan.FromSeconds(p.SleepSeconds))} of inactivity";
            PolicyText.Text = L.T($"当前电源计划（{source}）：无操作 {PowerPolicy.DescribeTimeout(p.DisplayOffSeconds)}关闭屏幕，{PowerPolicy.DescribeTimeout(p.SleepSeconds)}睡眠；{lockText}。\n{after}",
                $"Current power plan ({source}): screen off: {screenTimeout}; sleep: {sleepTimeout}; {lockText}.\n{after}");
        }

        var conflict = PowerToysAwake.CheckConflict();
        AwakeWarning.Message = conflict ?? "";
        AwakeWarning.IsOpen = conflict is not null;
        AwakeWarning.Visibility = conflict is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshPowerInfo();
    }

    /// <summary>按当前设置给出一行耗电评级：几乎无影响 / 轻微 / 中等 / 非常耗电。</summary>
    private void RefreshPowerInfo()
    {
        if (!_ready) return;
        int level; // 0 几乎无影响、1 轻微、2 中等、3 非常耗电
        string reason;
        if (!EnabledSwitch.IsOn)
        {
            (level, reason) = (0, L.T("未启用，摄像头关闭", "detection off, camera off"));
        }
        else if (PreviewWanted)
        {
            // 预览可见时摄像头一直工作
            (level, reason) = (3, L.T("预览让摄像头一直工作，关闭预览或把窗口收到托盘即可省电",
                "the preview keeps the camera on; turn off the preview or close this window to save power"));
        }
        else
        {
            // 后台运行时每次检测打开摄像头约 1–2 秒，间隔越短，摄像头工作的比例越高
            var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds));
            (level, reason) = IntervalSeconds switch
            {
                <= 15 => (2, L.T($"每 {interval}打开一次摄像头，间隔较短", $"the camera turns on every {interval}; frequent checks use more power")),
                <= 30 => (1, L.T($"每 {interval}短暂打开一次摄像头", $"briefly opens the camera every {interval}")),
                _ => (0, L.T($"每 {interval}短暂打开一次摄像头", $"briefly opens the camera every {interval}")),
            };
            // 接管模式下，离开后到锁定前屏幕保持常亮、摄像头持续工作；延迟 1 分钟以上时值得提醒
            var lockDelay = _settings.LockDelaySeconds < 0 ? (int)IntervalSeconds : _settings.LockDelaySeconds;
            if (_settings.LockWhenAway && lockDelay >= 60)
            {
                level = Math.Max(level, 1);
                reason += L.T("；离开后到锁定前屏幕保持常亮", "; the screen stays on during the lock countdown");
            }
        }
        string[] levels = L.Zh ? ["几乎无影响", "轻微", "中等", "非常耗电"] : ["Negligible", "Low", "Moderate", "Very high"];
        PowerSummaryText.Text = L.T($"耗电：{levels[level]}（{reason}）", $"Power use: {levels[level]} ({reason})");
        _trayPower = levels[level];
        UpdateTrayTooltip();
    }

    private string _trayStatus, _trayPower = "";

    // 托盘提示最多 127 个字符，所以耗电只显示等级，不带原因
    private void UpdateTrayTooltip()
    {
        var status = _trayStatus.Length > 60 ? _trayStatus[..60] + "…" : _trayStatus;
        _tray.SetTooltip(L.T($"『在吗』正在运行\n状态：{status}\n耗电：{_trayPower}", $"AreYouThere is running\nStatus: {status}\nPower use: {_trayPower}"));
    }

    #endregion

    #region 摄像头

    private async Task RefreshCamerasAsync()
    {
        if (Interlocked.Exchange(ref _refreshingCameras, 1) == 1) return;
        try
        {
            _cameras = await CameraCatalog.FindAsync();
            _suppressCameraChange = true;
            CameraBox.ItemsSource = _cameras.Select(c => c.Label).ToList();
            var index = _cameras.ToList().FindIndex(c => c.Id == _settings.CameraId);
            if (index < 0 && _cameras.Count > 0) { index = 0; _settings.CameraId = _cameras[0].Id; }
            CameraBox.SelectedIndex = index;
            CameraBox.IsEnabled = _cameras.Count > 0;
            CameraBox.PlaceholderText = _cameras.Count > 0 ? "" : L.T("未找到摄像头", "No camera found");
            _suppressCameraChange = false;

            if (_cameras.Count == 0)
            {
                ShowCameraProblem(L.T("未找到摄像头", "No camera found"),
                    L.T("『在吗』需要一个摄像头（笔记本内置或 USB 摄像头都可以）才能判断你是否在电脑前。请连接摄像头，插入后通常会自动识别，也可以点击“刷新”。",
                        "AreYouThere needs a built-in or USB camera for presence detection. Connect a camera and wait for it to appear, or click Refresh."));
                _noCameraShown = true;
                if (EnabledSwitch.IsOn) SetSwitchOff();
            }
            else if (_noCameraShown)
            {
                HideCameraProblem();
            }
            UpdateSwitchEnabled();
        }
        catch (Exception ex)
        {
            ShowCameraProblem(L.T("无法列出摄像头", "Can't list cameras"), ex.Message);
        }
        finally
        {
            _suppressCameraChange = false;
            Volatile.Write(ref _refreshingCameras, 0);
        }
    }

    private async void CameraBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCameraChange || CameraBox.SelectedIndex < 0 || CameraBox.SelectedIndex >= _cameras.Count) return;
        var id = _cameras[CameraBox.SelectedIndex].Id;
        if (id == _settings.CameraId) return;
        _settings.CameraId = id;
        _settings.Save();
        // 运行中切换摄像头：直接用新摄像头重新开始
        if (EnabledSwitch.IsOn && !_starting) await StartPresenceAsync();
    }

    private async void RefreshCameras_Click(object sender, RoutedEventArgs e) => await RefreshCamerasAsync();

    private async void CameraPrivacyButton_Click(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-webcam"));
    }

    private void Presence_CameraFailed(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            _presence.Stop();
            ClearPreview();
            SetSwitchOff();
            ShowCameraProblem(L.T("摄像头不可用", "Camera unavailable"), message);
            Report(L.T("摄像头不可用，已停止检测", "Camera unavailable; detection stopped"));
            await RefreshCamerasAsync();
        });
    }

    private void ShowCameraProblem(string title, string message, bool showPrivacyButton = false)
    {
        _noCameraShown = false;
        CameraProblem.Title = title;
        CameraProblem.Message = message;
        CameraPrivacyButton.Visibility = showPrivacyButton ? Visibility.Visible : Visibility.Collapsed;
        CameraProblem.IsOpen = true;
        CameraProblem.Visibility = Visibility.Visible;
    }

    private bool _noCameraShown;

    private void HideCameraProblem()
    {
        _noCameraShown = false;
        CameraProblem.IsOpen = false;
        CameraProblem.Visibility = Visibility.Collapsed;
    }

    private void UpdateSwitchEnabled() => EnabledSwitch.IsEnabled = !_startupPending && !_starting && _cameras.Count > 0;

    private void SetSwitchOff()
    {
        _suppressToggle = true;
        EnabledSwitch.IsOn = false;
        _suppressToggle = false;
        RefreshPowerInfo();
    }

    #endregion

    #region 窗口与托盘

    // 点关闭按钮只隐藏到托盘；真正退出走托盘菜单的“退出”。
    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        sender.Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloon(L.T("『在吗』仍在运行", "AreYouThere is still running"),
                L.T("窗口已收起到托盘。右键托盘图标可启用/关闭或退出。", "The app is running in the system tray. Right-click its icon to turn detection on or off, or to exit."));
        }
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
    }

    private void ToggleFromTray()
    {
        // 没有摄像头等原因无法启用时，打开窗口让用户看到原因
        if (EnabledSwitch.IsEnabled) EnabledSwitch.IsOn = !EnabledSwitch.IsOn;
        else if (!_starting) ShowFromTray();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    // 锁屏期间没必要开着摄像头，也不能继续保持唤醒（否则锁屏界面会一直亮着）。
    private async void Tray_SessionLockChanged(bool locked)
    {
        if (locked)
        {
            if (!EnabledSwitch.IsOn || _pausedByLock) return;
            _pausedByLock = true;
            _presence.Stop();
            ClearPreview();
            Report(L.T("电脑已锁定，暂停检测", "PC locked; detection paused"));
        }
        else if (_pausedByLock)
        {
            _pausedByLock = false;
            if (!EnabledSwitch.IsOn) return;
            await Task.Delay(1000); // 刚解锁时摄像头可能还没准备好
            if (EnabledSwitch.IsOn && EnabledSwitch.IsEnabled) await StartPresenceAsync();
        }
    }

    private void Presence_LockCountdownStarted(object? sender, TimeSpan delay)
    {
        DispatcherQueue.TryEnqueue(() => _tray.ShowBalloon(L.T("『在吗』未检测到你", "No qualifying face detected"),
            L.T($"将在 {PowerPolicy.Describe(delay)}后锁定电脑。回到摄像头前即可取消。", $"The PC will lock in {PowerPolicy.Describe(delay)}. Return to the camera's view before the countdown ends to cancel the pending lock.")));
    }

    #endregion

    #region 检测与预览

    // FrameReady 在摄像头工作线程上触发；XAML 对象必须在 UI 线程上创建和访问。
    // 上一帧还没显示完时直接丢弃新帧，避免 UI 队列堆积。
    private void Presence_FrameReady(object? sender, PresenceFrame e)
    {
        if (Interlocked.Exchange(ref _previewBusy, 1) == 1) { e.Bitmap.Dispose(); return; }
        var queued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (!AppWindow.IsVisible) return;
                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(e.Bitmap);
                PreviewElement.Source = source;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                DrawFaces(e.Faces, e.Bitmap.PixelWidth);
            }
            catch { }
            finally { e.Bitmap.Dispose(); Volatile.Write(ref _previewBusy, 0); }
        });
        if (!queued) { e.Bitmap.Dispose(); Volatile.Write(ref _previewBusy, 0); }
    }

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush CloseFaceBrush = new(Windows.UI.Color.FromArgb(255, 0x35, 0xD0, 0x7F));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FarFaceBrush = new(Windows.UI.Color.FromArgb(200, 0xB0, 0xB0, 0xB0));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush LabelTextBrush = new(Microsoft.UI.Colors.Black);

    // 画布按摄像头原始像素布局（外层 Viewbox 统一缩放），线宽和字号按画面宽度换算，缩放后大小一致
    private void DrawFaces(IReadOnlyList<DetectedFace> faces, int frameWidth)
    {
        FaceCanvas.Children.Clear();
        var unit = frameWidth / 320.0;
        foreach (var face in faces)
        {
            var brush = face.Close ? CloseFaceBrush : FarFaceBrush;
            var rect = new Microsoft.UI.Xaml.Shapes.Rectangle
            {
                Width = face.Box.Width, Height = face.Box.Height, Stroke = brush,
                StrokeThickness = 1.5 * unit, RadiusX = 2 * unit, RadiusY = 2 * unit
            };
            Canvas.SetLeft(rect, face.Box.X);
            Canvas.SetTop(rect, face.Box.Y);
            var label = new Border
            {
                Background = brush, CornerRadius = new CornerRadius(2 * unit), Padding = new Thickness(2 * unit, 0, 2 * unit, 0),
                Child = new TextBlock { Text = $"{face.AreaPercent:0.0}%", FontSize = 10 * unit, Foreground = LabelTextBrush }
            };
            Canvas.SetLeft(label, face.Box.X);
            Canvas.SetTop(label, Math.Max(0, face.Box.Y - 14 * unit));
            FaceCanvas.Children.Add(rect);
            FaceCanvas.Children.Add(label);
        }
    }

    private void Presence_StatusChanged(object? sender, PresenceStatus e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CameraStatusText.Text = L.T($"摄像头状态：{e.CameraMessage}", $"Camera: {e.CameraMessage}");
            FaceStatusText.Text = L.T($"人脸状态：{e.FaceMessage}", $"Face detection: {e.FaceMessage}");
        });
    }

    /// <summary>显示状态正文；界面上加“状态：”前缀，托盘提示里只用正文。</summary>
    private void Report(string message) => DispatcherQueue.TryEnqueue(() =>
    {
        StatusText.Text = L.Status(message);
        _trayStatus = message;
        UpdateTrayTooltip();
    });

    private async void EnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        if (EnabledSwitch.IsOn)
        {
            await StartPresenceAsync();
        }
        else
        {
            _pausedByLock = false;
            _presence.Stop();
            ClearPreview();
            Report(L.T("未启用", "Off"));
        }
        RefreshPowerInfo();
    }

    private async Task StartPresenceAsync()
    {
        if (_settings.CameraId is not { } cameraId) return;
        // 打开摄像头需要一点时间，期间禁用开关，防止重复启动
        _starting = true;
        UpdateSwitchEnabled();
        StatusText.Text = L.Status(L.T("正在打开摄像头…", "Opening the camera…"));
        var result = await _presence.StartAsync(cameraId, IntervalSeconds, PreviewWanted, Report);
        _starting = false;
        UpdateSwitchEnabled();
        if (result.Started)
        {
            HideCameraProblem();
            Report(RunningStatus());
        }
        else if (!_pausedByLock)
        {
            // 启动途中锁屏导致的取消则保持开关不变，解锁后自动恢复
            SetSwitchOff();
            if (result.Error is not null)
            {
                ShowCameraProblem(result.PermissionDenied ? L.T("没有摄像头权限", "Camera access denied") : L.T("无法打开摄像头", "Can't open the camera"),
                    result.Error, result.PermissionDenied);
                Report(L.T("无法打开摄像头", "Can't open the camera"));
            }
        }
        RefreshPowerInfo();
    }

    /// <summary>预览开关打开、且窗口可见未最小化时，才需要摄像头持续工作。</summary>
    private bool PreviewWanted => PreviewSwitch.IsOn && AppWindow.IsVisible
        && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void UpdatePreviewWanted()
    {
        if (!_ready) return;
        var wanted = PreviewWanted;
        if (EnabledSwitch.IsOn) _presence.SetPreview(wanted);
        if (!wanted) ClearPreview();
        RefreshPowerInfo();
    }

    private void PreviewSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.PreviewEnabled = PreviewSwitch.IsOn;
        _settings.Save();
        UpdatePreviewWanted();
    }

    private void ClearPreview()
    {
        PreviewElement.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        FaceCanvas.Children.Clear();
    }

    #endregion

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
