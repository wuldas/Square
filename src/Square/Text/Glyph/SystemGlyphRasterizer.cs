using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using Square.Graphics;

namespace Square.Text.Glyph;

/// <summary>光栅化后的字形位图与度量信息。</summary>
public sealed class RasterizedGlyph
{
    /// <summary>字形位图宽度（像素）。</summary>
    public required int Width { get; init; }
    /// <summary>字形位图高度（像素）。</summary>
    public required int Height { get; init; }
    /// <summary>位图每行字节数（已按 4 字节对齐）。</summary>
    public required int Stride { get; init; }
    /// <summary>字形相对于原点的水平偏移（像素）。</summary>
    public required int OffsetX { get; init; }
    /// <summary>字形位图顶部相对于基线的垂直偏移（像素，基线上方为负）。</summary>
    public required int OffsetY { get; init; }
    /// <summary>该字形的水平步进宽度（像素）。</summary>
    public required float AdvanceX { get; init; }
    /// <summary>灰度覆盖率数据（0..255），按 Stride 逐行排列。</summary>
    public required byte[] Coverage { get; init; }
}

/// <summary>系统字形光栅器；自定义字体使用 stb_truetype，系统字体优先使用平台原生光栅化。</summary>
public sealed partial class SystemGlyphRasterizer
{
    internal static SystemGlyphRasterizer Shared { get; } = new();

    private readonly Dictionary<GlyphKey, RasterizedGlyph?> _cache = [];
    private readonly object _cacheGate = new();
    private readonly StbGlyphRasterizer _stbRasterizer;
    private readonly bool _cacheGlyphs;
    private static Func<Font, char, RasterizedGlyph?>? _platformRasterizer;
    private static Func<Font, FontMetrics?>? _platformMetrics;

    /// <summary>初始化实例。</summary>
    /// <param name="cacheGlyphs">是否缓存已光栅化的字形。</param>
    public SystemGlyphRasterizer(bool cacheGlyphs = true)
    {
        _cacheGlyphs = cacheGlyphs;
        _stbRasterizer = new StbGlyphRasterizer(cacheGlyphs);
    }
    /// <summary>注册平台原生字形栅格器；平台程序集加载时调用。</summary>
    public static void RegisterPlatformRasterizer(Func<Font, char, RasterizedGlyph?> rasterizer)
    {
        ArgumentNullException.ThrowIfNull(rasterizer);
        _platformRasterizer = rasterizer;
    }

    /// <summary>注册平台原生字体度量提供器；平台程序集加载时调用。</summary>
    public static void RegisterPlatformFontMetrics(Func<Font, FontMetrics?> metricsProvider)
    {
        ArgumentNullException.ThrowIfNull(metricsProvider);
        _platformMetrics = metricsProvider;
    }

    internal static bool TryGetPlatformFontMetrics(Font font, out FontMetrics metrics)
    {
        if (OperatingSystem.IsAndroid() && !FontCollection.Shared.IsCustomFamily(font.Family) &&
            _platformMetrics?.Invoke(font) is { } value)
        {
            metrics = value;
            return true;
        }
        metrics = default;
        return false;
    }

    private RasterizedGlyph? RasterizePlatformOrStb(Font font, char character)
    {
        if (FontCollection.Shared.IsCustomFamily(font.Family))
            return _stbRasterizer.Rasterize(font, character);
        if (OperatingSystem.IsAndroid() && _platformRasterizer?.Invoke(font, character) is { } platform)
            return platform;
        return OperatingSystem.IsWindows()
            ? RasterizeWin32(font, character)
            : _stbRasterizer.Rasterize(font, character);
    }

    /// <summary>当前平台是否可用光栅化（Windows 或已加载字体）。</summary>
    public bool IsAvailable => OperatingSystem.IsWindows() || _stbRasterizer.IsAvailable ||
                               OperatingSystem.IsAndroid() && _platformRasterizer != null;

    /// <summary>清空字形缓存。</summary>
    public void Clear()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
            _stbRasterizer.Clear();
        }
    }

    /// <summary>光栅化指定字体的单个字符，返回字形位图与度量；不可用或失败时返回 null。</summary>
    /// <param name="font">目标字体。</param>
    /// <param name="character">要光栅化的字符。</param>
    public RasterizedGlyph? Rasterize(Font font, char character)
    {
        if (!IsAvailable) return null;
        var family = ResolveFontFamily(font.Family, character);
        var effectiveFont = family == font.Family
            ? font
            : new Font(family, font.Size, font.Weight, font.Style);
        if (!_cacheGlyphs)
            return RasterizePlatformOrStb(effectiveFont, character);
        var key = new GlyphKey(effectiveFont.Family, effectiveFont.Size, effectiveFont.Weight, effectiveFont.Style,
            character, FontCollection.Shared.CustomGeneration);

        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;

            var glyph = RasterizePlatformOrStb(effectiveFont, character);
            _cache[key] = glyph;
            return glyph;
        }
    }

    private static string ResolveFontFamily(string requestedFamily, char character)
    {
        if (FontCollection.Shared.IsCustomFamily(requestedFamily))
            return requestedFamily;
        if (!string.Equals(requestedFamily, "Segoe UI", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(requestedFamily, "Segoe UI Variable", StringComparison.OrdinalIgnoreCase))
            return requestedFamily;

        if (character is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9f')
            return "Yu Gothic UI";
        if (character is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff')
            return "Microsoft YaHei UI";
        return requestedFamily;
    }

    private static RasterizedGlyph? RasterizeWin32(Font font, char character)
    {
#if PLATFORM_WIN32
        var dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;

        var fontHandle = NativeMethods.CreateFont(
            -(int)MathF.Round(font.Size), 0, 0, 0, (int)font.Weight,
            font.Style == FontStyle.Italic ? 1u : 0u,
            0, 0, NativeMethods.DefaultCharset, 0, 0,
            NativeMethods.AntialiasedQuality, 0, font.Family);
        if (fontHandle == IntPtr.Zero)
        {
            NativeMethods.DeleteDC(dc);
            return null;
        }

        var oldFont = NativeMethods.SelectObject(dc, fontHandle);
        try
        {
            var transform = NativeMethods.Mat2.Identity;
            var size = NativeMethods.GetGlyphOutline(
                dc, character, NativeMethods.Gray8Bitmap,
                out var metrics, 0, IntPtr.Zero, ref transform);
            if (size == NativeMethods.GdiError) return null;

            var coverage = size == 0 ? [] : new byte[size];
            if (size > 0)
            {
                var handle = GCHandle.Alloc(coverage, GCHandleType.Pinned);
                try
                {
                    var written = NativeMethods.GetGlyphOutline(
                        dc, character, NativeMethods.Gray8Bitmap,
                        out metrics, size, handle.AddrOfPinnedObject(), ref transform);
                    if (written == NativeMethods.GdiError) return null;
                }
                finally
                {
                    handle.Free();
                }

                // GDI GGO_GRAY8_BITMAP coverage is 0..64; normalize to 0..255.
                for (var i = 0; i < coverage.Length; i++)
                    coverage[i] = (byte)Math.Min(255, coverage[i] * 255 / 64);
            }

            var width = (int)metrics.BlackBoxX;
            return new RasterizedGlyph
            {
                Width = width,
                Height = (int)metrics.BlackBoxY,
                Stride = (width + 3) & ~3,
                OffsetX = metrics.GlyphOrigin.X,
                OffsetY = -metrics.GlyphOrigin.Y,
                AdvanceX = metrics.CellIncrementX > 0
                    ? metrics.CellIncrementX
                    : Math.Max(1, (int)MathF.Round(font.Size * 0.5f)),
                Coverage = coverage
            };
        }
        finally
        {
            if (oldFont != IntPtr.Zero) NativeMethods.SelectObject(dc, oldFont);
            NativeMethods.DeleteObject(fontHandle);
            NativeMethods.DeleteDC(dc);
        }
#else
        return null;
#endif
    }

    internal static bool TryGetWin32FontMetrics(Font font, out FontMetrics metrics)
    {
#if PLATFORM_WIN32
        var family = ResolveGenericFontFamily(font.Family);
        var dc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            metrics = default;
            return false;
        }

        var fontHandle = NativeMethods.CreateFont(
            -(int)MathF.Round(font.Size), 0, 0, 0, (int)font.Weight,
            font.Style == FontStyle.Italic ? 1u : 0u,
            0, 0, NativeMethods.DefaultCharset, 0, 0,
            NativeMethods.AntialiasedQuality, 0, family);
        if (fontHandle == IntPtr.Zero)
        {
            NativeMethods.DeleteDC(dc);
            metrics = default;
            return false;
        }

        var oldFont = NativeMethods.SelectObject(dc, fontHandle);
        try
        {
            if (!NativeMethods.GetTextMetrics(dc, out var textMetrics))
            {
                metrics = default;
                return false;
            }

            metrics = new FontMetrics(
                -textMetrics.Ascent,
                -textMetrics.Ascent,
                textMetrics.Descent,
                textMetrics.Descent,
                textMetrics.ExternalLeading);
            return true;
        }
        finally
        {
            if (oldFont != IntPtr.Zero) NativeMethods.SelectObject(dc, oldFont);
            NativeMethods.DeleteObject(fontHandle);
            NativeMethods.DeleteDC(dc);
        }
#else
        metrics = default;
        return false;
#endif
    }

    internal static string ResolveGenericFontFamily(string family) => FontCollection.Shared.IsCustomFamily(family)
        ? family
        : family.ToLowerInvariant() switch
    {
        "sans-serif" or "system-ui" or "ui-sans-serif" => OperatingSystem.IsWindows()
            ? "Segoe UI"
            : OperatingSystem.IsAndroid() ? "sans-serif" : "DejaVu Sans",
        "serif" or "ui-serif" => OperatingSystem.IsWindows()
            ? "Times New Roman"
            : OperatingSystem.IsAndroid() ? "serif" : "DejaVu Serif",
        "monospace" or "ui-monospace" => OperatingSystem.IsWindows()
            ? "Consolas"
            : OperatingSystem.IsAndroid() ? "monospace" : "DejaVu Sans Mono",
        _ => family
    };

    private readonly record struct GlyphKey(
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style,
        char Character,
        int CustomGeneration);

#if PLATFORM_WIN32
    private static partial class NativeMethods
    {
        internal const uint DefaultCharset = 1;
        internal const uint AntialiasedQuality = 4;
        internal const uint Gray8Bitmap = 6;
        internal const uint GdiError = 0xFFFFFFFF;

        [LibraryImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
        internal static partial IntPtr CreateCompatibleDC(IntPtr dc);

        [LibraryImport("gdi32.dll", EntryPoint = "DeleteDC")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteDC(IntPtr dc);

        [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr CreateFont(
            int height, int width, int escapement, int orientation, int weight,
            uint italic, uint underline, uint strikeOut, uint charSet,
            uint outputPrecision, uint clipPrecision, uint quality,
            uint pitchAndFamily, string faceName);

        [LibraryImport("gdi32.dll", EntryPoint = "SelectObject")]
        internal static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DeleteObject(IntPtr obj);

        [LibraryImport("gdi32.dll", EntryPoint = "GetTextMetricsW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetTextMetrics(IntPtr dc, out TextMetrics metrics);

        [LibraryImport("gdi32.dll", EntryPoint = "GetGlyphOutlineW")]
        internal static partial uint GetGlyphOutline(
            IntPtr dc, uint character, uint format, out GlyphMetrics metrics,
            uint bufferSize, IntPtr buffer, ref Mat2 transform);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GlyphMetrics
        {
            internal uint BlackBoxX;
            internal uint BlackBoxY;
            internal Point GlyphOrigin;
            internal short CellIncrementX;
            internal short CellIncrementY;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Fixed
        {
            internal ushort Fraction;
            internal short Value;

            internal static Fixed One => new() { Value = 1 };
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Mat2
        {
            internal Fixed M11;
            internal Fixed M12;
            internal Fixed M21;
            internal Fixed M22;

            internal static Mat2 Identity => new() { M11 = Fixed.One, M22 = Fixed.One };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal struct TextMetrics
        {
            internal int Height;
            internal int Ascent;
            internal int Descent;
            internal int InternalLeading;
            internal int ExternalLeading;
            internal int AverageCharWidth;
            internal int MaxCharWidth;
            internal int Weight;
            internal int Overhang;
            internal int DigitizedAspectX;
            internal int DigitizedAspectY;
            internal ushort FirstChar;
            internal ushort LastChar;
            internal ushort DefaultChar;
            internal ushort BreakChar;
            internal byte Italic;
            internal byte Underlined;
            internal byte StruckOut;
            internal byte PitchAndFamily;
            internal byte CharSet;
        }
    }
#endif
}

internal static class SystemTextMeasurementRegistration
{
    private static readonly SystemGlyphRasterizer Rasterizer = SystemGlyphRasterizer.Shared;
    private static readonly ITextMetricsProvider MetricsProvider = new SystemTextMetricsProvider(Rasterizer);
    private static readonly object Sync = new();

#pragma warning disable CA2255 // Square.Text installs the optional font metrics provider for Square.Graphics.
    [ModuleInitializer]
    internal static void Register()
    {
        TextLayout.RegisterAdvanceProvider(MeasureAdvance);
        TextMetrics.RegisterProvider(MetricsProvider);
    }
#pragma warning restore CA2255

    private static float? MeasureAdvance(Rune rune, Font font)
    {
        if (!rune.IsBmp || !Rasterizer.IsAvailable) return null;
        var family = SystemGlyphRasterizer.ResolveGenericFontFamily(font.Family);
        var effectiveFont = family == font.Family
            ? font
            : new Font(family, font.Size, font.Weight, font.Style);
        lock (Sync)
            return Rasterizer.Rasterize(effectiveFont, (char)rune.Value)?.AdvanceX;
    }
}

/// <summary>基于系统光栅器提供字体与字形度量。</summary>
internal sealed class SystemTextMetricsProvider(SystemGlyphRasterizer rasterizer) : ITextMetricsProvider
{
    private readonly object _sync = new();
    private readonly Dictionary<FontMetricsKey, FontMetrics> _fontMetrics = [];

    /// <summary>获取指定字体的整体度量。</summary>
    /// <param name="font">目标字体。</param>
    /// <param name="metrics">输出的字体度量。</param>
    /// <returns>始终返回 true。</returns>
    public bool TryGetFontMetrics(Font font, out FontMetrics metrics)
    {
        var customFace = FontCollection.Shared.ResolveCustomFace(font.Family, font.Weight, font.Style);
        if (customFace?.TryGetFontMetrics(font.Size, out metrics) == true)
            return true;

        var key = new FontMetricsKey(font.Family, font.Size, font.Weight, font.Style);
        if (SystemGlyphRasterizer.TryGetPlatformFontMetrics(font, out metrics))
        {
            lock (_sync) _fontMetrics[key] = metrics;
            return true;
        }

        if (OperatingSystem.IsWindows() &&
            TryGetWin32FontMetrics(font, out metrics))
        {
            lock (_sync) _fontMetrics[key] = metrics;
            return true;
        }

        // 非 Windows：从解析到的系统字体条目读取真实度量（与 stb 光栅化一致）。
        // 估算 fallback（0.8em ascent）比实际字形边界矮，会导致选区/行盒无法覆盖字形墨迹。
        var resolved = FontCollection.Shared.Resolve(font.Family, 'A', font.Weight, font.Style);
        if (resolved?.TryGetFontMetrics(font.Size, out metrics) == true)
        {
            lock (_sync) _fontMetrics[key] = metrics;
            return true;
        }

        var height = Math.Max(1, font.Size * TextLayout.DefaultLineHeight);
        var ascent = font.Size * 0.8f;
        metrics = new FontMetrics(-ascent, -ascent, height - ascent, height - ascent, 0);
        lock (_sync) _fontMetrics[key] = metrics;
        return true;
    }

    private static bool TryGetWin32FontMetrics(Font font, out FontMetrics metrics)
    {
#if PLATFORM_WIN32
        return SystemGlyphRasterizer.TryGetWin32FontMetrics(font, out metrics);
#else
        metrics = default;
        return false;
#endif
    }

    /// <summary>获取指定字体的单个字形度量。</summary>
    /// <param name="font">目标字体。</param>
    /// <param name="rune">目标字符。</param>
    /// <param name="metrics">输出的字形度量。</param>
    /// <returns>成功获取返回 true；字符非 BMP 或光栅失败返回 false。</returns>
    public bool TryGetGlyphMetrics(Font font, Rune rune, out GlyphMetrics metrics)
    {
        if (!rune.IsBmp || !rasterizer.IsAvailable)
        {
            metrics = default;
            return false;
        }

        RasterizedGlyph? glyph;
        lock (_sync)
            glyph = rasterizer.Rasterize(font, (char)rune.Value);
        if (glyph == null)
        {
            metrics = default;
            return false;
        }

        metrics = new GlyphMetrics(
            glyph.AdvanceX,
            new Rect(
                glyph.OffsetX,
                glyph.OffsetY,
                glyph.Width,
                glyph.Height));
        return true;
    }

    private readonly record struct FontMetricsKey(
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style);

}
