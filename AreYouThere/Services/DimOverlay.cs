using Microsoft.UI.Dispatching;
using System.Runtime.InteropServices;

namespace AreYouThere.Services;

/// <summary>
/// 锁定倒计时期间逐渐调暗屏幕，提醒“快要锁定了”，比只弹通知更容易注意到。
/// 做法是在每个显示器上盖一层半透明黑色窗口：置顶、不抢焦点、鼠标键盘直接穿透，不影响继续操作。
/// 不改显示器亮度或系统设置，台式机外接显示器同样有效；倒计时取消或锁定后立即移除。只在 UI 线程上使用。
/// </summary>
internal sealed class DimOverlay : IDisposable
{
    private static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(600);
    // 预览最多到这个不透明度：选 100%（全黑）时也要能看见正在拖的滑块
    private const double PreviewMax = 0.85;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    private readonly DispatcherQueueTimer _timer;
    private readonly List<IntPtr> _windows = [];
    private DateTimeOffset _startedAt;
    private TimeSpan _duration;
    private bool _preview;
    private IReadOnlySet<string> _previewSkip = Empty;
    private static readonly IReadOnlySet<string> Empty = new HashSet<string>();

    /// <summary>最暗时的不透明度（0–1），即设置里的“压暗强度”。倒计时开始时先快速变暗到一半，之后随倒计时推进加深到这个值。</summary>
    public double Level { get; set; } = 0.7;
    private static ushort _class;

    public DimOverlay(DispatcherQueue dispatcher)
    {
        _timer = dispatcher.CreateTimer();
        _timer.Interval = FrameInterval;
        _timer.Tick += (_, _) => Update();
    }

    /// <summary>
    /// 开始调暗，持续 duration（锁定倒计时长度）。重复调用会重新开始。
    /// skip：已经用显示器亮度调暗的显示器（显示输出名），这些不再加遮罩。
    /// </summary>
    public void Show(TimeSpan duration, IReadOnlySet<string>? skip = null)
    {
        Hide();
        _duration = duration > TimeSpan.Zero ? duration : TimeSpan.FromSeconds(1);
        _startedAt = DateTimeOffset.UtcNow;
        Open(skip);
        if (_windows.Count > 0) _timer.Start();
    }

    /// <summary>
    /// 拖动“压暗强度”滑块时预览：按 Level 静态显示，直到 Hide（松开滑块时）。skip 同 Show。
    /// 拖动过程中反复调用只更新不透明度；跳过的显示器变了才重建遮罩。
    /// </summary>
    public void ShowPreview(IReadOnlySet<string>? skip = null)
    {
        skip ??= Empty;
        if (!_preview || !skip.SetEquals(_previewSkip))
        {
            Hide();
            Open(skip);
            _preview = true;
            _previewSkip = skip;
        }
        SetOpacity(Math.Min(Level, PreviewMax));
    }

    private void Open(IReadOnlySet<string>? skip)
    {
        if (!EnsureClass()) return;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, AddMonitor, IntPtr.Zero);

        // 显示器坐标是物理像素（程序按每显示器 DPI 感知运行），直接用来铺满整个屏幕
        bool AddMonitor(IntPtr monitor, IntPtr hdc, ref RECT r, IntPtr data)
        {
            if (skip is not null && BrightnessDimmer.DeviceName(monitor) is { } device && skip.Contains(device)) return true;
            var hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                ClassName, "", WS_POPUP, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (hwnd != IntPtr.Zero)
            {
                SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                _windows.Add(hwnd);
            }
            return true;
        }
    }

    public void Hide()
    {
        _timer.Stop();
        _preview = false;
        foreach (var hwnd in _windows) DestroyWindow(hwnd);
        _windows.Clear();
    }

    private void Update()
    {
        var elapsed = DateTimeOffset.UtcNow - _startedAt;
        var progress = Math.Clamp(elapsed / _duration, 0, 1);
        var fade = Math.Clamp(elapsed / FadeIn, 0, 1);
        SetOpacity(Level * (0.5 + 0.5 * progress) * fade);
        // 到时间后保持最暗，等锁定或取消时由调用方 Hide
        if (progress >= 1 && fade >= 1) _timer.Stop();
    }

    private void SetOpacity(double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        foreach (var hwnd in _windows) SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
    }

    private bool EnsureClass()
    {
        if (_class != 0) return true;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(SharedWndProc),
            hInstance = GetModuleHandle(null),
            hbrBackground = GetStockObject(BLACK_BRUSH),
            lpszClassName = ClassName,
        };
        _class = RegisterClassEx(ref wc);
        return _class != 0;
    }

    public void Dispose() => Hide();

    private const string ClassName = "AreYouThere.DimOverlay";
    // 窗口类在进程内只注册一次，窗口过程委托必须一直存活
    private static readonly WndProc SharedWndProc = DefWindowProc;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
    private const uint LWA_ALPHA = 0x2;
    private const int SW_SHOWNOACTIVATE = 4, BLACK_BRUSH = 4;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASSEX
    {
        public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm;
    }
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW")] private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int obj);
}
