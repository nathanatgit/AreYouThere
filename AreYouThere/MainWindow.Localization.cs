using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AreYouThere.Services;

namespace AreYouThere;

// 界面文字：按当前语言设置所有固定文字，以及切换语言。
public sealed partial class MainWindow
{
    /// <summary>按当前语言设置所有固定文字。启动时调用一次，切换语言时再调用。</summary>
    private void Localize()
    {
        Title = L.T("在吗 AreYouThere", "AreYouThere");
        AppTitleBarText.Text = Title;
        AboutCard.Header = L.T("关于", "About");
        AppTitleText.Text = L.T("在吗", "AreYouThere");
        AppSubtitleText.Visibility = L.Zh ? Visibility.Visible : Visibility.Collapsed;
        IntroText.Text = L.T("微软官方的“存在感知”(Presence Sensing) 只在部分新款设备上提供。『在吗』是它的轻量替代方案：用普通摄像头判断你是否在电脑前，在的时候阻止 Windows 自动息屏，离开后锁定电脑。",
            "Microsoft's Presence Sensing is available only on certain newer devices. AreYouThere is a lightweight alternative that uses a standard webcam to detect whether someone is at the PC. It keeps the screen on while someone is present and can lock the PC when no one is detected.");
        OfflineBar.Title = L.T("完全离线，只在本机处理", "Works entirely offline");
        OfflineBar.Message = L.T("『在吗』没有任何联网功能：不上传、不收集、不保存任何画面或检测数据。摄像头画面只在本机内存中分析，分析完立即丢弃；偏好设置仅保存在本机。",
            "AreYouThere never connects to the internet. Camera images are processed only in memory on this PC, then discarded. No images or detection results are saved or uploaded. Only your preferences are saved locally.");
        PrivacyBar.Message = L.T("隐私提醒：启用后摄像头指示灯会亮起（显示预览时常亮，后台运行时每次检测短暂亮起）。请勿在隐私敏感场所使用，以免引起误会或纠纷。在公共场所使用时，请遵守场所规定，并调大人脸面积阈值，避免路人入镜被当成你，导致离开后不自动锁屏。",
            "Privacy notice: the camera light stays on while the preview is visible and lights up briefly during each background check. Avoid using the camera where privacy is expected, as this may cause concern or disputes. In public places, follow the venue's rules and increase the minimum face size to help filter out passersby. The app detects faces, not identities: another person's face may keep the PC unlocked after you leave.");
        CameraPrivacyButton.Content = L.T("打开摄像头隐私设置", "Open camera privacy settings");
        CameraCard.Header = L.T("摄像头", "Camera");
        if (_cameras.Count == 0) CameraBox.PlaceholderText = L.T("正在查找摄像头…", "Looking for cameras…");
        RefreshCamerasButton.Content = L.T("刷新", "Refresh");
        CameraHintText.Text = L.T("名称不准确时，可以开启检测后看预览确认。", "If the names are unclear, turn on detection and check the preview.");
        DetectionHeader.Text = L.T("检测", "Detection");
        AwayHeader.Text = L.T("锁定策略", "Lock settings");
        AwayCard.Header = L.T("离开电脑后", "When you leave");
        OtherHeader.Text = L.T("其他设置", "Other settings");
        NotificationCard.Header = L.T("通知", "Notifications");
        NotifyQuietOption.Content = L.T("全屏应用（如游戏）在前台时不通知", "Hide notifications while using a full-screen app, such as a game");
        NotifyNeverOption.Content = L.T("永不通知（坐姿提醒等将不会显示）", "Never show notifications (including posture reminders)");
        NotifyAlwaysOption.Content = L.T("始终通知（可能打断全屏应用）", "Always show notifications (may interrupt full-screen apps)");
        PauseButton.Content = L.T("暂停", "Pause");
        ToolTipService.SetToolTip(PauseButton, L.T("暂停检测一段时间，到时自动恢复", "Pause detection for a while; it resumes automatically"));
        ResumeButton.Content = L.T("恢复", "Resume");
        BuildPauseMenu();
        foreach (var toggle in new[] { EnabledSwitch, StartupSwitch, PreviewSwitch, SmartSwitch, PostureSwitch, DimSwitch })
        {
            toggle.OnContent = L.T("开", "On");
            toggle.OffContent = L.T("关", "Off");
        }
        EnabledCard.Header = L.T("启用在场检测", "Presence detection");
        StartupCard.Header = L.T("登录 Windows 后自动运行", "Start when I sign in to Windows");
        StartupStatusText.Text = DefaultStartupStatus();
        RestoreStartupReminderButton.Content = L.T("恢复每次开机运行提醒", "Restore startup reminders");
        ToolTipService.SetToolTip(PreviewSwitch, L.T("显示摄像头预览", "Show camera preview"));
        SmartCard.Header = L.T("智能检测", "Smart detection");
        SmartHintText.Text = L.T("在操作电脑时（鼠标、键盘操作）不检测，当停止操作后一段时间才启用检测；关闭则按照固定时间检测。",
            "Presence checks pause while you're using the keyboard or mouse and resume after the idle period you set below. Turn this off to check at a fixed interval.");
        PostureCard.Header = L.T("坐姿检测", "Posture reminders");
        PostureHintText.Text = L.T("检测会在发现人脸位置低于坐姿线或者太接近屏幕时发出提醒。",
            "Reminds you when your face is below the posture line or too close to the screen.");
        PostureIntervalHeader.Text = L.T("坐姿检测间隔", "Posture check interval");
        PostureFollowText.Text = L.T("跟随在场检测间隔：只在在场检测时顺便检测坐姿", "Check posture during presence checks");
        PostureCustomOption.Content = L.T("自定义间隔：单独按下方时间检测坐姿", "Custom: check posture on its own schedule");
        PostureFollowNote.Text = L.T("当前已开启智能检测，持续打字或移动鼠标时不会触发在场检测，因此也不会有检测坐姿提醒。如果需要操作电脑时也提醒，请选择自定义间隔。",
            "Smart detection pauses presence checks while you're typing or moving the mouse, so posture checks also pause in this mode. Choose a custom interval to keep receiving posture reminders while using the PC.");
        PostureIntervalPicker.SetLabels(L.T("时", "h"), L.T("分", "min"), L.T("秒", "s"));
        PostureAreaSlider.Header = HeaderWithHelp(L.T("距离阈值", "Distance threshold"),
            L.T("人脸区域面积大小，超出此阈值提醒", "A reminder appears when you sit too close"),
            L.T("调整好坐姿和摄像头位置，滑动距离阈值滑块，直至预览中人脸区域框由橙变绿，更大的距离阈值表示更宽松的检测。",
                "Sit in your usual position and adjust the camera. Move the distance slider until the box around your face turns from orange to green. A higher threshold lets you sit closer before a reminder appears."));
        PostureSlider.Header = HeaderWithHelp(L.T("坐姿线高度阈值", "Posture line height"),
            L.T("从画面底部算起，低于此阈值提醒", "A reminder appears when your face drops below this line"),
            L.T("调整好坐姿和摄像头位置，滑动坐姿线高度滑块，直至预览中人脸区域框由橙变绿，更低的坐姿线高度阈值表示更宽松的检测。",
                "Sit upright and adjust the camera. Move the posture line slider until the box around your face turns from orange to green. A lower line lets you sit lower before a reminder appears."));
        BatterySaverCard.Header = L.T($"{ZhSaver}下", $"When {EnSaver} is on");
        BatterySaverPauseText.Text = L.T("暂停检测", "Pause detection");
        BatterySaverPauseNote.Text = L.T($"在{ZhSaver}下，由系统电源设置接管。", $"While {EnSaver} is on, your Windows power settings take over.");
        BatterySaverKeepText.Text = L.T("继续检测", "Keep detecting");
        BatterySaverKeepNote.Text = L.T($"在{ZhSaver}下仍保持检测。", $"Detection keeps running while {EnSaver} is on.");
        UpdatePostureText();
        UpdateRotateButton();
        UpdateIntervalLabel();
        IntervalPicker.SetLabels(L.T("时", "h"), L.T("分", "min"), L.T("秒", "s"));
        IntervalStepCard.Header = L.T("递增步长", "Interval increase step");
        IntervalStepHintText.Text = L.T("停止操作后，每次检测到你在电脑前自动增加下次检测间隔，设置为0秒不开启。",
            "After you stop using the keyboard and mouse, each check that finds you at the PC lengthens the next interval. Set to 0 seconds to turn this off.");
        IntervalStepPicker.SetLabels(L.T("时", "h"), L.T("分", "min"), L.T("秒", "s"));
        IntervalGrowthCard.Header = L.T("递增方式", "How the interval grows");
        GrowthExponentialText.Text = L.T("指数增长", "Exponential");
        GrowthLinearText.Text = L.T("线性增长", "Linear");
        MaxIntervalCard.Header = L.T("最长检测间隔", "Maximum interval");
        MaxIntervalHintText.Text = L.T("间隔越长越省电，但离开后可能要等更久才锁定。", "Longer intervals save power, but it can take longer to lock the PC after you leave.");
        MaxIntervalPicker.SetLabels(L.T("时", "h"), L.T("分", "min"), L.T("秒", "s"));
        UpdateIntervalGrowthUi();
        AwayFollowText.Text = L.T("跟随系统电源策略", "Follow Windows power settings");
        AwayLockText.Text = L.T("由『在吗』接管：检测到离开后锁定电脑", "Let AreYouThere lock the PC when you leave");
        LockDelayCard.Header = L.T("锁定时机", "Lock delay after absence is detected");
        LockDelayHintText.Text = L.T("检测到离开后等待多久再锁定，设为 0 秒立即锁定。", "How long to wait after absence is detected before locking. Set to 0 seconds to lock immediately.");
        LockDelayPicker.SetLabels(L.T("时", "h"), L.T("分", "min"), L.T("秒", "s"));
        DimCard.Header = L.T("倒计时期间调暗屏幕", "Dim the screen during the countdown");
        DimHintText.Text = L.T("锁定前屏幕变暗，提醒你快要锁定了；回到摄像头前即可取消。",
            "The screen darkens before the PC locks. Return to the camera's view to cancel.");
        DimModeCard.Header = L.T("调暗方式", "How to dim");
        DimOverlayText.Text = L.T("黑色遮罩", "Black overlay");
        DimOverlayNote.Text = L.T("所有显示器都有效。调到 100% 时整屏全黑，临时挡住屏幕内容。", "Works on every display. At 100% the screen goes fully black, hiding its contents.");
        DimBrightnessText.Text = L.T("降低显示器亮度", "Lower display brightness");
        DimLevelCard.Header = L.T("压暗强度", "Dim strength");
        if (_ready) _ = UpdateDimModeNotesAsync();
        TrayHintText.Text = L.T("关闭窗口后在托盘继续运行；右键托盘图标可启用/关闭或退出。",
            "Closing the window keeps the app running in the tray; right-click the tray icon to turn detection on or off, or to exit.");
        LanguageCard.Header = L.T("语言", "Language");
        ThemeCard.Header = L.T("主题", "Theme");
        SetItemTexts(ThemeBox, L.Zh ? ["跟随系统", "浅色", "深色"] : ["System default", "Light", "Dark"]);
        ((ComboBoxItem)LanguageBox.Items[0]).Content = L.T("跟随系统", "System default");
        ShowNoticesLink.Content = L.T("重新显示已关闭的提示", "Show dismissed notices again");
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        AboutText.Text = L.T($"在吗 AreYouThere v{version} · MIT 许可证 · 开源免费", $"AreYouThere v{version} · MIT License · Free and open source");
        ToolTipService.SetToolTip(RepoLink, L.T("在浏览器中打开", "Open in browser"));
        RepoLinkText.Text = L.T("github.com/nathanatgit/AreYouThere（在浏览器中打开）", "github.com/nathanatgit/AreYouThere (opens in browser)");
        PreviewTitle.Text = L.T("摄像头预览", "Camera preview");
        PreviewPlaceholder.Text = L.T("预览未开启", "Preview is off");
        CameraStatusText.Text = L.T("摄像头状态：尚未打开", "Camera: not started");
        FaceStatusText.Text = L.T("人脸状态：等待检测", "Face detection: waiting for a check");
        PreviewNoteText.Text = L.T("预览仅用于本地调整位置，不保存或上传图像。", "The preview is only for positioning the camera. No images are saved or uploaded.");
        FaceAreaSlider.Header = HeaderWithHelp(L.T("人脸区域识别阈值", "Face detection threshold"),
            L.T("人脸区域占画面的百分比，低于此阈值视为离开", "Faces smaller than this are ignored"),
            L.T("调整好坐姿和摄像头位置，把阈值设为略低于预览中的百分比。面积大于此阈值的人脸才算你在电脑前；太小的人脸，会被忽略。在公共场合请稍微调大此阈值避免误认路人，导致自动锁定失效。",
                "Sit in your usual position and adjust the camera. Set the threshold slightly below the percentage shown above your face in the preview. Smaller faces are ignored. In public places, increase the threshold to help filter out passersby, whose faces could otherwise keep the PC unlocked after you leave."));
    }

    /// <summary>
    /// 滑块标题：第一行标题 + “如何设置”，第二行小字说明。
    /// “如何设置”悬停显示提示，点击弹出同样的说明（触摸和键盘也能看到）。
    /// </summary>
    private StackPanel HeaderWithHelp(string title, string subtitle, string help)
    {
        var link = new HyperlinkButton
        {
            Content = L.T("如何设置", "How to adjust"), FontSize = 12, Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        // 提示和弹出层不会继承窗口里选的主题，显示前按链接当前的主题设置
        var tip = new ToolTip { Content = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap } };
        link.PointerEntered += (_, _) => tip.RequestedTheme = link.ActualTheme;
        ToolTipService.SetToolTip(link, tip);
        link.Click += (_, _) =>
        {
            var style = new Style(typeof(FlyoutPresenter));
            style.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, link.ActualTheme));
            new Flyout
            {
                Content = new TextBlock { Text = help, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 },
                FlyoutPresenterStyle = style
            }.ShowAt(link);
        };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        // 与设置卡片同一层级：标题 16、说明 12（灰色），样式在 MainWindow.xaml 里统一定义
        titleRow.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Style = (Style)WindowRoot.Resources["CardTitle"] });
        titleRow.Children.Add(link);
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(titleRow);
        header.Children.Add(new TextBlock { Text = subtitle, Style = (Style)WindowRoot.Resources["Hint"] });
        return header;
    }

    private void UpdateIntervalLabel() => IntervalCard.Header = _settings.SmartDetection
        ? L.T("停止操作多久后检测", "Start checking after this idle period")
        : L.T("检测间隔", "Check interval");

    private static void SetItemTexts(ComboBox box, string[] texts)
    {
        for (var i = 0; i < texts.Length && i < box.Items.Count; i++) ((ComboBoxItem)box.Items[i]).Content = texts[i];
    }

    private void UpdateShowNoticesLink() => ShowNoticesLink.Visibility =
        _settings.HideOfflineNotice || _settings.HidePrivacyNotice ? Visibility.Visible : Visibility.Collapsed;

    private void ShowNoticesLink_Click(object sender, RoutedEventArgs e)
    {
        _settings.HideOfflineNotice = _settings.HidePrivacyNotice = false;
        _settings.Save();
        OfflineBar.IsOpen = PrivacyBar.IsOpen = true;
        UpdateShowNoticesLink();
    }

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
        Report(!_detection.Enabled ? L.T("未启用", "Off") : _detection.Paused != PauseReason.None ? PausedStatus() : RunningStatus());
        _ = RefreshCamerasAsync();
    }
}
