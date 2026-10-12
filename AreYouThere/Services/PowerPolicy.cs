using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace AreYouThere.Services;

/// <summary>当前电源计划中与“离开后”相关的超时设置，单位秒，0 表示从不。</summary>
internal sealed record PowerPolicy(bool OnBattery, uint DisplayOffSeconds, uint SleepSeconds, uint? ScreenSaverLockSeconds)
{
    private static Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    public static PowerPolicy Read()
    {
        var onBattery = GetSystemPowerStatus(out var status) && status.ACLineStatus == 0;
        uint display = 0, sleep = 0;
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) == 0)
        {
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                display = ReadValue(scheme, SubVideo, VideoIdle, onBattery);
                sleep = ReadValue(scheme, SubSleep, StandbyIdle, onBattery);
            }
            finally { LocalFree(schemePtr); }
        }

        uint? screenSaverLock = null;
        if (SystemParametersInfo(SPI_GETSCREENSAVEACTIVE, 0, out var active, 0) && active != 0
            && SystemParametersInfo(SPI_GETSCREENSAVESECURE, 0, out var secure, 0) && secure != 0
            && SystemParametersInfo(SPI_GETSCREENSAVETIMEOUT, 0, out var timeout, 0))
            screenSaverLock = (uint)timeout;

        return new PowerPolicy(onBattery, display, sleep, screenSaverLock);
    }

    private static uint ReadValue(Guid scheme, Guid subgroup, Guid setting, bool onBattery)
    {
        uint value;
        var result = onBattery
            ? PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value)
            : PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, out value);
        return result == 0 ? value : 0;
    }

    public static string Describe(TimeSpan span) => span.TotalSeconds switch
    {
        <= 0 => "立即",
        < 60 => $"{span.TotalSeconds:0} 秒",
        < 3600 => span.Seconds == 0 ? $"{span.TotalMinutes:0} 分钟" : $"{span.Minutes} 分 {span.Seconds} 秒",
        _ => span.Minutes == 0 ? $"{span.TotalHours:0} 小时" : $"{span.Hours} 小时 {span.Minutes} 分钟",
    };

    public static string DescribeTimeout(uint seconds) => seconds == 0 ? "从不" : Describe(TimeSpan.FromSeconds(seconds)) + "后";

    private const uint SPI_GETSCREENSAVETIMEOUT = 0x000E, SPI_GETSCREENSAVEACTIVE = 0x0010, SPI_GETSCREENSAVESECURE = 0x0076;
    [StructLayout(LayoutKind.Sequential)] private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr activeScheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint param, out int value, uint winIni);
}

/// <summary>
/// 检测 PowerToys Awake：它也通过电源请求保持唤醒，开启时会让“离开后息屏/睡眠”失效。
/// </summary>
internal static class PowerToysAwake
{
    /// <summary>没有冲突时返回 null，否则返回给用户的提示。</summary>
    public static string? CheckConflict()
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
            return "检测到 PowerToys Awake 正在运行。如果它处于保持唤醒状态，可能与「在吗」冲突。";
        }

        // Awake 模式：0 被动（使用电源计划）、1 无限期、2 定时、3 保持到指定时间
        var modeText = mode switch { 1 => "无限期保持唤醒", 2 => "定时保持唤醒", 3 => "保持唤醒到指定时间", _ => null };
        if (modeText is null) return null;
        var effect = keepDisplayOn
            ? "屏幕会一直保持常亮，你离开后也不会息屏。"
            : "电脑不会进入睡眠，依赖睡眠的自动锁屏可能不会发生。";
        return $"PowerToys Awake 当前为「{modeText}」模式：{effect}这可能与「在吗」冲突，建议在 PowerToys 中把 Awake 改回使用电源计划（被动）模式。";
    }
}
