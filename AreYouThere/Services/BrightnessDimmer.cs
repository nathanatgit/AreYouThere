using System.Runtime.InteropServices;

namespace AreYouThere.Services;

/// <summary>
/// 用显示器自身的亮度调暗屏幕（DimOverlay 的另一种方式）：笔记本内置屏走显卡驱动的亮度接口（\\.\LCD），
/// 外接显示器走 DDC/CI。调不了亮度的显示器由调用方改用 DimOverlay 遮罩，保证每个屏幕都会变暗。
/// 调暗前把原亮度写到本机文件，恢复后删除：程序中途崩溃或被结束，下次启动时 RestoreLeftover 会恢复，屏幕不会一直暗着。
/// DDC/CI 每条命令要几十到几百毫秒，所有读写都在后台线程上依次进行。
/// </summary>
internal sealed class BrightnessDimmer
{
    private static readonly string RestoreFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AreYouThere", "brightness-restore.txt");
    private const string LcdKey = "LCD";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _dimmed;

    /// <summary>能调亮度的显示器数和显示器总数（按 Windows 里的显示器计），用于设置里的说明。只读，不改亮度。</summary>
    public Task<(int Controllable, int Total)> CountAsync() => Run(() =>
    {
        var total = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (_, _, _, _) => { total++; return true; }, IntPtr.Zero);
        return (Controllable().Count, total);
    }, (0, 0));

    /// <summary>
    /// 按 level（0–1，越大越暗）把每个能调的显示器调到“原亮度 ×（1 − level）”。
    /// 返回调暗了的显示器（显示输出名，如 \\.\DISPLAY1），其余显示器由调用方用遮罩调暗。
    /// 已经调暗时再次调用只改目标亮度，原亮度仍按第一次记下的恢复。
    /// </summary>
    public Task<IReadOnlySet<string>> DimAsync(double level) => Run<IReadOnlySet<string>>(() =>
    {
        var saved = _dimmed ? Load() : Snapshot();
        if (saved.Count == 0) return Empty;
        if (!_dimmed)
        {
            Save(saved);
            _dimmed = true;
        }
        var factor = 1 - Math.Clamp(level, 0, 1);
        if (saved.FirstOrDefault(s => s.Key == LcdKey) is { } lcd)
            SetLcd((byte)Math.Round(lcd.Ac * factor), (byte)Math.Round(lcd.Dc * factor));
        ForEachDdc((key, handle, _) =>
        {
            if (saved.FirstOrDefault(s => s.Key == key) is { } s)
                SetMonitorBrightness(handle, (uint)Math.Round(s.Min + (s.Ac - s.Min) * factor));
        });
        return Controllable();
    }, Empty);

    /// <summary>恢复调暗前的亮度。没有调暗时什么也不做。</summary>
    public Task RestoreAsync() => Run(() => { RestoreCore(); return true; }, false);

    /// <summary>同步恢复，用于退出程序时。</summary>
    public void Restore()
    {
        _gate.Wait();
        try { RestoreCore(); } finally { _gate.Release(); }
    }

    /// <summary>上次运行调暗后没来得及恢复（崩溃、被结束、断电）：启动时恢复。</summary>
    public static void RestoreLeftover()
    {
        if (!File.Exists(RestoreFile)) return;
        try { Apply(Load()); } catch { }
        try { File.Delete(RestoreFile); } catch { }
    }

    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    private void RestoreCore()
    {
        if (!_dimmed && !File.Exists(RestoreFile)) return;
        try { Apply(Load()); } catch { }
        try { File.Delete(RestoreFile); } catch { }
        _dimmed = false;
    }

    private static void Apply(List<Saved> saved)
    {
        if (saved.FirstOrDefault(s => s.Key == LcdKey) is { } lcd) SetLcd((byte)lcd.Ac, (byte)lcd.Dc);
        ForEachDdc((key, handle, _) =>
        {
            if (saved.FirstOrDefault(s => s.Key == key) is { } s) SetMonitorBrightness(handle, s.Ac);
        });
    }

    private Task<T> Run<T>(Func<T> action, T fallback) => Task.Run(() =>
    {
        _gate.Wait();
        try { return action(); }
        catch { return fallback; }
        finally { _gate.Release(); }
    });

    /// <summary>能调亮度的显示器：支持 DDC/CI 的，加上能用 \\.\LCD 调的内置屏。</summary>
    private static HashSet<string> Controllable()
    {
        var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ForEachDdc((key, _, ok) => { if (ok) devices.Add(key[..key.LastIndexOf('/')]); });
        if (ReadLcd() is not null) devices.UnionWith(InternalDisplays());
        return devices;
    }

    /// <summary>记下每个能调的显示器当前亮度。LCD 记交流/电池两个值；DDC/CI 记当前值和最小值。</summary>
    private static List<Saved> Snapshot()
    {
        var list = new List<Saved>();
        if (ReadLcd() is { } lcd) list.Add(new(LcdKey, lcd.Ac, lcd.Dc, 0));
        ForEachDdc((key, handle, ok) =>
        {
            if (ok && GetMonitorBrightness(handle, out var min, out var cur, out _)) list.Add(new(key, cur, cur, min));
        });
        return list;
    }

    // 恢复文件每行一个显示器：键、交流亮度、电池亮度、最小亮度，用制表符分隔
    private sealed record Saved(string Key, uint Ac, uint Dc, uint Min);

    private static void Save(List<Saved> saved)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RestoreFile)!);
        File.WriteAllLines(RestoreFile, saved.Select(s => $"{s.Key}\t{s.Ac}\t{s.Dc}\t{s.Min}"));
    }

    private static List<Saved> Load()
    {
        var list = new List<Saved>();
        if (!File.Exists(RestoreFile)) return list;
        foreach (var line in File.ReadAllLines(RestoreFile))
        {
            var p = line.Split('\t');
            if (p.Length == 4 && uint.TryParse(p[1], out var ac) && uint.TryParse(p[2], out var dc) && uint.TryParse(p[3], out var min))
                list.Add(new(p[0], ac, dc, min));
        }
        return list;
    }

    #region 内置屏：\\.\LCD

    private static (uint Ac, uint Dc)? ReadLcd()
    {
        var lcd = OpenLcd();
        if (lcd == InvalidHandle) return null;
        try
        {
            var buffer = new byte[3];
            return DeviceIoControl(lcd, IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, null, 0, buffer, 3, out _, IntPtr.Zero)
                ? (buffer[1], buffer[2]) : null;
        }
        finally { CloseHandle(lcd); }
    }

    private static void SetLcd(byte ac, byte dc)
    {
        var lcd = OpenLcd();
        if (lcd == InvalidHandle) return;
        try { DeviceIoControl(lcd, IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS, [DISPLAYPOLICY_BOTH, ac, dc], 3, null, 0, out _, IntPtr.Zero); }
        finally { CloseHandle(lcd); }
    }

    private static IntPtr OpenLcd() =>
        CreateFile(@"\\.\LCD", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

    /// <summary>
    /// 内置屏对应的显示输出名。\\.\LCD 只能调内置屏，但不说是哪个显示器；
    /// 按 Windows 显示配置里各输出的接口类型（内部、嵌入式 DisplayPort 等）找出来。
    /// </summary>
    private static List<string> InternalDisplays()
    {
        var names = new List<string>();
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return names;
        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new byte[modeCount * 64]; // DISPLAYCONFIG_MODE_INFO 每个 64 字节，这里用不到内容
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return names;
        foreach (var path in paths.Take((int)pathCount))
        {
            if (path.targetOutputTechnology is not (OUTPUT_INTERNAL or OUTPUT_LVDS or OUTPUT_DISPLAYPORT_EMBEDDED or OUTPUT_UDI_EMBEDDED)) continue;
            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterLow = path.sourceAdapterLow, adapterHigh = path.sourceAdapterHigh, id = path.sourceId
            };
            if (DisplayConfigGetDeviceInfo(ref source) == 0) names.Add(source.viewGdiDeviceName);
        }
        return names;
    }

    #endregion

    #region 外接显示器：DDC/CI

    /// <summary>
    /// 枚举每个物理显示器：键是“显示输出名/序号”（如 \\.\DISPLAY2/0），重启后通常不变，用来对应恢复文件；
    /// ok 表示它支持 DDC/CI 亮度。回调结束后句柄即释放。
    /// </summary>
    private static void ForEachDdc(Action<string, IntPtr, bool> action)
    {
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Visit, IntPtr.Zero);

        bool Visit(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
        {
            if (DeviceName(monitor) is not { } device) return true;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0) return true;
            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
            try
            {
                for (var i = 0; i < physical.Length; i++)
                {
                    var ok = GetMonitorBrightness(physical[i].hPhysicalMonitor, out _, out _, out _);
                    action($"{device}/{i}", physical[i].hPhysicalMonitor, ok);
                }
            }
            finally { DestroyPhysicalMonitors(count, physical); }
            return true;
        }
    }

    #endregion

    /// <summary>显示器的显示输出名（如 \\.\DISPLAY1），DimOverlay 用它判断哪些显示器已经调了亮度。</summary>
    public static string? DeviceName(IntPtr monitor)
    {
        var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(monitor, ref info) ? info.szDevice : null;
    }

    private static readonly IntPtr InvalidHandle = new(-1);
    private const uint IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS = 0x230498, IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS = 0x23049C;
    private const byte DISPLAYPOLICY_BOTH = 3;
    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    private const uint QDC_ONLY_ACTIVE_PATHS = 2, DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint OUTPUT_LVDS = 6, OUTPUT_DISPLAYPORT_EMBEDDED = 11, OUTPUT_UDI_EMBEDDED = 13, OUTPUT_INTERNAL = 0x80000000;

    // DISPLAYCONFIG_PATH_INFO 展开写，只命名用到的字段（共 72 字节）
    [StructLayout(LayoutKind.Sequential)] private struct DISPLAYCONFIG_PATH_INFO
    {
        public uint sourceAdapterLow; public int sourceAdapterHigh; public uint sourceId, sourceModeIdx, sourceFlags;
        public uint targetAdapterLow; public int targetAdapterHigh; public uint targetId, targetModeIdx, targetOutputTechnology,
            targetRotation, targetScaling, refreshNumerator, refreshDenominator, scanLineOrdering, targetAvailable, targetFlags;
        public uint flags;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public uint type, size, adapterLow; public int adapterHigh; public uint id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MONITORINFOEX
    {
        public uint cbSize; public int l1, t1, r1, b1, l2, t2, r2, b2; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);
    [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount, [Out] byte[] modes, IntPtr topology);
    [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);
    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll")] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateFileW", SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern bool DeviceIoControl(IntPtr device, uint code, byte[]? input, uint inputSize, byte[]? output, uint outputSize, out uint returned, IntPtr overlapped);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
