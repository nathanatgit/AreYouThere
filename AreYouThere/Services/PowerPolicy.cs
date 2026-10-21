using System.Diagnostics;
using System.Text.Json;

namespace AreYouThere.Services;

/// <summary>时长的显示文字。</summary>
internal static class PowerPolicy
{
    /// <summary>时长文字。中文直接嵌在句中（“每 1 分钟检测一次”），英文同理（“every 1 minute”）。</summary>
    public static string Describe(TimeSpan span) => L.Zh
        ? span.TotalSeconds switch
        {
            <= 0 => "立即",
            < 60 => $"{span.TotalSeconds:0} 秒",
            < 3600 => span.Seconds == 0 ? $"{span.TotalMinutes:0} 分钟" : $"{span.Minutes} 分 {span.Seconds} 秒",
            _ => span.Minutes == 0 ? $"{span.TotalHours:0} 小时" : $"{span.Hours} 小时 {span.Minutes} 分钟",
        }
        : span.TotalSeconds switch
        {
            <= 0 => "immediately",
            < 60 => Plural(span.TotalSeconds, "second"),
            < 3600 => span.Seconds == 0 ? Plural(span.TotalMinutes, "minute") : $"{span.Minutes} min {span.Seconds} s",
            _ => span.Minutes == 0 ? Plural(span.TotalHours, "hour") : $"{span.Hours} h {span.Minutes} min",
        };

    private static string Plural(double value, string unit) => $"{value:0} {unit}{(Math.Round(value) == 1 ? "" : "s")}";
}

/// <summary>
/// 检测 PowerToys Awake：它也通过电源请求保持唤醒，开启时会让“离开后息屏/睡眠”失效。
/// </summary>
internal static class PowerToysAwake
{
    /// <summary>
    /// 『在吗』离开后照常锁定，Awake 不影响锁定；但 Awake 保持屏幕常亮时，锁定后屏幕也不会息屏，需要提示。
    /// 没有需要提示的情况时返回 null。
    /// </summary>
    public static string? Check()
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName("PowerToys.Awake"); }
        catch { return null; }
        if (processes.Length == 0) return null;
        foreach (var p in processes) p.Dispose();

        int mode; bool keepDisplayOn;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "PowerToys", "Awake", "settings.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var props = doc.RootElement.GetProperty("properties");
            mode = props.GetProperty("mode").GetInt32();
            keepDisplayOn = props.TryGetProperty("keepDisplayOn", out var k) && k.GetBoolean();
        }
        catch
        {
            // 读不到 Awake 的设置：不知道它当前的模式，按可能的情况提示
            return L.T("检测到 PowerToys Awake 正在运行。如果它设置为保持屏幕常亮，即使『在吗』锁定了电脑，屏幕也不会息屏。",
                "PowerToys Awake is running. If it keeps the screen on, the screen won't turn off even after AreYouThere locks the PC.");
        }

        // Awake 模式：0 被动（使用电源计划）、1 无限期、2 定时、3 保持到指定时间。只让电脑不睡眠时不必提示
        var modeText = mode switch
        {
            1 => L.T("无限期保持唤醒", "Keep awake indefinitely"),
            2 => L.T("定时保持唤醒", "Keep awake for a time interval"),
            3 => L.T("保持唤醒到指定时间", "Keep awake until expiration"),
            _ => null
        };
        if (modeText is null || !keepDisplayOn) return null;
        return L.T($"PowerToys Awake 当前为“{modeText}”模式并保持屏幕常亮：『在吗』仍会在你离开后锁定电脑，但锁屏后不会息屏。",
            $"PowerToys Awake is set to \"{modeText}\" and keeps the screen on. AreYouThere still locks the PC after you leave, but the screen won't turn off after locking.");
    }
}
