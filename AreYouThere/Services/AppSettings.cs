using System.Text.Json;
using System.Text.Json.Serialization;

namespace AreYouThere.Services;

/// <summary>本地用户设置。不保存检测开关；手动启动默认关闭，登录启动仅在明确同意后自动检测。</summary>
internal sealed class AppSettings
{
    /// <summary>MediaFrameSourceGroup.Id；为空或已拔出时使用第一个可用摄像头。</summary>
    public string? CameraId { get; set; }
    public uint IntervalSeconds { get; set; } = 60;
    public bool PreviewEnabled { get; set; } = true;
    /// <summary>默认离开后锁定：本工具的目的就是人不在时不让屏幕暴露给别人。</summary>
    public bool LockWhenAway { get; set; } = true;
    /// <summary>人脸框至少占画面的百分比，才算“在电脑前”。</summary>
    public double MinFaceAreaPercent { get; set; } = 3;
    public bool StartupConsent { get; set; }
    public bool SkipStartupReminder { get; set; }
    /// <summary>离开后多少秒锁定；-1 表示延迟一个检测间隔。</summary>
    public int LockDelaySeconds { get; set; } = 30;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AreYouThere", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize(File.ReadAllText(FilePath), AppSettingsJsonContext.Default.AppSettings) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try { SaveOrThrow(); }
        catch { }
    }

    public void SaveOrThrow()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings));
    }
}

// 源生成序列化，发布时开启裁剪也能正常工作
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
