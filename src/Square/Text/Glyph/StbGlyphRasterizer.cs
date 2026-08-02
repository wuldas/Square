using System.Runtime.InteropServices;
using System.Text;
using Square.Graphics;
using StbTrueTypeSharp;

namespace Square.Text.Glyph;

/// <summary>基于 stb_truetype 的字形光栅器，用于非 Windows 平台或自定义字体的回退光栅化。</summary>
internal sealed class StbGlyphRasterizer
{
    private readonly Dictionary<GlyphKey, RasterizedGlyph?> _cache = [];
    private readonly FontCollection _fonts = FontCollection.Shared;

    /// <summary>当前是否已加载任何可用字体。</summary>
    public bool IsAvailable => _fonts.HasAnyFont;

    /// <summary>光栅化指定字体的单个字符，返回字形位图与度量；不可用或无字形时返回 null。</summary>
    /// <param name="font">目标字体。</param>
    /// <param name="character">要光栅化的字符。</param>
    public RasterizedGlyph? Rasterize(Font font, char character)
    {
        if (!IsAvailable) return null;
        var entry = _fonts.Resolve(font.Family, character);
        if (entry == null) return null;

        var effectiveFont = entry.Family == font.Family
            ? font
            : new Font(entry.Family, font.Size, font.Weight, font.Style);
        var key = new GlyphKey(effectiveFont.Family, effectiveFont.Size, effectiveFont.Weight, effectiveFont.Style, character);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var glyph = RasterizeStb(entry, effectiveFont, character);
        _cache[key] = glyph;
        return glyph;
    }

    private static unsafe RasterizedGlyph? RasterizeStb(FontEntry entry, Font font, char character)
    {
        var info = entry.AcquireFontInfo();
        if (info == null) return null;

        // GDI CreateFont(-size) uses character height; ScaleForPixelHeight matches that
        // for most fonts, but UI fonts can look slightly smaller than Segoe UI optically.
        // A modest boost keeps Linux text closer to Windows without changing layout CSS.
        var pixelHeight = font.Size * 1.12f;
        var scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelHeight);
        if (scale <= 0) return null;

        var codepoint = (int)character;
        var glyphIndex = StbTrueType.stbtt_FindGlyphIndex(info, codepoint);
        if (glyphIndex == 0) return null;

        int advanceWidth, leftSideBearing;
        StbTrueType.stbtt_GetCodepointHMetrics(info, codepoint, &advanceWidth, &leftSideBearing);

        int width, height, xoff, yoff;
        byte* bitmap = StbTrueType.stbtt_GetCodepointBitmap(info, scale, scale, codepoint, &width, &height, &xoff, &yoff);
        try
        {
            if (bitmap == null || width <= 0 || height <= 0)
            {
                return new RasterizedGlyph
                {
                    Width = 0,
                    Height = 0,
                    Stride = 0,
                    OffsetX = xoff,
                    OffsetY = yoff,
                    AdvanceX = (int)MathF.Round(advanceWidth * scale),
                    Coverage = []
                };
            }

            var stride = (width + 3) & ~3;
            var coverage = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy((IntPtr)(bitmap + y * width), coverage, y * stride, width);
            }

            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);
            return new RasterizedGlyph
            {
                Width = width,
                Height = height,
                Stride = stride,
                OffsetX = xoff,
                OffsetY = (int)MathF.Round(ascent * scale) + yoff,
                AdvanceX = (int)MathF.Round(advanceWidth * scale),
                Coverage = coverage
            };
        }
        finally
        {
            if (bitmap != null) StbTrueType.stbtt_FreeBitmap(bitmap, null);
        }
    }

    private readonly record struct GlyphKey(
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style,
        char Character);
}

/// <summary>已加载或延迟加载的字体条目，封装 stbtt_fontinfo 与字体数据来源。</summary>
internal sealed class FontEntry
{
    private readonly string? _path;
    private readonly int _offset;
    private byte[]? _data;
    private StbTrueType.stbtt_fontinfo? _info;

    /// <summary>字体族名称。</summary>
    public string Family { get; }

    /// <summary>已加载字节（内存中）；路径字体在首次使用时再读入。</summary>
    public FontEntry(string family, byte[] data, int offset = 0)
    {
        Family = family;
        _data = data;
        _offset = offset;
    }

    /// <summary>延迟从文件加载，避免启动时把整个系统字体目录读进内存。</summary>
    public FontEntry(string family, string path, int offset = 0)
    {
        Family = family;
        _path = path;
        _offset = offset;
    }

    /// <summary>获取或初始化 stbtt_fontinfo；首次调用时加载字体数据。</summary>
    /// <returns>字体信息；加载失败返回 null。</returns>
    public StbTrueType.stbtt_fontinfo? AcquireFontInfo()
    {
        if (_info != null) return _info;
        var data = EnsureData();
        if (data == null) return null;
        var offset = _offset;
        if (offset == 0)
        {
            var firstFaceOffset = GetFirstFaceOffset(data);
            if (firstFaceOffset >= 0) offset = firstFaceOffset;
        }
        _info = StbTrueType.CreateFont(data, offset);
        return _info;
    }

    private static unsafe int GetFirstFaceOffset(byte[] data)
    {
        fixed (byte* pointer = data)
            return StbTrueType.stbtt_GetFontOffsetForIndex(pointer, 0);
    }

    private byte[]? EnsureData()
    {
        if (_data != null) return _data;
        if (string.IsNullOrEmpty(_path)) return null;
        try
        {
            _data = File.ReadAllBytes(_path);
            return _data;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 系统与已注册自定义字体集合。进程内共享 <see cref="Shared"/>，供光栅与 FontFace 加载使用。
/// </summary>
internal sealed class FontCollection
{
    private static readonly Lazy<FontCollection> SharedLazy = new(() => new FontCollection());

    /// <summary>进程内共享字体集合（系统字体 + FontFace 注册）。</summary>
    public static FontCollection Shared => SharedLazy.Value;

    private readonly object _gate = new();
    private readonly Dictionary<string, FontEntry> _byFamily = new(NormalizedComparer.Instance);
    private readonly HashSet<string> _customFamilies = new(NormalizedComparer.Instance);
    private readonly List<FontEntry> _fallbacks = [];
    private string? _cjkFamily;
    private string? _japaneseFamily;
    private string? _koreanFamily;

    /// <summary>是否已加载任何字体。</summary>
    public bool HasAnyFont { get; private set; }

    private FontCollection()
    {
        try
        {
            LoadSystemFonts();
        }
        catch
        {
        }
        ConfigureScriptFallbacks();
    }

    /// <summary>
    /// 注册已加载的字体数据（FontFace.Load 成功后调用）。
    /// 同名族覆盖为自定义面，并优先于系统回退。
    /// </summary>
    public void Register(string family, byte[] data, int offset = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            var entry = new FontEntry(family, data, offset);
            var norm = Normalize(family);
            _byFamily[norm] = entry;
            _customFamilies.Add(norm);
            // Custom faces are selected only by family name. They must not become
            // fallbacks for normal text, especially for private-use icon fonts.
            _fallbacks.RemoveAll(e => Normalize(e.Family) == norm);
            HasAnyFont = true;
        }
    }

    /// <summary>是否已有该族（系统或已注册）。</summary>
    public bool ContainsFamily(string family)
    {
        lock (_gate)
            return _byFamily.ContainsKey(Normalize(family));
    }

    /// <summary>判断指定字体族是否为已注册的自定义字体族。</summary>
    /// <param name="family">字体族名称。</param>
    /// <returns>是自定义族返回 true。</returns>
    public bool IsCustomFamily(string family)
    {
        lock (_gate)
            return _customFamilies.Contains(Normalize(family));
    }

    /// <summary>按请求字体族与字符解析最合适的字体条目。</summary>
    /// <param name="requestedFamily">请求的字体族名称。</param>
    /// <param name="character">用于脚本回退判断的字符。</param>
    /// <returns>匹配的字体条目；无可用字体返回 null。</returns>
    public FontEntry? Resolve(string requestedFamily, char character)
    {
        lock (_gate)
        {
            if (_byFamily.Count == 0) return null;

            var normRequested = Normalize(requestedFamily);
            // 按 Unicode 范围选脚本回退族（不建 per-char 大字典，省内存）
            var scriptFamily = ResolveScriptFamily(character);
            if (scriptFamily != null
                && _byFamily.TryGetValue(Normalize(scriptFamily), out var scriptEntry))
                return scriptEntry;

            if (!string.IsNullOrEmpty(requestedFamily) && _byFamily.TryGetValue(normRequested, out var entry))
                return entry;

            foreach (var fb in _fallbacks)
            {
                if (Normalize(fb.Family) != normRequested) return fb;
            }
            return _fallbacks.Count > 0 ? _fallbacks[0] : null;
        }
    }

    private string? ResolveScriptFamily(char character)
    {
        if (character is >= '\u3040' and <= '\u30ff' or >= '\uff66' and <= '\uff9f')
            return _japaneseFamily;
        if (character is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff')
            return _cjkFamily;
        if (character is >= '\uac00' and <= '\ud7af')
            return _koreanFamily;
        return null;
    }

    private void ConfigureScriptFallbacks()
    {
        if (_byFamily.Count == 0) return;

        string Pick(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                var n = Normalize(c);
                if (_byFamily.ContainsKey(n)) return c;
            }
            return _fallbacks.Count > 0 ? _fallbacks[0].Family : _byFamily.First().Key;
        }

        _cjkFamily = Pick("PingFang SC", "Hiragino Sans",
                          "NotoSansCJK", "NotoSansCJKsc", "NotoSansCJKtc", "NotoSansCJKjp",
                          "SourceHanSansSC", "SourceHanSansCN", "WenQuanYiZenHei",
                          "DroidSansFallback", "Microsoft YaHei UI", "MicrosoftYaHeiUI", "Yu Gothic UI", "YuGothicUI");
        _japaneseFamily = Pick("Hiragino Sans", "PingFang SC", "NotoSansCJKjp",
                               "Yu Gothic UI", "YuGothicUI", "Yu Gothic", "YuGothic", _cjkFamily);
        _koreanFamily = Pick("NotoSansCJKkr", "Malgun Gothic", "MalgunGothic", _cjkFamily);
    }

    private void LoadSystemFonts()
    {
        // 仅索引「优先族」字体路径，按需读字节，避免 Windows Fonts 全量进内存（可达数百 MB）。
        var preferred = GetPreferredFamilies();
        var roots = GetPlatformFontRoots();
        var remaining = new HashSet<string>(preferred, StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root) || remaining.Count == 0) break;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions
                {
                    RecurseSubdirectories = !OperatingSystem.IsWindows(),
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                });
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (!file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                    continue;

                var nameKey = Path.GetFileNameWithoutExtension(file);
                var family = GuessFamilyFromName(nameKey);
                if (family == null) continue;

                string? matchedPreferred = null;
                foreach (var pref in remaining)
                {
                    if (family.Contains(pref, StringComparison.OrdinalIgnoreCase)
                        || pref.Contains(family, StringComparison.OrdinalIgnoreCase)
                        || Normalize(family) == Normalize(pref))
                    {
                        matchedPreferred = pref;
                        break;
                    }
                }
                if (matchedPreferred == null) continue;

                var normFamily = Normalize(matchedPreferred);
                if (_byFamily.ContainsKey(normFamily))
                {
                    remaining.Remove(matchedPreferred);
                    continue;
                }

                // 延迟加载：只记路径，首次绘制该族时再读文件
                var entry = new FontEntry(matchedPreferred, file, offset: 0);
                _byFamily[normFamily] = entry;
                _fallbacks.Add(entry);
                HasAnyFont = true;
                remaining.Remove(matchedPreferred);
                if (remaining.Count == 0) break;
            }
        }

        RegisterAliases();
    }

    private static string[] GetPreferredFamilies()
    {
        if (OperatingSystem.IsWindows())
        {
            return
            [
                "Segoe UI",
                "Segoe UI Variable",
                "Segoe UI Symbol",
                "Microsoft YaHei UI",
                "Yu Gothic UI",
                "Malgun Gothic",
                "Cascadia Mono",
                "Cascadia Code",
                "Consolas",
                "Times New Roman",
                "Arial",
                "Segoe UI Emoji"
            ];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                "Ubuntu",
                "Noto Sans",
                "NotoSans",
                "DejaVu Sans",
                "DejaVuSans",
                "Noto Sans CJK SC",
                "NotoSansCJK",
                "Noto Sans Mono",
                "DejaVu Sans Mono"
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "Helvetica",
                "Helvetica Neue",
                "PingFang SC",
                "Hiragino Sans",
                "Menlo",
                "Times New Roman",
                "Arial"
            ];
        }

        return ["sans-serif"];
    }

    private void RegisterAliases()
    {
        void Alias(string alias, string target)
        {
            var normTarget = Normalize(target);
            if (_byFamily.TryGetValue(normTarget, out var entry))
            {
                var normAlias = Normalize(alias);
                if (!_byFamily.ContainsKey(normAlias))
                    _byFamily[normAlias] = entry;
            }
        }

        string FirstAvailable(params string[] candidates)
        {
            foreach (var c in candidates)
            {
                if (_byFamily.ContainsKey(Normalize(c))) return c;
            }
            return _fallbacks.Count > 0 ? _fallbacks[0].Family : candidates[0];
        }

        if (_byFamily.Count == 0) return;

        if (OperatingSystem.IsWindows())
        {
            Alias("sans-serif", "Segoe UI");
            Alias("serif", "Times New Roman");
            Alias("monospace", "Consolas");
            Alias("Arial", "Segoe UI");
        }
        else
        {
            // Prefer modern UI fonts with metrics closer to Segoe UI when available.
            var sans = FirstAvailable("Ubuntu", "UbuntuSans", "NotoSans", "DejaVuSans");
            var serif = FirstAvailable("NotoSerif", "DejaVuSerif");
            var mono = FirstAvailable("UbuntuMono", "UbuntuSansMono", "NotoSansMono", "DejaVuSansMono");
            Alias("Segoe UI", sans);
            Alias("sans-serif", sans);
            Alias("Arial", sans);
            Alias("serif", serif);
            Alias("Times New Roman", serif);
            Alias("monospace", mono);
            Alias("Consolas", mono);
        }
    }

    private static unsafe int GetTtcOffset(byte[] data, int index)
    {
        fixed (byte* p = data)
            return StbTrueType.stbtt_GetFontOffsetForIndex(p, index);
    }

    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private sealed class NormalizedComparer : IEqualityComparer<string>
    {
        public static readonly NormalizedComparer Instance = new();
        public bool Equals(string? x, string? y) => Normalize(x ?? "") == Normalize(y ?? "");
        public int GetHashCode(string obj) => Normalize(obj ?? "").GetHashCode();
    }

    private static string? GuessFamilyFromName(string fileName)
    {
        var name = fileName.Replace('-', ' ').Replace('_', ' ');
        var cleaned = string.Join(' ', name.Split(' ')
            .Where(t => !t.Equals("Regular", StringComparison.OrdinalIgnoreCase)
                     && !t.Equals("Book", StringComparison.OrdinalIgnoreCase)
                     && !t.Equals("Normal", StringComparison.OrdinalIgnoreCase)));
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static IEnumerable<string> GetPlatformFontRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return [Path.Combine(winDir, "Fonts")];
        }

        if (OperatingSystem.IsLinux())
        {
            return
            [
                "/usr/share/fonts",
                "/usr/local/share/fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "fonts")
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "/System/Library/Fonts",
                "/Library/Fonts",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts")
            ];
        }

        return [];
    }
}
