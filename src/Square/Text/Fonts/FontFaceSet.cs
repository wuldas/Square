using System.Collections;
using System.Text.RegularExpressions;
using Square.Graphics;

namespace Square.Text.Fonts;

/// <summary>
/// 字体面集合（对齐 CSS Font Loading <c>FontFaceSet</c> / <c>document.fonts</c> 子集）。
/// 支持 Add、异步 Load、Check 与 Ready。
/// </summary>
public sealed class FontFaceSet : IReadOnlyCollection<FontFace>
{
    private static readonly Lazy<FontFaceSet> DefaultLazy = new(() => new FontFaceSet());
    private readonly object _gate = new();
    private readonly List<FontFace> _faces = [];

    /// <summary>进程默认字体集（对齐单页应用的 <c>document.fonts</c>）。</summary>
    public static FontFaceSet Default => DefaultLazy.Value;

    /// <summary>集合中的字体面数量（对齐 <c>size</c>）。</summary>
    public int Count
    {
        get { lock (_gate) return _faces.Count; }
    }

    /// <summary>添加字体面（对齐 <c>add</c>）；不自动 load。</summary>
    public void Add(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        lock (_gate)
        {
            if (!_faces.Contains(face))
                _faces.Add(face);
        }
    }

    /// <summary>移除字体面（对齐 <c>delete</c>）。</summary>
    public bool Delete(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        lock (_gate)
            return _faces.Remove(face);
    }

    /// <summary>清空集合（对齐 <c>clear</c>）。</summary>
    public void Clear()
    {
        lock (_gate)
            _faces.Clear();
    }

    /// <summary>是否包含该字体面实例。</summary>
    public bool Contains(FontFace face)
    {
        ArgumentNullException.ThrowIfNull(face);
        lock (_gate)
            return _faces.Contains(face);
    }

    /// <summary>按族名、字重与样式选择已加载的最佳字体面。</summary>
    public FontFace? Match(
        string family,
        FontWeight weight = FontWeight.Normal,
        FontStyle style = FontStyle.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        family = family.Trim().Trim('\'', '"');
        lock (_gate)
        {
            return _faces
                .Where(face => face.Status == FontFaceLoadStatus.Loaded &&
                               string.Equals(face.Family, family, StringComparison.OrdinalIgnoreCase))
                .OrderBy(face => face.Style == style ? 0 : 1)
                .ThenBy(face => Math.Abs((int)face.Weight - (int)weight))
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// 检查是否已有可用于描述的字体（对齐 <c>check</c> 简化版）。
    /// <paramref name="font"/> 形如 <c>16px MyFont</c> 或 <c>MyFont</c>。
    /// </summary>
    public bool Check(string font)
    {
        var description = ParseFontDescription(font);
        var family = description.Family;
        if (string.IsNullOrEmpty(family)) return false;

        if (Match(family, description.Weight, description.Style) != null)
            return true;

        return Square.Text.FontManager.Instance.IsFamilyKnown(family);
    }

    /// <summary>
    /// 加载匹配的字体面（对齐 <c>load</c> 简化版）。
    /// 对集合中族名匹配且未加载的 <see cref="FontFace"/> 调用 <see cref="FontFace.LoadAsync"/>。
    /// </summary>
    public async Task LoadAsync(string font, string text = " ", CancellationToken cancellationToken = default)
    {
        _ = text; // 完整实现可按字符子集加载；当前加载整个 face
        var family = ParseFamilyFromFont(font);
        List<FontFace> toLoad;
        lock (_gate)
        {
            toLoad = _faces
                .Where(f =>
                    (string.IsNullOrEmpty(family) ||
                     string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase)) &&
                    f.Status != FontFaceLoadStatus.Loaded)
                .ToList();
        }

        foreach (var face in toLoad)
            await face.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待集合中全部字体面加载结束（成功或失败均算完成）（对齐 <c>ready</c> 简化版）。
    /// </summary>
    public Task Ready
    {
        get
        {
            List<Task> tasks;
            lock (_gate)
            {
                tasks = _faces
                    .Where(f => f.Status is FontFaceLoadStatus.Unloaded or FontFaceLoadStatus.Loading)
                    .Select(f => f.LoadAsync())
                    .ToList();
            }

            if (tasks.Count == 0)
                return Task.CompletedTask;

            return Task.WhenAll(tasks.Select(async t =>
            {
                try { await t.ConfigureAwait(false); }
                catch { /* ready 不因单个失败而失败 */ }
            }));
        }
    }

    /// <summary>枚举字体面。</summary>
    public IEnumerator<FontFace> GetEnumerator()
    {
        lock (_gate)
            return _faces.ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>从 <c>16px Family</c> 或 <c>Family</c> 解析族名。</summary>
    public static string ParseFamilyFromFont(string font)
    {
        return ParseFontDescription(font).Family;
    }

    private static (string Family, FontWeight Weight, FontStyle Style) ParseFontDescription(string font)
    {
        if (string.IsNullOrWhiteSpace(font))
            return ("", FontWeight.Normal, FontStyle.Normal);

        var value = font.Trim();
        var weight = FontWeight.Normal;
        var style = FontStyle.Normal;
        while (value.Length > 0)
        {
            var separator = value.IndexOfAny([' ', '\t', '\r', '\n']);
            var token = separator < 0 ? value : value[..separator];
            var remainder = separator < 0 ? "" : value[(separator + 1)..].TrimStart();

            if (token.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("oblique", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("normal", StringComparison.OrdinalIgnoreCase) && style == FontStyle.Normal)
            {
                style = token.Equals("italic", StringComparison.OrdinalIgnoreCase)
                    ? FontStyle.Italic
                    : token.Equals("oblique", StringComparison.OrdinalIgnoreCase)
                        ? FontStyle.Oblique
                        : FontStyle.Normal;
                value = remainder;
                continue;
            }

            var isNumericWeight = ushort.TryParse(token, out var numericWeight) && numericWeight is >= 100 and <= 900;
            if (token.Equals("bold", StringComparison.OrdinalIgnoreCase) || isNumericWeight)
            {
                weight = token.Equals("bold", StringComparison.OrdinalIgnoreCase)
                    ? FontWeight.Bold
                    : (FontWeight)(numericWeight / 100 * 100);
                value = remainder;
                continue;
            }

            if (Regex.IsMatch(token, @"^\d+(?:\.\d+)?px$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                value = remainder;
            break;
        }

        return (value.Trim().Trim('\'', '"'), weight, style);
    }
}
