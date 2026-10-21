using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using AreYouThere.Services;

namespace AreYouThere;

// 电源策略：离开后（电脑锁定时）由『在吗』设置 Windows 电源模式、锁定后息屏时间和睡眠时间，解锁后恢复原值。
// 每项都可以“不修改”，第一个选项里显示 Windows 当前的值。
public sealed partial class MainWindow
{
    // 与 Windows 设置页面的下拉选项相同（秒），0 为从不；锁定后息屏多两个更短的选项
    private static readonly uint[] SleepChoices = [60, 120, 180, 300, 600, 900, 1200, 1500, 1800, 2700, 3600, 7200, 10800, 14400, 18000, 0];
    private static readonly uint[] ScreenOffChoices = [15, 30, .. SleepChoices];
    private bool _suppressPowerUi;

    private ComboBox[] PowerBoxes => [PowerModeAcBox, PowerModeDcBox, ScreenOffAcBox, SleepAcBox, ScreenOffDcBox, SleepDcBox];

    private void LocalizePower()
    {
        PowerHeader.Text = L.T("电源策略", "Power");
        PowerHintText.Text = L.T("电脑锁定期间使用以下电源设置，解锁后恢复。",
            "These power settings are used while the PC is locked and restored when you unlock.");
        PowerSettingsLink.Content = L.T("打开 Windows 电源设置", "Open Windows power settings");
        PowerModeCard.Header = CardHeader(L.T("锁定期间的电源模式", "Power mode while locked"));
        PowerModeAcCard.Header = TimeoutAcHeader.Text = L.T("已接通电源", "Plugged in");
        PowerModeDcCard.Header = TimeoutDcHeader.Text = L.T("使用电池", "On battery");
        TimeoutCard.Header = CardHeader(L.T("锁定期间的屏幕和睡眠", "Screen and sleep while locked"));
        ScreenOffAcCard.Header = ScreenOffDcCard.Header = L.T("锁定后在此时间后关闭屏幕", "Turn off the screen after locking");
        SleepAcCard.Header = SleepDcCard.Header = L.T("使我的设备在以下时间后进入睡眠状态", "Make my device sleep after");
        RefreshPowerUi();
    }

    private static string ModeName(PowerMode? mode) => mode switch
    {
        PowerMode.Efficiency => L.T("最佳能效", "Best power efficiency"),
        PowerMode.Balanced => L.T("平衡（推荐）", "Balanced (recommended)"),
        PowerMode.Performance => L.T("最佳性能", "Best performance"),
        _ => L.T("其他", "Other"),
    };

    private static string TimeoutName(uint? seconds) => seconds switch
    {
        null => L.T("未知", "unknown"),
        0 => L.T("从不", "Never"),
        _ => PowerPolicy.Describe(TimeSpan.FromSeconds(seconds.Value)),
    };

    private static string Unchanged(string current) => L.T($"不修改（当前：{current}）", $"Don't change (current: {current})");

    /// <summary>
    /// 按 Windows 当前的值重建下拉选项（第一项“不修改”里显示当前值），并选中设置里保存的值。
    /// 定时刷新和窗口激活时都会调用；下拉框打开时跳过，避免把它关掉。
    /// </summary>
    private void RefreshPowerUi()
    {
        if (PowerBoxes.Any(b => b.IsDropDownOpen)) return;
        _suppressPowerUi = true;
        try
        {
            var modeSupported = SystemPower.PowerModeSupported;
            FillMode(PowerModeAcBox, _settings.AwayPowerModeAc, SystemPower.GetPowerMode(battery: false));
            FillMode(PowerModeDcBox, _settings.AwayPowerModeDc, SystemPower.GetPowerMode(battery: true));
            FillTimeout(ScreenOffAcBox, ScreenOffChoices, _settings.AwayScreenOffAcSeconds, SystemPower.GetTimeout(PowerTimeout.LockScreenOff, battery: false));
            FillTimeout(SleepAcBox, SleepChoices, _settings.AwaySleepAcSeconds, SystemPower.GetTimeout(PowerTimeout.Sleep, battery: false));
            FillTimeout(ScreenOffDcBox, ScreenOffChoices, _settings.AwayScreenOffDcSeconds, SystemPower.GetTimeout(PowerTimeout.LockScreenOff, battery: true));
            FillTimeout(SleepDcBox, SleepChoices, _settings.AwaySleepDcSeconds, SystemPower.GetTimeout(PowerTimeout.Sleep, battery: true));

            // 标题右侧显示此刻生效的电源模式（按当前是接通电源还是用电池），不是锁定期间的设置
            var currentMode = ModeName(SystemPower.GetPowerMode(SystemPower.OnBattery));
            PowerModeCurrentText.Text = modeSupported ? L.T($"当前：{currentMode}", $"Current: {currentMode}") : L.T("不可用", "Unavailable");
            PowerModeHintText.Text = modeSupported
                ? L.T("根据电源使用情况和性能来优化你的设备", "Optimize your device based on power use and performance")
                : L.T("此电脑不能设置电源模式：需要 Windows 11，且电源计划为“平衡”。", "Power mode isn't available on this PC. It requires Windows 11 and the Balanced power plan.");
            PowerModeAcCard.IsEnabled = PowerModeDcCard.IsEnabled = modeSupported;

            // 台式机没有电池：和 Windows 设置一样只显示“已接通电源”，也不需要分组标题
            var dc = SystemPower.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            PowerModeDcCard.Visibility = TimeoutAcHeader.Visibility = TimeoutDcHeader.Visibility = ScreenOffDcCard.Visibility = SleepDcCard.Visibility = dc;
            UpdatePowerWarnings();
        }
        finally { _suppressPowerUi = false; }
    }

    /// <summary>
    /// 用电池时锁定期间从不睡眠（设成“从不”，或不修改而 Windows 本来就是“从不”）：提醒电量耗尽会直接关机。
    /// 现代待机的电脑：提醒睡眠一段时间后可能转入休眠。
    /// </summary>
    private void UpdatePowerWarnings()
    {
        var noSleepOnBattery = SystemPower.HasBattery
            && (_settings.AwaySleepDcSeconds ?? SystemPower.GetTimeout(PowerTimeout.Sleep, battery: true)) == 0;
        NoSleepWarningText.Visibility = noSleepOnBattery ? Visibility.Visible : Visibility.Collapsed;
        NoSleepWarningText.Text = L.T("使用电池时锁定期间不会睡眠：会有电量耗尽关机风险，未保存的工作可能丢失。",
            "On battery, the PC won't sleep while locked. It may shut down when the battery runs out, and unsaved work may be lost.");
        StandbyNoteText.Visibility = SystemPower.ModernStandbyMayHibernate ? Visibility.Visible : Visibility.Collapsed;
        StandbyNoteText.Text = L.T("此设备支持现代待机：睡眠一段时间后，Windows 可能转入休眠以节省电量。",
            "This device supports Modern Standby. After sleeping for a while, Windows may switch to hibernation to save battery.");
    }

    private static void FillMode(ComboBox box, PowerMode? chosen, PowerMode? current)
    {
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = Unchanged(ModeName(current)) });
        foreach (var mode in Enum.GetValues<PowerMode>()) box.Items.Add(new ComboBoxItem { Content = ModeName(mode), Tag = mode });
        box.SelectedIndex = chosen is { } c ? 1 + (int)c : 0;
    }

    private static void FillTimeout(ComboBox box, uint[] choices, uint? chosen, uint? current)
    {
        box.Items.Clear();
        box.Items.Add(new ComboBoxItem { Content = Unchanged(TimeoutName(current)) });
        // 导入的设置里可能有不在列表中的时长，放在“从不”前面
        if (chosen is { } extra && !choices.Contains(extra))
            choices = choices.Where(s => s != 0).Append(extra).Order().Append(0u).ToArray();
        foreach (var seconds in choices) box.Items.Add(new ComboBoxItem { Content = TimeoutName(seconds), Tag = seconds });
        box.SelectedIndex = chosen is { } c ? 1 + Array.IndexOf(choices, c) : 0;
    }

    private void PowerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPowerUi || !_ready || sender is not ComboBox { SelectedItem: ComboBoxItem item } box) return;
        var tag = item.Tag;
        if (box == PowerModeAcBox) _settings.AwayPowerModeAc = tag as PowerMode?;
        else if (box == PowerModeDcBox) _settings.AwayPowerModeDc = tag as PowerMode?;
        else if (box == ScreenOffAcBox) _settings.AwayScreenOffAcSeconds = tag as uint?;
        else if (box == SleepAcBox) _settings.AwaySleepAcSeconds = tag as uint?;
        else if (box == ScreenOffDcBox) _settings.AwayScreenOffDcSeconds = tag as uint?;
        else if (box == SleepDcBox) _settings.AwaySleepDcSeconds = tag as uint?;
        _settings.Save();
        UpdatePowerWarnings();
    }

    /// <summary>
    /// 电脑锁定：检测开着、且没有因为节电模式或手动暂停而交给 Windows 时，应用离开后的电源策略。
    /// 解锁时恢复。应用失败时在窗口里提示（解锁后能看到）。
    /// </summary>
    private void ApplyAwayPower(bool locked)
    {
        bool ok;
        if (locked)
        {
            if (!_detection.Enabled || (_detection.Paused & ~PauseReason.SessionLocked) != PauseReason.None) return;
            ok = PowerOverride.Apply(_settings.AwayPower);
        }
        else ok = PowerOverride.Restore();
        ShowPowerProblem(ok ? null : locked
            ? L.T("锁定期间的部分电源设置未能应用，可能被组织策略限制。", "Some power settings couldn't be applied while the PC was locked. They may be restricted by your organization.")
            : L.T("部分电源设置未能恢复为原来的值，可以在 Windows 电源设置中手动修改。『在吗』下次启动时会再试一次。",
                "Some power settings couldn't be restored. You can change them in Windows power settings. AreYouThere will try again the next time it starts."));
        RefreshPowerUi();
    }

    private void ShowPowerProblem(string? message)
    {
        PowerProblem.IsOpen = message is not null;
        PowerProblem.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        if (message is not null) PowerProblem.Message = message;
    }
}
