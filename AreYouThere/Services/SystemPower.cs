using System.Runtime.InteropServices;

namespace AreYouThere.Services;

/// <summary>Windows 11 设置 › 电源 里的“电源模式”。</summary>
internal enum PowerMode { Efficiency, Balanced, Performance }

/// <summary>LockScreenOff：锁定后多久关闭屏幕（锁定时 Windows 用它，而不是平时的“关闭屏幕”时间）；Sleep：空闲多久后睡眠。</summary>
internal enum PowerTimeout { LockScreenOff, Sleep }

/// <summary>
/// 读写 Windows 的电源模式、锁定后息屏时间和睡眠时间，都不需要管理员权限。
/// 电源模式和睡眠时间与“设置 › 系统 › 电源”页面是同一份设置；时间写在当前电源计划里（设置页面改的也是它）。
/// 电源模式分“已接通电源/使用电池”两个值，用 Windows 11 设置页面所用的 powrprof 接口；
/// Windows 10 没有这些接口，或当前电源计划不是“平衡”（此时 Windows 自己也不提供电源模式）时不可用。
/// </summary>
internal static class SystemPower
{
    private static readonly Guid BalancedScheme = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoConLock = new("8ec4b3a5-6868-48c2-be75-4f3044be88a7");
    private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    private static readonly Dictionary<PowerMode, Guid> ModeGuids = new()
    {
        [PowerMode.Efficiency] = new("961cc777-2547-4f9d-8174-7d86181b8a7a"),
        [PowerMode.Balanced] = Guid.Empty,
        [PowerMode.Performance] = new("ded574b5-45a0-4f42-8737-46345c09c238"),
    };

    /// <summary>有电池（笔记本）时才分“已接通电源/使用电池”；台式机只有接通电源。</summary>
    public static bool HasBattery => GetSystemPowerStatus(out var s) && (s.BatteryFlag & 128) == 0;
    public static bool OnBattery => GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    /// <summary>
    /// 现代待机（S0 低电量待机）且能休眠：睡眠一段时间后 Windows 可能自动转入休眠。
    /// 读 SYSTEM_POWER_CAPABILITIES 的 AoAc（偏移 20）、SystemS4（6）、HiberFilePresent（8）。
    /// </summary>
    public static bool ModernStandbyMayHibernate
    {
        get
        {
            var caps = new byte[128];
            return GetPwrCapabilities(caps) && caps[20] != 0 && caps[6] != 0 && caps[8] != 0;
        }
    }

    /// <summary>电源模式能否设置：有 Windows 11 的接口，且当前电源计划是“平衡”。</summary>
    public static bool PowerModeSupported
    {
        get
        {
            try { return ActiveScheme() == BalancedScheme && PowerGetUserConfiguredACPowerMode(out _) == 0; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    /// <summary>当前设置的电源模式；不支持或是厂商自定义的模式时返回 null。</summary>
    public static PowerMode? GetPowerMode(bool battery)
    {
        try
        {
            var result = battery ? PowerGetUserConfiguredDCPowerMode(out var guid) : PowerGetUserConfiguredACPowerMode(out guid);
            if (result != 0) return null;
            foreach (var (mode, g) in ModeGuids) if (g == guid) return mode;
        }
        catch (EntryPointNotFoundException) { }
        return null;
    }

    public static bool SetPowerMode(bool battery, PowerMode mode)
    {
        var guid = ModeGuids[mode];
        try { return (battery ? PowerSetUserConfiguredDCPowerMode(ref guid) : PowerSetUserConfiguredACPowerMode(ref guid)) == 0; }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>当前电源计划里的超时秒数，0 为从不；读取失败返回 null。</summary>
    public static uint? GetTimeout(PowerTimeout kind, bool battery)
    {
        if (ActiveScheme() is not { } scheme) return null;
        var (sub, setting) = Ids(kind);
        var result = battery
            ? PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out var value)
            : PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, out value);
        return result == 0 ? value : null;
    }

    /// <summary>写入当前电源计划并重新激活，立即生效。</summary>
    public static bool SetTimeout(PowerTimeout kind, bool battery, uint seconds)
    {
        if (ActiveScheme() is not { } scheme) return false;
        var (sub, setting) = Ids(kind);
        var result = battery
            ? PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, seconds)
            : PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref setting, seconds);
        return result == 0 && PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;
    }

    private static (Guid Sub, Guid Setting) Ids(PowerTimeout kind) =>
        kind == PowerTimeout.LockScreenOff ? (SubVideo, VideoConLock) : (SubSleep, StandbyIdle);

    private static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr mem);
    [DllImport("powrprof.dll")] private static extern bool GetPwrCapabilities(byte[] capabilities);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr rootKey, out IntPtr activeScheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr rootKey, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadACValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteACValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr rootKey, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    // Windows 11 设置页面用的电源模式接口（powrprof 导出，未写入官方文档）；Windows 10 上不存在
    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredACPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerGetUserConfiguredDCPowerMode(out Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredACPowerMode(ref Guid mode);
    [DllImport("powrprof.dll")] private static extern uint PowerSetUserConfiguredDCPowerMode(ref Guid mode);
}

/// <summary>离开后的电源策略：每项为 null 表示不修改。</summary>
internal sealed record AwayPower(PowerMode? ModeAc, PowerMode? ModeDc, uint? ScreenOffAc, uint? SleepAc, uint? ScreenOffDc, uint? SleepDc);

/// <summary>
/// 电脑锁定时应用离开后的电源策略，解锁后恢复。修改前先把各项原值写到本机文件，恢复成功后删除：
/// 程序崩溃、被结束或在锁定状态下关机，下次启动时调用 Restore 即可恢复，不会一直留着离开时的设置。
/// </summary>
internal static class PowerOverride
{
    private static readonly string RestoreFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AreYouThere", "power-restore.txt");

    /// <summary>应用 away 里选了具体值、且与当前不同的项。返回是否全部成功；记不下原值时什么也不改。</summary>
    public static bool Apply(AwayPower away)
    {
        if (File.Exists(RestoreFile)) return true; // 上次的原值还没恢复：不能用离开时的值覆盖它
        var modes = SystemPower.PowerModeSupported;
        var changes = new List<(string Key, uint Old, Func<bool> Write)>();
        void Mode(string key, bool battery, PowerMode? wanted)
        {
            if (wanted is { } m && modes && SystemPower.GetPowerMode(battery) is { } old && old != m)
                changes.Add((key, (uint)old, () => SystemPower.SetPowerMode(battery, m)));
        }
        void Timeout(string key, PowerTimeout kind, bool battery, uint? wanted)
        {
            if (wanted is { } v && SystemPower.GetTimeout(kind, battery) is { } old && old != v)
                changes.Add((key, old, () => SystemPower.SetTimeout(kind, battery, v)));
        }
        Mode("ModeAc", false, away.ModeAc);
        Mode("ModeDc", true, away.ModeDc);
        Timeout("ScreenOffAc", PowerTimeout.LockScreenOff, false, away.ScreenOffAc);
        Timeout("SleepAc", PowerTimeout.Sleep, false, away.SleepAc);
        Timeout("ScreenOffDc", PowerTimeout.LockScreenOff, true, away.ScreenOffDc);
        Timeout("SleepDc", PowerTimeout.Sleep, true, away.SleepDc);
        if (changes.Count == 0) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RestoreFile)!);
            File.WriteAllLines(RestoreFile, changes.Select(c => $"{c.Key}={c.Old}"));
        }
        catch { return false; }
        var ok = true;
        foreach (var c in changes) ok &= c.Write();
        return ok;
    }

    /// <summary>恢复修改前的值。全部恢复成功才删除文件，否则留到下次再试。没有改过时什么也不做。</summary>
    public static bool Restore()
    {
        if (!File.Exists(RestoreFile)) return true;
        var ok = true;
        try
        {
            foreach (var line in File.ReadAllLines(RestoreFile))
            {
                var p = line.Split('=');
                if (p.Length != 2 || !uint.TryParse(p[1], out var v)) continue;
                ok &= p[0] switch
                {
                    "ModeAc" => SystemPower.SetPowerMode(false, (PowerMode)v),
                    "ModeDc" => SystemPower.SetPowerMode(true, (PowerMode)v),
                    "ScreenOffAc" => SystemPower.SetTimeout(PowerTimeout.LockScreenOff, false, v),
                    "SleepAc" => SystemPower.SetTimeout(PowerTimeout.Sleep, false, v),
                    "ScreenOffDc" => SystemPower.SetTimeout(PowerTimeout.LockScreenOff, true, v),
                    "SleepDc" => SystemPower.SetTimeout(PowerTimeout.Sleep, true, v),
                    _ => true,
                };
            }
            if (ok) File.Delete(RestoreFile);
        }
        catch { return false; }
        return ok;
    }
}
