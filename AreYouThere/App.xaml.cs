using Microsoft.UI.Xaml;
using AreYouThere.Services;
using System.Runtime.InteropServices;

namespace AreYouThere;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    public static MainWindow? Window { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startupLaunch = Environment.GetCommandLineArgs().Contains(StartupService.StartupArgument)
            || Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs().Kind
                == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        // 只允许一个实例：重复启动时通知已运行的实例显示窗口，然后自己退出。
        _singleInstance = new Mutex(true, "AreYouThere.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            PostMessage(HWND_BROADCAST, startupLaunch ? TrayService.StartupLaunchMessage : TrayService.ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
            Exit();
            return;
        }

        Window = new MainWindow(startupLaunch);
        Window.SetInitialSize();
        Window.Activate();
    }

    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
