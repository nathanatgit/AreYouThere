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

    public MainWindow()
    {
        InitializeComponent();
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        AppWindow.SetIcon(iconPath);
        AppWindow.Closing += AppWindow_Closing;
        _tray = new TrayService(this, iconPath, () => EnabledSwitch.IsOn, ToggleFromTray, ShowFromTray, ExitApp);
        _tray.SessionLockChanged += Tray_SessionLockChanged;
        _presence.StatusChanged += Presence_StatusChanged;
        _presence.FrameReady += Presence_FrameReady;
        _presence.LockCountdownStarted += Presence_LockCountdownStarted;
        _presence.CameraFailed += Presence_CameraFailed;
        _cameraCatalog.Changed += (_, _) => DispatcherQueue.TryEnqueue(() => _ = RefreshCamerasAsync());

        // 预览只在窗口可见时有意义；窗口隐藏/最小化后让摄像头进入省电的间歇工作模式
        AppWindow.Changed += (_, e) => { if (e.DidVisibilityChange || e.DidPresenterChange || e.DidSizeChange) UpdatePreviewWanted(); };
        VisibilityChanged += (_, _) => UpdatePreviewWanted();

        RootScroll.Loaded += (_, _) => RootScroll.Focus(FocusState.Programmatic);
        EnabledSwitch.IsEnabled = false; // 找到摄像头之前不能启用
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        AboutText.Text = $"在吗 AreYouThere v{version?.ToString(3)} · MIT 许可证 · 开源免费";
        ApplySettings();
        _ready = true;
        RefreshPolicyInfo();
        _ = RefreshCamerasAsync();

        // 电源来源（插拔电源）、系统设置和 Awake 状态都可能随时变化，定期刷新提示
        _policyTimer = DispatcherQueue.CreateTimer();
        _policyTimer.Interval = TimeSpan.FromSeconds(30);
        _policyTimer.Tick += (_, _) => RefreshPolicyInfo();
        _policyTimer.Start();
        Activated += (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated) RefreshPolicyInfo(); };

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

    #region 设置

    private uint IntervalSeconds => _settings.IntervalSeconds;

    private void ApplySettings()
    {
        SelectByTag(IntervalBox, _settings.IntervalSeconds.ToString());
        PreviewSwitch.IsOn = _settings.PreviewEnabled;
        AwayModeButtons.SelectedIndex = _settings.LockWhenAway ? 1 : 0;
        SelectByTag(LockDelayBox, _settings.LockDelaySeconds.ToString());
        LockDelayBox.Visibility = _settings.LockWhenAway ? Visibility.Visible : Visibility.Collapsed;
        _presence.LockWhenAway = _settings.LockWhenAway;
        _presence.LockDelay = _settings.LockDelaySeconds < 0 ? null : TimeSpan.FromSeconds(_settings.LockDelaySeconds);
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
            var when = delay == TimeSpan.Zero ? "立即锁定电脑" : $"再等 {PowerPolicy.Describe(delay)}，期间仍未回来就锁定电脑";
            PolicyText.Text = $"在电脑前时保持屏幕常亮，不受系统超时影响。检测到离开后{when}；回到摄像头前会取消锁定。"
                + $"\n从你离开到锁定，最长约 {PowerPolicy.Describe(interval + delay)}（检测间隔 + 锁定延迟）。";
        }
        else
        {
            var p = PowerPolicy.Read();
            var source = p.OnBattery ? "使用电池" : "接通电源";
            var lockText = p.ScreenSaverLockSeconds is uint s
                ? $"屏幕保护程序 {PowerPolicy.Describe(TimeSpan.FromSeconds(s))}后启动并锁定"
                : "未设置屏保锁定，通常在电脑睡眠后需要重新登录";
            var after = p.DisplayOffSeconds == 0
                ? "当前电源计划不会自动关闭屏幕。"
                : $"离开后最长约 {PowerPolicy.Describe(interval + TimeSpan.FromSeconds(p.DisplayOffSeconds))}息屏（检测间隔 + 系统息屏时间）。";
            PolicyText.Text = $"当前电源计划（{source}）：无操作 {PowerPolicy.DescribeTimeout(p.DisplayOffSeconds)}关闭屏幕，"
                + $"{PowerPolicy.DescribeTimeout(p.SleepSeconds)}睡眠；{lockText}。\n{after}";
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
        string level, reason;
        if (!EnabledSwitch.IsOn)
        {
            (level, reason) = ("几乎无影响", "未启用，摄像头关闭");
        }
        else if (PreviewWanted)
        {
            // 预览可见时摄像头一直工作
            (level, reason) = ("非常耗电", "预览让摄像头一直工作，关闭预览或把窗口收到托盘即可省电");
        }
        else
        {
            // 后台运行时每次检测打开摄像头约 1–2 秒，间隔越短，摄像头工作的比例越高
            var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds));
            (level, reason) = IntervalSeconds switch
            {
                <= 15 => ("中等", $"每 {interval}打开一次摄像头，间隔较短"),
                <= 30 => ("轻微", $"每 {interval}短暂打开一次摄像头"),
                _ => ("几乎无影响", $"每 {interval}短暂打开一次摄像头"),
            };
            // 接管模式下，离开后到锁定前屏幕保持常亮、摄像头持续工作；延迟 1 分钟以上时值得提醒
            var lockDelay = _settings.LockDelaySeconds < 0 ? (int)IntervalSeconds : _settings.LockDelaySeconds;
            if (_settings.LockWhenAway && lockDelay >= 60)
            {
                if (level == "几乎无影响") level = "轻微";
                reason += "；离开后到锁定前屏幕保持常亮";
            }
        }
        PowerSummaryText.Text = $"耗电：{level}（{reason}）";
        _trayPower = level;
        UpdateTrayTooltip();
    }

    private string _trayStatus = "未启用", _trayPower = "几乎无影响";

    // 托盘提示最多 127 个字符，所以耗电只显示等级，不带原因
    private void UpdateTrayTooltip()
    {
        var status = _trayStatus.Length > 60 ? _trayStatus[..60] + "…" : _trayStatus;
        _tray.SetTooltip($"「在吗」正在运行\n状态：{status}\n耗电：{_trayPower}");
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
            CameraBox.PlaceholderText = _cameras.Count > 0 ? "" : "未找到摄像头";
            _suppressCameraChange = false;

            if (_cameras.Count == 0)
            {
                ShowCameraProblem("未找到摄像头",
                    "「在吗」需要一个摄像头（笔记本内置或 USB 摄像头都可以）才能判断你是否在电脑前。请连接摄像头，插入后通常会自动识别，也可以点击“刷新”。");
                if (EnabledSwitch.IsOn) SetSwitchOff();
            }
            else if (CameraProblem.Title == "未找到摄像头")
            {
                HideCameraProblem();
            }
            UpdateSwitchEnabled();
        }
        catch (Exception ex)
        {
            ShowCameraProblem("无法列出摄像头", ex.Message);
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
            ShowCameraProblem("摄像头不可用", message);
            Report("状态：摄像头不可用，已停止检测");
            await RefreshCamerasAsync();
        });
    }

    private void ShowCameraProblem(string title, string message, bool showPrivacyButton = false)
    {
        CameraProblem.Title = title;
        CameraProblem.Message = message;
        CameraPrivacyButton.Visibility = showPrivacyButton ? Visibility.Visible : Visibility.Collapsed;
        CameraProblem.IsOpen = true;
        CameraProblem.Visibility = Visibility.Visible;
    }

    private void HideCameraProblem()
    {
        CameraProblem.IsOpen = false;
        CameraProblem.Visibility = Visibility.Collapsed;
    }

    private void UpdateSwitchEnabled() => EnabledSwitch.IsEnabled = !_starting && _cameras.Count > 0;

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
            _tray.ShowBalloon("「在吗」仍在运行", "窗口已收起到托盘。右键托盘图标可启用/关闭或退出。");
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
            Report("状态：电脑已锁定，暂停检测");
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
        DispatcherQueue.TryEnqueue(() => _tray.ShowBalloon("「在吗」未检测到你",
            $"将在 {PowerPolicy.Describe(delay)}后锁定电脑。回到摄像头前即可取消。"));
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
                if (e.FaceBox is { } box)
                {
                    Canvas.SetLeft(FaceBox, box.X);
                    Canvas.SetTop(FaceBox, box.Y);
                    FaceBox.Width = box.Width;
                    FaceBox.Height = box.Height;
                    FaceBox.Visibility = Visibility.Visible;
                }
                else FaceBox.Visibility = Visibility.Collapsed;
            }
            catch { }
            finally { e.Bitmap.Dispose(); Volatile.Write(ref _previewBusy, 0); }
        });
        if (!queued) { e.Bitmap.Dispose(); Volatile.Write(ref _previewBusy, 0); }
    }

    private void Presence_StatusChanged(object? sender, PresenceStatus e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            CameraStatusText.Text = $"摄像头状态：{e.CameraMessage}";
            FaceStatusText.Text = $"人脸状态：{e.FaceMessage}";
        });
    }

    private void Report(string message) => DispatcherQueue.TryEnqueue(() =>
    {
        StatusText.Text = message;
        _trayStatus = message.Replace("状态：", "");
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
            Report("状态：未启用");
        }
        RefreshPowerInfo();
    }

    private async Task StartPresenceAsync()
    {
        if (_settings.CameraId is not { } cameraId) return;
        // 打开摄像头需要一点时间，期间禁用开关，防止重复启动
        _starting = true;
        UpdateSwitchEnabled();
        StatusText.Text = "状态：正在打开摄像头…";
        var result = await _presence.StartAsync(cameraId, IntervalSeconds, PreviewWanted, Report);
        _starting = false;
        UpdateSwitchEnabled();
        if (result.Started)
        {
            HideCameraProblem();
            Report($"状态：运行中，每 {PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds))}检测一次");
        }
        else if (!_pausedByLock)
        {
            // 启动途中锁屏导致的取消则保持开关不变，解锁后自动恢复
            SetSwitchOff();
            if (result.Error is not null)
            {
                ShowCameraProblem(result.PermissionDenied ? "没有摄像头权限" : "无法打开摄像头", result.Error, result.PermissionDenied);
                Report("状态：无法打开摄像头");
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
        FaceBox.Visibility = Visibility.Collapsed;
    }

    #endregion

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
