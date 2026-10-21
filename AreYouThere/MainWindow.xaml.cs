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
    private readonly DetectionController _detection;
    private readonly DimOverlay _dim;
    private readonly BrightnessDimmer _brightness = new();
    private readonly TrayService _tray;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly DispatcherQueueTimer _policyTimer;
    private readonly CameraCatalog _cameraCatalog = new();
    private readonly BatterySaverMonitor _batterySaver = new();
    private IReadOnlyList<CameraInfo> _cameras = [];
    private int _previewBusy, _refreshingCameras;
    private bool _ready, _exiting, _trayHintShown, _suppressToggle, _suppressCameraChange;
    private bool _startupPending, _startupInitialized, _suppressStartupToggle, _startupBusy;
    private bool _showingStartupReminder;

    public MainWindow(bool startupLaunch = false)
    {
        _startupPending = startupLaunch;
        L.SetLanguage(_settings.Language); // 托盘、对话框等都按这里选定的语言
        _trayStatus = L.T("未启用", "Off");
        InitializeComponent();
        // Windows 11 用 Mica 背景；Windows 10 不支持，保留 XAML 里的普通主题背景
        if (Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            WindowRoot.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        SetupTitleBar();
        ApplyTheme();
        Localize();
        StatusText.Text = L.Status(_trayStatus);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.Closing += AppWindow_Closing;
        _detection = new DetectionController(_presence, StartRequestForCurrentSettings, Report);
        _detection.StateChanged += Detection_StateChanged;
        _detection.Started += () => { HideCameraProblem(); Report(RunningStatus()); };
        _detection.StartFailed += Detection_StartFailed;
        _dim = new DimOverlay(DispatcherQueue);
        SetupDimPreview();
        BrightnessDimmer.RestoreLeftover(); // 上次调暗后程序没正常结束：先把亮度恢复
        if (!PowerOverride.Restore()) // 上次锁定期间的电源设置没来得及恢复（崩溃、锁定状态下关机）
            ShowPowerProblem(L.T("部分电源设置未能恢复为原来的值，可以在 Windows 电源设置中手动修改。『在吗』下次启动时会再试一次。",
                "Some power settings couldn't be restored. You can change them in Windows power settings. AreYouThere will try again the next time it starts."));
        _ = ApplyAwayLockImageAsync(locked: false); // 同理恢复上次锁定期间换掉的锁屏图片
        _pauseTimer = DispatcherQueue.CreateTimer();
        _pauseTimer.Interval = PauseCheckInterval;
        _pauseTimer.Tick += (_, _) => CheckPauseExpired();
        _tray = new TrayService(this, iconPath, () => _detection.Enabled, () => _pausedUntil, ToggleFromTray, PauseFor, ShowFromTray, ExitApp);
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
        _presence.LockCountdownEnded += Presence_LockCountdownEnded;
        _presence.CameraFailed += Presence_CameraFailed;
        _presence.MultipleFacesDetected += Presence_MultipleFacesDetected;
        _presence.PostureAlert += Presence_PostureAlert;
        IntervalPicker.ValueChanged += IntervalPicker_ValueChanged;
        IntervalStepPicker.Minimum = TimeSpan.Zero; // 0 表示不递增
        // 最长检测间隔就是离开后最坏要等多久才发现，隐私工具不宜太长
        MaxIntervalPicker.Maximum = TimeSpan.FromMinutes(30);
        IntervalStepPicker.ValueChanged += IntervalStepPicker_ValueChanged;
        MaxIntervalPicker.ValueChanged += MaxIntervalPicker_ValueChanged;
        PostureIntervalPicker.ValueChanged += PostureIntervalPicker_ValueChanged;
        LockDelayPicker.Minimum = TimeSpan.Zero; // 0 秒为立即锁定
        LockDelayPicker.ValueChanged += LockDelayPicker_ValueChanged;
        _cameraCatalog.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => _ = RefreshCamerasAsync());
        // 节电模式的开关事件在系统线程上触发
        _batterySaver.Changed += initial => DispatcherQueue.TryEnqueue(() => BatterySaver_StatusChanged(initial));
        _batterySaverOn = _batterySaver.IsOn; // 初始状态可能在订阅前就已送达

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
        ApplyBatterySaver(); // 启动时已经开着节电模式：先记下暂停原因，打开检测时就不会启动摄像头
        RefreshPolicyInfo();
        InitProfiles();

        // 电源来源（插拔电源）、系统设置和 Awake 状态都可能随时变化，定期刷新提示
        _policyTimer = DispatcherQueue.CreateTimer();
        _policyTimer.Interval = TimeSpan.FromSeconds(30);
        // 窗口隐藏时看不到说明，不必每 30 秒枚举进程、读电源设置；重新显示时 Activated 会刷新
        _policyTimer.Tick += (_, _) => { if (AppWindow.IsVisible) RefreshPolicyInfo(); };
        _policyTimer.Start();
        Activated += async (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            RefreshPolicyInfo();
            if (_startupInitialized && !_startupBusy) await RefreshStartupStateAsync();
        };

        Closed += (_, _) =>
        {
            FlushPendingSave(); _batterySaver.Dispose(); _policyTimer.Stop(); _pauseTimer.Stop(); _presence.Dispose(); _dim.Dispose(); _brightness.Restore(); PowerOverride.Restore();
            // 锁屏图片的恢复是异步的；放到线程池上等，避免在 UI 线程上同步等待导致死锁
            Task.Run(LockScreenImage.RestoreAsync).Wait(TimeSpan.FromSeconds(3)); _cameraCatalog.Dispose(); _tray.Dispose();
        };
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
        // 宽窗口：预览在右侧一列；窄窗口：预览插在“检测”和“离开电脑后”之间，紧挨着相关设置
        if (wide)
        {
            if (SettingsPanel.Children.Remove(PreviewCard)) LayoutRoot.Children.Add(PreviewCard);
            Grid.SetRow(PreviewCard, 1);
            Grid.SetColumn(PreviewCard, 1);
        }
        else if (LayoutRoot.Children.Remove(PreviewCard))
        {
            SettingsPanel.Children.Insert(SettingsPanel.Children.IndexOf(AwayHeader), PreviewCard);
        }
        Grid.SetColumnSpan(SettingsPanel, wide ? 1 : 2);
        PreviewCard.Width = wide ? 320 : double.NaN;
        PreviewArea.Height = wide ? 180 : 260;
    }

    // 点关闭按钮只隐藏到托盘；真正退出走托盘菜单的“退出”。
    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exiting) return;
        args.Cancel = true;
        sender.Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            Notify(L.T("『在吗』仍在运行", "AreYouThere is still running"),
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
        else if (!_detection.Starting) ShowFromTray();
    }

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    private bool _sessionLocked;

    // 锁屏期间没必要开着摄像头，也不能继续保持唤醒（否则锁屏界面会一直亮着）。
    private async void Tray_SessionLockChanged(bool locked)
    {
        _sessionLocked = locked;
        ApplyAwayPower(locked); // 锁定时换成离开后的电源策略和锁屏图片，解锁时立即恢复（不等下面的延迟）
        _ = ApplyAwayLockImageAsync(locked);
        if (locked)
        {
            StopDimming();
            await _detection.SetPausedAsync(PauseReason.SessionLocked, true);
            return;
        }
        await Task.Delay(1000); // 刚解锁时摄像头可能还没准备好
        if (!_sessionLocked) await _detection.SetPausedAsync(PauseReason.SessionLocked, false); // 等待期间又锁定了就继续暂停
    }

    private bool _batterySaverOn;
    // 与系统界面用同一个名字：Windows 11 24H2 起是“节能模式”，之前是“节电模式”
    private static string ZhSaver => BatterySaverMonitor.UsesEnergySaverName ? "节能模式" : "节电模式";
    private static string EnSaver => BatterySaverMonitor.UsesEnergySaverName ? "energy saver" : "battery saver";
    private static string EnSaverCap => BatterySaverMonitor.UsesEnergySaverName ? "Energy saver" : "Battery saver";
    private bool HaltForBatterySaver => _settings.PauseInBatterySaver && _batterySaver.IsOn;

    // 只在节电模式真正开启/关闭时处理；启动时送来的初始状态只暂停/恢复，不通知
    private void BatterySaver_StatusChanged(bool initial)
    {
        var on = _batterySaver.IsOn;
        if (on == _batterySaverOn) return;
        _batterySaverOn = on;
        ApplyBatterySaver(notify: !initial);
    }

    // 节电模式下默认暂停：停掉摄像头并释放常亮，交给系统电源设置；关闭节电模式（或改为继续检测）后恢复。
    // notify：进入/退出节电模式时通知是暂停、恢复还是继续检测；在设置里改选项时不通知。
    private async void ApplyBatterySaver(bool notify = false)
    {
        if (!_ready) return;
        var halt = HaltForBatterySaver;
        var wasHalted = _detection.Paused.HasFlag(PauseReason.BatterySaver);
        // 只在节电模式是唯一影响因素时通知：检测关闭或另有暂停原因（锁屏、手动暂停）时，说“暂停/恢复”都不准确
        if (notify && _detection.Enabled && (_detection.Paused & ~PauseReason.BatterySaver) == PauseReason.None)
        {
            if (halt && !wasHalted)
                Notify(L.T($"{ZhSaver}已开启，『在吗』暂停检测", $"{EnSaverCap} on: detection paused"),
                    L.T($"暂停期间不使用摄像头，也不保持亮屏或自动锁定，由系统电源设置接管。关闭{ZhSaver}后自动恢复检测；也可以在设置中改为{ZhSaver}下继续检测。",
                        $"While paused, the camera isn't used and AreYouThere won't keep the screen on or lock the PC; your Windows power settings apply. Detection resumes when {EnSaver} turns off, or you can choose to keep detecting in settings."));
            else if (!halt && wasHalted)
                Notify(L.T($"{ZhSaver}已关闭，『在吗』恢复检测", $"{EnSaverCap} off: detection resumed"),
                    L.T("已重新开始在场检测。", "Presence detection has started again."));
            else if (!halt)
                // 设置为节电模式下继续检测：检测没有中断，只告知一声
                Notify(_batterySaverOn ? L.T($"{ZhSaver}已开启，『在吗』继续检测", $"{EnSaverCap} on: still detecting")
                        : L.T($"{ZhSaver}已关闭，『在吗』继续检测", $"{EnSaverCap} off: still detecting"),
                    _batterySaverOn ? L.T($"已设置为{ZhSaver}下继续检测，摄像头会照常按间隔工作。可以在设置中改为暂停检测。",
                            $"You chose to keep detecting in {EnSaver}, so the camera keeps checking at the usual interval. You can choose to pause instead in settings.")
                        : L.T("检测一直在进行，没有中断。", "Detection continued without interruption."));
        }
        await _detection.SetPausedAsync(PauseReason.BatterySaver, halt);
    }

    private void BatterySaverOption_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.PauseInBatterySaver = BatterySaverPauseOption.IsChecked == true;
        _settings.Save();
        UpdateBatterySaverNotes();
        ApplyBatterySaver();
    }

    // 只显示当前选中选项下面的说明
    private void UpdateBatterySaverNotes()
    {
        BatterySaverPauseNote.Visibility = _settings.PauseInBatterySaver ? Visibility.Visible : Visibility.Collapsed;
        BatterySaverKeepNote.Visibility = _settings.PauseInBatterySaver ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Presence_LockCountdownStarted(object? sender, TimeSpan delay)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_settings.DimDuringCountdown) StartDimming(delay);
            Notify(L.T("『在吗』未检测到你", "No qualifying face detected"),
                L.T($"将在 {PowerPolicy.Describe(delay)}后锁定电脑。回到摄像头前即可取消。", $"The PC will lock in {PowerPolicy.Describe(delay)}. Return to the camera's view before the countdown ends to cancel the pending lock."));
        });
    }

    // 倒计时取消（人回来了）或已经锁定：都撤掉调暗
    private void Presence_LockCountdownEnded(object? sender, bool locked) => DispatcherQueue.TryEnqueue(StopDimming);

    // 每次开始/停止调暗加一；调亮度要在后台等一会儿，等完发现已经停止（人回来了）就不再加遮罩
    private int _dimGeneration;
    private bool _countdownDimming;

    /// <summary>
    /// 调亮度方式：先调能调的显示器，其余显示器（不支持 DDC/CI 的外接屏等）加遮罩，保证每个屏幕都会变暗。
    /// 遮罩方式：所有显示器加遮罩。
    /// </summary>
    private async void StartDimming(TimeSpan delay)
    {
        var generation = ++_dimGeneration;
        _countdownDimming = true;
        _dimPreviewing = false; // 真正的倒计时优先：正在拖滑块预览也让位
        IReadOnlySet<string>? dimmed = null;
        if (_settings.DimMode == DimMode.Brightness)
        {
            dimmed = await _brightness.DimAsync(_settings.DimLevelPercent / 100);
            if (generation != _dimGeneration) return;
        }
        _dim.Show(delay, dimmed);
    }

    private void StopDimming()
    {
        _dimGeneration++;
        _countdownDimming = false;
        _dim.Hide();
        _ = _brightness.RestoreAsync();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
