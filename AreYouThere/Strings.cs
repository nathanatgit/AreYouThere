using System.Globalization;

namespace AreYouThere;

/// <summary>
/// 界面语言。中英文并排写在使用处：L.T("中文", "English") 按当前语言取其一，翻译时两边一起看、一起改。
/// 中文里产品名用『在吗』，其他引用用“”。
/// </summary>
internal static class L
{
    public static bool Zh { get; private set; } = true;

    /// <summary>"zh" / "en"；null 或空表示跟随 Windows 显示语言（中文系统用中文，其余用英文）。</summary>
    public static void SetLanguage(string? language) => Zh = language switch
    {
        "zh" => true,
        "en" => false,
        _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase),
    };

    public static string T(string zh, string en) => Zh ? zh : en;

    /// <summary>“状态：xxx”。托盘提示里不带前缀，只用正文。</summary>
    public static string Status(string text) => T($"状态：{text}", $"Status: {text}");
}
