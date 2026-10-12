using System.Text.Json;
using System.Text.Json.Serialization;

namespace AreYouThere.Services;

/// <summary>保存在 %LOCALAPPDATA%\AreYouThere\settings.json 的用户设置。不保存“是否启用”，避免启动即开摄像头。</summary>
internal sealed class AppSettings
{
    /// <summary>MediaFrameSourceGroup.Id；为空或已拔出时使用第一个可用摄像头。</summary>
    public string? CameraId { get; set; }
    public uint IntervalSeconds { get; set; } = 60;
    public bool PreviewEnabled { get; set; } = true;
    public bool LockWhenAway { get; set; }
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
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, AppSettingsJsonContext.Default.AppSettings));
        }
        catch { }
    }
}

// 源生成序列化，发布时开启裁剪也能正常工作
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
