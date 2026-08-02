using Square.Text.Fonts;
using Square.Text.Glyph;

namespace Square.Text;

/// <summary>
/// 字体族解析与匹配（对齐 CSS Font Matching 的简化实现）。
/// 将 CSS <c>font-family</c> 列表与通用族映射为平台可用族名；优先已通过 <see cref="FontFace"/> 加载的族。
/// </summary>
public sealed class FontManager
{
    private static FontManager? _instance;

    /// <summary>进程内默认实例。</summary>
    public static FontManager Instance => _instance ??= new FontManager();

    private readonly object _gate = new();
    private readonly HashSet<string> _loadedFamilies = new(StringComparer.OrdinalIgnoreCase);

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
        ["sans-serif"] = "Segoe UI",
        ["serif"] = "Times New Roman",
        ["monospace"] = "Consolas",
        ["cursive"] = "Segoe Script",
        ["fantasy"] = "Segoe UI",
        ["system-ui"] = "Segoe UI",
        ["ui-sans-serif"] = "Segoe UI",
        ["ui-serif"] = "Times New Roman",
        ["ui-monospace"] = "Consolas",
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

    /// <summary>
    /// 按 CSS <c>font-family</c> 列表顺序做简化 font matching：
    /// 优先已加载 FontFace / 缓存中的已知项，否则回退到 sans-serif。
    /// </summary>
    public string ResolveFamilyList(string? fontFamilyCss)
    {
        string? first = null;
        foreach (var family in Graphics.Font.ParseFamilyList(fontFamilyCss))
        {
            first ??= family;
            var key = family.Trim().Trim('\'', '"');
            lock (_gate)
            {
                if (_loadedFamilies.Contains(key))
                    return key;
                if (_familyCache.ContainsKey(key))
                    return ResolveFamily(key);
            }
            if (FontCollection.Shared.ContainsFamily(key))
                return key;
        }
        return ResolveFamily(first ?? "sans-serif");
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
        return Graphics.Font.FromCss(
            fontFamily,
            fontSize,
            fontWeight,
            fontStyle,
            defaultSize,
            css => ResolveFamilyList(css));
    }
}
