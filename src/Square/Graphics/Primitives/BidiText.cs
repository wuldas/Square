using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Square.Graphics;

/// <summary>Direction used by the basic bidirectional text resolver.</summary>
public enum BidiDirection : byte
{
    /// <summary>Determine the direction from the first strong character.</summary>
    Auto,
    /// <summary>Left-to-right.</summary>
    Ltr,
    /// <summary>Right-to-left.</summary>
    Rtl
}

/// <summary>Run-level subset of CSS <c>unicode-bidi</c> values.</summary>
public enum BidiTextMode : byte
{
    /// <summary>Resolve the text using its natural character classes.</summary>
    Normal,
    /// <summary>Resolve the text inside an embedding in the requested direction.</summary>
    Embed,
    /// <summary>Force every character in the run to the requested direction.</summary>
    BidiOverride
}

/// <summary>Basic bidi character classes used by <see cref="BidiText"/>.</summary>
public enum BidiCharacterClass : byte
{
    /// <summary>Strong left-to-right character.</summary>
    Ltr,
    /// <summary>Strong right-to-left character.</summary>
    Rtl,
    /// <summary>European decimal number.</summary>
    EuropeanNumber,
    /// <summary>Arabic-Indic or extended Arabic-Indic decimal number.</summary>
    ArabicNumber,
    /// <summary>Unicode whitespace.</summary>
    Whitespace,
    /// <summary>Punctuation, symbols, marks, controls, and other neutral characters.</summary>
    Neutral
}

/// <summary>Input direction and run-level bidi mode.</summary>
public readonly record struct BidiTextOptions(
    BidiDirection Direction = BidiDirection.Auto,
    BidiTextMode UnicodeBidi = BidiTextMode.Normal);

/// <summary>A visual run with a logical UTF-16 range.</summary>
public readonly record struct BidiTextRun(int Start, int Length, BidiDirection Direction, int Level)
{
    /// <summary>Exclusive logical UTF-16 end offset.</summary>
    public int End => Start + Length;
}

/// <summary>Resolved bidirectional order for one paragraph.</summary>
public sealed class BidiTextLayout
{
    internal BidiTextLayout(
        string text,
        BidiTextOptions options,
        BidiDirection baseDirection,
        IReadOnlyList<BidiTextRun> visualRuns,
        IReadOnlyList<int> visualToLogical,
        IReadOnlyList<int> logicalToVisual)
    {
        Text = text;
        Options = options;
        BaseDirection = baseDirection;
        VisualRuns = visualRuns;
        VisualToLogical = visualToLogical;
        LogicalToVisual = logicalToVisual;
    }

    /// <summary>Original paragraph text.</summary>
    public string Text { get; }

    /// <summary>Options used to resolve the paragraph.</summary>
    public BidiTextOptions Options { get; }

    /// <summary>Resolved paragraph direction.</summary>
    public BidiDirection BaseDirection { get; }

    /// <summary>
    /// Runs in visual order. Each run's <see cref="BidiTextRun.Start"/> and
    /// <see cref="BidiTextRun.Length"/> refer to the original UTF-16 text.
    /// </summary>
    public IReadOnlyList<BidiTextRun> VisualRuns { get; }

    /// <summary>
    /// Logical rune indexes in visual order. This is not a UTF-16 offset list;
    /// use the run ranges when a UTF-16 range is required.
    /// </summary>
    public IReadOnlyList<int> VisualToLogical { get; }

    /// <summary>Visual position for each logical rune index.</summary>
    public IReadOnlyList<int> LogicalToVisual { get; }
}

/// <summary>
/// Small run-level Unicode bidirectional layout helper for a single paragraph.
/// It does not perform Arabic shaping or full UAX #9 isolate and explicit-control
/// handling; callers should split paragraphs themselves. Basic ASCII mirroring is
/// available through <see cref="MirrorForVisual"/>.
/// Renderers may additionally use <see cref="MirrorForVisual"/> for basic ASCII bracket pairs.
/// </summary>
public static class BidiText
{
    /// <summary>Returns the basic ASCII bracket mirror for an RTL visual run.</summary>
    public static Rune MirrorForVisual(Rune rune, BidiDirection direction)
    {
        if (direction != BidiDirection.Rtl) return rune;
        return rune.Value switch
        {
            '(' => new Rune(')'),
            ')' => new Rune('('),
            '[' => new Rune(']'),
            ']' => new Rune('['),
            '{' => new Rune('}'),
            '}' => new Rune('{'),
            '<' => new Rune('>'),
            '>' => new Rune('<'),
            _ => rune
        };
    }

    /// <summary>Classifies one rune into the resolver's basic bidi classes.</summary>
    public static BidiCharacterClass Classify(Rune rune)
    {
        var value = rune.Value;
        if (IsArabicNumber(value)) return BidiCharacterClass.ArabicNumber;
        if (Rune.IsWhiteSpace(rune)) return BidiCharacterClass.Whitespace;

        var category = Rune.GetUnicodeCategory(rune);
        if (category == UnicodeCategory.DecimalDigitNumber)
            return BidiCharacterClass.EuropeanNumber;
        if (IsRtlLetter(value, category)) return BidiCharacterClass.Rtl;
        if (IsLetter(category)) return BidiCharacterClass.Ltr;
        return BidiCharacterClass.Neutral;
    }

    /// <summary>Resolves one paragraph into visual runs and rune ordering.</summary>
    public static BidiTextLayout Layout(string text, BidiTextOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        Validate(options);

        var characters = ReadCharacters(text);
        var baseDirection = DetermineBaseDirection(characters, options.Direction);
        if (characters.Count == 0)
        {
            return new BidiTextLayout(
                text,
                options,
                baseDirection,
                Array.Empty<BidiTextRun>(),
                Array.Empty<int>(),
                Array.Empty<int>());
        }

        var directions = ResolveDirections(characters, baseDirection, options);
        var levels = ResolveLevels(directions, baseDirection, options);
        var logicalRuns = BuildLogicalRuns(characters, directions, levels, out var runIndexes);
        var visualToLogical = BuildVisualOrder(levels);
        var logicalToVisual = BuildLogicalToVisual(visualToLogical);
        var visualRuns = BuildVisualRuns(logicalRuns, runIndexes, visualToLogical);

        return new BidiTextLayout(
            text,
            options,
            baseDirection,
            new ReadOnlyCollection<BidiTextRun>(visualRuns),
            new ReadOnlyCollection<int>(visualToLogical),
            new ReadOnlyCollection<int>(logicalToVisual));
    }

    private static List<Character> ReadCharacters(string text)
    {
        var characters = new List<Character>(text.Length);
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var length = rune.Utf16SequenceLength;
            characters.Add(new Character(offset, length, Classify(rune)));
            offset += length;
        }
        return characters;
    }

    private static BidiDirection DetermineBaseDirection(
        IReadOnlyList<Character> characters,
        BidiDirection requestedDirection)
    {
        if (requestedDirection != BidiDirection.Auto)
            return requestedDirection;

        foreach (var character in characters)
        {
            if (character.Class == BidiCharacterClass.Ltr) return BidiDirection.Ltr;
            if (character.Class == BidiCharacterClass.Rtl) return BidiDirection.Rtl;
        }

        return BidiDirection.Ltr;
    }

    private static BidiDirection[] ResolveDirections(
        IReadOnlyList<Character> characters,
        BidiDirection baseDirection,
        BidiTextOptions options)
    {
        var directions = new BidiDirection[characters.Count];
        if (options.UnicodeBidi == BidiTextMode.BidiOverride)
        {
            var overrideDirection = options.Direction == BidiDirection.Auto
                ? baseDirection
                : options.Direction;
            Array.Fill(directions, overrideDirection);
            return directions;
        }

        for (var index = 0; index < characters.Count; index++)
        {
            directions[index] = characters[index].Class switch
            {
                BidiCharacterClass.Ltr or
                BidiCharacterClass.EuropeanNumber or
                BidiCharacterClass.ArabicNumber => BidiDirection.Ltr,
                BidiCharacterClass.Rtl => BidiDirection.Rtl,
                _ => BidiDirection.Auto
            };
        }

        for (var start = 0; start < characters.Count;)
        {
            if (directions[start] != BidiDirection.Auto)
            {
                start++;
                continue;
            }

            var end = start + 1;
            while (end < characters.Count && directions[end] == BidiDirection.Auto)
                end++;

            var before = FindContextDirection(directions, start - 1, -1);
            var after = FindContextDirection(directions, end, 1);
            var resolved = before != BidiDirection.Auto && before == after
                ? before
                : baseDirection;
            for (var index = start; index < end; index++)
                directions[index] = resolved;
            start = end;
        }

        return directions;
    }

    private static BidiDirection FindContextDirection(
        IReadOnlyList<BidiDirection> directions,
        int index,
        int step)
    {
        for (; index >= 0 && index < directions.Count; index += step)
        {
            var direction = directions[index];
            if (direction != BidiDirection.Auto) return direction;
        }
        return BidiDirection.Auto;
    }

    private static int[] ResolveLevels(
        IReadOnlyList<BidiDirection> directions,
        BidiDirection baseDirection,
        BidiTextOptions options)
    {
        var levels = new int[directions.Count];
        if (options.UnicodeBidi == BidiTextMode.Normal)
        {
            for (var index = 0; index < directions.Count; index++)
                levels[index] = GetNormalLevel(directions[index], baseDirection);
            return levels;
        }

        if (options.UnicodeBidi == BidiTextMode.BidiOverride)
        {
            var overrideDirection = options.Direction == BidiDirection.Auto
                ? baseDirection
                : options.Direction;
            var level = GetNormalLevel(overrideDirection, baseDirection);
            Array.Fill(levels, level);
            return levels;
        }

        var embeddingDirection = options.Direction == BidiDirection.Auto
            ? baseDirection
            : options.Direction;
        var embeddingLevel = GetEmbeddingLevel(embeddingDirection, baseDirection);
        for (var index = 0; index < directions.Count; index++)
            levels[index] = directions[index] == embeddingDirection
                ? embeddingLevel
                : embeddingLevel + 1;
        return levels;
    }

    private static List<BidiTextRun> BuildLogicalRuns(
        IReadOnlyList<Character> characters,
        IReadOnlyList<BidiDirection> directions,
        IReadOnlyList<int> levels,
        out int[] runIndexes)
    {
        var runs = new List<BidiTextRun>();
        runIndexes = new int[characters.Count];
        for (var start = 0; start < characters.Count;)
        {
            var end = start + 1;
            while (end < characters.Count &&
                   directions[end] == directions[start] &&
                   levels[end] == levels[start])
            {
                end++;
            }

            var logicalStart = characters[start].Start;
            var logicalEnd = characters[end - 1].Start + characters[end - 1].Length;
            var run = new BidiTextRun(logicalStart, logicalEnd - logicalStart, directions[start], levels[start]);
            var runIndex = runs.Count;
            runs.Add(run);
            for (var index = start; index < end; index++)
                runIndexes[index] = runIndex;
            start = end;
        }
        return runs;
    }

    private static int[] BuildVisualOrder(IReadOnlyList<int> levels)
    {
        var ordering = new int[levels.Count];
        for (var index = 0; index < ordering.Length; index++)
            ordering[index] = index;

        var maximum = 0;
        var minimumOdd = int.MaxValue;
        foreach (var level in levels)
        {
            maximum = Math.Max(maximum, level);
            if ((level & 1) != 0) minimumOdd = Math.Min(minimumOdd, level);
        }

        if (minimumOdd == int.MaxValue) return ordering;
        for (var level = maximum; level >= minimumOdd; level--)
        {
            for (var start = 0; start < ordering.Length;)
            {
                if (levels[ordering[start]] < level)
                {
                    start++;
                    continue;
                }

                var end = start + 1;
                while (end < ordering.Length && levels[ordering[end]] >= level)
                    end++;
                Array.Reverse(ordering, start, end - start);
                start = end;
            }
        }
        return ordering;
    }

    private static int[] BuildLogicalToVisual(IReadOnlyList<int> visualToLogical)
    {
        var logicalToVisual = new int[visualToLogical.Count];
        for (var visualIndex = 0; visualIndex < visualToLogical.Count; visualIndex++)
            logicalToVisual[visualToLogical[visualIndex]] = visualIndex;
        return logicalToVisual;
    }

    private static List<BidiTextRun> BuildVisualRuns(
        IReadOnlyList<BidiTextRun> logicalRuns,
        IReadOnlyList<int> runIndexes,
        IReadOnlyList<int> visualToLogical)
    {
        var visualRuns = new List<BidiTextRun>(logicalRuns.Count);
        var previousRun = -1;
        foreach (var logicalIndex in visualToLogical)
        {
            var runIndex = runIndexes[logicalIndex];
            if (runIndex == previousRun) continue;
            visualRuns.Add(logicalRuns[runIndex]);
            previousRun = runIndex;
        }
        return visualRuns;
    }

    private static int GetNormalLevel(BidiDirection direction, BidiDirection baseDirection)
        => direction == BidiDirection.Rtl ? 1 : baseDirection == BidiDirection.Ltr ? 0 : 2;

    private static int GetEmbeddingLevel(BidiDirection direction, BidiDirection baseDirection)
    {
        var level = (baseDirection == BidiDirection.Rtl ? 1 : 0) + 1;
        if (direction == BidiDirection.Ltr && (level & 1) != 0) level++;
        if (direction == BidiDirection.Rtl && (level & 1) == 0) level++;
        return level;
    }

    private static bool IsArabicNumber(int value)
        => value is >= 0x0660 and <= 0x0669 or >= 0x06f0 and <= 0x06f9;

    private static bool IsRtlLetter(int value, UnicodeCategory category)
        => IsLetter(category) && (value is >= 0x0590 and <= 0x05ff or
            >= 0x0600 and <= 0x06ff or
            >= 0x0750 and <= 0x077f or
            >= 0x08a0 and <= 0x08ff or
            >= 0xfb1d and <= 0xfdff or
            >= 0xfe70 and <= 0xfeff or
            >= 0x1ee00 and <= 0x1eeff);

    private static bool IsLetter(UnicodeCategory category)
        => category is UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.LetterNumber;

    private static void Validate(BidiTextOptions options)
    {
        if (options.Direction is < BidiDirection.Auto or > BidiDirection.Rtl)
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown bidi direction.");
        if (options.UnicodeBidi is < BidiTextMode.Normal or > BidiTextMode.BidiOverride)
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown unicode-bidi mode.");
    }

    private readonly record struct Character(int Start, int Length, BidiCharacterClass Class);
}
