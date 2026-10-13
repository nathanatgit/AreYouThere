using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AreYouThere.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AreYouThere;

// 在场检测的开关、暂停、摄像头列表和预览。
public sealed partial class MainWindow
{
    private uint IntervalSeconds => _settings.IntervalSeconds;

    private string RunningStatus()
    {
        var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(IntervalSeconds));
        return L.T($"运行中，每 {interval}检测一次", $"Running, checking every {interval}");
    }

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
            UpdateRotateButton();
            CameraBox.IsEnabled = _cameras.Count > 0;
            CameraBox.PlaceholderText = _cameras.Count > 0 ? "" : L.T("未找到摄像头", "No camera found");
            _suppressCameraChange = false;

            if (_cameras.Count == 0)
            {
                ShowCameraProblem(L.T("未找到摄像头", "No camera found"),
                    L.T("『在吗』需要一个摄像头（笔记本内置或 USB 摄像头都可以）才能判断你是否在电脑前。请连接摄像头，插入后通常会自动识别，也可以点击“刷新”。",
                        "AreYouThere needs a built-in or USB camera for presence detection. Connect a camera and wait for it to appear, or click Refresh."));
                _noCameraShown = true;
                await _detection.SetEnabledAsync(false);
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
        UpdateRotateButton();
        // 运行中切换摄像头：直接用新摄像头重新开始
        await _detection.RestartAsync();
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
            _detection.OnCameraFailed();
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

    // 打开摄像头需要一点时间，期间禁用开关，防止重复启动
    private void UpdateSwitchEnabled() => EnabledSwitch.IsEnabled = !_startupPending && !_detection.Starting && _cameras.Count > 0;

    /// <summary>检测开关、暂停原因、启动中/运行中有变化：开关、暂停按钮、状态文字和耗电说明都跟着刷新。</summary>
    private void Detection_StateChanged()
    {
        _suppressToggle = true;
        EnabledSwitch.IsOn = _detection.Enabled;
        _suppressToggle = false;
        UpdateSwitchEnabled();
        if (!_detection.Running) ClearPreview();
        // 关闭检测时 DetectionController 会一并取消手动暂停
        if (!_detection.Paused.HasFlag(PauseReason.User)) ClearUserPause();
        UpdatePauseButtons();
        if (_detection.Enabled && _detection.Paused != PauseReason.None) Report(PausedStatus());
        RefreshPowerInfo();
    }

    private void Detection_StartFailed(StartResult result)
    {
        if (result.Error is null) return;
        ShowCameraProblem(result.PermissionDenied ? L.T("没有摄像头权限", "Camera access denied") : L.T("无法打开摄像头", "Can't open the camera"),
            result.Error, result.PermissionDenied);
        Report(L.T("无法打开摄像头", "Can't open the camera"));
    }

    private StartRequest? StartRequestForCurrentSettings() =>
        _settings.CameraId is { } id ? new StartRequest(id, IntervalSeconds, PreviewWanted, CurrentRotation) : null;

    /// <summary>暂停时的状态文字；有多个原因时说最主要的那个。</summary>
    private string PausedStatus()
    {
        var paused = _detection.Paused;
        if (paused.HasFlag(PauseReason.SessionLocked)) return L.T("电脑已锁定，暂停检测", "PC locked; detection paused");
        if (paused.HasFlag(PauseReason.User) && _pausedUntil is { } until)
            return L.T($"已暂停，{until:HH:mm} 自动恢复", $"Paused until {until:t}");
        return L.T($"{ZhSaver}已开启，暂停检测", $"{EnSaverCap} on; detection paused");
    }

    // 手动暂停：到时间后自动恢复。用定时检查到期时间而不是一次性长定时器，电脑睡眠唤醒后也能按时恢复
    private static readonly TimeSpan PauseCheckInterval = TimeSpan.FromSeconds(15);
    private readonly DispatcherQueueTimer _pauseTimer;
    private DateTimeOffset? _pausedUntil;

    /// <summary>暂停检测一段时间；null 表示立即恢复。托盘菜单和窗口里的“暂停”按钮都调用这里。</summary>
    private async void PauseFor(TimeSpan? duration)
    {
        if (duration is not { } d)
        {
            ClearUserPause();
            await _detection.SetPausedAsync(PauseReason.User, false);
            return;
        }
        if (!_detection.Enabled) return;
        _pausedUntil = DateTimeOffset.Now + d;
        _pauseTimer.Start();
        await _detection.SetPausedAsync(PauseReason.User, true);
    }

    private void CheckPauseExpired()
    {
        if (_pausedUntil is not { } until || DateTimeOffset.Now < until) return;
        PauseFor(null);
        Notify(L.T("『在吗』已恢复检测", "Presence detection resumed"),
            L.T("暂停时间已到，已重新开始在场检测。", "The pause has ended, so presence detection has started again."));
    }

    private void ClearUserPause()
    {
        _pausedUntil = null;
        _pauseTimer.Stop();
    }

    // 检测开启时显示“暂停”；暂停中换成“恢复”
    private void UpdatePauseButtons()
    {
        var paused = _pausedUntil is not null;
        PauseButton.Visibility = _detection.Enabled && !paused ? Visibility.Visible : Visibility.Collapsed;
        ResumeButton.Visibility = _detection.Enabled && paused ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildPauseMenu()
    {
        PauseMenu.Items.Clear();
        foreach (var choice in TrayService.PauseChoices)
        {
            var item = new MenuFlyoutItem { Text = PowerPolicy.Describe(choice) };
            item.Click += (_, _) => PauseFor(choice);
            PauseMenu.Items.Add(item);
        }
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e) => PauseFor(null);


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
                PreviewNoteText.Visibility = Visibility.Visible;
                DrawFaces(e.Faces, e.Bitmap.PixelWidth, e.Bitmap.PixelHeight);
            }
            catch { }
            finally { e.Bitmap.Dispose(); Volatile.Write(ref _previewBusy, 0); }
        });
        if (!queued) { e.Bitmap.Dispose(); Volatile.Write(ref _previewBusy, 0); }
    }

    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush CloseFaceBrush = new(Windows.UI.Color.FromArgb(255, 0x35, 0xD0, 0x7F));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush FarFaceBrush = new(Windows.UI.Color.FromArgb(200, 0xB0, 0xB0, 0xB0));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush LabelTextBrush = new(Microsoft.UI.Colors.Black);
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush PostureBrush = new(Windows.UI.Color.FromArgb(255, 0xFF, 0x9F, 0x1C));

    // 画布按摄像头原始像素布局（外层 Viewbox 统一缩放），线宽和字号按画面宽度换算，缩放后大小一致
    private void DrawFaces(IReadOnlyList<DetectedFace> faces, int frameWidth, int frameHeight)
    {
        FaceCanvas.Children.Clear();
        var unit = frameWidth / 320.0;
        var posture = _settings.PostureCheck;
        if (posture)
        {
            var y = frameHeight * (100 - PostureSlider.Value) / 100;
            FaceCanvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line
            {
                X1 = 0, X2 = frameWidth, Y1 = y, Y2 = y, Stroke = PostureBrush, StrokeThickness = 1.5 * unit,
                StrokeDashArray = [4, 3]
            });
        }
        foreach (var face in faces)
        {
            // 近处人脸中心低于坐姿线时用橙色，提示这样会被当作驼背
            var brush = !face.Close ? FarFaceBrush
                : posture && (face.CenterHeightPercent < PostureSlider.Value || face.AreaPercent > PostureAreaSlider.Value) ? PostureBrush
                : CloseFaceBrush;
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
        var on = EnabledSwitch.IsOn;
        if (!on) Report(L.T("未启用", "Off"));
        // 节电模式、锁屏等暂停原因由 DetectionController 处理：开关保持打开，原因解除后自动开始
        await _detection.SetEnabledAsync(on);
    }

    /// <summary>预览开关打开、且窗口可见未最小化时，才需要摄像头持续工作。</summary>
    private bool PreviewWanted => PreviewSwitch.IsOn && AppWindow.IsVisible
        && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    private void UpdatePreviewWanted()
    {
        if (!_ready) return;
        var wanted = PreviewWanted;
        _presence.SetPreview(wanted); // 没在检测时只是记下来，下次启动用
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
        PreviewNoteText.Visibility = Visibility.Collapsed;
        FaceCanvas.Children.Clear();
    }
}
