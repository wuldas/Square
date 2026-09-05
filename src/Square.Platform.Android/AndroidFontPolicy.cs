using System.IO;
using Square.Text;
using Square.Text.Glyph;
namespace Square.Platform.Android;

/// <summary>Android 系统字体策略和诊断入口。</summary>
public static class AndroidFontPolicy
{
    /// <summary>Android 常见系统字体根目录，按系统优先级排列。</summary>
    public static IReadOnlyList<string> SystemFontRoots { get; } =
    [
        "/system/fonts",
        "/product/fonts",
        "/system_ext/fonts",
        "/vendor/fonts"
    ];

    /// <summary>将 CSS 通用字体族映射到 Android Typeface 识别的原生通用族名。</summary>
    public static string ResolveGenericFamily(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return family.Trim().ToLowerInvariant() switch
        {
            "sans-serif" or "system-ui" or "ui-sans-serif" => "sans-serif",
            "serif" or "ui-serif" => "serif",
            "monospace" or "ui-monospace" => "monospace",
            _ => family
        };
    }
    /// <summary>记录系统字体扫描状态，便于设备验收定位。</summary>
    public static void LogDiagnostics()
    {
        var rasterizer = new SystemGlyphRasterizer();
        var sans = FontManager.Instance.ResolveFamily("sans-serif");
        var message = $"Android fonts: root={HasReadableSystemFontRoot}, stb={rasterizer.IsAvailable}, sans={sans}";
        global::Android.Util.Log.Warn("Square", message);
        System.Console.WriteLine("SQUARE_FONT " + message);
    }

    /// <summary>判断当前设备是否暴露可读系统字体根。</summary>
    public static bool HasReadableSystemFontRoot =>
        SystemFontRoots.Any(Directory.Exists);
}
