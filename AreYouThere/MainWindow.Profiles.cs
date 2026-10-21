using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using AreYouThere.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AreYouThere;

// 方案库：窗口顶部一排卡片，每个方案一张（图片是它的锁屏图片，没有单独设置时用系统锁屏图片），
// 最后是“根据当前设置新建 / 导入 / 导出”。点击卡片切换方案；右键可重命名、复制、导出、删除。
// 当前方案就是下面各项设置：改设置时自动写回当前方案（AppSettings.Saved）。
public sealed partial class MainWindow
{
    private ProfileStore? _profiles;
    private bool _switchingProfile;
    private ImageSource? _systemLockImage;
    // 卡片上随设置变化的部分，改设置时只更新当前方案的卡片，不用整排重建
    private readonly Dictionary<string, (TextBlock Summary, Image Image)> _profileTiles = [];

    private void InitProfiles()
    {
        _profiles = ProfileStore.Load(_settings, L.T("默认", "Default"));
        // settings.json 是最后保存的设置：启动时以它为准同步到当前方案
        _profiles.Active.Settings = ProfileSettings.From(_settings);
        _profiles.Save();
        _settings.Saved += OnSettingsSaved;
        _ = BuildGalleryAsync();
    }

    private void OnSettingsSaved()
    {
        if (_switchingProfile || _profiles is null) return;
        var active = _profiles.Active;
        var imageChanged = active.Settings.AwayLockScreenImage != _settings.AwayLockScreenImage;
        active.Settings = ProfileSettings.From(_settings);
        _profiles.Save();
        if (_profileTiles.TryGetValue(active.Id, out var tile))
        {
            tile.Summary.Text = Summary(active.Settings);
            if (imageChanged) tile.Image.Source = TileImage(active);
        }
    }

    private async Task BuildGalleryAsync()
    {
        if (_profiles is null) return;
        _systemLockImage = await LoadSystemLockImageAsync();
        ProfileGallery.Children.Clear();
        _profileTiles.Clear();
        foreach (var profile in _profiles.Profiles) ProfileGallery.Children.Add(CreateProfileTile(profile));
        // 三个操作竖排成一列，总高度与方案卡片相同；放在滚动区域外，方案多时也一直看得到
        var actions = ProfileActions;
        actions.Children.Clear();
        actions.Children.Add(CreateActionTile("\uE710", L.T("根据当前设置创建", "New from current settings"), NewProfile_Click));
        actions.Children.Add(CreateActionTile("\uE8B5", L.T("导入方案", "Import profile"), ImportProfile_Click));
        actions.Children.Add(CreateActionTile("\uEDE1", L.T("导出当前方案", "Export current profile"), (_, _) => _ = ExportProfileAsync(_profiles!.Active)));
    }

    private static async Task<ImageSource?> LoadSystemLockImageAsync()
    {
        if (LockScreenImage.CurrentImage() is not { } stream) return null;
        using (stream)
        {
            var bitmap = new BitmapImage { DecodePixelWidth = 440 };
            try { await bitmap.SetSourceAsync(stream); return bitmap; }
            catch { return null; }
        }
    }

    /// <summary>卡片图片：方案自己的锁屏图片 → 内置方案的封面 → 系统当前的锁屏图片。</summary>
    private ImageSource? TileImage(Profile profile) =>
        (profile.Settings.AwayLockScreenImage is { } path && File.Exists(path) ? path : profile.CoverPath) is { } image
            ? new BitmapImage(new Uri(image)) { DecodePixelWidth = 440 }
            : _systemLockImage;

    /// <summary>卡片下方的主要设置：检测方式、锁定时机，以及锁定期间的电源模式和睡眠（设置了才显示）。</summary>
    private static string Summary(ProfileSettings s)
    {
        var interval = PowerPolicy.Describe(TimeSpan.FromSeconds(s.IntervalSeconds));
        var parts = new List<string>
        {
            s.SmartDetection ? L.T($"停止操作 {interval}后检测", $"Checks after {interval} idle") : L.T($"每 {interval}检测", $"Checks every {interval}"),
            s.LockDelaySeconds == 0 ? L.T("离开后立即锁定", "Locks right away")
                : L.T($"离开 {PowerPolicy.Describe(TimeSpan.FromSeconds(s.LockDelaySeconds))}后锁定", $"Locks {PowerPolicy.Describe(TimeSpan.FromSeconds(s.LockDelaySeconds))} after you leave"),
        };
        // 电源相关按此刻的电源来源显示
        var battery = SystemPower.OnBattery;
        if ((battery ? s.AwayPowerModeDc : s.AwayPowerModeAc) is { } mode)
            parts.Add(L.T($"锁定时：{ModeName(mode)}", $"While locked: {ModeName(mode)}"));
        if ((battery ? s.AwaySleepDcSeconds : s.AwaySleepAcSeconds) is { } sleep)
            parts.Add(L.T($"锁定时睡眠：{TimeoutName(sleep)}", $"Sleep while locked: {TimeoutName(sleep)}"));
        return string.Join(" · ", parts);
    }

    private Button CreateProfileTile(Profile profile)
    {
        var active = profile.Id == _profiles!.ActiveId;
        var image = new Image { Stretch = Stretch.UniformToFill, Source = TileImage(profile) };
        var imageArea = new Grid { Height = 112, CornerRadius = new CornerRadius(4), Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"] };
        imageArea.Children.Add(image);
        if (active)
        {
            // 当前方案：左上角标签
            imageArea.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"], CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = L.T("当前", "Current"), FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"] }
            });
        }
        var summary = new TextBlock { Text = Summary(profile.Settings), Style = (Style)WindowRoot.Resources["Hint"], MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(imageArea);
        content.Children.Add(new TextBlock { Text = profile.Name, Style = (Style)WindowRoot.Resources["CardTitle"], TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(summary);
        _profileTiles[profile.Id] = (summary, image);

        var tile = new Button
        {
            Width = 220, Height = 230, Padding = new Thickness(8), Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(active ? 2 : 1),
            BorderBrush = active ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"] : (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
        };
        ToolTipService.SetToolTip(tile, active ? L.T("当前方案。右键可重命名、复制、导出或删除。", "Current profile. Right-click to rename, duplicate, export or delete.")
            : L.T("点击切换到此方案。右键可重命名、复制、导出或删除。", "Click to switch to this profile. Right-click to rename, duplicate, export or delete."));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, profile.Name);
        tile.Click += async (_, _) => await ActivateProfileAsync(profile);

        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("\uE8AC", L.T("重命名", "Rename"), async () => await RenameProfileAsync(profile)));
        menu.Items.Add(MenuItem("\uE8C8", L.T("复制", "Duplicate"), async () =>
        {
            _profiles.Add(L.T($"{profile.Name} 副本", $"{profile.Name} copy"), profile.Settings.Clone(), profile.Cover);
            await BuildGalleryAsync();
        }));
        menu.Items.Add(MenuItem("\uEDE1", L.T("导出", "Export"), async () => await ExportProfileAsync(profile)));
        menu.Items.Add(new MenuFlyoutSeparator());
        var delete = MenuItem("\uE74D", L.T("删除", "Delete"), async () => await DeleteProfileAsync(profile));
        delete.IsEnabled = _profiles.Profiles.Count > 1; // 至少保留一个方案
        menu.Items.Add(delete);
        tile.ContextFlyout = menu;
        return tile;
    }

    private static MenuFlyoutItem MenuItem(string glyph, string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += async (_, _) => await action();
        return item;
    }

    // 操作按钮：图标和文字横排，三个叠成一列，每个约为方案卡片高度的三分之一
    private Button CreateActionTile(string glyph, string text, RoutedEventHandler click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20 });
        content.Children.Add(new TextBlock { Text = text, Style = (Style)WindowRoot.Resources["CardContent"], TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var tile = new Button
        {
            Width = 180, Height = 71, Padding = new Thickness(16, 8, 16, 8), Content = content,
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, text);
        tile.Click += click;
        return tile;
    }

    /// <summary>
    /// 切换方案：把它的设置复制到当前设置并重新载入界面。载入期间 _ready 为 false，各设置的事件处理不会逐项保存；
    /// 之后补上运行中的检测间隔、节电模式等需要立即生效的部分。
    /// </summary>
    private async Task ActivateProfileAsync(Profile profile)
    {
        if (_profiles is null || profile.Id == _profiles.ActiveId) return;
        _profiles.ActiveId = profile.Id;
        _profiles.Save();
        _switchingProfile = true;
        try
        {
            profile.Settings.ApplyTo(_settings);
            _ready = false;
            ApplySettings();
            _ready = true;
            _settings.Save();
        }
        finally { _switchingProfile = false; }
        if (_detection.Running) _presence.SetInterval(_settings.IntervalSeconds);
        ApplyBatterySaver();
        RefreshPolicyInfo();
        UpdateLockImageUi();
        Report(!_detection.Enabled ? L.T("未启用", "Off") : _detection.Paused != PauseReason.None ? PausedStatus() : RunningStatus());
        await BuildGalleryAsync();
    }

    private async void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles is null || await AskProfileNameAsync(L.T("根据当前设置新建方案", "New profile from current settings"), L.T("新方案", "New profile")) is not { } name) return;
        var profile = _profiles.Add(name, ProfileSettings.From(_settings));
        _profiles.ActiveId = profile.Id; // 设置相同，直接设为当前方案，之后的修改写到新方案里
        _profiles.Save();
        await BuildGalleryAsync();
    }

    private async Task RenameProfileAsync(Profile profile)
    {
        if (await AskProfileNameAsync(L.T("重命名方案", "Rename profile"), profile.Name) is not { } name || name == profile.Name) return;
        profile.Name = _profiles!.UniqueName(name);
        _profiles.Save();
        await BuildGalleryAsync();
    }

    private async Task DeleteProfileAsync(Profile profile)
    {
        if (_profiles is null || _profiles.Profiles.Count <= 1) return;
        var dialog = new ContentDialog
        {
            XamlRoot = RootScroll.XamlRoot,
            Title = L.T($"删除方案“{profile.Name}”？", $"Delete \"{profile.Name}\"?"),
            Content = L.T("删除后无法恢复。可以先导出备份。", "This can't be undone. You can export it first as a backup."),
            PrimaryButtonText = L.T("删除", "Delete"), CloseButtonText = L.T("取消", "Cancel"), DefaultButton = ContentDialogButton.Close,
        };
        if (await Themed(dialog, RootScroll.XamlRoot).ShowAsync() != ContentDialogResult.Primary) return;
        // 删除当前方案：先切换到另一个
        if (profile.Id == _profiles.ActiveId) await ActivateProfileAsync(_profiles.Profiles.First(p => p.Id != profile.Id));
        _profiles.Profiles.Remove(profile);
        _profiles.Save();
        await BuildGalleryAsync();
    }

    private async Task ExportProfileAsync(Profile profile)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = profile.Name };
        picker.FileTypeChoices.Add(L.T("『在吗』方案", "AreYouThere profile"), [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        if (await picker.PickSaveFileAsync() is not { } file) return;
        try
        {
            await using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            await _profiles!.ExportAsync(profile, stream);
        }
        catch (Exception ex) { await ShowProfileErrorAsync(L.T($"导出失败：{ex.Message}", $"Couldn't export: {ex.Message}")); }
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        if (await picker.PickSingleFileAsync() is not { } file) return;
        try
        {
            await using var stream = await file.OpenStreamForReadAsync();
            await _profiles!.ImportAsync(stream);
            await BuildGalleryAsync();
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            await ShowProfileErrorAsync(ex is InvalidDataException ? ex.Message : L.T("这不是『在吗』导出的方案文件。", "This isn't a profile file exported by AreYouThere."));
        }
        catch (Exception ex) { await ShowProfileErrorAsync(L.T($"导入失败：{ex.Message}", $"Couldn't import: {ex.Message}")); }
    }

    private async Task<string?> AskProfileNameAsync(string title, string initial)
    {
        var box = new TextBox { Text = initial, MaxLength = 40 };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        var dialog = new ContentDialog
        {
            XamlRoot = RootScroll.XamlRoot, Title = title, Content = box,
            PrimaryButtonText = L.T("确定", "OK"), CloseButtonText = L.T("取消", "Cancel"), DefaultButton = ContentDialogButton.Primary,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(box.Text);
        return await Themed(dialog, RootScroll.XamlRoot).ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    private async Task ShowProfileErrorAsync(string message)
    {
        var dialog = new ContentDialog { XamlRoot = RootScroll.XamlRoot, Title = L.T("方案", "Profiles"), Content = message, CloseButtonText = L.T("确定", "OK") };
        await Themed(dialog, RootScroll.XamlRoot).ShowAsync();
    }
}
