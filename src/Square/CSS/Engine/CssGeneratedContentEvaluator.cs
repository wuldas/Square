using System.Globalization;
using System.Text;
using Square.UI;

namespace Square.CSS.Engine;

internal static class CssGeneratedContentEvaluator
{
    private static readonly (string Open, string Close)[] DefaultQuotes = [("\u201c", "\u201d"), ("\u2018", "\u2019")];

    public static IReadOnlyCollection<Element> Evaluate(Element root)
    {
        var changed = new HashSet<Element>();
        var counters = new CounterState();
        var quoteDepth = 0;
        EvaluateElement(root, counters, ref quoteDepth, changed);
        return changed;
    }

    private static void EvaluateElement(
        Element owner,
        CounterState counters,
        ref int quoteDepth,
        HashSet<Element> changed)
    {
        var resetCounters = ApplyCounterReset(owner.Style.Get("counter-reset"), counters);
        ApplyCounterIncrement(owner.Style.Get("counter-increment"), counters);

        EvaluatePseudo(owner, "marker", counters, ref quoteDepth, changed);
        EvaluatePseudo(owner, "before", counters, ref quoteDepth, changed);

        foreach (var child in owner.Children.Where(child => child is not CssGeneratedPseudoElement).ToArray())
            EvaluateElement(child, counters, ref quoteDepth, changed);

        EvaluatePseudo(owner, "after", counters, ref quoteDepth, changed);
        for (var i = resetCounters.Count - 1; i >= 0; i--)
            counters.Pop(resetCounters[i]);
    }

    private static void EvaluatePseudo(
        Element owner,
        string name,
        CounterState counters,
        ref int quoteDepth,
        HashSet<Element> changed)
    {
        var generated = owner.Children.OfType<CssGeneratedPseudoElement>()
            .FirstOrDefault(child => child.PseudoElementName == name);
        if (generated == null) return;

        var contentValue = generated.Style.Get("content");
        if (name != "marker" && (contentValue is null ||
            contentValue.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ||
            contentValue.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase)))
        {
            using (Element.SuppressInvalidation()) owner.Children.Remove(generated);
            changed.Add(owner);
            return;
        }

        var resetCounters = ApplyCounterReset(generated.Style.Get("counter-reset"), counters);
        ApplyCounterIncrement(generated.Style.Get("counter-increment"), counters);
        var hasContent = name == "marker"
            ? TryEvaluateMarker(owner, contentValue, counters, ref quoteDepth, out var content)
            : TryEvaluateContent(owner, contentValue, counters, ref quoteDepth, out content);
        for (var i = resetCounters.Count - 1; i >= 0; i--)
            counters.Pop(resetCounters[i]);
        if (!hasContent)
        {
            using (Element.SuppressInvalidation()) owner.Children.Remove(generated);
            changed.Add(owner);
            return;
        }

        var targetIndex = name switch
        {
            "marker" => 0,
            "before" => owner.Children.Any(child => child is CssGeneratedPseudoElement pseudo &&
                pseudo.PseudoElementName == "marker") ? 1 : 0,
            _ => owner.Children.Count - 1
        };
        var currentIndex = owner.Children.IndexOf(generated);
        if (currentIndex != targetIndex)
        {
            using (Element.SuppressInvalidation()) owner.Children.Move(currentIndex, targetIndex);
            changed.Add(owner);
        }
        if (generated.TextContent != content)
        {
            using (Element.SuppressInvalidation()) generated.TextContent = content;
            changed.Add(owner);
        }
        if (generated.IsNew)
        {
            generated.IsNew = false;
            changed.Add(owner);
        }
    }

    private static bool TryEvaluateMarker(
        Element owner,
        string? value,
        CounterState counters,
        ref int quoteDepth,
        out string content)
    {
        content = "";
        if (!string.Equals(owner.Style.Get("display")?.Trim(), "list-item", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(value) &&
            !value.Trim().Equals("normal", StringComparison.OrdinalIgnoreCase))
            return TryEvaluateContent(owner, value, counters, ref quoteDepth, out content);

        var type = owner.Style.Get("list-style-type")?.Trim().ToLowerInvariant() ?? "disc";
        if (type == "none") return false;
        var index = GetListItemIndex(owner);
        content = type switch
        {
            "disc" => "\u2022 ",
            "circle" => "\u25e6 ",
            "square" => "\u25aa ",
            "decimal" => FormatCounter(index, "decimal") + ". ",
            "lower-alpha" or "lower-latin" => FormatCounter(index, "lower-alpha") + ". ",
            "upper-alpha" or "upper-latin" => FormatCounter(index, "upper-alpha") + ". ",
            "lower-roman" => FormatCounter(index, "lower-roman") + ". ",
            "upper-roman" => FormatCounter(index, "upper-roman") + ". ",
            _ => "\u2022 "
        };
        return true;
    }

    private static int GetListItemIndex(Element owner)
    {
        if (owner.Parent == null) return 1;
        var index = 0;
        foreach (var sibling in owner.Parent.Children)
        {
            if (sibling is CssGeneratedPseudoElement) continue;
            if (string.Equals(sibling.Style.Get("display")?.Trim(), "list-item", StringComparison.OrdinalIgnoreCase))
                index++;
            if (ReferenceEquals(sibling, owner)) return Math.Max(1, index);
        }
        return 1;
    }

    private static bool TryEvaluateContent(
        Element owner,
        string? value,
        CounterState counters,
        ref int quoteDepth,
        out string content)
    {
        content = "";
        if (value is null) return false;
        value = value.Trim();
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("normal", StringComparison.OrdinalIgnoreCase)) return false;
        // 显式空字符串（content: ""）：保留为空装饰盒子，用于纯样式伪元素。
        if (value.Length == 0) return true;

        var quotes = ParseQuotes(owner.Style.Get("quotes"));
        var result = new StringBuilder();
        var index = 0;
        var foundToken = false;
        while (index < value.Length)
        {
            SkipWhitespace(value, ref index);
            if (index >= value.Length) break;
            foundToken = true;
            if (value[index] is '\'' or '"')
            {
                if (!TryReadString(value, ref index, out var text)) return false;
                result.Append(text);
                continue;
            }

            var name = ReadIdentifier(value, ref index);
            if (name.Length == 0) return false;
            if (index < value.Length && value[index] == '(')
            {
                if (!TryReadFunction(value, ref index, out var arguments)) return false;
                if (!AppendFunction(result, owner, counters, name, arguments)) return false;
                continue;
            }

            switch (name.ToLowerInvariant())
            {
                case "open-quote":
                    if (quotes.Length > 0) result.Append(quotes[Math.Min(quoteDepth, quotes.Length - 1)].Open);
                    quoteDepth++;
                    break;
                case "close-quote":
                    quoteDepth = Math.Max(0, quoteDepth - 1);
                    if (quotes.Length > 0) result.Append(quotes[Math.Min(quoteDepth, quotes.Length - 1)].Close);
                    break;
                case "no-open-quote":
                    quoteDepth++;
                    break;
                case "no-close-quote":
                    quoteDepth = Math.Max(0, quoteDepth - 1);
                    break;
                default:
                    return false;
            }
        }

        if (!foundToken) return false;
        content = result.ToString();
        return true;
    }

    private static bool AppendFunction(
        StringBuilder result,
        Element owner,
        CounterState counters,
        string name,
        string arguments)
    {
        var parts = SplitArguments(arguments);
        if (name.Equals("attr", StringComparison.OrdinalIgnoreCase) && parts.Count == 1)
        {
            var attributeName = parts[0].Trim();
            object? value = attributeName.Equals("id", StringComparison.OrdinalIgnoreCase)
                ? owner.Id
                : owner.GetProperty<object>(attributeName);
            result.Append(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
            return true;
        }
        if (name.Equals("counter", StringComparison.OrdinalIgnoreCase) && parts.Count is 1 or 2)
        {
            var counterName = parts[0].Trim();
            var style = parts.Count == 2 ? parts[1].Trim() : "decimal";
            result.Append(FormatCounter(counters.Current(counterName), style));
            return counterName.Length > 0;
        }
        if (name.Equals("counters", StringComparison.OrdinalIgnoreCase) && parts.Count is 2 or 3 &&
            TryParseSingleString(parts[1], out var separator))
        {
            var counterName = parts[0].Trim();
            var style = parts.Count == 3 ? parts[2].Trim() : "decimal";
            result.Append(string.Join(separator, counters.All(counterName).Select(number => FormatCounter(number, style))));
            return counterName.Length > 0;
        }
        return false;
    }

    private static List<string> ApplyCounterReset(string? value, CounterState counters)
    {
        var pushed = new List<string>();
        foreach (var (name, number) in ParseCounterAssignments(value, 0))
        {
            counters.Push(name, number);
            pushed.Add(name);
        }
        return pushed;
    }

    private static void ApplyCounterIncrement(string? value, CounterState counters)
    {
        foreach (var (name, number) in ParseCounterAssignments(value, 1))
            counters.Increment(name, number);
    }

    private static IEnumerable<(string Name, int Value)> ParseCounterAssignments(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            yield break;
        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var name = tokens[index];
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) continue;
            var number = defaultValue;
            if (index + 1 < tokens.Length &&
                int.TryParse(tokens[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                number = parsed;
                index++;
            }
            yield return (name, number);
        }
    }

    private static (string Open, string Close)[] ParseQuotes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return DefaultQuotes;
        value = value.Trim();
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        var strings = new List<string>();
        var index = 0;
        while (index < value.Length)
        {
            SkipWhitespace(value, ref index);
            if (index >= value.Length) break;
            if (!TryReadString(value, ref index, out var text)) return DefaultQuotes;
            strings.Add(text);
        }
        if (strings.Count == 0 || strings.Count % 2 != 0) return DefaultQuotes;
        return strings.Chunk(2).Select(pair => (pair[0], pair[1])).ToArray();
    }

    private static string FormatCounter(int value, string style)
    {
        style = style.Trim().ToLowerInvariant();
        return style switch
        {
            "lower-alpha" or "lower-latin" => FormatAlpha(value, false),
            "upper-alpha" or "upper-latin" => FormatAlpha(value, true),
            "lower-roman" => FormatRoman(value).ToLowerInvariant(),
            "upper-roman" => FormatRoman(value),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string FormatAlpha(int value, bool upper)
    {
        if (value <= 0) return value.ToString(CultureInfo.InvariantCulture);
        var result = new StringBuilder();
        while (value > 0)
        {
            value--;
            result.Insert(0, (char)((upper ? 'A' : 'a') + value % 26));
            value /= 26;
        }
        return result.ToString();
    }

    private static string FormatRoman(int value)
    {
        if (value is <= 0 or > 3999) return value.ToString(CultureInfo.InvariantCulture);
        var result = new StringBuilder();
        foreach (var (number, digits) in new[]
                 {
                     (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
                     (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
                 })
        {
            while (value >= number)
            {
                result.Append(digits);
                value -= number;
            }
        }
        return result.ToString();
    }

    private static List<string> SplitArguments(string value)
    {
        var result = new List<string>();
        var start = 0;
        var depth = 0;
        char quote = '\0';
        for (var index = 0; index <= value.Length; index++)
        {
            if (index < value.Length)
            {
                var character = value[index];
                if (quote != '\0')
                {
                    if (character == '\\') index++;
                    else if (character == quote) quote = '\0';
                    continue;
                }
                if (character is '\'' or '"')
                {
                    quote = character;
                    continue;
                }
                if (character == '(')
                {
                    depth++;
                    continue;
                }
                if (character == ')')
                {
                    depth--;
                    continue;
                }
                if (character != ',' || depth != 0) continue;
            }
            result.Add(value[start..index].Trim());
            start = index + 1;
        }
        return result;
    }

    private static bool TryParseSingleString(string value, out string result)
    {
        var index = 0;
        SkipWhitespace(value, ref index);
        if (!TryReadString(value, ref index, out result)) return false;
        SkipWhitespace(value, ref index);
        return index == value.Length;
    }

    private static bool TryReadString(string value, ref int index, out string result)
    {
        result = "";
        if (index >= value.Length || value[index] is not ('\'' or '"')) return false;
        var quote = value[index++];
        var text = new StringBuilder();
        while (index < value.Length)
        {
            var character = value[index++];
            if (character == quote)
            {
                result = text.ToString();
                return true;
            }
            if (character == '\\' && index < value.Length) character = value[index++];
            text.Append(character);
        }
        return false;
    }

    private static bool TryReadFunction(string value, ref int index, out string arguments)
    {
        arguments = "";
        if (index >= value.Length || value[index] != '(') return false;
        var start = ++index;
        var depth = 1;
        char quote = '\0';
        while (index < value.Length)
        {
            var character = value[index];
            if (quote != '\0')
            {
                if (character == '\\') index += 2;
                else
                {
                    if (character == quote) quote = '\0';
                    index++;
                }
                continue;
            }
            if (character is '\'' or '"') quote = character;
            else if (character == '(') depth++;
            else if (character == ')' && --depth == 0)
            {
                arguments = value[start..index];
                index++;
                return true;
            }
            index++;
        }
        return false;
    }

    private static string ReadIdentifier(string value, ref int index)
    {
        var start = index;
        while (index < value.Length && (char.IsLetterOrDigit(value[index]) || value[index] is '-' or '_')) index++;
        return value[start..index];
    }

    private static void SkipWhitespace(string value, ref int index)
    {
        while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
    }

    private sealed class CounterState
    {
        private readonly Dictionary<string, List<int>> _values = new(StringComparer.Ordinal);

        public void Push(string name, int value)
        {
            if (!_values.TryGetValue(name, out var values)) _values[name] = values = [];
            values.Add(value);
        }

        public void Pop(string name)
        {
            if (!_values.TryGetValue(name, out var values) || values.Count == 0) return;
            values.RemoveAt(values.Count - 1);
            if (values.Count == 0) _values.Remove(name);
        }

        public void Increment(string name, int amount)
        {
            if (!_values.TryGetValue(name, out var values) || values.Count == 0)
                Push(name, 0);
            values = _values[name];
            values[^1] += amount;
        }

        public int Current(string name) =>
            _values.TryGetValue(name, out var values) && values.Count > 0 ? values[^1] : 0;

        public IReadOnlyList<int> All(string name) =>
            _values.TryGetValue(name, out var values) && values.Count > 0 ? values : [0];
    }
}
