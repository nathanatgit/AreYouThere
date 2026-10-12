using System.Text.Json;
using System.Text.Json.Serialization;

namespace AreYouThere.Services;

/// <summary>本地用户设置。不保存检测开关；手动启动默认关闭，登录启动仅在明确同意后自动检测。</summary>
internal sealed class AppSettings
{
    /// <summary>MediaFrameSourceGroup.Id；为空或已拔出时使用第一个可用摄像头。</summary>
    public string? CameraId { get; set; }
    public uint IntervalSeconds { get; set; } = 30;
    /// <summary>智能检测：只在停止键鼠操作满一个检测间隔后才用摄像头检测。默认开启；关闭时按固定间隔检测。</summary>
    public bool SmartDetection { get; set; } = true;
    /// <summary>坐姿检测：人脸中心低于坐姿线时提醒坐直，人脸面积超过上限时提醒离屏幕远一些。</summary>
    public bool PostureCheck { get; set; }
    /// <summary>坐姿线离画面底部的高度（百分比）。</summary>
    public double PostureLineFromBottomPercent { get; set; } = 40;
    /// <summary>人脸面积超过这个百分比时提醒离屏幕远一些。</summary>
    public double PostureMaxAreaPercent { get; set; } = 20;
    /// <summary>true：只在在场检测时顺便看坐姿；false：按 PostureIntervalSeconds 单独检测坐姿。</summary>
    public bool PostureFollowDetection { get; set; }
    public uint PostureIntervalSeconds { get; set; } = 600;
    /// <summary>每个摄像头的画面旋转角度（顺时针 0/90/180/270），键为 CameraId。</summary>
    public Dictionary<string, int> CameraRotations { get; set; } = [];
    public bool PreviewEnabled { get; set; } = true;
    /// <summary>默认离开后锁定：本工具的目的就是人不在时不让屏幕暴露给别人。</summary>
    public bool LockWhenAway { get; set; } = true;
    /// <summary>人脸框至少占画面的百分比，才算“在电脑前”。</summary>
    public double MinFaceAreaPercent { get; set; } = 8;
    /// <summary>用户关掉了顶部的“完全离线”说明 / 隐私提醒。同样的提醒在开机自启动的确认和提醒对话框里仍会出现。</summary>
    public bool HideOfflineNotice { get; set; }
    public bool HidePrivacyNotice { get; set; }
    public bool StartupConsent { get; set; }
    public bool SkipStartupReminder { get; set; }
    /// <summary>托盘通知（坐姿提醒、锁定倒计时等）何时显示，默认全屏应用在前台时不显示。</summary>
    public NotificationMode Notifications { get; set; }
    /// <summary>界面主题：跟随系统 / 浅色 / 深色。</summary>
    public AppTheme Theme { get; set; }
    /// <summary>界面语言："zh" / "en"；null 表示跟随 Windows 显示语言。</summary>
    public string? Language { get; set; }
    /// <summary>离开后多少秒锁定；-1 表示延迟一个检测间隔。</summary>
    public int LockDelaySeconds { get; set; } = 10;
    /// <summary>选“自定义”时的锁定延迟（秒），切换到其他选项后仍保留，方便切回来。</summary>
    public uint LockDelayCustomSeconds { get; set; } = 10;

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

internal enum NotificationMode { QuietWhenFullscreen, Never, Always }
internal enum AppTheme { System, Light, Dark }

// 源生成序列化，发布时开启裁剪也能正常工作
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class AppSettingsJsonContext : JsonSerializerContext;
