using Microsoft.UI.Xaml;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace AreYouThere.Services;

/// <summary>
/// Win32 原生托盘图标。通过子类化主窗口的 WndProc 接收托盘回调，不依赖第三方托盘包。
/// 同一个 WndProc 也负责接收会话锁定/解锁通知（WM_WTSSESSION_CHANGE）。
/// </summary>
internal sealed class TrayService : IDisposable
{
    /// <summary>第二个实例启动时广播此消息，让已运行的实例显示窗口。</summary>
    public static readonly uint ShowWindowMessage = RegisterWindowMessage("AreYouThere.ShowWindow");
    public static readonly uint StartupLaunchMessage = RegisterWindowMessage("AreYouThere.StartupLaunch");
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

    private const uint WM_APP = 0x8000, WM_NULL = 0x0000, WM_SETTINGCHANGE = 0x001A,WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIM_ADD = 0x0, NIM_MODIFY = 0x1, NIM_DELETE = 0x2;
    private const uint NIIF_USER = 0x4, NIIF_NOSOUND = 0x10;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const int SM_CXSMICON = 49, GWLP_WNDPROC = -4;
    private const uint CallbackMessage = WM_APP + 41;

    private readonly IntPtr _hwnd, _oldProc;
    private readonly string _fallbackIconPath;
    private IntPtr _icon;
    private readonly WndProc _newProc;
    private readonly Func<bool> _isEnabled;
    private readonly Func<DateTimeOffset?> _pausedUntil;
    private readonly Action _toggle, _open, _exit;
    private readonly Action<TimeSpan?> _pause;
    private string _tip = L.T("『在吗』正在运行", "AreYouThere is running");
    private bool _disposed;
    private const uint WM_WTSSESSION_CHANGE = 0x02B1, WTS_SESSION_LOCK = 0x7, WTS_SESSION_UNLOCK = 0x8;

    /// <summary>当前会话被锁定（true）或解锁（false）。</summary>
    public event Action<bool>? SessionLockChanged;
    public event Action? StartupLaunchRequested;

    /// <summary>“暂停检测”可选的时长，托盘菜单和设置窗口共用。</summary>
    public static readonly TimeSpan[] PauseChoices = [TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2)];

    /// <param name="pausedUntil">手动暂停到何时；没有手动暂停时为 null。</param>
    /// <param name="pause">暂停一段时间；参数为 null 表示立即恢复。</param>
    public TrayService(Window window, string iconPath, Func<bool> isEnabled, Func<DateTimeOffset?> pausedUntil,
        Action toggle, Action<TimeSpan?> pause, Action open, Action exit)
    {
        _isEnabled = isEnabled; _pausedUntil = pausedUntil; _toggle = toggle; _pause = pause; _open = open; _exit = exit;
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _fallbackIconPath = iconPath;
        _icon = LoadTrayIcon();
        _newProc = WindowProc;
        _oldProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newProc));
        AddIcon();
        WTSRegisterSessionNotification(_hwnd, 0 /* NOTIFY_FOR_THIS_SESSION */);
    }

    public void SetTooltip(string text)
    {
        _tip = text.Length > 127 ? text[..127] : text;
        var data = NewData(NIF_TIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void ShowBalloon(string title, string text)
    {
        var data = NewData(NIF_INFO);
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NIIF_USER | NIIF_NOSOUND;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void AddIcon()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    /// <summary>托盘用单色图标：浅色任务栏用黑色，深色任务栏用白色；找不到时退回彩色应用图标。</summary>
    private IntPtr LoadTrayIcon()
    {
        var light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is int v && v == 1;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", light ? "tray-black.ico" : "tray-white.ico");
        if (!File.Exists(path)) path = _fallbackIconPath;
        var size = GetSystemMetricsForDpi(SM_CXSMICON, GetDpiForWindow(_hwnd));
        var icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, size, size, LR_LOADFROMFILE);
        return icon != IntPtr.Zero ? icon : LoadIcon(IntPtr.Zero, (IntPtr)32512);
    }

    /// <summary>任务栏深浅色切换后换上对应颜色的图标。</summary>
    private void UpdateIcon()
    {
        var old = _icon;
        _icon = LoadTrayIcon();
        var data = NewData(NIF_ICON);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
        DestroyIcon(old);
    }

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1,
        uFlags = flags, uCallbackMessage = CallbackMessage, hIcon = _icon, szTip = _tip
    };

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == CallbackMessage)
        {
            var action = unchecked((uint)lParam.ToInt64());
            if (action == WM_LBUTTONUP) _open();
            else if (action == WM_RBUTTONUP) ShowMenu();
        }
        else if (msg == TaskbarCreatedMessage) AddIcon(); // 资源管理器重启后托盘图标会丢失，需要重新添加
        else if (msg == WM_SETTINGCHANGE && lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet") UpdateIcon();
        else if (msg == ShowWindowMessage) { _open(); return IntPtr.Zero; }
        else if (msg == StartupLaunchMessage) { StartupLaunchRequested?.Invoke(); return IntPtr.Zero; }
        else if (msg == WM_WTSSESSION_CHANGE)
        {
            var reason = unchecked((uint)wParam.ToInt64());
            if (reason == WTS_SESSION_LOCK) SessionLockChanged?.Invoke(true);
            else if (reason == WTS_SESSION_UNLOCK) SessionLockChanged?.Invoke(false);
        }
        return CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        var enabled = _isEnabled();
        AppendMenu(menu, MF_STRING | (enabled ? MF_CHECKED : 0), 1, L.T("启用『在吗』", "Enable presence detection"));
        // 暂停中显示“恢复检测”；否则是“暂停检测”子菜单（未启用时灰掉）。子菜单命令 100+ 对应 PauseChoices 的下标
        IntPtr pauseMenu = IntPtr.Zero;
        if (_pausedUntil() is { } until)
            AppendMenu(menu, MF_STRING, 4, L.T($"恢复检测（已暂停到 {until.ToLocalTime():HH:mm}）", $"Resume detection (paused until {until.ToLocalTime():t})"));
        else
        {
            pauseMenu = CreatePopupMenu();
            for (var i = 0; i < PauseChoices.Length; i++)
                AppendMenu(pauseMenu, MF_STRING, (uint)(100 + i), PowerPolicy.Describe(PauseChoices[i]));
            AppendMenu(menu, MF_POPUP | (enabled ? 0 : MF_GRAYED), (nuint)pauseMenu, L.T("暂停检测", "Pause detection"));
        }
        AppendMenu(menu, MF_STRING, 2, L.T("打开设置窗口", "Open settings"));
        AppendMenu(menu, MF_SEPARATOR, 0, null);
        AppendMenu(menu, MF_STRING, 3, L.T("退出", "Exit"));
        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero); // 让菜单在点击别处时能正常关闭
        DestroyMenu(menu); // 同时销毁挂在上面的子菜单
        if (command == 1) _toggle(); else if (command == 2) _open(); else if (command == 3) _exit();
        else if (command == 4) _pause(null);
        else if (command >= 100 && command < 100 + PauseChoices.Length) _pause(PauseChoices[command - 100]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = NewData(0); Shell_NotifyIcon(NIM_DELETE, ref data);
        SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldProc);
        WTSUnRegisterSessionNotification(_hwnd);
        DestroyIcon(_icon);
        GC.KeepAlive(_newProc);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct NOTIFYICONDATA
    {
        public uint cbSize; public IntPtr hWnd; public uint uID, uFlags, uCallbackMessage; public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public IntPtr hBalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr hInstance, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    // 32 位 user32.dll 没有导出 SetWindowLongPtrW（它在头文件里只是 SetWindowLongW 的宏），x86 上必须改调 SetWindowLongW
    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : (IntPtr)SetWindowLong32(hWnd, nIndex, value.ToInt32());
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
