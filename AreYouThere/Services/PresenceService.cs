using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;

namespace AreYouThere.Services;

internal sealed record PresenceStatus(string CameraMessage, string FaceMessage);
/// <summary>一张检测到的人脸。AreaPercent 是人脸框占整个画面的面积百分比；Close 表示达到阈值、算作“在电脑前”。</summary>
internal sealed record DetectedFace(Windows.Foundation.Rect Box, double AreaPercent, bool Close);
internal sealed record PresenceFrame(SoftwareBitmap Bitmap, IReadOnlyList<DetectedFace> Faces);
/// <summary>Started=false 且 Error=null 表示启动途中被 Stop 取消。</summary>
internal sealed record StartResult(bool Started, string? Error = null, bool PermissionDenied = false);

/// <summary>
/// 打开摄像头、读取帧，并按间隔用 FaceDetector 判断是否有人，有人时阻止息屏。
/// 离开后要么交还系统电源策略，要么（接管模式）在延迟后锁定电脑。
/// 不显示预览时，两次检测之间会关闭摄像头流以省电。
/// 事件 FrameReady / StatusChanged / CameraFailed 可能在后台线程触发，订阅方需自行切回 UI 线程。
/// </summary>
internal sealed class PresenceService : IDisposable
{
    // 预览和检测都不需要满帧率，限制到约 15 fps 以降低 CPU 占用。
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(66);
    // 锁定倒计时期间加密检测，人一回来就能取消锁定。
    private static readonly TimeSpan CountdownRecheck = TimeSpan.FromSeconds(2);
    // 距离下次检测超过这个时长才关闭摄像头流，避免频繁开关。
    private static readonly TimeSpan PauseThreshold = TimeSpan.FromSeconds(8);
    // 帧超过这个时长没更新就视为过期，不能拿来判断。
    private static readonly TimeSpan StaleFrame = TimeSpan.FromSeconds(3);
    // 开流后这么久都拿不到画面，视为摄像头故障。
    private static readonly TimeSpan NoFrameTimeout = TimeSpan.FromSeconds(10);
    // 刚开流时自动曝光还没稳定，画面偏暗，丢弃前几帧。
    private const int WarmupFrames = 8;
    // 显示预览时额外检测人脸，只用于实时画框和显示面积，不参与在场判断。
    private static readonly TimeSpan OverlayInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>true：离开后由本程序锁定电脑；false：交还系统电源策略。</summary>
    public bool LockWhenAway { get; set; }
    /// <summary>离开多久后锁定；null 表示延迟一个检测间隔。</summary>
    public TimeSpan? LockDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>人脸框至少占画面多少百分比才算“在电脑前”；远处的人或墙上的照片通常较小。0 表示任何人脸都算。</summary>
    public double MinFaceAreaPercent { get; set; } = 3;

    public event EventHandler<PresenceStatus>? StatusChanged;
    public event EventHandler<PresenceFrame>? FrameReady;
    /// <summary>开始锁定倒计时（延迟大于 0 时）时触发，参数为倒计时长度。</summary>
    public event EventHandler<TimeSpan>? LockCountdownStarted;
    /// <summary>运行中摄像头出错（被拔出、被占用、长时间无画面），参数为给用户的说明。检测已停止。</summary>
    public event EventHandler<string>? CameraFailed;
    /// <summary>检测时发现多张达到阈值的人脸，参数为张数。同一段多人期间只触发一次。</summary>
    public event EventHandler<int>? MultipleFacesDetected;

    private CancellationTokenSource? _cts;
    private uint _intervalSeconds = 60;
    private DateTimeOffset _nextDetection;
    private DateTimeOffset? _awaySince;
    private MediaCapture? _capture;
    private FaceDetector? _detector;
    private MediaFrameReader? _reader;
    private bool _streaming;
    private DateTimeOffset _streamStartedAt;
    private readonly object _frameLock = new();
    private SoftwareBitmap? _latestFrame;
    private long _latestFrameTimestamp, _lastFrameTimestamp;
    private int _framesSinceStart;
    private volatile bool _previewWanted;
    private volatile IReadOnlyList<DetectedFace> _lastFaces = [];
    private DateTimeOffset _lastOverlay;
    private bool _multipleFacesWarned;

    public async Task<StartResult> StartAsync(string cameraId, uint seconds, bool previewWanted, Action<string> report)
    {
        Stop();
        _intervalSeconds = seconds;
        _previewWanted = previewWanted;
        var cts = _cts = new CancellationTokenSource();
        try
        {
            var group = await MediaFrameSourceGroup.FromIdAsync(cameraId);
            if (group is null) return Fail(cts, "所选摄像头当前不可用，请刷新摄像头列表后重新选择。");
            _capture = await OpenAsync(group);
            _capture.Failed += Capture_Failed;
            _detector = await FaceDetector.CreateAsync();
            var source = _capture.FrameSources.Values.FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
                ?? throw new InvalidOperationException("这个摄像头没有彩色画面");
            _reader = await _capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _reader.FrameArrived += Reader_FrameArrived;
            await StartStreamAsync();
            if (cts.IsCancellationRequested) return new(false);
            _nextDetection = DateTimeOffset.UtcNow;
            _ = LoopAsync(report, cts.Token);
            return new(true);
        }
        catch (UnauthorizedAccessException)
        {
            return Fail(cts, "没有摄像头权限。请在 Windows 设置 › 隐私和安全性 › 摄像头 中，开启“摄像头访问”和“允许桌面应用访问你的摄像头”。", permission: true);
        }
        catch (CameraInUseException)
        {
            return Fail(cts, "摄像头可能正被其他程序独占使用");
        }
        catch (Exception ex)
        {
            return Fail(cts, $"无法打开摄像头：{ex.Message}");
        }
    }

    private sealed class CameraInUseException : Exception;

    private StartResult Fail(CancellationTokenSource cts, string error, bool permission = false)
    {
        if (cts.IsCancellationRequested) return new(false);
        Stop();
        return new(false, error, permission);
    }

    // 优先只读共享：本程序只读取画面、不改摄像头设置，共享模式不会占住摄像头，
    // 视频会议、录像软件之后仍能正常打开并控制它。个别不支持共享的驱动才退回独占。
    private static async Task<MediaCapture> OpenAsync(MediaFrameSourceGroup group)
    {
        foreach (var mode in new[] { MediaCaptureSharingMode.SharedReadOnly, MediaCaptureSharingMode.ExclusiveControl })
        {
            var capture = new MediaCapture();
            try
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    SourceGroup = group,
                    SharingMode = mode,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video
                });
                return capture;
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException && mode == MediaCaptureSharingMode.SharedReadOnly)
            {
                capture.Dispose();
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException)
            {
                // 共享和独占都打不开：通常是其他程序占着不放，系统给出的错误信息对用户没有帮助
                capture.Dispose();
                throw new CameraInUseException();
            }
            catch
            {
                capture.Dispose();
                throw;
            }
        }
        throw new InvalidOperationException("unreachable");
    }

    public void SetInterval(uint seconds)
    {
        _intervalSeconds = seconds;
        // 从长间隔切到短间隔时不必等完旧周期
        var next = DateTimeOffset.UtcNow.AddSeconds(seconds);
        if (next < _nextDetection) _nextDetection = next;
    }

    /// <summary>界面上能看到预览时为 true。此时摄像头保持常开；否则只在检测时短暂打开。</summary>
    public void SetPreview(bool wanted)
    {
        _previewWanted = wanted;
        if (_capture is not null) ReportStatus(null);
    }

    private async Task StartStreamAsync()
    {
        if (_reader is null || _streaming) return;
        lock (_frameLock) { _latestFrame?.Dispose(); _latestFrame = null; }
        Interlocked.Exchange(ref _framesSinceStart, 0);
        var status = await _reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success)
            throw new InvalidOperationException($"摄像头无法开始传输画面（{status}）");
        _streaming = true;
        _streamStartedAt = DateTimeOffset.UtcNow;
        ReportStatus(null);
    }

    private async Task StopStreamAsync()
    {
        if (_reader is null || !_streaming) return;
        _streaming = false;
        await _reader.StopAsync();
        lock (_frameLock) { _latestFrame?.Dispose(); _latestFrame = null; }
        ReportStatus(null);
    }

    private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        var now = Stopwatch.GetTimestamp();
        if (Stopwatch.GetElapsedTime(_lastFrameTimestamp, now) < FrameInterval) return;
        _lastFrameTimestamp = now;

        using var frame = sender.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null) return;
        if (Interlocked.Increment(ref _framesSinceStart) <= WarmupFrames) return;
        var copy = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        SoftwareBitmap? preview = null;
        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = copy;
            _latestFrameTimestamp = now;
            if (_previewWanted) preview = SoftwareBitmap.Copy(copy);
        }
        if (preview is null) return;
        FrameReady?.Invoke(this, new PresenceFrame(preview, _lastFaces));
    }

    private void Capture_Failed(MediaCapture sender, MediaCaptureFailedEventArgs args)
    {
        CameraFailed?.Invoke(this, $"摄像头出现错误，已停止检测（{args.Message}）。可能是摄像头被拔出或被其他程序占用。");
    }

    private async Task LoopAsync(Action<string> report, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                if (!_streaming && (_previewWanted || now >= _nextDetection))
                {
                    try { await StartStreamAsync(); }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested) return;
                        KeepAwake(false);
                        CameraFailed?.Invoke(this, $"无法重新打开摄像头，已停止检测（{ex.Message}）。");
                        return;
                    }
                }

                if (now >= _nextDetection)
                {
                    try
                    {
                        // 刚开流时还没有可用的画面，此时不算一次检测，稍后重试
                        if (await DetectLatestFrameAsync() is { } faces)
                        {
                            if (token.IsCancellationRequested) return;
                            var close = faces.Count(f => f.Close);
                            ReportStatus(close > 0 ? $"已检测到人脸（{DescribeFaces(faces)}）"
                                : faces.Count > 0 ? $"人脸太小，视为不在（{DescribeFaces(faces)}）" : "未检测到人脸");
                            if (close >= 2 && !_multipleFacesWarned) MultipleFacesDetected?.Invoke(this, close);
                            _multipleFacesWarned = close >= 2;
                            _nextDetection = HandleResult(close > 0, report);
                        }
                        else if (now - _streamStartedAt > NoFrameTimeout)
                        {
                            KeepAwake(false);
                            CameraFailed?.Invoke(this, "摄像头长时间没有画面，已停止检测。可能是摄像头被拔出、被遮挡开关关闭，或被其他程序占用。");
                            return;
                        }
                    }
                    catch
                    {
                        _nextDetection = DateTimeOffset.UtcNow.AddSeconds(_intervalSeconds);
                        report("状态：本次摄像头检测失败，将在下个周期重试");
                    }
                }
                else if (_streaming && _previewWanted && now - _lastOverlay >= OverlayInterval)
                {
                    _lastOverlay = now;
                    try { await DetectLatestFrameAsync(); } catch { }
                }

                // 省电：看不到预览且离下次检测还早时，关闭摄像头流（指示灯也会熄灭）
                if (_streaming && !_previewWanted && _nextDetection - DateTimeOffset.UtcNow > PauseThreshold)
                    await StopStreamAsync();

                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>根据检测结果设置电源状态，返回下一次检测的时间。</summary>
    private DateTimeOffset HandleResult(bool faceSeen, Action<string> report)
    {
        var now = DateTimeOffset.UtcNow;
        var interval = TimeSpan.FromSeconds(_intervalSeconds);
        if (faceSeen)
        {
            _awaySince = null;
            KeepAwake(true);
            report("状态：检测到你在电脑前，已保持唤醒");
            return now + interval;
        }
        if (!LockWhenAway)
        {
            _awaySince = null;
            KeepAwake(false);
            report("状态：未检测到人脸，已交还系统电源策略");
            return now + interval;
        }

        // 接管模式：锁定之前继续保持唤醒，不让系统超时抢先息屏
        var delay = LockDelay ?? interval;
        if (_awaySince is null)
        {
            _awaySince = now;
            if (delay > TimeSpan.Zero) LockCountdownStarted?.Invoke(this, delay);
        }
        var deadline = _awaySince.Value + delay;
        if (now >= deadline)
        {
            _awaySince = null;
            KeepAwake(false);
            report("状态：未检测到人脸，已锁定电脑");
            LockWorkStation();
            return now + interval;
        }
        KeepAwake(true);
        report($"状态：未检测到人脸，{PowerPolicy.Describe(TimeSpan.FromSeconds(Math.Ceiling((deadline - now).TotalSeconds)))}后锁定");
        var next = now + CountdownRecheck;
        return next < deadline ? next : deadline;
    }

    private static string DescribeFaces(IReadOnlyList<DetectedFace> faces) =>
        faces.Count == 1 ? $"面积 {faces[0].AreaPercent:0.0}%" : $"{faces.Count} 张，最大 {faces.Max(f => f.AreaPercent):0.0}%";

    /// <summary>检测最新一帧中的人脸并更新预览框；没有可用画面时返回 null。</summary>
    private async Task<IReadOnlyList<DetectedFace>?> DetectLatestFrameAsync()
    {
        var detector = _detector;
        if (detector is null) return null;
        SoftwareBitmap? bitmap = null;
        lock (_frameLock)
        {
            // 过期的帧（例如摄像头卡住）不能用来判断，否则可能一直按旧画面保持唤醒
            if (_latestFrame is not null && Stopwatch.GetElapsedTime(_latestFrameTimestamp) < StaleFrame)
                // FaceDetector 只支持 Gray8 / Nv12，直接传 Bgra8 会抛异常导致永远检测失败。
                bitmap = SoftwareBitmap.Convert(_latestFrame, BitmapPixelFormat.Gray8);
        }
        if (bitmap is null) return null;
        using (bitmap)
        {
            var frameArea = (double)bitmap.PixelWidth * bitmap.PixelHeight;
            var threshold = MinFaceAreaPercent;
            var faces = (await detector.DetectFacesAsync(bitmap))
                .Select(f =>
                {
                    var b = f.FaceBox;
                    var percent = b.Width * b.Height / frameArea * 100;
                    return new DetectedFace(new Windows.Foundation.Rect(b.X, b.Y, b.Width, b.Height), percent, percent >= threshold);
                })
                .OrderByDescending(f => f.AreaPercent)
                .ToList();
            _lastFaces = faces;
            return faces;
        }
    }

    private string? _lastFaceMessage;

    private void ReportStatus(string? faceMessage)
    {
        if (faceMessage is not null) _lastFaceMessage = faceMessage;
        var camera = !_streaming ? "待机（两次检测之间关闭以省电）"
            : _previewWanted ? "工作中，预览中" : "工作中（检测完成后关闭）";
        StatusChanged?.Invoke(this, new(camera, _lastFaceMessage ?? "等待检测"));
    }

    // SetThreadExecutionState 作用于调用线程；LoopAsync 由 UI 线程启动，续体也回到 UI 线程，
    // 因此设置和清除都发生在同一线程上。
    private static void KeepAwake(bool keep)
    {
        SetThreadExecutionState(keep ? EsContinuous | EsDisplayRequired : EsContinuous);
    }

    public void Stop()
    {
        _cts?.Cancel(); _cts?.Dispose(); _cts = null;
        if (_reader is not null) { _reader.FrameArrived -= Reader_FrameArrived; _reader.Dispose(); _reader = null; }
        if (_capture is not null) { _capture.Failed -= Capture_Failed; _capture.Dispose(); _capture = null; }
        _detector = null;
        _streaming = false;
        lock (_frameLock) { _latestFrame?.Dispose(); _latestFrame = null; }
        _lastFaces = [];
        _lastFaceMessage = null;
        _awaySince = null;
        _multipleFacesWarned = false;
        StatusChanged?.Invoke(this, new("已关闭", "未检测"));
        KeepAwake(false);
    }

    public void Dispose() => Stop();

    private const uint EsContinuous = 0x80000000;
    private const uint EsDisplayRequired = 0x00000002;
    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();
}
