using System.Runtime.InteropServices;

namespace AreYouThere.Services;

/// <summary>
/// 监听节电模式。Windows 10 和较早的 Windows 11 叫“节电模式”(battery saver，GUID_POWER_SAVING_STATUS)；
/// Windows 11 24H2 起改为“节能模式”(energy saver，GUID_ENERGY_SAVER_STATUS)，开关它时旧的状态（包括
/// WinRT 的 PowerManager.EnergySaverStatus）不会变化。两个都订阅，任何一个开启就算开启。
/// </summary>
internal sealed class BatterySaverMonitor : IDisposable
{
    private static readonly Guid PowerSavingStatus = new("e00958c0-c213-4ace-ac77-fecced2eeea5");
    private static readonly Guid EnergySaverStatus = new("550e8400-e29b-41d4-a716-446655440000");
    private const uint DEVICE_NOTIFY_CALLBACK = 2, PBT_POWERSETTINGCHANGE = 0x8013;

    private readonly PowerCallback _callback;
    private readonly List<IntPtr> _handles = [];
    private volatile int _powerSaving, _energySaver; // 节能模式：0 关闭、1 标准、2 高节能
    private volatile bool _powerSavingKnown, _energySaverKnown;

    /// <summary>
    /// 节电/节能模式开启或关闭时触发，在系统线程上调用。参数为 true 表示这是注册后系统送来的初始状态，
    /// 不是用户刚刚切换的（启动时已经开着节电模式）。
    /// </summary>
    public event Action<bool>? Changed;

    public bool IsOn => _powerSaving != 0 || _energySaver != 0;

    /// <summary>Windows 11 24H2（build 26100）起系统界面里叫“节能模式 / Energy saver”，之前叫“节电模式 / Battery saver”。</summary>
    public static bool UsesEnergySaverName { get; } = Environment.OSVersion.Version.Build >= 26100;

    public BatterySaverMonitor()
    {
        _callback = OnPowerSetting;
        // 注册后系统会立即回调一次当前值；不支持的 GUID（如 Windows 10 上的节能模式）注册失败或不回调，按关闭处理
        foreach (var setting in new[] { PowerSavingStatus, EnergySaverStatus })
        {
            var guid = setting;
            var recipient = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { Callback = Marshal.GetFunctionPointerForDelegate(_callback) };
            if (PowerSettingRegisterNotification(ref guid, DEVICE_NOTIFY_CALLBACK, ref recipient, out var handle) == 0)
                _handles.Add(handle);
        }
    }

    private uint OnPowerSetting(IntPtr context, uint type, IntPtr setting)
    {
        if (type != PBT_POWERSETTINGCHANGE || setting == IntPtr.Zero) return 0;
        // POWERBROADCAST_SETTING：Guid PowerSetting; uint DataLength; byte Data[]
        var guid = Marshal.PtrToStructure<Guid>(setting);
        if (Marshal.ReadInt32(setting, 16) < 4) return 0;
        var value = Marshal.ReadInt32(setting, 20);
        var wasOn = IsOn;
        bool initial;
        if (guid == PowerSavingStatus) { initial = !_powerSavingKnown; _powerSavingKnown = true; _powerSaving = value; }
        else if (guid == EnergySaverStatus) { initial = !_energySaverKnown; _energySaverKnown = true; _energySaver = value; }
        else return 0;
        if (IsOn != wasOn) Changed?.Invoke(initial);
        return 0;
    }

    public void Dispose()
    {
        foreach (var handle in _handles) PowerSettingUnregisterNotification(handle);
        _handles.Clear();
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);
    [StructLayout(LayoutKind.Sequential)] private struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS { public IntPtr Callback; public IntPtr Context; }
    [DllImport("powrprof.dll")] private static extern uint PowerSettingRegisterNotification(ref Guid setting, uint flags, ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS recipient, out IntPtr handle);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingUnregisterNotification(IntPtr handle);
}
