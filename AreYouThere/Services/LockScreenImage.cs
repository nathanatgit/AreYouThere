using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.UserProfile;

namespace AreYouThere.Services;

/// <summary>
/// 锁定期间的锁屏图片：电脑锁定时换成用户选的图片，解锁后恢复原来的图片。
/// 换之前记下原图片的路径，并另存一份原图（原图可能被别的应用删掉，如轮换锁屏图片的应用），
/// 恢复时优先用原路径，找不到再用备份。记录写在本机文件里，程序崩溃或锁定状态下关机后，下次启动时恢复。
/// </summary>
internal static class LockScreenImage
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AreYouThere");
    private static readonly string RestoreFile = Path.Combine(Folder, "lockscreen-restore.txt");

    /// <summary>把用户选的图片复制到程序数据目录，返回副本路径；原文件之后移动或删除都不影响。</summary>
    public static async Task<string> ImportAsync(StorageFile picked)
    {
        var path = NewImagePath(picked.FileType);
        await using (var source = await picked.OpenStreamForReadAsync())
        await using (var target = File.Create(path))
            await source.CopyToAsync(target);
        return path;
    }

    /// <summary>导入方案时，把方案文件里内嵌的图片存成副本。</summary>
    public static async Task<string> SaveImageAsync(byte[] data, string fileType)
    {
        var path = NewImagePath(fileType);
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    // 每个方案可以有自己的图片，每张都用新文件名
    private static string NewImagePath(string fileType)
    {
        Directory.CreateDirectory(Folder);
        var extension = fileType is { Length: > 1 } && fileType.StartsWith('.') ? fileType : ".jpg";
        return Path.Combine(Folder, $"lockscreen-image-{Guid.NewGuid():N}{extension}");
    }

    /// <summary>删除不再被任何方案引用的图片副本。锁屏正在显示的副本（锁定期间）不删。</summary>
    public static void DeleteUnused(IEnumerable<string?> inUse)
    {
        var keep = new HashSet<string>(inUse.OfType<string>(), StringComparer.OrdinalIgnoreCase);
        string? showing = null;
        try { showing = LockScreen.OriginalImageFile?.LocalPath; } catch { }
        if (!Directory.Exists(Folder)) return;
        foreach (var file in Directory.GetFiles(Folder, "lockscreen-image-*"))
        {
            if (keep.Contains(file) || string.Equals(file, showing, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); } catch { }
        }
    }

    /// <summary>当前系统锁屏图片，用作没有单独设置图片的方案的缩略图。读取失败返回 null。</summary>
    public static IRandomAccessStream? CurrentImage()
    {
        try { return LockScreen.GetImageStream(); }
        catch { return null; }
    }

    /// <summary>当前锁屏图片是否由其他应用设置（路径在某个商店应用的数据目录里），它可能会覆盖这里的图片。</summary>
    public static bool ManagedByAnotherApp
    {
        get
        {
            try { return LockScreen.OriginalImageFile?.LocalPath.Contains(@"\AppData\Local\Packages\", StringComparison.OrdinalIgnoreCase) == true; }
            catch { return false; }
        }
    }

    /// <summary>换成 imagePath。已经换过（还没恢复）时不再覆盖记下的原图。返回是否成功；记不下原图时不换。</summary>
    public static async Task<bool> ApplyAsync(string imagePath)
    {
        if (File.Exists(RestoreFile)) return true;
        try
        {
            var image = await StorageFile.GetFileFromPathAsync(imagePath);
            Directory.CreateDirectory(Folder);
            // 备份沿用原图的扩展名，Windows 按扩展名识别图片格式
            var original = LockScreen.OriginalImageFile?.LocalPath ?? "";
            var extension = Path.GetExtension(original) is { Length: > 0 } ext ? ext : ".jpg";
            var backupPath = Path.Combine(Folder, "lockscreen-backup" + extension);
            using (var current = LockScreen.GetImageStream())
            await using (var backup = File.Create(backupPath))
                await current.AsStreamForRead().CopyToAsync(backup);
            // 第一行原图路径，第二行备份路径
            File.WriteAllLines(RestoreFile, [original, backupPath]);
            await LockScreen.SetImageFileAsync(image);
            return true;
        }
        catch { return false; }
    }

    /// <summary>恢复原图片：原路径还在就用原路径，否则用备份。全部成功才删除记录，否则留到下次再试。</summary>
    public static async Task<bool> RestoreAsync()
    {
        if (!File.Exists(RestoreFile)) return true;
        try
        {
            var lines = File.ReadAllLines(RestoreFile);
            var original = lines.ElementAtOrDefault(0) ?? "";
            var path = original.Length > 0 && File.Exists(original) ? original : lines.ElementAtOrDefault(1) ?? "";
            await LockScreen.SetImageFileAsync(await StorageFile.GetFileFromPathAsync(path));
            File.Delete(RestoreFile);
            return true;
        }
        catch { return false; }
    }
}
