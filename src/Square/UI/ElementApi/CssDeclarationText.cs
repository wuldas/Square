using Square.CSS.Properties;

namespace Square.UI.ElementApi;

/// <summary>
/// 解析出的单条声明行：保留原始偏移与行文本（偏移为完整声明块文本的 UTF-16 code unit、
/// end exclusive），并携带一次准备好的属性赋值。disabled/invalid 行保留原样但绝不应用。
/// </summary>
internal readonly record struct CssDeclarationTextEntry(
    int StartOffset,
    int EndOffset,
    string Text,
    string Property,
    string Value,
    bool Important,
    bool Disabled,
    bool ParsedOk,
    CssPropertyAssignment[] Assignments);

/// <summary>
/// 内联声明块文本解析器，供 <see cref="StyleAccessor"/> 的 CssText 写入与运行时样式编辑共用。
/// 在既有引号/括号/转义扫描上补注释与 CRLF：普通注释不成为属性；包裹单条声明的注释为
/// disabled 行（范围和 Text 均含注释标记）；未闭合引号/注释的剩余段为
/// parsedOk=false；值非法的行同样 parsedOk=false 保留但不应用。解析不抛出语法异常；
/// 未闭合剩余段允许用完整声明块替换修复。空文本返回空列表（仅清空内联）。
/// </summary>
internal static class CssDeclarationText
{
    internal static IReadOnlyList<CssDeclarationTextEntry> Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var entries = new List<CssDeclarationTextEntry>();
        var position = 0;
        while (position < text.Length)
        {
            var current = text[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }
            if (current == ';')
            {
                position++;
                continue;
            }
            if (current == '/' && position + 1 < text.Length && text[position + 1] == '*')
            {
                position = ParseComment(text, position, entries);
                continue;
            }
            position = ParseDeclaration(text, position, entries);
        }
        return entries;
    }

    /// <summary>
    /// 判定 offset 是否为安全剪切点：true 表示可以在此切分（不在字符串或块注释 token 内部，
    /// token 边界与 token 外的位置均为边界）；越界偏移返回 false。
    /// </summary>
    internal static bool IsEditBoundary(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (offset < 0 || offset > text.Length) return false;
        if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset])) return false;
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = FindCommentEnd(text, index);
                var tokenEnd = end < 0 ? text.Length : end;
                if (offset > index && offset < tokenEnd) return false;
                index = tokenEnd;
                continue;
            }
            if (current is '"' or '\'')
            {
                var end = FindStringEnd(text, index);
                var tokenEnd = end < 0 ? text.Length : end;
                if (offset > index && offset < tokenEnd) return false;
                index = tokenEnd;
                continue;
            }
            if (IsNameCodeUnit(current) || current == '#' ||
                (current is '.' or '+' && index + 1 < text.Length && char.IsDigit(text[index + 1])))
            {
                var start = index++;
                while (index < text.Length)
                {
                    if (IsNameCodeUnit(text[index]) || text[index] is '.' or '%') index++;
                    else if (text[index] == '\\') index += Math.Min(2, text.Length - index);
                    else break;
                }
                if (offset > start && offset < index) return false;
                continue;
            }
            if (current == '\\')
            {
                if (offset == index + 1) return false;
                index += Math.Min(2, text.Length - index);
                continue;
            }
            index++;
        }
        return true;
    }

    private static bool IsNameCodeUnit(char value) => char.IsLetterOrDigit(value) || value is '_' or '-' || value >= 128;

    /// <summary>返回块注释结束位置（<c>*/</c> 之后）；未闭合返回 -1。</summary>
    private static int FindCommentEnd(string text, int start)
    {
        var close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
        return close < 0 ? -1 : close + 2;
    }

    /// <summary>返回引号字符串结束位置（闭合引号之后，转义不终止字符串）；未闭合返回 -1。</summary>
    private static int FindStringEnd(string text, int start)
    {
        var quote = text[start];
        for (var index = start + 1; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '\\')
            {
                index++;
                continue;
            }
            if (current == quote) return index + 1;
        }
        return -1;
    }

    /// <summary>扫描以注释开头的片段；返回继续解析的位置。</summary>
    private static int ParseComment(string text, int start, List<CssDeclarationTextEntry> entries)
    {
        var close = text.IndexOf("*/", start + 2, StringComparison.Ordinal);
        if (close < 0)
        {
            AddRemainder(text, start, entries);
            return text.Length;
        }
        var end = close + 2;
        var segments = SplitTopLevelSegments(text[(start + 2)..close]);
        // 包裹单条声明的注释为 disabled 行；普通注释/多条声明不成为属性。
        if (segments.Count == 1 && FindTopLevelColon(segments[0]) > 0)
            entries.Add(CreateEntry(segments[0], start, end, disabled: true) with { Text = text[start..end] });
        return end;
    }

    /// <summary>扫描一条顶层声明（到分号或文本结束）；返回继续解析的位置。</summary>
    private static int ParseDeclaration(string text, int start, List<CssDeclarationTextEntry> entries)
    {
        var depth = 0;
        var index = start;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var commentEnd = FindCommentEnd(text, index);
                if (commentEnd < 0)
                {
                    AddRemainder(text, start, entries);
                    return text.Length;
                }
                index = commentEnd;
                continue;
            }
            if (current is '"' or '\'')
            {
                var stringEnd = FindStringEnd(text, index);
                if (stringEnd < 0)
                {
                    AddRemainder(text, start, entries);
                    return text.Length;
                }
                index = stringEnd;
                continue;
            }
            if (current == '\\')
            {
                index += Math.Min(2, text.Length - index);
                continue;
            }
            if (current == '(') depth++;
            else if (current == ')') depth = Math.Max(0, depth - 1);
            else if (current == ';' && depth == 0)
            {
                AddRow(text, start, index, entries);
                return index + 1;
            }
            index++;
        }
        AddRow(text, start, text.Length, entries);
        return text.Length;
    }

    /// <summary>把 [start, endExclusive) 的原始行收尾为 entry（保留原始空白之外的精确偏移）。</summary>
    private static void AddRow(string text, int start, int endExclusive, List<CssDeclarationTextEntry> entries)
    {
        while (endExclusive > start && char.IsWhiteSpace(text[endExclusive - 1])) endExclusive--;
        if (endExclusive <= start) return;
        entries.Add(CreateEntry(text[start..endExclusive], start, endExclusive, disabled: false));
    }

    /// <summary>未闭合引号/注释的剩余段：整行 parsedOk=false 保留，属性/值为尽力解析。</summary>
    private static void AddRemainder(string text, int start, List<CssDeclarationTextEntry> entries)
    {
        var end = text.Length;
        while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
        if (end <= start) return;
        var declaration = text[start..end];
        entries.Add(CreateEntry(declaration, start, end, disabled: false) with
        {
            ParsedOk = false,
            Assignments = []
        });
    }

    /// <summary>解析单条声明文本为 entry；值非法/结构残缺时 parsedOk=false，不抛出。</summary>
    private static CssDeclarationTextEntry CreateEntry(string declaration, int startOffset, int endOffset, bool disabled)
    {
        var property = "";
        var value = "";
        var important = false;
        var parsedOk = false;
        var assignments = Array.Empty<CssPropertyAssignment>();
        var parsed = RemoveComments(declaration);
        var colon = FindTopLevelColon(parsed);
        if (colon > 0)
        {
            property = StyleAccessor.NormalizePropertyName(parsed[..colon]);
            value = parsed[(colon + 1)..].Trim();
            if (property.Length > 0 && value.Length > 0)
            {
                important = TryRemoveImportant(ref value);
                if (value.Length > 0 &&
                    StyleAccessor.TryGetAssignments(property, value, out var prepared))
                {
                    assignments = prepared;
                    parsedOk = true;
                }
                else
                {
                    assignments = [];
                }
            }
        }
        return new CssDeclarationTextEntry(
            startOffset, endOffset, declaration, property, value, important, disabled, parsedOk, assignments);
    }

    /// <summary>按顶层分号拆分并修剪空段；引号内的分号不拆分。</summary>
    private static List<string> SplitTopLevelSegments(string value)
    {
        var segments = new List<string>();
        var depth = 0;
        var start = 0;
        char quote = '\0';
        for (var index = 0; index <= value.Length; index++)
        {
            if (index < value.Length)
            {
                var current = value[index];
                if (quote != '\0')
                {
                    if (current == '\\')
                    {
                        index++;
                        continue;
                    }
                    if (current == quote) quote = '\0';
                    continue;
                }
                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }
                if (current == '\\')
                {
                    index++;
                    continue;
                }
                if (current == '(') depth++;
                else if (current == ')') depth = Math.Max(0, depth - 1);
                if (current != ';' || depth > 0) continue;
            }
            var segment = value[start..index].Trim();
            if (segment.Length > 0) segments.Add(segment);
            start = index + 1;
        }
        return segments;
    }

    /// <summary>找顶层冒号；引号/注释/括号内不算。未闭合 token 返回 -1。</summary>
    private static int FindTopLevelColon(string value)
    {
        var depth = 0;
        var index = 0;
        while (index < value.Length)
        {
            var current = value[index];
            if (current == '/' && index + 1 < value.Length && value[index + 1] == '*')
            {
                var commentEnd = FindCommentEnd(value, index);
                if (commentEnd < 0) return -1;
                index = commentEnd;
                continue;
            }
            if (current is '"' or '\'')
            {
                var stringEnd = FindStringEnd(value, index);
                if (stringEnd < 0) return -1;
                index = stringEnd;
                continue;
            }
            if (current == '\\')
            {
                index += Math.Min(2, value.Length - index);
                continue;
            }
            if (current == '(') depth++;
            else if (current == ')') depth = Math.Max(0, depth - 1);
            else if (current == ':' && depth == 0) return index;
            index++;
        }
        return -1;
    }

    private static string RemoveComments(string text)
    {
        System.Text.StringBuilder? result = null;
        var copiedThrough = 0;
        for (var index = 0; index < text.Length;)
        {
            var current = text[index];
            if (current is '"' or '\'')
            {
                var end = FindStringEnd(text, index);
                index = end < 0 ? text.Length : end;
            }
            else if (current == '\\') index += Math.Min(2, text.Length - index);
            else if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = FindCommentEnd(text, index);
                if (end < 0) break;
                result ??= new System.Text.StringBuilder(text.Length);
                result.Append(text, copiedThrough, index - copiedThrough).Append(' ');
                index = copiedThrough = end;
            }
            else index++;
        }
        return result == null ? text : result.Append(text, copiedThrough, text.Length - copiedThrough).ToString();
    }

    /// <summary>与既有 CSSOM 语义一致的 <c>!important</c> 摘除；前缀为空视为无效行。</summary>
    private static bool TryRemoveImportant(ref string value)
    {
        var index = value.LastIndexOf('!');
        if (index < 0 || !string.Equals(value[(index + 1)..].Trim(), "important", StringComparison.OrdinalIgnoreCase))
            return false;
        value = value[..index].TrimEnd();
        return value.Length > 0;
    }
}
