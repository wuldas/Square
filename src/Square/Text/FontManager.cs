using Square.Text.Fonts;
using Square.Text.Glyph;

namespace Square.Text;

/// <summary>
/// 字体族解析与匹配（对齐 CSS Font Matching 的简化实现）。
/// 将 CSS <c>font-family</c> 列表与通用族映射为平台可用族名；优先已通过 <see cref="FontFace"/> 加载的族。
/// </summary>
public sealed class FontManager
{
    private IReadOnlyList<string> _defaultFallbackFamilies = [];

    /// <summary>进程内默认实例。</summary>
    public static FontManager Instance { get; } = new();

    /// <summary>应用级字形回退族，按顺序在局部列表之后、系统回退之前查询。建议在创建窗口前配置。</summary>
    public IReadOnlyList<string> DefaultFallbackFamilies
    {
        get => Volatile.Read(ref _defaultFallbackFamilies);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var families = value.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Volatile.Write(ref _defaultFallbackFamilies, Array.AsReadOnly(families));
        }
    }

    private readonly object _gate = new();
    private readonly HashSet<string> _loadedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _windowsFamilies = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _familyCache = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Segoe UI"] = "Segoe UI",
        ["Segoe UI Variable"] = "Segoe UI Variable",
        ["Segoe UI Symbol"] = "Segoe UI Symbol",
        ["Arial"] = "Arial",
        ["Helvetica"] = "Arial",
        ["Times New Roman"] = "Times New Roman",
        ["Times"] = "Times New Roman",
        ["Courier New"] = "Consolas",
        ["Courier"] = "Consolas",
        ["Consolas"] = "Consolas",
        ["Cascadia Code"] = "Cascadia Code",
        ["Cascadia Mono"] = "Cascadia Mono",
        ["sans-serif"] = OperatingSystem.IsAndroid() ? "sans-serif" : "Segoe UI",
        ["serif"] = OperatingSystem.IsAndroid() ? "serif" : "Times New Roman",
        ["monospace"] = OperatingSystem.IsAndroid() ? "monospace" : "Consolas",
        ["cursive"] = "Segoe Script",
        ["fantasy"] = OperatingSystem.IsAndroid() ? "sans-serif" : "Segoe UI",
        ["system-ui"] = OperatingSystem.IsAndroid() ? "sans-serif" : "Segoe UI",
        ["ui-sans-serif"] = OperatingSystem.IsAndroid() ? "sans-serif" : "Segoe UI",
        ["ui-serif"] = OperatingSystem.IsAndroid() ? "serif" : "Times New Roman",
        ["ui-monospace"] = OperatingSystem.IsAndroid() ? "monospace" : "Consolas",
    };

    /// <summary>已知/已缓存的族名列表（含已加载自定义族）。</summary>
    public IReadOnlyList<string> AvailableFamilies
    {
        get
        {
            lock (_gate)
            {
                return _familyCache.Keys.Concat(_loadedFamilies).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>默认字体集（对齐 <c>document.fonts</c>）。</summary>
    public FontFaceSet Fonts => FontFaceSet.Default;

    /// <summary>由 <see cref="FontFace"/> 加载成功后注册族名。</summary>
    public void RegisterLoadedFamily(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        lock (_gate)
        {
            _loadedFamilies.Add(family.Trim());
            // 自定义族解析为自身
            _familyCache[family.Trim()] = family.Trim();
        }
    }

    /// <summary>族名是否已知（系统映射或已加载 FontFace）。</summary>
    public bool IsFamilyKnown(string family)
    {
        var key = family.Trim().Trim('\'', '"');
        lock (_gate)
        {
            if (_loadedFamilies.Contains(key) || _familyCache.ContainsKey(key))
                return true;
        }
        return FontCollection.Shared.ContainsFamily(key);
    }

    /// <summary>解析单个族名或通用族（对齐 CSS 字体回退链中的一项）。</summary>
    public string ResolveFamily(string family)
    {
        var key = family.Trim().Trim('\'', '"');
        lock (_gate)
        {
            if (_loadedFamilies.Contains(key))
                return key;
            if (_familyCache.TryGetValue(key, out var resolved))
                return resolved;
        }
        return family;
    }

    /// <summary>从 CSS 列表中选择第一个可用字体族作为主族。</summary>
    public string ResolveFamilyList(string? fontFamilyCss)
    {
        var entries = Graphics.Font.ParseFamilyEntries(fontFamilyCss ?? "sans-serif");
        return ResolvePrimaryFamily(entries).Family;
    }

    private (string Family, int Index) ResolvePrimaryFamily(IReadOnlyList<(string Name, bool Quoted)> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var family = ResolveEntry(entry);
            if (!entry.Quoted && Graphics.Font.IsGenericFamily(entry.Name) || IsAvailableFamily(entry, family))
                return (family, i);
        }
        return (ResolveEntry(entries[0]), 0);
    }

    private string ResolveEntry((string Name, bool Quoted) entry) => entry.Quoted ? entry.Name :
        ResolveFamily(Graphics.Font.IsFontFamilyKeyword(entry.Name) ? "sans-serif" : entry.Name);

    private bool IsAvailableFamily((string Name, bool Quoted) entry, string family)
    {
        lock (_gate)
        {
            if (_loadedFamilies.Contains(family)) return true;
            if (!entry.Quoted && _familyCache.ContainsKey(entry.Name)) return true;
            if (OperatingSystem.IsWindows() && _windowsFamilies.TryGetValue(family, out var available))
                return available;
        }
        if (OperatingSystem.IsWindows())
        {
            var available = SystemGlyphRasterizer.TryGetWin32FontMetrics(
                new Graphics.Font(family, 16), out _, requireExactFamily: true);
            lock (_gate) _windowsFamilies[family] = available;
            return available;
        }
        return FontCollection.Shared.ContainsFamily(family);
    }

    /// <summary>匹配得到绘图用 <see cref="Graphics.Font"/>。</summary>
    public Graphics.Font Match(string family, float size, Graphics.FontWeight weight, Graphics.FontStyle style)
    {
        return new Graphics.Font(ResolveFamily(family), size, weight, style);
    }

    /// <summary>
    /// 从 CSS 字体属性构造 <see cref="Graphics.Font"/>（对齐 CSS 字体简写相关字段）。
    /// </summary>
    public Graphics.Font FromCss(
        string? fontFamily,
        string? fontSize,
        string? fontWeight = null,
        string? fontStyle = null,
        float defaultSize = 16f)
    {
        var entries = Graphics.Font.ParseFamilyEntries(fontFamily ?? "sans-serif");
        var (family, index) = ResolvePrimaryFamily(entries);
        var fallback = index + 1 < entries.Count
            ? Array.AsReadOnly(entries.Skip(index + 1).Select(ResolveEntry)
                .Where(name => !string.Equals(name, family, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            : (IReadOnlyList<string>)[];
        return new Graphics.Font(family, Graphics.Font.ParseSize(fontSize, defaultSize),
            Graphics.Font.ParseWeight(fontWeight), Graphics.Font.ParseStyle(fontStyle))
        {
            FallbackFamilies = fallback,
            UseLiteralFamily = entries[index].Quoted
        };
    }
}
