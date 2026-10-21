using System.Text.Json;
using System.Text.Json.Serialization;

namespace AreYouThere.Services;

/// <summary>
/// 一个方案里保存的设置：检测、锁定、压暗、离开后的电源策略和锁屏图片。
/// 摄像头、预览、通知、开机自启动、语言和主题与这台电脑或界面有关，是全局设置，不放在方案里。
/// </summary>
internal sealed class ProfileSettings
{
    public uint IntervalSeconds { get; set; } = 30;
    public bool SmartDetection { get; set; } = true;
    public uint IntervalStepSeconds { get; set; }
    public bool LinearGrowth { get; set; }
    public uint MaxIntervalSeconds { get; set; } = 600;
    public double MinFaceAreaPercent { get; set; } = 8;
    public bool PostureCheck { get; set; }
    public double PostureLineFromBottomPercent { get; set; } = 40;
    public double PostureMaxAreaPercent { get; set; } = 20;
    public bool PostureFollowDetection { get; set; }
    public uint PostureIntervalSeconds { get; set; } = 600;
    public bool PauseInBatterySaver { get; set; } = true;
    public int LockDelaySeconds { get; set; } = 10;
    public bool DimDuringCountdown { get; set; } = true;
    public double DimLevelPercent { get; set; } = 70;
    public DimMode DimMode { get; set; }
    public PowerMode? AwayPowerModeAc { get; set; }
    public PowerMode? AwayPowerModeDc { get; set; }
    public uint? AwayScreenOffAcSeconds { get; set; }
    public uint? AwaySleepAcSeconds { get; set; }
    public uint? AwayScreenOffDcSeconds { get; set; }
    public uint? AwaySleepDcSeconds { get; set; }
    /// <summary>锁屏图片在程序数据目录里的副本路径；null 表示不修改。</summary>
    public string? AwayLockScreenImage { get; set; }

    public static ProfileSettings From(AppSettings s) => new()
    {
        IntervalSeconds = s.IntervalSeconds, SmartDetection = s.SmartDetection, IntervalStepSeconds = s.IntervalStepSeconds,
        LinearGrowth = s.LinearGrowth, MaxIntervalSeconds = s.MaxIntervalSeconds, MinFaceAreaPercent = s.MinFaceAreaPercent,
        PostureCheck = s.PostureCheck, PostureLineFromBottomPercent = s.PostureLineFromBottomPercent, PostureMaxAreaPercent = s.PostureMaxAreaPercent,
        PostureFollowDetection = s.PostureFollowDetection, PostureIntervalSeconds = s.PostureIntervalSeconds, PauseInBatterySaver = s.PauseInBatterySaver,
        LockDelaySeconds = s.LockDelaySeconds, DimDuringCountdown = s.DimDuringCountdown, DimLevelPercent = s.DimLevelPercent, DimMode = s.DimMode,
        AwayPowerModeAc = s.AwayPowerModeAc, AwayPowerModeDc = s.AwayPowerModeDc, AwayScreenOffAcSeconds = s.AwayScreenOffAcSeconds,
        AwaySleepAcSeconds = s.AwaySleepAcSeconds, AwayScreenOffDcSeconds = s.AwayScreenOffDcSeconds, AwaySleepDcSeconds = s.AwaySleepDcSeconds,
        AwayLockScreenImage = s.AwayLockScreenImage,
    };

    public void ApplyTo(AppSettings s)
    {
        s.IntervalSeconds = IntervalSeconds; s.SmartDetection = SmartDetection; s.IntervalStepSeconds = IntervalStepSeconds;
        s.LinearGrowth = LinearGrowth; s.MaxIntervalSeconds = MaxIntervalSeconds; s.MinFaceAreaPercent = MinFaceAreaPercent;
        s.PostureCheck = PostureCheck; s.PostureLineFromBottomPercent = PostureLineFromBottomPercent; s.PostureMaxAreaPercent = PostureMaxAreaPercent;
        s.PostureFollowDetection = PostureFollowDetection; s.PostureIntervalSeconds = PostureIntervalSeconds; s.PauseInBatterySaver = PauseInBatterySaver;
        s.LockDelaySeconds = LockDelaySeconds; s.DimDuringCountdown = DimDuringCountdown; s.DimLevelPercent = DimLevelPercent; s.DimMode = DimMode;
        s.AwayPowerModeAc = AwayPowerModeAc; s.AwayPowerModeDc = AwayPowerModeDc; s.AwayScreenOffAcSeconds = AwayScreenOffAcSeconds;
        s.AwaySleepAcSeconds = AwaySleepAcSeconds; s.AwayScreenOffDcSeconds = AwayScreenOffDcSeconds; s.AwaySleepDcSeconds = AwaySleepDcSeconds;
        s.AwayLockScreenImage = AwayLockScreenImage;
    }

    public ProfileSettings Clone() => JsonSerializer.Deserialize(JsonSerializer.Serialize(this, ProfileJsonContext.Default.ProfileSettings), ProfileJsonContext.Default.ProfileSettings)!;
}

internal sealed class Profile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>内置示例方案的封面（Assets/Profiles/{Cover}.png），只用作卡片缩略图，不会设成锁屏图片。</summary>
    public string? Cover { get; set; }
    public ProfileSettings Settings { get; set; } = new();

    /// <summary>封面图片的完整路径；没有封面或文件不存在时为 null。</summary>
    [JsonIgnore] public string? CoverPath => Cover is { } c && File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Profiles", c + ".png"))
        ? Path.Combine(AppContext.BaseDirectory, "Assets", "Profiles", c + ".png") : null;
}

/// <summary>导出文件：方案设置，加上内嵌的锁屏图片，拿到另一台电脑也能用。</summary>
internal sealed class ProfileExport
{
    public string Format { get; set; } = ProfileStore.ExportFormat;
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public string? Cover { get; set; }
    public ProfileSettings Settings { get; set; } = new();
    public string? LockScreenImageType { get; set; }
    public string? LockScreenImageBase64 { get; set; }
}

/// <summary>
/// 所有方案和当前方案，保存在本机 profiles.json。当前方案就是界面上正在用的设置：
/// 改设置时自动写回当前方案；切换方案时把它的设置复制到 AppSettings。
/// </summary>
internal sealed class ProfileStore
{
    public const string ExportFormat = "AreYouThere.Profile";
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AreYouThere");
    private static readonly string FilePath = Path.Combine(Folder, "profiles.json");

    public List<Profile> Profiles { get; set; } = [];
    public string? ActiveId { get; set; }
    /// <summary>是否已经加过内置的示例方案（只加一次：删掉后不会再出现）。</summary>
    public bool PresetsAdded { get; set; }

    [JsonIgnore] public Profile Active => Profiles.FirstOrDefault(p => p.Id == ActiveId) ?? Profiles[0];

    /// <summary>读取方案；第一次运行（或文件损坏）时用当前设置建一个“默认”方案。</summary>
    public static ProfileStore Load(AppSettings current, string defaultName)
    {
        ProfileStore? store = null;
        try { store = JsonSerializer.Deserialize(File.ReadAllText(FilePath), ProfileJsonContext.Default.ProfileStore); }
        catch { }
        if (store is null || store.Profiles.Count == 0)
        {
            var first = new Profile { Name = defaultName, Settings = ProfileSettings.From(current) };
            store = new ProfileStore { Profiles = [first], ActiveId = first.Id };
            store.Save();
        }
        if (store.Profiles.All(p => p.Id != store.ActiveId)) store.ActiveId = store.Profiles[0].Id;
        if (!store.PresetsAdded)
        {
            foreach (var preset in Presets())
                store.Profiles.Add(new Profile { Name = store.UniqueName(preset.Name), Cover = preset.Cover, Settings = preset.Settings });
            store.PresetsAdded = true;
            store.Save();
        }
        return store;
    }

    /// <summary>
    /// 内置的示例方案，开箱即可切换使用：办公室、公共场所、居家。只设置与场景相关的项，其余用默认值。
    /// </summary>
    private static IEnumerable<(string Name, string Cover, ProfileSettings Settings)> Presets()
    {
        // 办公室：短暂离开就锁定，倒计时压暗提醒；锁定期间省电
        yield return (L.T("办公室", "Office"), "office", new ProfileSettings
        {
            IntervalSeconds = 10, SmartDetection = true, MinFaceAreaPercent = 8, LockDelaySeconds = 10,
            DimDuringCountdown = true, DimMode = DimMode.Overlay, DimLevelPercent = 70,
            AwayPowerModeAc = PowerMode.Efficiency, AwayPowerModeDc = PowerMode.Efficiency,
            AwayScreenOffAcSeconds = 15, AwayScreenOffDcSeconds = 15, AwaySleepAcSeconds = 600, AwaySleepDcSeconds = 180,
        });
        // 公共场所：检测更频繁，只认离摄像头近的人脸，离开立即锁定；节电模式下也继续保护
        yield return (L.T("公共场所", "Public place"), "public", new ProfileSettings
        {
            IntervalSeconds = 5, SmartDetection = true, MinFaceAreaPercent = 15, LockDelaySeconds = 0, PauseInBatterySaver = false,
            AwayPowerModeAc = PowerMode.Efficiency, AwayPowerModeDc = PowerMode.Efficiency,
            AwayScreenOffAcSeconds = 15, AwayScreenOffDcSeconds = 15, AwaySleepAcSeconds = 180, AwaySleepDcSeconds = 60,
        });
        // 居家：检测间隔随在场逐渐拉长，更省电；锁定前留足时间，开坐姿提醒
        yield return (L.T("居家", "Home"), "home", new ProfileSettings
        {
            IntervalSeconds = 30, SmartDetection = true, IntervalStepSeconds = 30, MaxIntervalSeconds = 300, MinFaceAreaPercent = 5,
            LockDelaySeconds = 30, DimDuringCountdown = true, DimMode = DimMode.Overlay, DimLevelPercent = 50,
            PostureCheck = true, PostureFollowDetection = false, PostureIntervalSeconds = 600,
            AwayScreenOffAcSeconds = 60, AwayScreenOffDcSeconds = 30,
        });
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ProfileJsonContext.Default.ProfileStore));
        }
        catch { }
        LockScreenImage.DeleteUnused(Profiles.Select(p => p.Settings.AwayLockScreenImage));
    }

    /// <summary>名字已存在时加上“ (2)”“ (3)”……</summary>
    public string UniqueName(string name)
    {
        if (Profiles.All(p => p.Name != name)) return name;
        for (var i = 2; ; i++) if (Profiles.All(p => p.Name != $"{name} ({i})")) return $"{name} ({i})";
    }

    public Profile Add(string name, ProfileSettings settings, string? cover = null)
    {
        var profile = new Profile { Name = UniqueName(name), Cover = cover, Settings = settings };
        Profiles.Add(profile);
        Save();
        return profile;
    }

    public async Task ExportAsync(Profile profile, Stream target)
    {
        var export = new ProfileExport { Name = profile.Name, Cover = profile.Cover, Settings = profile.Settings.Clone() };
        if (profile.Settings.AwayLockScreenImage is { } image && File.Exists(image))
        {
            export.LockScreenImageType = Path.GetExtension(image);
            export.LockScreenImageBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(image));
        }
        export.Settings.AwayLockScreenImage = null; // 本机路径对别的电脑没有意义
        await JsonSerializer.SerializeAsync(target, export, ProfileJsonContext.Default.ProfileExport);
    }

    /// <summary>读取导出文件并新建方案；不是『在吗』的方案文件时抛出 InvalidDataException。</summary>
    public async Task<Profile> ImportAsync(Stream source)
    {
        var export = await JsonSerializer.DeserializeAsync(source, ProfileJsonContext.Default.ProfileExport);
        if (export is null || export.Format != ExportFormat || export.Settings is null)
            throw new InvalidDataException(L.T("这不是『在吗』导出的方案文件。", "This isn't a profile file exported by AreYouThere."));
        var settings = export.Settings;
        settings.AwayLockScreenImage = export.LockScreenImageBase64 is { } data
            ? await LockScreenImage.SaveImageAsync(Convert.FromBase64String(data), export.LockScreenImageType ?? ".jpg")
            : null;
        return Add(string.IsNullOrWhiteSpace(export.Name) ? L.T("导入的方案", "Imported profile") : export.Name.Trim(), settings, export.Cover);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ProfileStore))]
[JsonSerializable(typeof(ProfileExport))]
[JsonSerializable(typeof(ProfileSettings))]
internal sealed partial class ProfileJsonContext : JsonSerializerContext;
