using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AreYouThere.Services;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;

namespace AreYouThere;

// 设置项的读取和修改，以及“离开后会发生什么”和耗电说明。
public sealed partial class MainWindow
{
    private void ApplySettings()
    {
        // 选择器有上限（隐私相关的最长 59 分 59 秒，最长检测间隔 30 分钟）；旧设置超出时按选择器夹紧后的值保存
        var saved = (_settings.IntervalSeconds, _settings.IntervalStepSeconds, _settings.MaxIntervalSeconds, _settings.LockDelaySeconds);
        IntervalPicker.Value = TimeSpan.FromSeconds(_settings.IntervalSeconds);
        _settings.IntervalSeconds = (uint)IntervalPicker.Value.TotalSeconds;
        PreviewSwitch.IsOn = _settings.PreviewEnabled;
        NotificationModeButtons.SelectedIndex = (int)_settings.Notifications;
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        OfflineBar.IsOpen = !_settings.HideOfflineNotice;
        PrivacyBar.IsOpen = !_settings.HidePrivacyNotice;
        UpdateShowNoticesLink();
        SmartSwitch.IsOn = _settings.SmartDetection;
        _presence.SmartDetection = _settings.SmartDetection;
        IntervalStepPicker.Value = TimeSpan.FromSeconds(_settings.IntervalStepSeconds);
        _settings.IntervalStepSeconds = (uint)IntervalStepPicker.Value.TotalSeconds;
        MaxIntervalPicker.Value = TimeSpan.FromSeconds(_settings.MaxIntervalSeconds);
        _settings.MaxIntervalSeconds = (uint)MaxIntervalPicker.Value.TotalSeconds;
        GrowthExponentialOption.IsChecked = !_settings.LinearGrowth;
        GrowthLinearOption.IsChecked = _settings.LinearGrowth;
        _presence.IntervalStepSeconds = _settings.IntervalStepSeconds;
        _presence.MaxIntervalSeconds = _settings.MaxIntervalSeconds;
        _presence.LinearGrowth = _settings.LinearGrowth;
        UpdateIntervalGrowthUi();
        PostureSwitch.IsOn = _settings.PostureCheck;
        _presence.PostureCheck = _settings.PostureCheck;
        PostureSlider.Value = Math.Clamp(_settings.PostureLineFromBottomPercent, PostureSlider.Minimum, PostureSlider.Maximum);
        _presence.PostureLinePercent = PostureSlider.Value;
        PostureAreaSlider.Value = Math.Clamp(_settings.PostureMaxAreaPercent, PostureAreaSlider.Minimum, PostureAreaSlider.Maximum);
        _presence.PostureMaxAreaPercent = PostureAreaSlider.Value;
        PostureFollowOption.IsChecked = _settings.PostureFollowDetection;
        PostureCustomOption.IsChecked = !_settings.PostureFollowDetection;
        PostureIntervalPicker.Value = TimeSpan.FromSeconds(_settings.PostureIntervalSeconds);
        ApplyPostureInterval();
        UpdatePostureVisibility();
        UpdatePostureText();
        UpdateIntervalLabel();
        BatterySaverPauseOption.IsChecked = _settings.PauseInBatterySaver;
        BatterySaverKeepOption.IsChecked = !_settings.PauseInBatterySaver;
        UpdateBatterySaverNotes();
        ShowRestoreButton(_settings.SkipStartupReminder);
        SelectByTag(LanguageBox, _settings.Language ?? "system");
        // 旧版本的“延迟一个在场检测间隔”（-1）换算成当前的检测间隔秒数；0 为立即锁定
        if (_settings.LockDelaySeconds < 0) _settings.LockDelaySeconds = (int)_settings.IntervalSeconds;
        LockDelayPicker.Value = TimeSpan.FromSeconds(_settings.LockDelaySeconds);
        _settings.LockDelaySeconds = (int)LockDelayPicker.Value.TotalSeconds;
        DimSwitch.IsOn = _settings.DimDuringCountdown;
        DimLevelSlider.Value = Math.Clamp(_settings.DimLevelPercent, DimLevelSlider.Minimum, DimLevelSlider.Maximum);
        ApplyDimLevel();
        DimOverlayOption.IsChecked = _settings.DimMode == DimMode.Overlay;
        DimBrightnessOption.IsChecked = _settings.DimMode == DimMode.Brightness;
        _ = UpdateDimModeNotesAsync();
        UpdateLockCards();
        _presence.LockDelay = TimeSpan.FromSeconds(_settings.LockDelaySeconds);
        FaceAreaSlider.Value = Math.Clamp(_settings.MinFaceAreaPercent, FaceAreaSlider.Minimum, FaceAreaSlider.Maximum);
        ApplyFaceArea();
        if (saved != (_settings.IntervalSeconds, _settings.IntervalStepSeconds, _settings.MaxIntervalSeconds, _settings.LockDelaySeconds))
            _settings.Save();
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
        DispatcherQueue.TryEnqueue(() => Notify(L.T("『在吗』检测到多张近处人脸", "Multiple faces detected"),
            L.T($"画面中有 {count} 张人脸达到面积阈值。只要其中一张还在，『在吗』就会认为你在电脑前，你离开后电脑可能不会锁定。请调整摄像头角度或提高人脸面积阈值。",
                $"{count} faces meet the minimum size. Detection of any one of them will keep the PC awake and may prevent it from locking after you leave. Adjust the camera angle or increase the minimum face size.")));
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        var item = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
        if (item is not null) box.SelectedItem = item;
    }

    // 只有用户点关闭按钮才记住；启动时按设置隐藏不会触发 Closed
    private void NoticeBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (!_ready || args.Reason != InfoBarCloseReason.CloseButton) return;
        if (sender == OfflineBar) _settings.HideOfflineNotice = true;
        else if (sender == PrivacyBar) _settings.HidePrivacyNotice = true;
        _settings.Save();
        UpdateShowNoticesLink();
    }

    private void PostureSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.PostureCheck = PostureSwitch.IsOn;
        _settings.Save();
        _presence.PostureCheck = PostureSwitch.IsOn;
        UpdatePostureVisibility();
        RefreshPowerInfo();
    }

    private void UpdatePostureVisibility()
    {
        PosturePanel.Visibility = _settings.PostureCheck ? Visibility.Visible : Visibility.Collapsed;
        // 展开项一直显示，关闭时灰掉（同 Windows 设置）：隐藏的话，关闭时点开卡片是空的。
        // 打开坐姿检测时展开，显示坐姿检测间隔；关闭时收起
        PostureIntervalPanel.IsEnabled = _settings.PostureCheck;
        PostureCard.IsExpanded = _settings.PostureCheck;
    }

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || ThemeBox.SelectedIndex < 0) return;
        _settings.Theme = (AppTheme)ThemeBox.SelectedIndex;
        _settings.Save();
        ApplyTheme();
    }

    /// <summary>窗口内容和标题栏按设置切换浅色/深色；弹出层在打开时按 ActualTheme 设置（见 Themed）。</summary>
    private void ApplyTheme()
    {
        WindowRoot.RequestedTheme = _settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        AppWindow.TitleBar.PreferredTheme = _settings.Theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
        UpdateCaptionButtons();
    }

    /// <summary>
    /// 沉浸式标题栏：内容延伸到标题栏，拖动区域是 AppTitleBar。最小化/最大化/关闭按钮仍由系统绘制，
    /// Windows 10 上同样可用；按钮背景透明，让 Mica / 窗口背景透出来。
    /// </summary>
    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall; // 与 48 像素高的 AppTitleBar 对齐
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        WindowRoot.ActualThemeChanged += (_, _) => UpdateCaptionButtons();
        Activated += (_, e) => AppTitleBarText.Opacity = e.WindowActivationState == WindowActivationState.Deactivated ? 0.5 : 1;
    }

    // 按钮图标颜色跟随当前浅色/深色主题（跟随系统时，系统切换主题也会更新）
    private void UpdateCaptionButtons()
    {
        var dark = WindowRoot.ActualTheme == ElementTheme.Dark;
        var bar = AppWindow.TitleBar;
        bar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        bar.ButtonHoverForegroundColor = bar.ButtonForegroundColor;
        bar.ButtonPressedForegroundColor = bar.ButtonForegroundColor;
        bar.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x80, 0, 0, 0);
        bar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x10, 0, 0, 0);
        bar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x08, 0, 0, 0);
    }

    /// <summary>对话框等弹出层不会自动继承窗口里选的主题，显示前调用。</summary>
    internal static T Themed<T>(T popup, XamlRoot root) where T : FrameworkElement
    {
        if (root.Content is FrameworkElement content) popup.RequestedTheme = content.ActualTheme;
        return popup;
    }

    private void NotificationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || NotificationModeButtons.SelectedIndex < 0) return;
        _settings.Notifications = (NotificationMode)NotificationModeButtons.SelectedIndex;
        _settings.Save();
    }

    /// <summary>所有托盘通知都走这里，按“通知”设置决定是否显示。</summary>
    private void Notify(string title, string text)
    {
        switch (_settings.Notifications)
        {
            case NotificationMode.Never:
                return;
            case NotificationMode.QuietWhenFullscreen when FullscreenAppInFront():
                return;
        }
        _tray.ShowBalloon(title, text);
    }

    // 与 Windows 自己判断“玩游戏/全屏时勿扰”用的是同一个接口
    private static bool FullscreenAppInFront() =>
        SHQueryUserNotificationState(out var state) == 0 && state is QunsBusy or QunsRunningD3DFullScreen or QunsPresentationMode;

    private const int QunsBusy = 2, QunsRunningD3DFullScreen = 3, QunsPresentationMode = 4;
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    private void PostureIntervalMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.PostureFollowDetection = PostureFollowOption.IsChecked == true;
        _settings.Save();
        ApplyPostureInterval();
    }

    private void PostureIntervalPicker_ValueChanged(object? sender, TimeSpan value)
    {
        if (!_ready) return;
        _settings.PostureIntervalSeconds = (uint)value.TotalSeconds;
        _settings.Save();
        ApplyPostureInterval();
    }

    /// <summary>跟随模式下 PresenceService 的坐姿间隔为 0（只随在场检测）；自定义时按设定的秒数单独检测。</summary>
    private void ApplyPostureInterval()
    {
        var follow = _settings.PostureFollowDetection;
        _presence.PostureIntervalSeconds = follow ? 0 : _settings.PostureIntervalSeconds;
        PostureIntervalPicker.Visibility = follow ? Visibility.Collapsed : Visibility.Visible;
        PostureFollowNote.Visibility = follow && _settings.SmartDetection ? Visibility.Visible : Visibility.Collapsed;
        RefreshPowerInfo();
    }

    private void PostureAreaSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.PostureMaxAreaPercent = PostureAreaSlider.Value;
        _presence.PostureMaxAreaPercent = PostureAreaSlider.Value;
        UpdatePostureText();
        _settings.Save();
    }

    private void PostureSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_ready) return;
        _settings.PostureLineFromBottomPercent = PostureSlider.Value;
        _presence.PostureLinePercent = PostureSlider.Value;
        UpdatePostureText();
        _settings.Save();
    }

    private void UpdatePostureText()
    {
        PostureLineText.Text = L.T($"当前高度阈值：距底部 {PostureSlider.Value:0}%", $"Height threshold: {PostureSlider.Value:0}% from the bottom");
        PostureAreaText.Text = L.T($"当前距离阈值：{PostureAreaSlider.Value:0.#}%", $"Distance threshold: {PostureAreaSlider.Value:0.#}%");
    }

    private int CurrentRotation => _settings.CameraId is { } id && _settings.CameraRotations.TryGetValue(id, out var r) ? r : 0;

    private void UpdateRotateButton() => RotateButton.Content =
        L.T($"旋转画面 90°（当前 {CurrentRotation}°）", $"Rotate image 90° (current rotation: {CurrentRotation}°)");

    // 每个摄像头单独记住方向；运行中立即生效，预览和检测都按新方向
    private void RotateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.CameraId is not { } id) return;
        var rotation = (CurrentRotation + 90) % 360;
        if (rotation == 0) _settings.CameraRotations.Remove(id); else _settings.CameraRotations[id] = rotation;
        _settings.Save();
        _presence.Rotation = rotation;
        UpdateRotateButton();
    }

    private void Presence_PostureAlert(object? sender, PostureAlertArgs e)
    {
        var text = e.SuggestRest ? L.T("太累了？休息一下吧！", "Tired? Take a break!")
            : e.Issues == (PostureIssue.Slouching | PostureIssue.TooClose)
                ? L.T("抬头挺腰，注意用眼距离。", "Sit up straight and keep a healthy distance from the screen.")
            : e.Issues == PostureIssue.TooClose
                ? L.T("注意用眼距离。", "Keep a healthy distance from the screen.")
                : L.T("记得抬头挺腰。", "Remember to sit up straight.");
        DispatcherQueue.TryEnqueue(() => Notify(L.T("坐姿提醒", "Posture reminder"), text));
    }

    private void SmartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.SmartDetection = SmartSwitch.IsOn;
        _settings.Save();
        _presence.SmartDetection = SmartSwitch.IsOn;
        UpdateIntervalLabel();
        ApplyPostureInterval(); // 跟随模式下的智能检测说明随之显示/隐藏
        UpdateIntervalGrowthUi();
        // 开或关都会改变下面生效的设置（停止操作多久后检测 / 固定检测间隔），展开让用户看到
        SmartCard.IsExpanded = true;
        RefreshPolicyInfo();
    }

    private void IntervalPicker_ValueChanged(object? sender, TimeSpan value)
    {
        if (!_ready) return;
        var seconds = (uint)value.TotalSeconds;
        _settings.IntervalSeconds = seconds;
        _settings.Save();
        if (_detection.Running) _presence.SetInterval(seconds);
        UpdateIntervalGrowthUi();
        RefreshPolicyInfo();
    }

    /// <summary>智能检测下检测间隔会递增：离开后最长要等“最长检测间隔”才发现。</summary>
    private bool IntervalGrows => _settings.SmartDetection && _settings.IntervalStepSeconds > 0;
    private TimeSpan LongestCheckGap => TimeSpan.FromSeconds(IntervalGrows ? Math.Max(_settings.MaxIntervalSeconds, IntervalSeconds) : IntervalSeconds);

    private void IntervalStepPicker_ValueChanged(object? sender, TimeSpan value)
    {
        if (!_ready) return;
        _settings.IntervalStepSeconds = _presence.IntervalStepSeconds = (uint)value.TotalSeconds;
        _settings.Save();
        UpdateIntervalGrowthUi();
        RefreshPolicyInfo();
    }

    private void MaxIntervalPicker_ValueChanged(object? sender, TimeSpan value)
    {
        if (!_ready) return;
        _settings.MaxIntervalSeconds = _presence.MaxIntervalSeconds = (uint)value.TotalSeconds;
        _settings.Save();
        UpdateIntervalGrowthUi();
        RefreshPolicyInfo();
    }

    private void GrowthOption_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.LinearGrowth = _presence.LinearGrowth = GrowthLinearOption.IsChecked == true;
        _settings.Save();
        UpdateIntervalGrowthUi();
    }

    // 递增设置只在智能检测下有意义；步长不为 0 时展开卡片，显示递增方式和最长间隔。示例序列只显示在选中的方式下面
    private void UpdateIntervalGrowthUi()
    {
        IntervalStepCard.Visibility = _settings.SmartDetection ? Visibility.Visible : Visibility.Collapsed;
        MaxIntervalCard.Visibility = IntervalGrowthCard.Visibility = IntervalGrows ? Visibility.Visible : Visibility.Collapsed;
        GrowthExponentialNote.Text = GrowthExample(linear: false);
        GrowthLinearNote.Text = GrowthExample(linear: true);
        GrowthExponentialNote.Visibility = _settings.LinearGrowth ? Visibility.Collapsed : Visibility.Visible;
        GrowthLinearNote.Visibility = _settings.LinearGrowth ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>按当前设置列出检测间隔的变化，例如“检测间隔以10秒→1分10秒→2分20秒→4分40秒→9分20秒→10分钟的方式递增”。</summary>
    private string GrowthExample(bool linear)
    {
        // 中文写得紧凑：去掉数字和单位之间的空格
        static string Short(TimeSpan span) => L.Zh ? PowerPolicy.Describe(span).Replace(" ", "") : PowerPolicy.Describe(span);
        var max = LongestCheckGap;
        var items = new List<string>();
        var last = TimeSpan.MinValue;
        for (var k = 0; k < 64; k++)
        {
            var gap = PresenceService.CheckGap(IntervalSeconds, _settings.IntervalStepSeconds, _settings.MaxIntervalSeconds, linear, k);
            if (gap == last) break; // 已到最长间隔
            if (items.Count == 6 && gap != max)
            {
                items.Add("…");
                items.Add(Short(max));
                break;
            }
            items.Add(Short(gap));
            last = gap;
        }
        return L.T($"检测间隔以{string.Join("→", items)}的方式递增", $"The check interval grows as {string.Join(" → ", items)}");
    }

    // 压暗只在有倒计时（延迟大于 0）时有意义
    private void UpdateLockCards()
    {
        DimCard.Visibility = DimModeCard.Visibility = DimLevelCard.Visibility =
            _settings.LockDelaySeconds > 0 ? Visibility.Visible : Visibility.Collapsed;
        DimModeCard.IsEnabled = DimLevelCard.IsEnabled = _settings.DimDuringCountdown;
    }

    private void DimMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.DimMode = DimBrightnessOption.IsChecked == true ? DimMode.Brightness : DimMode.Overlay;
        _settings.Save();
        _ = UpdateDimModeNotesAsync();
    }

    // 说明只显示在选中的选项下面；调亮度方式下说明有几个显示器能调，其余的用遮罩
    private async Task UpdateDimModeNotesAsync()
    {
        var brightness = _settings.DimMode == DimMode.Brightness;
        DimOverlayNote.Visibility = brightness ? Visibility.Collapsed : Visibility.Visible;
        DimBrightnessNote.Visibility = brightness ? Visibility.Visible : Visibility.Collapsed;
        if (!brightness) return;
        var (controllable, total) = await _brightness.CountAsync();
        DimBrightnessNote.Text = controllable == 0
            ? L.T("这台电脑的显示器都不支持调亮度，将改用黑色遮罩。", "None of your displays support brightness control, so the black overlay is used instead.")
            : controllable < total
                ? L.T($"{total} 个显示器中有 {controllable} 个能调亮度，其余的用黑色遮罩调暗。倒计时结束或回来后恢复原亮度。",
                    $"{controllable} of {total} displays support brightness control; the others are dimmed with the black overlay. Brightness is restored when the countdown ends.")
                : L.T("倒计时期间降低显示器亮度，结束或回来后恢复原亮度。外接显示器需要支持 DDC/CI。",
                    "Lowers display brightness during the countdown and restores it afterwards. External monitors need DDC/CI support.");
    }

    private void DimSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _settings.DimDuringCountdown = DimSwitch.IsOn;
        _settings.Save();
        if (!DimSwitch.IsOn) StopDimming();
        UpdateLockCards();
    }

    private void DimLevelSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // 初始化时滑块被夹到最小值也会触发，那时调暗层还没创建；启动时由 ApplySettings 设置
        if (!_ready) return;
        ApplyDimLevel();
        _settings.Save();
        UpdateDimPreview();
    }

    // 按住“压暗强度”滑块时按当前方式和程度预览，松开即恢复。只认鼠标/触摸按住拖动，键盘调整不预览（没有“松开”可以结束）
    private bool _dimPreviewing, _dimPreviewBusy, _dimPreviewDirty;

    private void SetupDimPreview()
    {
        // 滑块自己处理了按下/松开事件，要用 handledEventsToo 才收得到
        DimLevelSlider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => BeginDimPreview()), true);
        DimLevelSlider.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EndDimPreview()), true);
        DimLevelSlider.AddHandler(UIElement.PointerCaptureLostEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EndDimPreview()), true);
    }

    private void BeginDimPreview()
    {
        if (_countdownDimming) return; // 正在锁定倒计时：不打断真正的调暗
        _dimPreviewing = true;
        UpdateDimPreview();
    }

    private async void UpdateDimPreview()
    {
        if (!_dimPreviewing) return;
        if (_settings.DimMode == DimMode.Overlay)
        {
            _dim.ShowPreview();
            return;
        }
        // 调亮度慢（DDC/CI 每条命令几十到几百毫秒）：正在调时只记下“有新值”，调完再按最新的值调一次，不排长队
        if (_dimPreviewBusy) { _dimPreviewDirty = true; return; }
        _dimPreviewBusy = true;
        do
        {
            _dimPreviewDirty = false;
            var dimmed = await _brightness.DimAsync(_settings.DimLevelPercent / 100);
            if (_dimPreviewing) _dim.ShowPreview(dimmed); // 调不了亮度的显示器用遮罩预览
        } while (_dimPreviewDirty && _dimPreviewing);
        _dimPreviewBusy = false;
    }

    private void EndDimPreview()
    {
        if (!_dimPreviewing) return;
        _dimPreviewing = false;
        _dim.Hide();
        _ = _brightness.RestoreAsync(); // 排在正在进行的调亮度之后，最后一定恢复
    }

    private void ApplyDimLevel()
    {
        _settings.DimLevelPercent = DimLevelSlider.Value;
        _dim.Level = DimLevelSlider.Value / 100;
        DimLevelText.Text = $"{DimLevelSlider.Value:0}%";
    }

    /// <summary>锁定时机：0 秒立即锁定，大于 0 为检测到离开后的倒计时。</summary>
    private void LockDelayPicker_ValueChanged(object? sender, TimeSpan value)
    {
        if (!_ready) return;
        _settings.LockDelaySeconds = (int)value.TotalSeconds;
        _settings.Save();
        _presence.LockDelay = value;
        UpdateLockCards();
        RefreshPolicyInfo();
    }

    /// <summary>刷新“离开后会发生什么”的说明，以及 PowerToys Awake 冲突提示。</summary>
    private void RefreshPolicyInfo()
    {
        if (!_ready) return;
        var delay = TimeSpan.FromSeconds(_settings.LockDelaySeconds);
        AwayLockNote.Text = delay == TimeSpan.Zero
            ? L.T("检测到离开后立即锁定。", "Locks the PC as soon as you leave.")
            : L.T($"检测到离开后等待 {PowerPolicy.Describe(delay)}，回到摄像头前会取消锁定。",
                $"Waits {PowerPolicy.Describe(delay)} after you leave, then locks the PC. Returning to the camera cancels the lock.");

        // Awake 保持屏幕常亮时，『在吗』照常锁定，但锁定后屏幕不会息屏
        var awake = PowerToysAwake.Check();
        if (awake is not null)
        {
            AwakeWarning.Title = L.T("提示", "Note");
            AwakeWarning.Message = awake;
        }
        AwakeWarning.IsOpen = awake is not null;
        AwakeWarning.Visibility = awake is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshPowerUi(); // Windows 里的电源设置可能被改过，刷新“当前”值
        RefreshPowerInfo();
    }

    /// <summary>按当前设置给出一行耗电评级：几乎无影响 / 轻微 / 中等 / 非常耗电。</summary>
    private void RefreshPowerInfo()
    {
        if (!_ready) return;
        int level; // 0 几乎无影响、1 轻微、2 中等、3 非常耗电
        string reason;
        if (!_detection.Enabled)
        {
            (level, reason) = (0, L.T("未启用，摄像头关闭", "detection off, camera off"));
        }
        else if (_detection.Paused != PauseReason.None)
        {
            (level, reason) = (0, L.T("已暂停，摄像头关闭", "paused, camera off"));
        }
        else
        {
            // 只按检测频率评级，不管预览是否打开：每次检测开摄像头约 1–2 秒，间隔越短，摄像头工作的比例越高；
            // 间隔短到 10 秒以内时两次检测之间来不及关摄像头，等于一直开着
            static int LevelFor(uint seconds) => seconds switch { <= 10 => 3, <= 15 => 2, <= 30 => 1, _ => 0 };
            var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds));
            level = LevelFor(IntervalSeconds);
            reason = L.T($"每 {interval}检测一次", $"checks every {interval}");
            // 智能检测：使用键鼠期间不做在场检测，实际开摄像头的次数更少
            if (_settings.SmartDetection)
                (level, reason) = (Math.Max(0, level - 1),
                    IntervalGrows
                        ? L.T($"停止操作 {interval}后才检测，之后间隔逐渐延长至 {PowerPolicy.Describe(LongestCheckGap)}", $"checks after {interval} of keyboard and mouse inactivity, then less and less often, up to every {PowerPolicy.Describe(LongestCheckGap)}")
                        : L.T($"停止操作 {interval}后才检测，之后每 {interval}检测一次", $"checks after {interval} of keyboard and mouse inactivity, then every {interval} until input resumes"));
            // 坐姿单独检测时按两者中更频繁的评级
            if (_settings.PostureCheck && !_settings.PostureFollowDetection)
            {
                level = Math.Max(level, LevelFor(_settings.PostureIntervalSeconds));
                var posture = PowerPolicy.Describe(TimeSpan.FromSeconds(_settings.PostureIntervalSeconds));
                reason += L.T($"；坐姿每 {posture}检测一次", $"; posture checked every {posture}");
            }
            // 离开后到锁定前屏幕保持常亮、摄像头持续工作；延迟 1 分钟以上时值得提醒
            var lockDelay = _settings.LockDelaySeconds;
            if (lockDelay >= 60)
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
}
