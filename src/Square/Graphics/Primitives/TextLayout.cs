using System.Globalization;
using System.Text;

namespace Square.Graphics;

/// <summary>CSS <c>white-space</c> 的文本布局子集。</summary>
public enum TextWhiteSpaceMode : byte
{
    Normal,
    Pre,
    Nowrap,
    PreWrap,
    PreLine
}

/// <summary>CSS <c>text-transform</c> 的文本布局子集。</summary>
public enum TextTransformMode : byte
{
    None,
    Capitalize,
    Uppercase,
    Lowercase
}

[Flags]
public enum TextDecorationLine : byte
{
    None = 0,
    Underline = 1,
    Overline = 2,
    LineThrough = 4
}

/// <summary>文本布局，负责测量、换行、偏移定位与命中测试。</summary>
public sealed class TextLayout
{
    /// <summary>默认行高倍数（1.2）。</summary>
    public const float DefaultLineHeight = 1.2f;
    private static Func<Rune, Font, float?>? _advanceProvider;

    /// <summary>文本内容。</summary>
    public string Text { get; set; } = "";
    /// <summary>字体。</summary>
    public Font Font { get; set; } = new();
    /// <summary>最大尺寸，Width 用于换行。</summary>
    public Size MaxSize { get; set; } = new(float.MaxValue, float.MaxValue);
    /// <summary>文本对齐方式。</summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;
    /// <summary>行高倍数（相对字号）。</summary>
    public float LineHeight { get; set; } = DefaultLineHeight;
    /// <summary>段落基方向；Auto 使用第一个强方向字符。</summary>
    public BidiDirection Direction { get; set; } = BidiDirection.Auto;
    /// <summary>CSS <c>unicode-bidi</c> 的基础子集。</summary>
    public BidiTextMode UnicodeBidi { get; set; } = BidiTextMode.Normal;
    /// <summary>CSS <c>white-space</c> 的布局子集。</summary>
    public TextWhiteSpaceMode WhiteSpace { get; set; } = TextWhiteSpaceMode.Normal;
    /// <summary>CSS <c>letter-spacing</c>，单位为逻辑像素。</summary>
    public float LetterSpacing { get; set; }
    /// <summary>CSS <c>word-spacing</c>，单位为逻辑像素。</summary>
    public float WordSpacing { get; set; }
    /// <summary>CSS <c>text-transform</c> 的布局子集。</summary>
    public TextTransformMode TextTransform { get; set; } = TextTransformMode.None;
    /// <summary>CSS <c>text-indent</c>，单位为逻辑像素。</summary>
    public float TextIndent { get; set; }
    /// <summary>CSS 文本模式下是否将显式换行折叠为空格。</summary>
    public bool CollapseNewlines { get; set; }
    /// <summary>CSS <c>text-decoration-line</c> 的可绘制子集。</summary>
    public TextDecorationLine TextDecorationLines { get; set; }
    /// <summary>返回当前文本布局使用的 CSS 文本换行选项。</summary>
    public TextWrappingOptions WrappingOptions => CreateWrappingOptions();

    /// <summary>构造默认布局。</summary>
    public TextLayout() { }
    /// <summary>构造指定文本和字体的布局。</summary>
    public TextLayout(string text, Font font) { Text = text; Font = font; }

    /// <summary>注册字符前进宽度提供器（兼容旧 API，已被 <see cref="TextMetrics"/> 取代）。</summary>
    public static void RegisterAdvanceProvider(Func<Rune, Font, float?> provider)
        => _advanceProvider = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>测量文本尺寸。</summary>
    public Size Measure() => MeasureCore();

    /// <summary>测量从行首到指定 UTF-16 偏移的水平距离。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="offset"/> 越界。</exception>
    public float MeasureOffset(int offset)
    {
        if (offset < 0 || offset > Text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        var line = EnumerateVisualLines().FirstOrDefault();
        if (line.Runes is not { Count: > 0 }) return 0;

        var x = GetLineIndent(0);
        foreach (var rune in line.Runes)
        {
            if (offset <= rune.StartOffset)
                return x + (rune.Direction == BidiDirection.Rtl ? rune.Advance : 0);
            if (offset <= rune.EndOffset)
                return x + (rune.Direction == BidiDirection.Rtl ? 0 : rune.Advance);
            x += rune.Advance;
        }
        return x;
    }

    /// <summary>按水平坐标命中测试，返回最近的 UTF-16 偏移。</summary>
    public int HitTestOffset(float x)
    {
        if (string.IsNullOrEmpty(Text)) return 0;

        var line = EnumerateVisualLines().FirstOrDefault();
        if (line.Runes is not { Count: > 0 }) return 0;
        if (x <= GetLineIndent(0)) return GetVisualEdgeOffset(line.Runes[0], leading: true);

        var position = GetLineIndent(0);
        foreach (var rune in line.Runes)
        {
            if (x < position + rune.Advance / 2f)
                return rune.Direction == BidiDirection.Rtl ? rune.EndOffset : rune.StartOffset;
            position += rune.Advance;
        }

        var last = line.Runes[^1];
        return GetVisualEdgeOffset(last, leading: false);
    }

    /// <summary>返回按逻辑换行、每行按视觉顺序排列的字符。</summary>
    public IReadOnlyList<TextVisualLine> GetVisualLines()
    {
        var lines = TextWrapping.Wrap(Text, MaxSize.Width, (_, rune) => MeasureRuneAdvance(rune, Font),
            CreateWrappingOptions());
        var result = new List<TextVisualLine>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            result.Add(new TextVisualLine(line, GetVisualRunes(line), GetLineIndent(index)));
        }
        return result;
    }

    /// <summary>枚举按逻辑换行、每行按视觉顺序排列的字符。</summary>
    public IEnumerable<TextVisualLine> EnumerateVisualLines() => GetVisualLines();

    /// <summary>枚举指定逻辑行中的视觉字符，字符偏移仍指向原始 UTF-16 文本。</summary>
    public IEnumerable<TextVisualRune> EnumerateVisualRunes(TextLineRange line) => GetVisualRunes(line);

    private Size MeasureCore()
    {
        if (string.IsNullOrEmpty(Text))
            return Size.Zero;

        var lineHeight = TextMetrics.GetLineHeight(Font, LineHeight);
        var maxWidth = MaxSize.Width;
        var lines = TextWrapping.Wrap(Text, maxWidth, (_, rune) => MeasureRuneAdvance(rune, Font),
            CreateWrappingOptions());
        var widestLine = lines.Count == 0
            ? 0
            : lines.Select((line, index) => line.Width + GetLineIndent(index)).Max();
        var constrainWidth = float.IsFinite(maxWidth) && maxWidth > 0;
        var wrapped = lines.Count > Text.Count(character => character == '\n') + 1;
        return new Size(constrainWidth && wrapped ? maxWidth : widestLine, lines.Count * lineHeight);
    }

    private IReadOnlyList<TextVisualRune> GetVisualRunes(TextLineRange line)
    {
        if (line.StartOffset < 0 || line.EndOffset < line.StartOffset || line.EndOffset > Text.Length)
            throw new ArgumentOutOfRangeException(nameof(line));
        if (line.StartOffset == line.EndOffset) return [];

        var tokens = TextWrapping.CreateTokens(Text, CreateWrappingOptions(), (_, rune) => MeasureRuneAdvance(rune, Font))
            .Where(token => token.Start >= line.StartOffset && token.End <= line.EndOffset)
            .ToArray();
        if (tokens.Length == 0) return [];

        var logicalRunes = tokens
            .Select(token => new LogicalRune(token.Rune, token.Start, token.End))
            .ToArray();
        var localText = string.Concat(tokens.Select(token => token.Rune.ToString()));
        var paragraphDirection = GetParagraphDirection(line);
        var bidi = BidiText.Layout(localText, new BidiTextOptions(paragraphDirection, UnicodeBidi));
        var visual = new List<TextVisualRune>(logicalRunes.Length);
        var localOffsets = new int[logicalRunes.Length];
        var localOffset = 0;
        for (var index = 0; index < logicalRunes.Length; index++)
        {
            localOffsets[index] = localOffset;
            localOffset += logicalRunes[index].Rune.Utf16SequenceLength;
        }

        foreach (var localIndex in bidi.VisualToLogical)
        {
            if ((uint)localIndex >= (uint)logicalRunes.Length) continue;
            var logical = logicalRunes[localIndex];
            var tokenOffset = localOffsets[localIndex];
            var direction = BidiDirection.Ltr;
            foreach (var run in bidi.VisualRuns)
            {
                if (tokenOffset >= run.Start && tokenOffset < run.End)
                {
                    direction = run.Direction;
                    break;
                }
            }
            visual.Add(new TextVisualRune(
                logical.Rune,
                logical.StartOffset,
                logical.EndOffset,
                MeasureRuneAdvanceForLayout(logical.StartOffset, logical.Rune),
                direction));
        }
        return visual;
    }

    /// <summary>返回指定行的首行缩进。</summary>
    public float GetLineIndent(int lineIndex) => lineIndex == 0 ? TextIndent : 0;

    /// <summary>返回指定行相对于文本原点的起始 X 坐标。</summary>
    public float GetLineOriginX(float originX, int lineIndex, float lineWidth) =>
        originX + GetLineIndent(lineIndex) + GetTextAlignmentOffset(lineWidth + GetLineIndent(lineIndex));

    /// <summary>返回当前文本各行的 CSS 装饰矩形。</summary>
    public IReadOnlyList<Rect> GetDecorationRects(Point origin)
    {
        if (TextDecorationLines == TextDecorationLine.None || string.IsNullOrEmpty(Text)) return [];

        var lineHeight = TextMetrics.GetLineHeight(Font, LineHeight);
        var baseline = TextMetrics.GetBaselineOffset(Font, lineHeight);
        var thickness = Math.Max(1f, Font.Size / 16f);
        var result = new List<Rect>();
        var lines = GetVisualLines();
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var x = GetLineOriginX(origin.X, index, line.Width);
            var width = Math.Max(0, line.Width);
            var top = origin.Y + index * lineHeight;
            if ((TextDecorationLines & TextDecorationLine.Underline) != 0)
                result.Add(new Rect(x, top + lineHeight - thickness, width, thickness));
            if ((TextDecorationLines & TextDecorationLine.Overline) != 0)
                result.Add(new Rect(x, top, width, thickness));
            if ((TextDecorationLines & TextDecorationLine.LineThrough) != 0)
                result.Add(new Rect(x, top + baseline - Font.Size * 0.3f - thickness / 2f, width, thickness));
        }
        return result;
    }

    private TextWrappingOptions CreateWrappingOptions() => new(
        WhiteSpace,
        LetterSpacing,
        WordSpacing,
        TextTransform,
        TextIndent,
        CollapseNewlines,
        TextDecorationLines);

    private float GetTextAlignmentOffset(float lineWidth) =>
        !float.IsFinite(MaxSize.Width) || MaxSize.Width <= lineWidth
            ? 0
            : Alignment switch
            {
                TextAlignment.Center => (MaxSize.Width - lineWidth) / 2f,
                TextAlignment.Right => MaxSize.Width - lineWidth,
                _ => 0
            };

    private float MeasureRuneAdvanceForLayout(int offset, Rune rune)
    {
        var advance = MeasureRuneAdvance(rune, Font);
        if (rune.Value != '\n') advance += LetterSpacing;
        if (Rune.IsWhiteSpace(rune)) advance += WordSpacing;
        return Math.Max(0, advance);
    }

    private BidiDirection GetParagraphDirection(TextLineRange line)
    {
        if (Direction != BidiDirection.Auto) return Direction;

        var paragraphStart = Text.LastIndexOf('\n', Math.Max(0, line.StartOffset - 1)) + 1;
        var paragraphEnd = Text.IndexOf('\n', line.EndOffset);
        if (paragraphEnd < 0) paragraphEnd = Text.Length;

        return BidiText.Layout(
            Text[paragraphStart..paragraphEnd],
            new BidiTextOptions(BidiDirection.Auto, UnicodeBidi)).BaseDirection;
    }

    private static int GetVisualEdgeOffset(TextVisualRune rune, bool leading)
    {
        if (rune.Direction == BidiDirection.Rtl)
            return leading ? rune.EndOffset : rune.StartOffset;
        return leading ? rune.StartOffset : rune.EndOffset;
    }

    /// <summary>按字号估算字符前进宽度（回退，不依赖字体度量）。</summary>
    public static float MeasureRuneAdvance(Rune rune, float fontSize)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return 0;

        return IsFullWidth(rune.Value) ? fontSize : fontSize * 0.5f;
    }

    /// <summary>测量字符前进宽度，优先使用注册的度量提供器。</summary>
    public static float MeasureRuneAdvance(Rune rune, Font font)
    {
        if (TextMetrics.IsZeroAdvanceCategory(rune)) return 0;
        var provided = TextMetrics.GetGlyphMetrics(font, rune).AdvanceX;
        if (provided >= 0 && float.IsFinite(provided)) return provided;
        var measured = _advanceProvider?.Invoke(rune, font);
        if (measured is >= 0 and float value && float.IsFinite(value)) return value;
        return MeasureRuneAdvanceFallback(rune, font);
    }

    internal static float MeasureRuneAdvanceFallback(Rune rune, Font font)
    {
        var measured = _advanceProvider?.Invoke(rune, font);
        if (measured is >= 0 and float value && float.IsFinite(value)) return value;
        var advance = MeasureRuneAdvance(rune, font.Size);
        return font.Weight >= FontWeight.Bold ? advance * 1.08f : advance;
    }

    private static bool IsFullWidth(int value)
    {
        return value is >= 0x1100 and <= 0x115f or
            0x231a or 0x231b or 0x2329 or 0x232a or
            >= 0x2e80 and <= 0xa4cf or
            >= 0xac00 and <= 0xd7a3 or
            >= 0xf900 and <= 0xfaff or
            >= 0xfe10 and <= 0xfe19 or
            >= 0xfe30 and <= 0xfe6f or
            >= 0xff01 and <= 0xff60 or
            >= 0xffe0 and <= 0xffe6 or
            >= 0x1f300 and <= 0x1faff or
            >= 0x20000 and <= 0x3fffd;
    }

    private readonly record struct LogicalRune(Rune Rune, int StartOffset, int EndOffset);
}

public readonly record struct TextLineRange(int StartOffset, int EndOffset, float Width);

/// <summary>一条逻辑换行对应的视觉字符序列。</summary>
public readonly record struct TextVisualLine(TextLineRange LogicalRange, IReadOnlyList<TextVisualRune> Runes, float Indent = 0)
{
    public float Width => LogicalRange.Width;
}

/// <summary>视觉位置上的字符，同时保留原始逻辑 UTF-16 偏移。</summary>
public readonly record struct TextVisualRune(
    Rune Rune,
    int StartOffset,
    int EndOffset,
    float Advance,
    BidiDirection Direction)
{
    /// <summary>Glyph rune after the supported basic ASCII RTL mirroring step.</summary>
    public Rune Glyph => BidiText.MirrorForVisual(Rune, Direction);
}

public static class TextWrapping
{
    public static IReadOnlyList<TextLineRange> Wrap(
        string text,
        float maxWidth,
        Func<int, Rune, float> measureAdvance,
        TextWrappingOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureAdvance);
        if (text.Length == 0) return [];

        var constrainWidth = float.IsFinite(maxWidth) && maxWidth > 0;
        var lines = new List<TextLineRange>();
        var paragraph = new List<TextWrapToken>();
        var paragraphStart = 0;
        foreach (var token in CreateTokens(text, options, measureAdvance))
        {
            if (token.ForceBreak)
            {
                if (paragraph.Count == 0)
                    lines.Add(new TextLineRange(paragraphStart, paragraphStart, 0));
                else
                    WrapParagraph(paragraph, maxWidth, constrainWidth, lines, options);
                paragraph.Clear();
                paragraphStart = token.End;
            }
            else
            {
                paragraph.Add(token);
            }
        }
        if (paragraph.Count == 0)
            lines.Add(new TextLineRange(paragraphStart, paragraphStart, 0));
        else
            WrapParagraph(paragraph, maxWidth, constrainWidth, lines, options);
        return lines;
    }

    public static IReadOnlyList<TextLineRange> Wrap(
        string text,
        float maxWidth,
        Func<int, Rune, float> measureAdvance)
        => Wrap(text, maxWidth, measureAdvance, default);

    internal static IReadOnlyList<TextWrapToken> CreateTokens(
        string text,
        TextWrappingOptions options,
        Func<int, Rune, float> measureAdvance)
    {
        var tokens = new List<TextWrapToken>();
        var collapseWhitespace = options.WhiteSpace is TextWhiteSpaceMode.Normal or TextWhiteSpaceMode.Nowrap or TextWhiteSpaceMode.PreLine;
        var pendingWhitespaceStart = -1;
        var pendingWhitespaceEnd = -1;
        var atWordStart = true;

        void FlushWhitespace()
        {
            if (pendingWhitespaceStart < 0) return;
            AddToken(pendingWhitespaceStart, pendingWhitespaceEnd, new Rune(' '), isWhitespace: true);
            pendingWhitespaceStart = pendingWhitespaceEnd = -1;
        }

        void AddToken(int start, int end, Rune sourceRune, bool isWhitespace = false)
        {
            var transformed = TransformRune(sourceRune, options.TextTransform, ref atWordStart);
            foreach (var rune in transformed.EnumerateRunes())
            {
                var advance = measureAdvance(start, rune);
                if (rune.Value != '\n') advance += options.LetterSpacing;
                if (isWhitespace) advance += options.WordSpacing;
                tokens.Add(new TextWrapToken(start, end, rune, Math.Max(0, advance), isWhitespace, false));
            }
        }

        var offset = 0;
        foreach (var sourceRune in text.EnumerateRunes())
        {
            var start = offset;
            offset += sourceRune.Utf16SequenceLength;
            var isNewline = sourceRune.Value == '\n';
            var isWhitespace = Rune.IsWhiteSpace(sourceRune) && !isNewline;

            if (isNewline && !options.CollapseNewlines)
            {
                FlushWhitespace();
                tokens.Add(new TextWrapToken(start, offset, sourceRune, 0, true, true));
                atWordStart = true;
                continue;
            }

            if ((isNewline || isWhitespace) && collapseWhitespace)
            {
                pendingWhitespaceStart = pendingWhitespaceStart < 0 ? start : pendingWhitespaceStart;
                pendingWhitespaceEnd = offset;
                atWordStart = true;
                continue;
            }

            if (isWhitespace)
            {
                FlushWhitespace();
                AddToken(start, offset, sourceRune, isWhitespace: true);
                continue;
            }

            FlushWhitespace();
            AddToken(start, offset, sourceRune, isWhitespace: false);
        }
        FlushWhitespace();
        return tokens;
    }

    private static void WrapParagraph(
        List<TextWrapToken> tokens,
        float maxWidth,
        bool constrainWidth,
        List<TextLineRange> lines,
        TextWrappingOptions options)
    {
        if (tokens.Count == 0) return;

        var lineStart = 0;
        while (lineStart < tokens.Count)
        {
            if (options.CollapseNewlines &&
                options.WhiteSpace is TextWhiteSpaceMode.Normal or TextWhiteSpaceMode.Nowrap or TextWhiteSpaceMode.PreLine)
            {
                while (lineStart < tokens.Count && tokens[lineStart].IsWhitespace)
                    lineStart++;
                if (lineStart >= tokens.Count)
                {
                    lines.Add(new TextLineRange(tokens[0].Start, tokens[0].Start, 0));
                    break;
                }
            }

            var width = 0f;
            var lastBreak = -1;
            var widthAtBreak = 0f;
            var wrapped = false;
            for (var index = lineStart; index < tokens.Count; index++)
            {
                if (index > lineStart &&
                    (tokens[index - 1].IsWhitespace || CanBreakBetween(tokens[index - 1].Rune, tokens[index].Rune)))
                {
                    lastBreak = index;
                    widthAtBreak = width;
                }

                var advance = tokens[index].Advance;
                var allowWrap = options.WhiteSpace is not (TextWhiteSpaceMode.Pre or TextWhiteSpaceMode.Nowrap);
                if (allowWrap && constrainWidth && width > 0 && width + advance > maxWidth)
                {
                    var lineEnd = lastBreak > lineStart ? lastBreak : index;
                    var lineWidth = lastBreak > lineStart ? widthAtBreak : width;
                    if (lineEnd <= lineStart) lineEnd = Math.Min(tokens.Count, lineStart + 1);
                    lines.Add(new TextLineRange(tokens[lineStart].Start, tokens[lineEnd - 1].End, lineWidth));
                    lineStart = lineEnd;
                    wrapped = true;
                    break;
                }
                width += advance;
            }

            if (wrapped) continue;
            var lineEndFinal = tokens.Count;
            if (options.CollapseNewlines &&
                options.WhiteSpace is TextWhiteSpaceMode.Normal or TextWhiteSpaceMode.Nowrap or TextWhiteSpaceMode.PreLine)
            {
                while (lineEndFinal > lineStart && tokens[lineEndFinal - 1].IsWhitespace)
                {
                    width -= tokens[lineEndFinal - 1].Advance;
                    lineEndFinal--;
                }
            }
            if (lineEndFinal > lineStart)
                lines.Add(new TextLineRange(tokens[lineStart].Start, tokens[lineEndFinal - 1].End, Math.Max(0, width)));
            break;
        }
    }

    private static bool CanBreakBetween(Rune previous, Rune current)
    {
        if (previous.Value == 0x200b) return true;
        if (Rune.IsWhiteSpace(previous)) return true;
        if (previous.Value is '-' or '/' or '\\') return true;
        if (!IsCjk(previous) && !IsCjk(current)) return false;
        return !IsOpeningPunctuation(previous.Value) && !IsClosingPunctuation(current.Value);
    }

    private static bool IsCjk(Rune rune) => rune.Value is
        >= 0x2e80 and <= 0x9fff or
        >= 0xac00 and <= 0xd7af or
        >= 0xf900 and <= 0xfaff or
        >= 0xff01 and <= 0xff60 or
        >= 0x20000 and <= 0x3fffd;

    private static bool IsOpeningPunctuation(int value) => value is
        '(' or '[' or '{' or 0x2018 or 0x201c or 0x3008 or 0x300a or 0x300c or 0x300e or 0x3010 or 0x3014 or 0xff08 or 0xff3b;

    private static bool IsClosingPunctuation(int value) => value is
        ')' or ']' or '}' or ',' or '.' or '!' or '?' or ':' or ';' or
        0x2019 or 0x201d or 0x3001 or 0x3002 or 0x3009 or 0x300b or 0x300d or 0x300f or 0x3011 or 0x3015 or 0xff09 or 0xff0c or 0xff0e or 0xff01 or 0xff1f;

    internal static string TransformRune(Rune rune, TextTransformMode mode, ref bool atWordStart)
    {
        var text = rune.ToString();
        if (Rune.IsWhiteSpace(rune))
        {
            atWordStart = true;
            return text;
        }

        var transformed = mode switch
        {
            TextTransformMode.Uppercase => text.ToUpperInvariant(),
            TextTransformMode.Lowercase => text.ToLowerInvariant(),
            TextTransformMode.Capitalize when atWordStart => text.ToUpperInvariant(),
            _ => text
        };
        if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter)
            atWordStart = false;
        return transformed;
    }
}

public readonly record struct TextWrappingOptions(
    TextWhiteSpaceMode WhiteSpace = TextWhiteSpaceMode.Normal,
    float LetterSpacing = 0,
    float WordSpacing = 0,
    TextTransformMode TextTransform = TextTransformMode.None,
    float TextIndent = 0,
    bool CollapseNewlines = false,
    TextDecorationLine TextDecorationLines = TextDecorationLine.None);

internal readonly record struct TextWrapToken(
    int Start,
    int End,
    Rune Rune,
    float Advance,
    bool IsWhitespace,
    bool ForceBreak);
