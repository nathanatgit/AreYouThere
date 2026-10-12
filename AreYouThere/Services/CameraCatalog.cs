using Windows.Devices.Enumeration;
using Windows.Media.Capture.Frames;

namespace AreYouThere.Services;

internal sealed record CameraInfo(string Id, string Label);

/// <summary>
/// 枚举可用的彩色摄像头，并在插拔摄像头时发出通知。
/// 有些设备上摄像头名称是空的或很笼统（如 “USB Video Device”），所以标签里同时带上编号和前置/后置信息。
/// </summary>
internal sealed class CameraCatalog : IDisposable
{
    private readonly DeviceWatcher _watcher;
    private bool _enumerated;

    /// <summary>摄像头被插入或移除（可能在后台线程触发）。</summary>
    public event EventHandler? Changed;

    public CameraCatalog()
    {
        _watcher = DeviceInformation.CreateWatcher(DeviceClass.VideoCapture);
        _watcher.Added += (_, _) => { if (_enumerated) Changed?.Invoke(this, EventArgs.Empty); };
        _watcher.Removed += (_, _) => { if (_enumerated) Changed?.Invoke(this, EventArgs.Empty); };
        _watcher.Updated += (_, _) => { };
        _watcher.EnumerationCompleted += (_, _) => _enumerated = true;
        _watcher.Start();
    }

    public static async Task<IReadOnlyList<CameraInfo>> FindAsync()
    {
        var cameras = new List<CameraInfo>();
        foreach (var group in await MediaFrameSourceGroup.FindAllAsync())
        {
            // 只保留有彩色画面的摄像头；Windows Hello 的红外摄像头不能用于人脸检测
            var color = group.SourceInfos.FirstOrDefault(s => s.SourceKind == MediaFrameSourceKind.Color);
            if (color is null) continue;
            var name = string.IsNullOrWhiteSpace(group.DisplayName) ? "未命名摄像头" : group.DisplayName.Trim();
            var panel = color.DeviceInformation?.EnclosureLocation?.Panel switch
            {
                Windows.Devices.Enumeration.Panel.Front => "（前置）",
                Windows.Devices.Enumeration.Panel.Back => "（后置）",
                _ => ""
            };
            cameras.Add(new CameraInfo(group.Id, $"摄像头 {cameras.Count + 1}：{name}{panel}"));
        }
        return cameras;
    }

    public void Dispose()
    {
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) _watcher.Stop();
    }
}
