using System.Collections.ObjectModel;
using Square.Graphics;
using Square.Text.Glyph;

namespace Square.Backends.Direct2D;

/// <summary>Small value-keyed LRU for the unchanged fallback text preparation path.</summary>
internal sealed class Direct2DTextPreparationCache
{
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private readonly Dictionary<Key, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = [];

    public Direct2DTextPreparationCache(int maxEntries = 256, long maxBytes = 4L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    public int Count => _entries.Count;
    public long Bytes { get; private set; }
    public long BuildCount { get; private set; }
    public long HitCount { get; private set; }

    public Preparation? Get(TextLayout text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!UsesStandardProviders || text.Text is null || text.Font is null || text.Font.Family is null) return null;
        var key = Key.Capture(text);
        if (_entries.TryGetValue(key, out var hit))
        {
            _lru.Remove(hit);
            _lru.AddLast(hit);
            HitCount++;
            return hit.Value.Value;
        }
        var prepared = Build(key);
        BuildCount++;
        // Do not retain a result across a provider/font registration during preparation.
        if (!UsesStandardProviders || key.FontGeneration != FontCollection.Shared.CustomGeneration ||
            key.PlatformGeneration != SystemGlyphRasterizer.PlatformMetricsGeneration) return null;
        var bytes = 512L + (long)key.Text.Length * sizeof(char) + (long)key.Font.Family.Length * sizeof(char) + prepared.EstimatedBytes;
        if (_maxEntries == 0 || bytes > _maxBytes) return prepared;
        while (_entries.Count >= _maxEntries || Bytes > _maxBytes - bytes)
        {
            var victim = _lru.First!;
            _lru.RemoveFirst();
            _entries.Remove(victim.Value.Key);
            Bytes -= victim.Value.Bytes;
        }
        // Release index capacity left by a large item displacing many small entries.
        if (_entries.Count * 2 < _entries.EnsureCapacity(0)) _entries.TrimExcess();
        var node = _lru.AddLast(new Entry(key, prepared, bytes));
        _entries.Add(key, node);
        Bytes += bytes;
        return prepared;
    }

    /// <summary>Releases retained text and arrays; diagnostic counters stay cumulative.</summary>
    public void Clear()
    {
        _entries.Clear();
        _entries.TrimExcess();
        _lru.Clear();
        Bytes = 0;
    }

    private static bool UsesStandardProviders => TextLayout.UsesStandardAdvanceProvider && TextMetrics.UsesStandardProvider;

    private static Preparation Build(Key key)
    {
        // Recreate only the fields consumed by the old fallback path, from value snapshots.
        var text = new TextLayout(key.Text, key.Font.CreateFont())
        {
            MaxSize = new Size(Float(key.MaxWidth), Float(key.MaxHeight)),
            Alignment = key.Alignment,
            LineHeight = Float(key.LineHeight),
            Direction = key.Direction,
            UnicodeBidi = key.UnicodeBidi,
            WhiteSpace = key.Wrapping.WhiteSpace,
            LetterSpacing = Float(key.Wrapping.LetterSpacing),
            WordSpacing = Float(key.Wrapping.WordSpacing),
            TextTransform = key.Wrapping.Transform,
            TextIndent = Float(key.Wrapping.Indent),
            CollapseNewlines = key.Wrapping.CollapseNewlines,
            TextDecorationLines = key.Wrapping.Decoration
        };
        // Preserve the preparation order and use the exact existing routines.
        var lines = TextWrapping.Wrap(text.Text, text.MaxSize.Width,
            (_, rune) => TextLayout.MeasureRuneAdvance(rune, text.Font), text.WrappingOptions).ToArray();
        var lineHeight = TextMetrics.GetLineHeight(text.Font, text.LineHeight);
        var baseline = TextMetrics.GetBaselineOffset(text.Font, lineHeight);
        var visual = new ReadOnlyCollection<TextVisualRune>[lines.Length];
        var bytes = 128L + (long)lines.Length * 12;
        for (var index = 0; index < lines.Length; index++)
        {
            var runes = text.EnumerateVisualRunes(lines[index]).ToArray();
            visual[index] = Array.AsReadOnly(runes);
            bytes += 96L + (long)runes.Length * 32;
        }
        return new Preparation(Array.AsReadOnly(lines), visual, lineHeight, baseline, bytes);
    }

    internal sealed class Preparation
    {
        private readonly ReadOnlyCollection<TextLineRange> _lines;
        private readonly ReadOnlyCollection<TextVisualRune>[] _visual;
        public IReadOnlyList<TextLineRange> Lines => _lines;
        public float LineHeight { get; }
        public float BaselineOffset { get; }
        internal long EstimatedBytes { get; }

        internal Preparation(ReadOnlyCollection<TextLineRange> lines, ReadOnlyCollection<TextVisualRune>[] visual,
            float lineHeight, float baselineOffset, long estimatedBytes)
        {
            _lines = lines;
            _visual = visual;
            LineHeight = lineHeight;
            BaselineOffset = baselineOffset;
            EstimatedBytes = estimatedBytes;
        }

        public IEnumerable<TextVisualRune> GetVisualRunes(int lineIndex) => _visual[lineIndex];
    }

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);
    private static float Float(int bits) => BitConverter.Int32BitsToSingle(bits);

    private readonly record struct FontState(string Family, int Size, FontWeight Weight, FontStyle Style)
    {
        public static FontState Capture(Font font) => new(font.Family, Bits(font.Size), font.Weight, font.Style);
        public Font CreateFont() => new(Family, Float(Size), Weight, Style);
    }

    private readonly record struct WrappingState(TextWhiteSpaceMode WhiteSpace, int LetterSpacing, int WordSpacing,
        TextTransformMode Transform, int Indent, bool CollapseNewlines, TextDecorationLine Decoration)
    {
        public static WrappingState Capture(TextWrappingOptions options) => new(options.WhiteSpace, Bits(options.LetterSpacing),
            Bits(options.WordSpacing), options.TextTransform, Bits(options.TextIndent), options.CollapseNewlines, options.TextDecorationLines);
    }

    private readonly record struct Key(string Text, FontState Font, int MaxWidth, int MaxHeight,
        TextAlignment Alignment, int LineHeight, BidiDirection Direction, BidiTextMode UnicodeBidi,
        WrappingState Wrapping, int FontGeneration, int PlatformGeneration)
    {
        public static Key Capture(TextLayout text) => new(text.Text, FontState.Capture(text.Font), Bits(text.MaxSize.Width),
            Bits(text.MaxSize.Height), text.Alignment, Bits(text.LineHeight), text.Direction, text.UnicodeBidi,
            WrappingState.Capture(text.WrappingOptions), FontCollection.Shared.CustomGeneration,
            SystemGlyphRasterizer.PlatformMetricsGeneration);
    }

    private sealed record Entry(Key Key, Preparation Value, long Bytes);
}
