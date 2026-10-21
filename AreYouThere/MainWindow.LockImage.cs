using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using AreYouThere.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace AreYouThere;

// 锁定期间的锁屏图片：锁定时换成所选图片，解锁后恢复原图片（与电源策略同一时机）。
public sealed partial class MainWindow
{
    private void LocalizeLockImage()
    {
        LockImageCard.Header = L.T("锁定期间的锁屏图片", "Lock screen picture while locked");
        LockImageHintText.Text = L.T("锁定时显示所选图片，解锁后恢复原来的图片。", "Shows this picture while the PC is locked, then restores your previous picture when you unlock.");
        LockImageOtherAppText.Text = L.T("锁屏图片目前由其他应用设置，它轮换图片时可能会覆盖这里的图片。",
            "Your lock screen picture is currently set by another app, which may replace this picture when it rotates pictures.");
        LockImageUnchangedText.Text = L.T("不修改", "Don't change");
        LockImageClearButton.Content = L.T("不修改", "Don't change");
        LockImagePickButton.Content = L.T("选择图片…", "Choose picture…");
        UpdateLockImageUi();
    }

    /// <summary>选了图片时显示缩略图和“不修改”按钮；没选时显示“不修改”文字。</summary>
    private void UpdateLockImageUi()
    {
        var path = _settings.AwayLockScreenImage;
        var chosen = path is not null && File.Exists(path);
        LockImagePreview.Source = chosen ? new BitmapImage(new Uri(path!)) : null;
        LockImagePreview.Visibility = LockImageClearButton.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        LockImageUnchangedText.Visibility = chosen ? Visibility.Collapsed : Visibility.Visible;
        LockImageOtherAppText.Visibility = chosen && LockScreenImage.ManagedByAnotherApp ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void LockImagePick_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        foreach (var type in new[] { ".jpg", ".jpeg", ".png", ".bmp" }) picker.FileTypeFilter.Add(type);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); // 未打包的桌面程序要指定所属窗口
        if (await picker.PickSingleFileAsync() is not { } file) return;
        try
        {
            _settings.AwayLockScreenImage = await LockScreenImage.ImportAsync(file);
            _settings.Save();
        }
        catch (Exception ex)
        {
            ShowPowerProblem(L.T($"无法使用这张图片：{ex.Message}", $"Can't use this picture: {ex.Message}"));
        }
        UpdateLockImageUi();
    }

    private void LockImageClear_Click(object sender, RoutedEventArgs e)
    {
        _settings.AwayLockScreenImage = null;
        _settings.Save();
        UpdateLockImageUi();
    }

    /// <summary>锁定时换成所选图片，解锁时恢复。与 ApplyAwayPower 相同的条件；失败时在窗口里提示（解锁后能看到）。</summary>
    private async Task ApplyAwayLockImageAsync(bool locked)
    {
        bool ok;
        if (locked)
        {
            if (_settings.AwayLockScreenImage is not { } image || !File.Exists(image)) return;
            if (!_detection.Enabled || (_detection.Paused & ~PauseReason.SessionLocked) != PauseReason.None) return;
            ok = await LockScreenImage.ApplyAsync(image);
        }
        else ok = await LockScreenImage.RestoreAsync();
        if (!ok)
            ShowPowerProblem(locked
                ? L.T("锁定期间未能更换锁屏图片。", "Couldn't change the lock screen picture while the PC was locked.")
                : L.T("未能恢复原来的锁屏图片，可以在 Windows 设置 › 个性化 › 锁屏界面 中重新选择。『在吗』下次启动时会再试一次。",
                    "Couldn't restore your previous lock screen picture. You can choose it again in Windows Settings > Personalization > Lock screen. AreYouThere will try again the next time it starts."));
    }
}
