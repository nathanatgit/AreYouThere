namespace AreYouThere.Services;

/// <summary>检测暂停的原因。可以同时有多个；全部解除后自动恢复检测。</summary>
[Flags]
internal enum PauseReason
{
    None = 0,
    /// <summary>电脑已锁定：锁屏期间不开摄像头，也不能保持唤醒（否则锁屏界面会一直亮着）。</summary>
    SessionLocked = 1,
    /// <summary>节电模式开启，且设置为节电模式下暂停。</summary>
    BatterySaver = 2,
    /// <summary>用户从托盘或窗口选择“暂停一段时间”。</summary>
    User = 4,
}

/// <summary>启动检测所需的参数，由界面按当前设置提供；返回 null 表示没有可用的摄像头。</summary>
internal sealed record StartRequest(string CameraId, uint IntervalSeconds, bool PreviewWanted, int Rotation);

/// <summary>
/// 决定在场检测此刻该不该运行：用户打开了开关（Enabled），且没有任何暂停原因（Paused）时才运行。
/// 原因的增减和开关变化都只调用这里，由 ReconcileAsync 统一开启或停止 PresenceService，
/// 界面不再自己记“是不是因为锁屏/节电模式停的”。只在 UI 线程上使用。
/// </summary>
internal sealed class DetectionController(PresenceService presence, Func<StartRequest?> request, Action<string> report)
{
    public PresenceService Presence { get; } = presence;

    /// <summary>用户是否打开了“启用在场检测”。暂停期间仍为 true，暂停解除后自动恢复。</summary>
    public bool Enabled { get; private set; }
    public PauseReason Paused { get; private set; }
    /// <summary>正在打开摄像头；期间开关应禁用，防止重复启动。</summary>
    public bool Starting { get; private set; }
    /// <summary>PresenceService 已成功启动并在检测。</summary>
    public bool Running { get; private set; }

    /// <summary>状态变化（开关、暂停原因、启动中、运行中）后触发，界面据此刷新开关和状态文字。</summary>
    public event Action? StateChanged;
    /// <summary>启动失败时触发；检测已自动关闭。参数为 PresenceService 的结果（含错误说明）。</summary>
    public event Action<StartResult>? StartFailed;
    /// <summary>成功开始检测（包括暂停解除后的恢复）时触发。</summary>
    public event Action? Started;

    private bool ShouldRun => Enabled && Paused == PauseReason.None;

    public Task SetEnabledAsync(bool enabled)
    {
        if (Enabled == enabled) return Task.CompletedTask;
        Enabled = enabled;
        // 关闭检测时，手动暂停一并取消；锁屏、节电是外部状态，保留到它们自己解除
        if (!enabled) Paused &= ~PauseReason.User;
        return ReconcileAsync();
    }

    public Task SetPausedAsync(PauseReason reason, bool paused)
    {
        var next = paused ? Paused | reason : Paused & ~reason;
        if (next == Paused) return Task.CompletedTask;
        Paused = next;
        return ReconcileAsync();
    }

    /// <summary>用新设置重新打开摄像头（例如换了摄像头）；不该运行时什么也不做。</summary>
    public async Task RestartAsync()
    {
        if (!Running) return;
        Stop();
        await ReconcileAsync();
    }

    private void Stop()
    {
        Presence.Stop();
        Running = false;
    }

    /// <summary>
    /// 让 PresenceService 的状态跟上“该不该运行”。打开摄像头要等待，期间状态可能又变了，
    /// 所以每次等待之后都重新判断，直到稳定为止。
    /// </summary>
    private async Task ReconcileAsync()
    {
        if (Starting)
        {
            // 正在打开摄像头时被暂停或关闭：Stop 让 StartAsync 以“已取消”返回，由下面的循环重新判断
            if (!ShouldRun) Presence.Stop();
            StateChanged?.Invoke();
            return;
        }
        while (true)
        {
            if (!ShouldRun) { if (Running) Stop(); break; }
            if (Running) break;
            if (request() is not { } r) { Enabled = false; break; }

            Starting = true;
            StateChanged?.Invoke();
            report(L.T("正在打开摄像头…", "Opening the camera…"));
            Presence.Rotation = r.Rotation;
            var result = await Presence.StartAsync(r.CameraId, r.IntervalSeconds, r.PreviewWanted, report);
            Starting = false;
            if (result.Started)
            {
                Running = true;
                if (ShouldRun) Started?.Invoke();
                continue; // 启动途中又被暂停或关闭时，下一轮会停下
            }
            if (result.Error is not null)
            {
                // 真正失败了：关闭开关，告诉用户原因
                Enabled = false;
                StartFailed?.Invoke(result);
                break;
            }
            // 没有错误说明就是被取消（启动途中暂停或关闭），下一轮重新判断
        }
        StateChanged?.Invoke();
    }

    /// <summary>运行中摄像头出错，PresenceService 已停止：关闭开关。</summary>
    public void OnCameraFailed()
    {
        Stop();
        Enabled = false;
        StateChanged?.Invoke();
    }
}
