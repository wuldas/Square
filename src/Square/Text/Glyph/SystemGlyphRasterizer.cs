using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
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
    private static int _platformMetricsGeneration;

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
        // 先写委托再递增代际：观察到新代际的读者必然能看到新委托，
        // 度量缓存据此淘汰替换提供器之前缓存的估算/回退值。
        Interlocked.Increment(ref _platformMetricsGeneration);
    }

    /// <summary>平台度量提供器代际；参与字体度量缓存键，防止旧提供器的结果遮蔽新提供器。</summary>
    internal static int PlatformMetricsGeneration => Volatile.Read(ref _platformMetricsGeneration);

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

    private static readonly string[] WindowsSymbolFallbacks = ["Segoe UI Symbol", "Segoe UI Emoji", "Segoe UI"];
    private static readonly string[] WindowsEmojiFallbacks = ["Segoe UI Emoji", "Segoe UI Symbol", "Segoe UI"];

    /// <summary>光栅化单个 BMP 字符。补充平面字符请传入完整的 <see cref="Rune"/>。</summary>
    public RasterizedGlyph? Rasterize(Font font, char character) =>
        Rune.TryCreate(character, out var rune) ? Rasterize(font, rune) : null;

    /// <summary>按主族、局部、应用级和平台回退顺序查找并光栅化一个 Unicode 标量。</summary>
    public RasterizedGlyph? Rasterize(Font font, Rune rune)
    {
        if (!IsAvailable) return null;
        var family = OperatingSystem.IsWindows() && !font.UseLiteralFamily ? ResolveGenericFontFamily(font.Family) : font.Family;
        var generation = FontCollection.Shared.CustomGeneration;
        lock (_cacheGate)
        {
            if (RasterizeFamily(font, family, rune, generation) is { } primary) return primary;

            foreach (var fallback in font.FallbackFamilies)
                if (RasterizeFallback(font, family, fallback, rune, generation, literal: true) is { } glyph) return glyph;
            foreach (var fallback in FontManager.Instance.DefaultFallbackFamilies)
                if (RasterizeFallback(font, family, fallback, rune, generation) is { } glyph) return glyph;

            // Custom faces (notably icon fonts) and private-use positions are only matched explicitly.
            if (Rune.GetUnicodeCategory(rune) == System.Globalization.UnicodeCategory.PrivateUse ||
                FontCollection.Shared.IsCustomFamily(family)) return null;

            if (!OperatingSystem.IsWindows())
                return _stbRasterizer.Rasterize(font, rune, allowSystemFallback: true);

            var scriptFamily = rune.Value switch
            {
                >= 0x3040 and <= 0x30ff or >= 0xff66 and <= 0xff9f => "Yu Gothic UI",
                >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff => "Microsoft YaHei UI",
                >= 0xac00 and <= 0xd7af => "Malgun Gothic",
                _ => null
            };
            if (scriptFamily is not null &&
                RasterizeFallback(font, family, scriptFamily, rune, generation) is { } scriptGlyph)
                return scriptGlyph;

            foreach (var fallback in rune.IsBmp ? WindowsSymbolFallbacks : WindowsEmojiFallbacks)
                if (RasterizeFallback(font, family, fallback, rune, generation) is { } glyph) return glyph;
            return null;
        }
    }

    private RasterizedGlyph? RasterizeFallback(Font font, string primary, string fallback, Rune rune, int generation, bool literal = false)
    {
        if (string.IsNullOrWhiteSpace(fallback)) return null;
        var family = OperatingSystem.IsWindows() && !literal ? ResolveGenericFontFamily(fallback) : fallback;
        return string.Equals(family, primary, StringComparison.OrdinalIgnoreCase)
            ? null : RasterizeFamily(font, family, rune, generation);
    }

    private RasterizedGlyph? RasterizeFamily(Font font, string family, Rune rune, int generation)
    {
        var key = new GlyphKey(family, font.Size, font.Weight, font.Style, rune.Value, generation);
        if (_cacheGlyphs && _cache.TryGetValue(key, out var cached)) return cached;

        var face = family == font.Family ? font : new Font(family, font.Size, font.Weight, font.Style);
        RasterizedGlyph? glyph;
        if (FontCollection.Shared.IsCustomFamily(family) || !OperatingSystem.IsWindows())
        {
            glyph = OperatingSystem.IsAndroid() && rune.IsBmp && !FontCollection.Shared.IsCustomFamily(family)
                ? _platformRasterizer?.Invoke(face, (char)rune.Value)
                : null;
            glyph ??= _stbRasterizer.Rasterize(face, rune, allowSystemFallback: false);
        }
        else
        {
            glyph = RasterizeWin32(face, rune);
        }

        if (_cacheGlyphs) _cache[key] = glyph;
        return glyph;
    }

    private static RasterizedGlyph? RasterizeWin32(Font font, Rune rune)
    {
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
            var glyphIndex = GetWin32GlyphIndex(dc, font.Family, rune);
            if (glyphIndex == 0) return null;

            var transform = NativeMethods.Mat2.Identity;
            const uint format = NativeMethods.Gray8Bitmap | NativeMethods.GlyphIndex;
            var size = NativeMethods.GetGlyphOutline(
                dc, glyphIndex, format,
                out var metrics, 0, IntPtr.Zero, ref transform);
            if (size == NativeMethods.GdiError) return null;

            var coverage = size == 0 ? [] : new byte[size];
            if (size > 0)
            {
                var handle = GCHandle.Alloc(coverage, GCHandleType.Pinned);
                try
                {
                    var written = NativeMethods.GetGlyphOutline(
                        dc, glyphIndex, format,
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
    }

    private static unsafe uint GetWin32GlyphIndex(IntPtr dc, string family, Rune rune)
    {
        if (!Win32FaceMatches(dc, family)) return 0;

        if (rune.IsBmp)
        {
            var character = (char)rune.Value;
            ushort index;
            return NativeMethods.GetGlyphIndices(dc, &character, 1, &index, NativeMethods.MarkMissingGlyphs)
                       == NativeMethods.GdiError || index == 0xffff ? 0u : index;
        }

        // GDI's character APIs accept one UTF-16 code unit per glyph. Read the selected
        // face's format-12 cmap to obtain the glyph index for a supplementary scalar.
        const uint cmapTag = 0x70616d63; // little-endian GDI table tag for 'cmap'
        var size = NativeMethods.GetFontData(dc, cmapTag, 0, null, 0);
        if (size < 4 || size > 16 * 1024 * 1024) return 0;
        var table = new byte[size];
        fixed (byte* buffer = table)
            if (NativeMethods.GetFontData(dc, cmapTag, 0, buffer, size) != size) return 0;
        return FindSupplementaryGlyph(table, (uint)rune.Value);
    }

    private static unsafe bool Win32FaceMatches(IntPtr dc, string family)
    {
        // CreateFont can silently substitute the default face for an unavailable family.
        Span<char> face = stackalloc char[64];
        fixed (char* name = face)
        {
            var length = NativeMethods.GetTextFace(dc, face.Length, name);
            if (length <= 0 || length > face.Length) return false;
            var actual = face[..length];
            if (actual[^1] == '\0') actual = actual[..^1];
            return actual.Equals(family, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static uint FindSupplementaryGlyph(ReadOnlySpan<byte> cmap, uint scalar)
    {
        var records = BinaryPrimitives.ReadUInt16BigEndian(cmap[2..]);
        for (var i = 0; i < records && 4 + (i + 1) * 8 <= cmap.Length; i++)
        {
            var record = cmap[(4 + i * 8)..];
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var encoding = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            if (platform != 0 && (platform != 3 || encoding != 10)) continue;
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
            if (offset > cmap.Length - 16) continue;
            var subtable = cmap[(int)offset..];
            if (BinaryPrimitives.ReadUInt16BigEndian(subtable) != 12) continue;
            var length = BinaryPrimitives.ReadUInt32BigEndian(subtable[4..]);
            var groups = BinaryPrimitives.ReadUInt32BigEndian(subtable[12..]);
            if (length < 16 || length > subtable.Length || groups > (length - 16) / 12) continue;

            var low = 0;
            var high = (int)groups - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var group = subtable[(16 + middle * 12)..];
                var first = BinaryPrimitives.ReadUInt32BigEndian(group);
                var last = BinaryPrimitives.ReadUInt32BigEndian(group[4..]);
                if (scalar < first) high = middle - 1;
                else if (scalar > last) low = middle + 1;
                else
                {
                    var startIndex = BinaryPrimitives.ReadUInt32BigEndian(group[8..]);
                    var index = (ulong)startIndex + scalar - first;
                    return index <= ushort.MaxValue ? (uint)index : 0;
                }
            }
        }
        return 0;
    }
    internal static bool TryGetWin32FontMetrics(Font font, out FontMetrics metrics, bool requireExactFamily = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            metrics = default;
            return false;
        }

        var family = requireExactFamily || font.UseLiteralFamily ? font.Family : ResolveGenericFontFamily(font.Family);
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
            if (requireExactFamily && !Win32FaceMatches(dc, family) || !NativeMethods.GetTextMetrics(dc, out var textMetrics))
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
        int CodePoint,
        int CustomGeneration);

    private static partial class NativeMethods
    {
        internal const uint DefaultCharset = 1;
        internal const uint AntialiasedQuality = 4;
        internal const uint Gray8Bitmap = 6;
        internal const uint GlyphIndex = 0x80;
        internal const uint MarkMissingGlyphs = 1;
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

        [LibraryImport("gdi32.dll", EntryPoint = "GetTextFaceW")]
        internal static unsafe partial int GetTextFace(IntPtr dc, int count, char* name);

        [LibraryImport("gdi32.dll", EntryPoint = "GetGlyphIndicesW")]
        internal static unsafe partial uint GetGlyphIndices(IntPtr dc, char* text, int count, ushort* indices, uint flags);

        [LibraryImport("gdi32.dll")]
        internal static unsafe partial uint GetFontData(IntPtr dc, uint table, uint offset, byte* buffer, uint size);

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
}

internal static class SystemTextMeasurementRegistration
{
    private static readonly SystemGlyphRasterizer Rasterizer = SystemGlyphRasterizer.Shared;
    internal static readonly ITextMetricsProvider MetricsProvider = new SystemTextMetricsProvider(Rasterizer);
    private static readonly object Sync = new();

#pragma warning disable CA2255 // Square.Text installs the optional font metrics provider for Square.Graphics.
    [ModuleInitializer]
    internal static void Register()
    {
        TextLayout.RegisterAdvanceProvider(MeasureAdvance);
        TextMetrics.RegisterProvider(MetricsProvider);
    }
#pragma warning restore CA2255

    internal static float? MeasureAdvance(Rune rune, Font font)
    {
        lock (Sync)
            return Rasterizer.Rasterize(font, rune)?.AdvanceX;
    }
}

/// <summary>基于系统光栅器提供字体与字形度量。</summary>
internal sealed class SystemTextMetricsProvider(SystemGlyphRasterizer rasterizer) : ITextMetricsProvider
{
    // 度量缓存上限：键为 (族, 字号, 字重, 样式, 字体/提供器代际)，溢出时整体清空重建，
    // 与字形缓存保持同等简单的生命周期约定，避免无界增长；重新计算仅一轮原生查询。
    private const int MaxMetricsEntries = 256;

    private readonly object _sync = new();
    private readonly Dictionary<FontMetricsKey, FontMetrics> _fontMetrics = [];

    /// <summary>获取指定字体的整体度量。</summary>
    /// <param name="font">目标字体。</param>
    /// <param name="metrics">输出的字体度量。</param>
    /// <returns>始终返回 true。</returns>
    public bool TryGetFontMetrics(Font font, out FontMetrics metrics)
    {
        // 键携带自定义字体与平台度量提供器代际：代际不变意味着自定义面集合未变
        // （FontCollection.Register 是唯一变更入口并递增 CustomGeneration），
        // 因此命中时可安全跳过自定义面解析与原生 GDI 查询，也不会用旧值
        // 遮蔽之后注册/替换的真实字体度量（注册本身会推进代际使旧键失配）。
        var key = new FontMetricsKey(
            font.Family,
            font.Size,
            font.Weight,
            font.Style,
            FontCollection.Shared.CustomGeneration,
            SystemGlyphRasterizer.PlatformMetricsGeneration);
        lock (_sync)
        {
            if (_fontMetrics.TryGetValue(key, out metrics))
                return true;
        }

        var customFace = FontCollection.Shared.ResolveCustomFace(font.Family, font.Weight, font.Style);
        if (customFace?.TryGetFontMetrics(font.Size, out metrics) == true)
        {
            Store(key, metrics);
            return true;
        }

        if (SystemGlyphRasterizer.TryGetPlatformFontMetrics(font, out metrics))
        {
            Store(key, metrics);
            return true;
        }

        if (SystemGlyphRasterizer.TryGetWin32FontMetrics(font, out metrics))
        {
            Store(key, metrics);
            return true;
        }

        // 非 Windows：从解析到的系统字体条目读取真实度量（与 stb 光栅化一致）。
        // 估算 fallback（0.8em ascent）比实际字形边界矮，会导致选区/行盒无法覆盖字形墨迹。
        var resolved = FontCollection.Shared.Resolve(font.Family, 'A', font.Weight, font.Style);
        if (resolved?.TryGetFontMetrics(font.Size, out metrics) == true)
        {
            Store(key, metrics);
            return true;
        }

        var height = Math.Max(1, font.Size * TextLayout.DefaultLineHeight);
        var ascent = font.Size * 0.8f;
        metrics = new FontMetrics(-ascent, -ascent, height - ascent, height - ascent, 0);
        Store(key, metrics);
        return true;
    }

    /// <summary>获取指定字体的单个字形度量。</summary>
    /// <param name="font">目标字体。</param>
    /// <param name="rune">目标字符。</param>
    /// <param name="metrics">输出的字形度量。</param>
    /// <returns>成功获取返回 true；光栅失败返回 false。</returns>
    public bool TryGetGlyphMetrics(Font font, Rune rune, out GlyphMetrics metrics)
    {
        if (!rasterizer.IsAvailable)
        {
            metrics = default;
            return false;
        }

        RasterizedGlyph? glyph;
        lock (_sync)
            glyph = rasterizer.Rasterize(font, rune);
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

    private void Store(FontMetricsKey key, FontMetrics metrics)
    {
        lock (_sync)
        {
            if (_fontMetrics.Count >= MaxMetricsEntries && !_fontMetrics.ContainsKey(key))
                _fontMetrics.Clear();
            _fontMetrics[key] = metrics;
        }
    }

    private readonly record struct FontMetricsKey(
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style,
        int CustomGeneration,
        int PlatformMetricsGeneration);
}
