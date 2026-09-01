using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using DirectN;
using DirectN.Extensions;
using DirectN.Extensions.Com;
using DirectN.Extensions.Utilities;
using Square.Graphics;
using Square.Text.Glyph;
using Font = Square.Graphics.Font;

namespace Square.Backends.Direct2D;

[SupportedOSPlatform("windows6.1")]
internal sealed unsafe class DirectWriteTextLayoutProvider : ITextLayoutProvider, IDisposable
{
    internal const int MaxFormatEntries = 128;
    internal const int MaxLayoutEntries = 256;
    internal const long MaxLayoutBytes = 16 * 1024 * 1024;
    private const float MaximumLayoutDimension = 1_000_000f;
    private const float LayoutWidthEpsilon = 1f / 64f;

    internal static DirectWriteTextLayoutProvider Shared { get; } = new();

    private readonly object _gate = new();
    private readonly IComObject<IDWriteFactory> _factory;
    private readonly Dictionary<FormatKey, FormatEntry> _formats = [];
    private readonly LinkedList<FormatKey> _formatLru = [];
    private readonly Dictionary<LayoutKey, LayoutEntry> _layouts = [];
    private readonly LinkedList<LayoutKey> _layoutLru = [];
    private readonly HashSet<LayoutKey> _unsupportedLayouts = [];
    private readonly Dictionary<MetricKey, FontMetrics> _fontMetrics = [];
    private readonly Dictionary<GlyphMetricKey, GlyphMetrics> _glyphMetrics = [];
    private long _layoutBytes;
    private DWriteFontCollectionLoader? _customLoader;
    private readonly Dictionary<string, CustomCollectionEntry> _customCollections =
        new(StringComparer.OrdinalIgnoreCase);
    private int _customGeneration = -1;
    private bool _disposed;

    private DirectWriteTextLayoutProvider()
    {
        _factory = DWriteFunctions.DWriteCreateFactory(DWRITE_FACTORY_TYPE.DWRITE_FACTORY_TYPE_SHARED);
    }

    internal int LayoutCacheCount { get { lock (_gate) return _layouts.Count; } }
    internal long LayoutCacheBytes { get { lock (_gate) return _layoutBytes; } }
    internal int FormatCacheCount { get { lock (_gate) return _formats.Count; } }

    public bool TryCreateLayout(TextLayout layout, out ITextLayoutSnapshot? snapshot)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!CanHandle(layout))
        {
            snapshot = null;
            return false;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                var entry = GetOrCreateLayout(layout);
                snapshot = entry.Snapshot;
                if (entry.IsTransient) entry.Dispose();
                return true;
            }
            catch (UnsupportedLayoutException)
            {
                snapshot = null;
                return false;
            }
        }
    }

    internal bool TryDraw(
        TextLayout layout,
        ID2D1RenderTarget target,
        Point origin,
        ID2D1Brush brush)
    {
        if (!CanHandle(layout)) return false;
        lock (_gate)
        {
            ThrowIfDisposed();
            LayoutEntry entry;
            try
            {
                entry = GetOrCreateLayout(layout);
            }
            catch (UnsupportedLayoutException)
            {
                return false;
            }
            try
            {
                var options = OperatingSystem.IsWindowsVersionAtLeast(6, 3)
                    ? D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT
                    : D2D1_DRAW_TEXT_OPTIONS.D2D1_DRAW_TEXT_OPTIONS_NONE;
                target.DrawTextLayout(new D2D_POINT_2F(origin.X, origin.Y), entry.Layout, brush, options);
                return true;
            }
            finally
            {
                if (entry.IsTransient) entry.Dispose();
            }
        }
    }

    public bool TryGetFontMetrics(Font font, out FontMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(font);
        lock (_gate)
        {
            ThrowIfDisposed();
            var key = new MetricKey(font.Family, font.Size, font.Weight, font.Style,
                FontCollection.Shared.CustomGeneration);
            if (_fontMetrics.TryGetValue(key, out metrics)) return true;
            var format = GetOrCreateFormat(font);
            using var layout = _factory.CreateTextLayout(format.Format, "Hg", 2,
                MaximumLayoutDimension, MaximumLayoutDimension);
            var lines = ReadLineMetrics(layout.Object);
            if (lines.Length == 0)
            {
                metrics = default;
                return false;
            }
            var line = lines[0];
            metrics = new FontMetrics(-line.baseline, -line.baseline, line.height - line.baseline,
                line.height - line.baseline, 0);
            if (_fontMetrics.Count >= 256) _fontMetrics.Clear();
            _fontMetrics[key] = metrics;
            return true;
        }
    }

    public bool TryGetGlyphMetrics(Font font, Rune rune, out GlyphMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(font);
        lock (_gate)
        {
            ThrowIfDisposed();
            var key = new GlyphMetricKey(font.Family, font.Size, font.Weight, font.Style, rune.Value,
                FontCollection.Shared.CustomGeneration);
            if (_glyphMetrics.TryGetValue(key, out metrics)) return true;
            var text = rune.ToString();
            var format = GetOrCreateFormat(font);
            using var layout = _factory.CreateTextLayout(format.Format, text, text.Length,
                MaximumLayoutDimension, MaximumLayoutDimension);
            layout.Object.GetMetrics(out var nativeMetrics).ThrowOnError();
            layout.Object.GetOverhangMetrics(out var overhang).ThrowOnError();
            var lines = ReadLineMetrics(layout.Object);
            var baseline = lines.Length == 0 ? font.Size : lines[0].baseline;
            var left = nativeMetrics.left - Math.Max(0, overhang.left);
            var top = nativeMetrics.top - Math.Max(0, overhang.top) - baseline;
            var width = Math.Max(0,
                nativeMetrics.width + Math.Max(0, overhang.left) + Math.Max(0, overhang.right));
            var height = Math.Max(0,
                nativeMetrics.height + Math.Max(0, overhang.top) + Math.Max(0, overhang.bottom));
            metrics = new GlyphMetrics(nativeMetrics.widthIncludingTrailingWhitespace,
                new Rect(left, top, width, height));
            if (_glyphMetrics.Count >= 4096) _glyphMetrics.Clear();
            _glyphMetrics[key] = metrics;
            return true;
        }
    }

    internal void ClearCaches()
    {
        lock (_gate)
        {
            foreach (var layout in _layouts.Values) layout.Dispose();
            _layouts.Clear();
            _layoutLru.Clear();
            _layoutBytes = 0;
            _unsupportedLayouts.Clear();
            foreach (var format in _formats.Values) format.Dispose();
            _formats.Clear();
            _formatLru.Clear();
            foreach (var collection in _customCollections.Values) collection.Dispose();
            _customCollections.Clear();
            _customLoader?.Dispose();
            _customLoader = null;
            _fontMetrics.Clear();
            _glyphMetrics.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            ClearCaches();
            _factory.Dispose();
        }
    }

    private static bool CanHandle(TextLayout layout)
    {
        if (string.IsNullOrEmpty(layout.Text) || layout.TextIndent != 0 ||
            layout.UnicodeBidi != BidiTextMode.Normal)
            return false;
        if (layout.Direction == BidiDirection.Auto && layout.Text.Contains('\n'))
        {
            var directions = layout.Text.Split('\n')
                .Where(paragraph => paragraph.Length > 0)
                .Select(paragraph => BidiText.Layout(paragraph,
                    new BidiTextOptions(BidiDirection.Auto, layout.UnicodeBidi)).BaseDirection)
                .Distinct()
                .Take(2)
                .Count();
            if (directions > 1) return false;
        }
        return true;
    }

    private static bool IsCustomFont(Font font) =>
        FontCollection.Shared.IsCustomFamily(font.Family);

    private LayoutEntry GetOrCreateLayout(TextLayout layout)
    {
        var key = LayoutKey.Create(layout, FontCollection.Shared.CustomGeneration);
        if (_unsupportedLayouts.Contains(key)) throw new UnsupportedLayoutException();
        if (_layouts.TryGetValue(key, out var cached))
        {
            TouchLayout(cached);
            return cached;
        }

        var prepared = PreparedText.Create(layout);
        var format = GetOrCreateFormat(layout.Font);
        var maxWidth = NormalizeDimension(layout.MaxSize.Width);
        if (float.IsFinite(layout.MaxSize.Width) && layout.MaxSize.Width > 0)
            maxWidth = Math.Min(MaximumLayoutDimension, maxWidth + LayoutWidthEpsilon);
        var maxHeight = NormalizeDimension(layout.MaxSize.Height);
        IComObject nativeOwner;
        IDWriteTextLayout nativeLayout;
        if (layout.LetterSpacing != 0 || layout.WordSpacing != 0)
        {
#pragma warning disable CA1416 // Querying IDWriteTextLayout1 is the runtime capability test.
            try
            {
                var native = _factory.CreateTextLayout<IDWriteTextLayout1>(
                    format.Format, prepared.Text, prepared.Text.Length, maxWidth, maxHeight);
                nativeOwner = native;
                nativeLayout = native.Object;
            }
            catch
            {
                if (_unsupportedLayouts.Count >= MaxLayoutEntries) _unsupportedLayouts.Clear();
                _unsupportedLayouts.Add(key);
                throw new UnsupportedLayoutException();
            }
#pragma warning restore CA1416
        }
        else
        {
            var native = _factory.CreateTextLayout(
                format.Format, prepared.Text, prepared.Text.Length, maxWidth, maxHeight);
            nativeOwner = native;
            nativeLayout = native.Object;
        }
        try
        {
            ConfigureLayout(nativeLayout, layout, prepared);
#pragma warning disable CA1416 // nativeLayout passed the IDWriteTextLayout1 runtime query.
        if (nativeLayout is IDWriteTextLayout1 advanced)
            ConfigureSpacing(advanced, layout, prepared);
#pragma warning restore CA1416
            if (layout.WhiteSpace is not (TextWhiteSpaceMode.Pre or TextWhiteSpaceMode.Nowrap) &&
                float.IsFinite(layout.MaxSize.Width) && layout.MaxSize.Width > 0)
            {
                nativeLayout.DetermineMinWidth(out var minimumWidth).ThrowOnError();
                if (minimumWidth > maxWidth)
                {
                    if (_unsupportedLayouts.Count >= MaxLayoutEntries) _unsupportedLayouts.Clear();
                    _unsupportedLayouts.Add(key);
                    throw new UnsupportedLayoutException();
                }
            }
            var snapshot = CreateSnapshot(nativeLayout, layout, prepared);
            var estimatedBytes = EstimateLayoutBytes(prepared.Text.Length, snapshot);
            if (estimatedBytes > MaxLayoutBytes)
                return new LayoutEntry(nativeOwner, nativeLayout, snapshot, estimatedBytes, null, isTransient: true);
            var node = _layoutLru.AddFirst(key);
            var entry = new LayoutEntry(nativeOwner, nativeLayout, snapshot, estimatedBytes, node, isTransient: false);
            _layouts.Add(key, entry);
            _layoutBytes += estimatedBytes;
            TrimLayouts(entry);
            return entry;
        }
        catch
        {
            nativeOwner.Dispose();
            throw;
        }
    }

    private FormatEntry GetOrCreateFormat(Font font)
    {
        var family = font.Family;
        IComObject<IDWriteFontCollection>? collection = null;
        var fontGeneration = 0;
        if (IsCustomFont(font))
        {
            var custom = GetOrCreateCustomCollection(font.Family);
            family = custom.FamilyName;
            collection = custom.Collection;
            fontGeneration = _customGeneration;
        }
        var key = new FormatKey(font.Family, family, font.Size, font.Weight, font.Style, CurrentLocale, fontGeneration);
        if (_formats.TryGetValue(key, out var cached))
        {
            _formatLru.Remove(cached.Node);
            _formatLru.AddFirst(cached.Node);
            return cached;
        }

        var native = _factory.CreateTextFormat(
            family,
            font.Size,
            collection?.Object!,
            (DWRITE_FONT_WEIGHT)(int)font.Weight,
            font.Style switch
            {
                FontStyle.Italic => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_ITALIC,
                FontStyle.Oblique => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_OBLIQUE,
                _ => DWRITE_FONT_STYLE.DWRITE_FONT_STYLE_NORMAL
            },
            DWRITE_FONT_STRETCH.DWRITE_FONT_STRETCH_NORMAL,
            CurrentLocale);
        var node = _formatLru.AddFirst(key);
        var entry = new FormatEntry(native, node);
        _formats.Add(key, entry);
        while (_formats.Count > MaxFormatEntries && _formatLru.Last is { } last)
        {
            var victimKey = last.Value;
            _formatLru.RemoveLast();
            if (_formats.Remove(victimKey, out var victim)) victim.Dispose();
        }
        return entry;
    }

    private CustomCollectionEntry GetOrCreateCustomCollection(string family)
    {
        EnsureCustomGeneration();
        if (_customCollections.TryGetValue(family, out var cached)) return cached;
        var faces = FontCollection.Shared.GetCustomFaces(family)
            .Where(face => face.Data.Length > 0)
            .ToArray();
        if (faces.Length == 0)
            throw new InvalidOperationException($"No loaded custom font data is available for '{family}'.");

        _customLoader ??= CreateCustomLoader();
        var keyBytes = Encoding.UTF8.GetBytes(family);
        var keyHandle = GCHandle.Alloc(keyBytes, GCHandleType.Pinned);
        try
        {
            _factory.Object.CreateCustomFontCollection(
                _customLoader,
                keyHandle.AddrOfPinnedObject(),
                (uint)keyBytes.Length,
                out var nativeCollection).ThrowOnError();
            var collection = new ComObject<IDWriteFontCollection>(nativeCollection);
            nativeCollection.GetFontFamily(0, out var nativeFamily).ThrowOnError();
            using var familyOwner = new ComObject<IDWriteFontFamily>(nativeFamily);
            var actualFamily = nativeFamily.GetNames()
                .Select(name => name.String)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? family;
            var entry = new CustomCollectionEntry(collection, actualFamily, keyHandle);
            _customCollections.Add(family, entry);
            return entry;
        }
        catch
        {
            keyHandle.Free();
            throw;
        }
    }

    private static DWriteFontCollectionLoader CreateCustomLoader() => new()
    {
        EnumerableFunc = (_, key) =>
        {
            var family = key == null ? "" : Encoding.UTF8.GetString(key);
                return FontCollection.Shared.GetCustomFaces(family)
                    .Where(face => face.Data.Length > 0)
                    .Select(face => (DWriteFontFile)new DirectWriteMemoryFontFile(face.Data))
                    .ToArray();
        }
    };

    private void EnsureCustomGeneration()
    {
        var generation = FontCollection.Shared.CustomGeneration;
        if (_customGeneration == generation) return;
        foreach (var layout in _layouts.Values) layout.Dispose();
        _layouts.Clear();
        _layoutLru.Clear();
        _layoutBytes = 0;
        _unsupportedLayouts.Clear();
        foreach (var format in _formats.Values) format.Dispose();
        _formats.Clear();
        _formatLru.Clear();
        foreach (var collection in _customCollections.Values) collection.Dispose();
        _customCollections.Clear();
        _customLoader?.Dispose();
        _customLoader = null;
        _fontMetrics.Clear();
        _glyphMetrics.Clear();
        _customGeneration = generation;
    }

    private static void ConfigureLayout(
        IDWriteTextLayout native,
        TextLayout layout,
        PreparedText prepared)
    {
        var direction = ResolveDirection(layout, prepared.Text);
        native.SetTextAlignment(layout.Alignment switch
        {
            TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_CENTER,
            TextAlignment.Right => direction == BidiDirection.Rtl
                ? DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING
                : DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING,
            TextAlignment.Justify => DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_JUSTIFIED,
            _ => direction == BidiDirection.Rtl
                ? DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_TRAILING
                : DWRITE_TEXT_ALIGNMENT.DWRITE_TEXT_ALIGNMENT_LEADING
        }).ThrowOnError();
        native.SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT.DWRITE_PARAGRAPH_ALIGNMENT_NEAR).ThrowOnError();
        native.SetWordWrapping(layout.WhiteSpace is TextWhiteSpaceMode.Pre or TextWhiteSpaceMode.Nowrap
            ? DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_NO_WRAP
            : DWRITE_WORD_WRAPPING.DWRITE_WORD_WRAPPING_WRAP).ThrowOnError();
        native.SetReadingDirection(direction == BidiDirection.Rtl
            ? DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_RIGHT_TO_LEFT
            : DWRITE_READING_DIRECTION.DWRITE_READING_DIRECTION_LEFT_TO_RIGHT).ThrowOnError();
        native.SetFlowDirection(DWRITE_FLOW_DIRECTION.DWRITE_FLOW_DIRECTION_TOP_TO_BOTTOM).ThrowOnError();

        var lineHeight = TextMetrics.GetLineHeight(layout.Font, layout.LineHeight);
        var baseline = TextMetrics.GetBaselineOffset(layout.Font, lineHeight);
        native.SetLineSpacing(DWRITE_LINE_SPACING_METHOD.DWRITE_LINE_SPACING_METHOD_UNIFORM, lineHeight, baseline)
            .ThrowOnError();

    }

    private static void ConfigureSpacing(IDWriteTextLayout1 native, TextLayout layout, PreparedText prepared)
    {
#pragma warning disable CA1416 // The caller obtained IDWriteTextLayout1 at runtime.
        if (layout.LetterSpacing != 0)
            native.SetCharacterSpacing(0, layout.LetterSpacing, 0,
                new DWRITE_TEXT_RANGE(0, (uint)prepared.Text.Length)).ThrowOnError();
        if (layout.WordSpacing == 0) return;
        foreach (var range in prepared.WhitespaceRanges)
            native.SetCharacterSpacing(0, layout.LetterSpacing + layout.WordSpacing, 0,
                new DWRITE_TEXT_RANGE((uint)range.Start, (uint)range.Length)).ThrowOnError();
#pragma warning restore CA1416
    }

    private DirectWriteSnapshot CreateSnapshot(
        IDWriteTextLayout native,
        TextLayout layout,
        PreparedText prepared)
    {
        native.GetMetrics(out var metrics).ThrowOnError();
        native.GetOverhangMetrics(out var overhang).ThrowOnError();
        var lineMetrics = ReadLineMetrics(native);
        var clusterMetrics = ReadClusterMetrics(native);
        var preparedClusters = new List<PreparedCluster>(clusterMetrics.Length);
        var preparedOffset = 0;
        foreach (var clusterMetric in clusterMetrics)
        {
            var length = clusterMetric.length;
            if (length == 0 || preparedOffset >= prepared.Text.Length) continue;
            native.HitTestTextPosition((uint)preparedOffset, false, out var leadingX, out var leadingY, out var hit)
                .ThrowOnError();
            native.HitTestTextPosition((uint)preparedOffset, true, out var trailingX, out var trailingY, out _)
                .ThrowOnError();
            var sourceRange = prepared.MapRange(preparedOffset, Math.Min(prepared.Text.Length, preparedOffset + length));
            Rune.DecodeFromUtf16(prepared.Text.AsSpan(preparedOffset, length), out var rune, out _);
            preparedClusters.Add(new PreparedCluster(
                preparedOffset,
                preparedOffset + length,
                new TextLayoutCluster(
                    sourceRange.Start,
                    sourceRange.End,
                    rune,
                    new Rect(hit.left, hit.top, hit.width, hit.height),
                    clusterMetric.isRightToLeft ? BidiDirection.Rtl : BidiDirection.Ltr),
                new Point(leadingX, leadingY),
                new Point(trailingX, trailingY)));
            preparedOffset += length;
        }

        var lines = BuildLines(lineMetrics, preparedClusters, prepared);
        var clusters = preparedClusters.Select(value => value.Cluster).ToArray();
        var explicitLineCount = prepared.Text.Count(character => character == '\n') + 1;
        var wrapped = lineMetrics.Length > explicitLineCount;
        var measuredWidth = wrapped && float.IsFinite(layout.MaxSize.Width) && layout.MaxSize.Width > 0
            ? layout.MaxSize.Width
            : metrics.widthIncludingTrailingWhitespace;
        var size = new Size(measuredWidth, metrics.height);
        var ink = new Rect(
            metrics.left - Math.Max(0, overhang.left),
            metrics.top - Math.Max(0, overhang.top),
            Math.Max(0, metrics.width + Math.Max(0, overhang.left) + Math.Max(0, overhang.right)),
            Math.Max(0, metrics.height + Math.Max(0, overhang.top) + Math.Max(0, overhang.bottom)));
        return new DirectWriteSnapshot(size, ink, lines, clusters, preparedClusters, prepared);
    }

    private static DWRITE_LINE_METRICS[] ReadLineMetrics(IDWriteTextLayout native)
    {
        native.GetLineMetrics(IntPtr.Zero, 0, out var count);
        if (count == 0) return [];
        var result = new DWRITE_LINE_METRICS[count];
        fixed (DWRITE_LINE_METRICS* pointer = result)
            native.GetLineMetrics((IntPtr)pointer, count, out count).ThrowOnError();
        return result;
    }

    private static DWRITE_CLUSTER_METRICS[] ReadClusterMetrics(IDWriteTextLayout native)
    {
        native.GetClusterMetrics(IntPtr.Zero, 0, out var count);
        if (count == 0) return [];
        var result = new DWRITE_CLUSTER_METRICS[count];
        fixed (DWRITE_CLUSTER_METRICS* pointer = result)
            native.GetClusterMetrics((IntPtr)pointer, count, out count).ThrowOnError();
        return result;
    }

    private static IReadOnlyList<TextLayoutLine> BuildLines(
        IReadOnlyList<DWRITE_LINE_METRICS> nativeLines,
        IReadOnlyList<PreparedCluster> clusters,
        PreparedText prepared)
    {
        var result = new List<TextLayoutLine>(nativeLines.Count);
        var preparedOffset = 0;
        var top = 0f;
        foreach (var nativeLine in nativeLines)
        {
            var lineEnd = Math.Min(prepared.Text.Length, preparedOffset + (int)nativeLine.length);
            var sourceRange = prepared.MapRange(preparedOffset, lineEnd);
            var lineClusters = clusters
                .Where(cluster => cluster.PreparedStart >= preparedOffset && cluster.PreparedStart < lineEnd)
                .Select(cluster => cluster.Cluster)
                .OrderBy(cluster => cluster.Bounds.Left)
                .ToArray();
            var width = lineClusters.Sum(cluster => cluster.Bounds.Width);
            result.Add(new TextLayoutLine(
                sourceRange.Start,
                sourceRange.End,
                width,
                nativeLine.height,
                nativeLine.baseline,
                lineClusters)
            {
                Top = top
            });
            preparedOffset = lineEnd;
            top += nativeLine.height;
        }
        return result;
    }

    private void TouchLayout(LayoutEntry entry)
    {
        _layoutLru.Remove(entry.Node!);
        _layoutLru.AddFirst(entry.Node!);
    }

    private void TrimLayouts(LayoutEntry protectedEntry)
    {
        while ((_layouts.Count > MaxLayoutEntries || _layoutBytes > MaxLayoutBytes) &&
               _layoutLru.Last is { } last)
        {
            if (ReferenceEquals(_layouts[last.Value], protectedEntry) && _layouts.Count == 1) break;
            var key = last.Value;
            _layoutLru.RemoveLast();
            if (!_layouts.Remove(key, out var victim)) continue;
            _layoutBytes -= victim.EstimatedBytes;
            victim.Dispose();
        }
    }

    private static long EstimateLayoutBytes(int textLength, DirectWriteSnapshot snapshot) =>
        512L + textLength * 2L + snapshot.Lines.Count * 64L + snapshot.ClusterCount * 64L;

    private static float NormalizeDimension(float value) =>
        float.IsFinite(value) && value > 0 ? Math.Min(value, MaximumLayoutDimension) : MaximumLayoutDimension;

    private static BidiDirection ResolveDirection(TextLayout layout, string text)
    {
        if (layout.Direction != BidiDirection.Auto) return layout.Direction;
        return BidiText.Layout(text, new BidiTextOptions(BidiDirection.Auto, layout.UnicodeBidi)).BaseDirection;
    }

    private static string CurrentLocale => string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name)
        ? "en-US"
        : CultureInfo.CurrentUICulture.Name;

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private readonly record struct FormatKey(
        string RequestedFamily,
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style,
        string Locale,
        int FontGeneration);

    private readonly record struct PreparedCluster(
        int PreparedStart,
        int PreparedEnd,
        TextLayoutCluster Cluster,
        Point LeadingCaret,
        Point TrailingCaret);

    private readonly record struct MetricKey(
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style,
        int FontGeneration);

    private readonly record struct GlyphMetricKey(
        string Family,
        float Size,
        FontWeight Weight,
        FontStyle Style,
        int Rune,
        int FontGeneration);

    private readonly record struct LayoutKey(
        string SourceText,
        FormatKey Format,
        float MaxWidth,
        float MaxHeight,
        TextAlignment Alignment,
        float LineHeight,
        BidiDirection Direction,
        TextWhiteSpaceMode WhiteSpace,
        float LetterSpacing,
        float WordSpacing,
        TextTransformMode Transform,
        bool CollapseNewlines)
    {
        public static LayoutKey Create(TextLayout layout, int fontGeneration) => new(
            layout.Text,
            new FormatKey(
                layout.Font.Family,
                layout.Font.Family,
                layout.Font.Size,
                layout.Font.Weight,
                layout.Font.Style,
                CurrentLocale,
                fontGeneration),
            layout.MaxSize.Width,
            layout.MaxSize.Height,
            layout.Alignment,
            layout.LineHeight,
            layout.Direction,
            layout.WhiteSpace,
            layout.LetterSpacing,
            layout.WordSpacing,
            layout.TextTransform,
            layout.CollapseNewlines);
    }

    private sealed class FormatEntry(
        IComObject<IDWriteTextFormat> format,
        LinkedListNode<FormatKey> node) : IDisposable
    {
        public IComObject<IDWriteTextFormat> Format { get; } = format;
        public LinkedListNode<FormatKey> Node { get; } = node;
        public void Dispose() => Format.Dispose();
    }

    private sealed class CustomCollectionEntry(
        IComObject<IDWriteFontCollection> collection,
        string familyName,
        GCHandle keyHandle) : IDisposable
    {
        public IComObject<IDWriteFontCollection> Collection { get; } = collection;
        public string FamilyName { get; } = familyName;

        public void Dispose()
        {
            Collection.Dispose();
            if (keyHandle.IsAllocated) keyHandle.Free();
        }
    }

    private sealed class DirectWriteMemoryFontFile(byte[] data) : DWriteFontFile, IDisposable
    {
        public override long? Length => data.Length;

        public override byte[] ReadFileFragment(long offset, int length, out int read)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            read = length;
            if (offset == 0 && length == data.Length) return data;
            return data.AsSpan((int)offset, length).ToArray();
        }

        public void Dispose()
        {
        }
    }

    private sealed class UnsupportedLayoutException : Exception;

    private sealed class LayoutEntry(
        IComObject owner,
        IDWriteTextLayout layout,
        DirectWriteSnapshot snapshot,
        long estimatedBytes,
        LinkedListNode<LayoutKey>? node,
        bool isTransient) : IDisposable
    {
        public IDWriteTextLayout Layout { get; } = layout;
        public DirectWriteSnapshot Snapshot { get; } = snapshot;
        public long EstimatedBytes { get; } = estimatedBytes;
        public LinkedListNode<LayoutKey>? Node { get; } = node;
        public bool IsTransient { get; } = isTransient;
        public void Dispose() => owner.Dispose();
    }

    private sealed class DirectWriteSnapshot : ITextLayoutSnapshot
    {
        private readonly IReadOnlyList<TextLayoutCluster> _clusters;
        private readonly IReadOnlyList<PreparedCluster> _preparedClusters;
        private readonly PreparedText _prepared;

        public DirectWriteSnapshot(
            Size size,
            Rect inkBounds,
            IReadOnlyList<TextLayoutLine> lines,
            IReadOnlyList<TextLayoutCluster> clusters,
            IReadOnlyList<PreparedCluster> preparedClusters,
            PreparedText prepared)
        {
            Size = size;
            InkBounds = inkBounds;
            Lines = lines;
            _clusters = clusters;
            _preparedClusters = preparedClusters;
            _prepared = prepared;
        }

        public Size Size { get; }
        public Rect InkBounds { get; }
        public IReadOnlyList<TextLayoutLine> Lines { get; }
        public int ClusterCount => _clusters.Count;

        public float MeasureOffset(int utf16Offset)
        {
            utf16Offset = Math.Clamp(utf16Offset, 0, _prepared.SourceLength);
            var cluster = _clusters.FirstOrDefault(value =>
                utf16Offset >= value.StartOffset && utf16Offset <= value.EndOffset);
            if (cluster.EndOffset == 0 && utf16Offset > 0)
                return _clusters.Count == 0 ? 0 : _clusters[^1].Bounds.Right;
            var x = cluster.Direction == BidiDirection.Rtl
                ? utf16Offset <= cluster.StartOffset ? cluster.Bounds.Right : cluster.Bounds.Left
                : utf16Offset >= cluster.EndOffset ? cluster.Bounds.Right : cluster.Bounds.Left;
            var ownerLine = Lines.FirstOrDefault(line => line.Clusters.Contains(cluster));
            var lineLeft = ownerLine?.Clusters.Min(value => value.Bounds.Left) ?? 0;
            return x - lineLeft;
        }

        public Point GetCaretPoint(int utf16Offset, bool trailing = false)
        {
            utf16Offset = Math.Clamp(utf16Offset, 0, _prepared.SourceLength);
            var emptyLine = Lines.FirstOrDefault(line => line.Clusters.Count == 0 &&
                utf16Offset >= line.StartOffset && utf16Offset <= line.EndOffset);
            if (emptyLine != null) return new Point(0, emptyLine.Top);
            var preparedCluster = _preparedClusters.FirstOrDefault(value =>
                utf16Offset >= value.Cluster.StartOffset && utf16Offset <= value.Cluster.EndOffset);
            var cluster = preparedCluster.Cluster;
            if (cluster.EndOffset == 0 && utf16Offset > 0)
                return _preparedClusters.Count == 0
                    ? Point.Zero
                    : _preparedClusters[^1].TrailingCaret;
            var useTrailing = trailing || utf16Offset >= cluster.EndOffset;
            return useTrailing ? preparedCluster.TrailingCaret : preparedCluster.LeadingCaret;
        }

        public int HitTestPoint(Point point)
        {
            if (_clusters.Count == 0) return 0;
            var line = Lines
                .OrderBy(value => DistanceToRange(point.Y, value.Top, value.Top + value.Height))
                .First();
            if (line.Clusters.Count == 0) return line.StartOffset;
            var first = line.Clusters[0];
            if (point.X <= first.Bounds.Left)
                return first.Direction == BidiDirection.Rtl ? first.EndOffset : first.StartOffset;
            var last = line.Clusters[^1];
            if (point.X >= last.Bounds.Right)
                return last.Direction == BidiDirection.Rtl ? last.StartOffset : last.EndOffset;
            var cluster = line.Clusters
                .OrderBy(value => DistanceToRange(point.X, value.Bounds.Left, value.Bounds.Right))
                .First();
            var trailing = point.X >= cluster.Bounds.X + cluster.Bounds.Width / 2f;
            return cluster.Direction == BidiDirection.Rtl
                ? trailing ? cluster.StartOffset : cluster.EndOffset
                : trailing ? cluster.EndOffset : cluster.StartOffset;
        }

        public IReadOnlyList<Rect> GetSelectionRects(int start, int length)
        {
            var end = start + length;
            var result = new List<Rect>();
            foreach (var line in Lines)
            {
                Rect? current = null;
                foreach (var cluster in line.Clusters)
                {
                    if (cluster.EndOffset <= start || cluster.StartOffset >= end)
                    {
                        if (current is { } interrupted) result.Add(interrupted);
                        current = null;
                        continue;
                    }
                    current = current is { } active
                        ? Rect.Union(active, cluster.Bounds)
                        : cluster.Bounds;
                }
                if (current is { } final) result.Add(final);
            }
            return result;
        }

        private static float DistanceToRange(float value, float start, float end) =>
            value < start ? start - value : value > end ? value - end : 0;
    }

    private sealed record PreparedText(
        string Text,
        int SourceLength,
        int[] SourceStarts,
        int[] SourceEnds,
        IReadOnlyList<(int Start, int Length)> WhitespaceRanges)
    {
        public static PreparedText Create(TextLayout layout)
        {
            var builder = new StringBuilder(layout.Text.Length);
            var starts = new List<int>(layout.Text.Length);
            var ends = new List<int>(layout.Text.Length);
            var whitespaceRanges = new List<(int, int)>();
            foreach (var token in TextWrapping.CreateTokens(
                         layout.Text,
                         layout.WrappingOptions,
                         static (_, _) => 0))
            {
                var outputStart = builder.Length;
                var text = token.ForceBreak ? "\n" : token.Rune.ToString();
                builder.Append(text);
                for (var index = 0; index < text.Length; index++)
                {
                    starts.Add(token.Start);
                    ends.Add(token.End);
                }
                if (token.IsWhitespace && !token.ForceBreak)
                    whitespaceRanges.Add((outputStart, text.Length));
            }
            return new PreparedText(builder.ToString(), layout.Text.Length, starts.ToArray(), ends.ToArray(), whitespaceRanges);
        }

        public (int Start, int End) MapRange(int preparedStart, int preparedEnd)
        {
            if (SourceStarts.Length == 0) return (0, 0);
            if (preparedStart == preparedEnd)
            {
                var sourceOffset = preparedStart >= SourceStarts.Length
                    ? SourceLength
                    : SourceStarts[Math.Max(0, preparedStart)];
                return (sourceOffset, sourceOffset);
            }
            preparedStart = Math.Clamp(preparedStart, 0, SourceStarts.Length - 1);
            preparedEnd = Math.Clamp(preparedEnd, preparedStart + 1, SourceEnds.Length);
            var start = SourceStarts[preparedStart];
            var end = SourceEnds[preparedEnd - 1];
            return (start, end);
        }
    }
}
