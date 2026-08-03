using Square.Text.Glyph;

namespace Square.Text.Fonts;

/// <summary>字体面加载状态（对齐 CSS Font Loading <c>FontFaceLoadStatus</c>）。</summary>
public enum FontFaceLoadStatus
{
    /// <summary>尚未开始加载。</summary>
    Unloaded = 0,
    /// <summary>加载中。</summary>
    Loading = 1,
    /// <summary>已加载可用。</summary>
    Loaded = 2,
    /// <summary>加载失败。</summary>
    Error = 3
}

/// <summary>
/// 可加载的字体面（对齐 CSS Font Loading API <c>FontFace</c> 子集）。
/// 支持本地文件路径或原始字节；成功后注册到光栅字体集合与 <see cref="Square.Text.FontManager"/>。
/// </summary>
public sealed class FontFace
{
    private readonly object _gate = new();
    private Task? _loadTask;
    private byte[]? _data;
    private readonly string? _sourcePath;
    private readonly byte[]? _sourceBytes;

    /// <summary>
    /// 使用 CSS 族名与源创建字体面。
    /// <paramref name="source"/> 为本地文件路径，或 <c>url(...)</c> 中的路径字符串（当前仅支持本地路径，不发起网络请求）。
    /// </summary>
    public FontFace(
        string family,
        string source,
        Graphics.FontWeight weight = Graphics.FontWeight.Normal,
        Graphics.FontStyle style = Graphics.FontStyle.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Family = family.Trim();
        Weight = weight;
        Style = style;
        _sourcePath = NormalizeSourcePath(source);
        Source = source.Trim();
        Status = FontFaceLoadStatus.Unloaded;
    }

    /// <summary>使用 CSS 族名与字体文件字节创建（如嵌入资源）。</summary>
    public FontFace(
        string family,
        byte[] data,
        Graphics.FontWeight weight = Graphics.FontWeight.Normal,
        Graphics.FontStyle style = Graphics.FontStyle.Normal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new ArgumentException("Font data is empty.", nameof(data));
        Family = family.Trim();
        Weight = weight;
        Style = style;
        _sourceBytes = data;
        Source = "";
        Status = FontFaceLoadStatus.Unloaded;
    }

    /// <summary>CSS 字体族名（对齐 <c>family</c>）。</summary>
    public string Family { get; }

    /// <summary>该字体面的 CSS 字重。</summary>
    public Graphics.FontWeight Weight { get; }

    /// <summary>该字体面的 CSS 样式。</summary>
    public Graphics.FontStyle Style { get; }

    /// <summary>源描述字符串（路径或空）。</summary>
    public string Source { get; }

    /// <summary>加载状态（对齐 <c>status</c>）。</summary>
    public FontFaceLoadStatus Status { get; private set; }

    /// <summary>加载失败时的错误信息。</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>已加载的字体字节；未加载成功时为 null。</summary>
    public IReadOnlyList<byte>? Data => _data;

    /// <summary>
    /// 异步加载字体数据并注册到全局字体集合（对齐 <c>FontFace.load()</c>）。
    /// 已加载则立即完成；加载中则返回同一 Task。
    /// </summary>
    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (Status == FontFaceLoadStatus.Loaded)
                return Task.CompletedTask;
            if (_loadTask != null)
                return _loadTask;
            _loadTask = LoadCoreAsync(cancellationToken);
            return _loadTask;
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        Status = FontFaceLoadStatus.Loading;
        ErrorMessage = null;
        try
        {
            byte[] bytes;
            if (_sourceBytes != null)
            {
                bytes = _sourceBytes;
            }
            else
            {
                var path = _sourcePath ?? throw new InvalidOperationException("No font source.");
                if (!File.Exists(path))
                    throw new FileNotFoundException("Font file not found.", path);
                bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            }

            if (bytes.Length == 0)
                throw new InvalidOperationException("Font file is empty.");

            var offset = 0;
            if (_sourcePath != null &&
                _sourcePath.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                offset = GetTtcOffset(bytes, 0);
                if (offset < 0)
                    throw new InvalidOperationException("Invalid TTC font collection.");
            }

            // 验证 STB 可解析
            var info = StbTrueTypeSharp.StbTrueType.CreateFont(bytes, offset);
            if (info == null)
                throw new InvalidOperationException("Unable to parse font data.");

            _data = bytes;
            FontCollection.Shared.Register(Family, bytes, offset, Weight, Style);
            Square.Text.FontManager.Instance.RegisterLoadedFamily(Family);
            Status = FontFaceLoadStatus.Loaded;
        }
        catch (Exception ex)
        {
            Status = FontFaceLoadStatus.Error;
            ErrorMessage = ex.Message;
            lock (_gate) _loadTask = null;
            throw;
        }
    }

    private static string NormalizeSourcePath(string source)
    {
        source = source.Trim();
        // 支持 url("path") / url('path') / url(path)
        if (source.StartsWith("url(", StringComparison.OrdinalIgnoreCase) && source.EndsWith(')'))
        {
            source = source[4..^1].Trim().Trim('\'', '"');
        }
        return source;
    }

    private static unsafe int GetTtcOffset(byte[] data, int index)
    {
        fixed (byte* p = data)
            return StbTrueTypeSharp.StbTrueType.stbtt_GetFontOffsetForIndex(p, index);
    }
}
