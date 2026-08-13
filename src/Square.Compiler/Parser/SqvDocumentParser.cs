using System.Text.RegularExpressions;

namespace Square.Compiler.Parser;

/// <summary>
/// Vue 文档分区解析器：拆分 &lt;template&gt; / &lt;script&gt; / &lt;style&gt;，提取脚本元数据，
/// 并把模板内容交给 <see cref="SqvTemplateParser"/>。不依赖 <c>SqxCoreParser</c>。
/// </summary>
internal static class SqvDocumentParser
{
    public static SqxDocument Parse(string source, string fileName, bool tolerant = false)
    {
        var sections = SplitSections(source, tolerant);
        if (!sections.TryGetValue("template", out var templateSection))
        {
            if (tolerant)
            {
                return new SqxDocument
                {
                    Name = string.IsNullOrEmpty(fileName) ? "Component" : Path.GetFileNameWithoutExtension(fileName),
                    SourcePath = fileName,
                    Template = new SqxTemplate()
                };
            }
            throw new SqxParseException("Missing required <template> section", 0);
        }

        var roots = SqvTemplateParser.Parse(templateSection.Content, templateSection.ContentStart, tolerant);
        if (!tolerant)
            SqvValidator.Validate(roots);

        var document = new SqxDocument
        {
            Name = string.IsNullOrEmpty(fileName) ? "Component" : Path.GetFileNameWithoutExtension(fileName),
            SourcePath = fileName,
            Template = new SqxTemplate { Roots = roots }
        };

        if (sections.TryGetValue("script", out var scriptSection))
        {
            var meta = ParseScriptMetadata(scriptSection.OpeningTag);
            if (!string.Equals(meta.Language, "csharp", StringComparison.OrdinalIgnoreCase))
                throw new SqxParseException(
                    "Unsupported script language '" + meta.Language + "'",
                    scriptSection.Start,
                    "SQV0001");
            document.ScriptCode = scriptSection.Content.Trim();
            document.ScriptLang = meta.Language;
            document.Namespace = meta.Namespace;
            document.Access = meta.Access;
            if (!string.IsNullOrEmpty(meta.ComponentName)) document.Name = meta.ComponentName;
        }

        if (sections.TryGetValue("style", out var styleSection))
            document.StyleCode = styleSection.Content.Trim();

        return document;
    }

    private static Dictionary<string, Section> SplitSections(string source, bool tolerant)
    {
        var sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < source.Length)
        {
            SkipTrivia(source, ref position, tolerant);
            if (position >= source.Length) break;
            if (source[position] != '<')
                throw new SqxParseException("Unexpected content outside a top-level section", position);

            var nameStart = position + 1;
            var nameEnd = nameStart;
            while (nameEnd < source.Length && char.IsLetter(source[nameEnd])) nameEnd++;
            if (nameEnd == nameStart)
                throw new SqxParseException("Invalid top-level section", position);

            var sourceName = source.Substring(nameStart, nameEnd - nameStart);
            var name = sourceName.ToLowerInvariant();
            // 必须是完整的标签名边界（空格/属性/>/ /），避免误匹配嵌套的 <template v-slot:...>
            var boundary = nameEnd;
            if (boundary < source.Length && source[boundary] != '>' && source[boundary] != '/' && !char.IsWhiteSpace(source[boundary]))
                throw new SqxParseException("Invalid top-level section", position);
            if (name != "template" && name != "script" && name != "style")
                throw new SqxParseException("Unknown top-level section <" + sourceName + ">", position);
            if (sections.ContainsKey(name))
                throw new SqxParseException("Duplicate <" + name + "> section", position);

            var openingEnd = FindTagEnd(source, nameEnd);
            if (openingEnd < 0)
            {
                if (!tolerant) throw new SqxParseException("Unclosed <" + name + "> opening tag", position);
                sections.Add(name, new Section(source.Substring(position), string.Empty, position, source.Length));
                break;
            }

            var closeStart = FindMatchingCloseTag(source, name, openingEnd + 1);
            var closeEnd = closeStart < 0 ? -1 : FindTagEnd(source, closeStart + name.Length + 2);
            var contentStart = openingEnd + 1;
            if (closeStart < 0 || closeEnd < 0)
            {
                if (!tolerant)
                    throw new SqxParseException(
                        closeStart < 0 ? "Unclosed <" + name + "> section" : "Unclosed </" + name + "> tag",
                        closeStart < 0 ? position : closeStart);
                sections.Add(name, new Section(
                    source.Substring(position, openingEnd - position + 1),
                    source.Substring(contentStart),
                    position,
                    contentStart));
                break;
            }
            sections.Add(name, new Section(
                source.Substring(position, openingEnd - position + 1),
                source.Substring(contentStart, closeStart - contentStart),
                position,
                contentStart));
            position = closeEnd + 1;
        }
        return sections;
    }

    private static ScriptMetadata ParseScriptMetadata(string openingTag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var tagNameEnd = openingTag.IndexOf("script", StringComparison.OrdinalIgnoreCase) + 6;
        var attributeText = openingTag.Substring(tagNameEnd, openingTag.Length - tagNameEnd - 1);
        var matches = Regex.Matches(attributeText, @"([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*(?:""([^""]*)""|'([^']*)')");
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            if (attributes.ContainsKey(name)) continue;
            attributes.Add(name, match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
        }

        var language = attributes.TryGetValue("lang", out var lang) ? lang : "csharp";
        var access = attributes.TryGetValue("access", out var acc) ? acc : "public";
        attributes.TryGetValue("namespace", out var ns);
        attributes.TryGetValue("name", out var componentName);
        return new ScriptMetadata(language, ns, componentName, access);
    }

    private static void SkipTrivia(string source, ref int position, bool tolerant)
    {
        while (position < source.Length)
        {
            if (char.IsWhiteSpace(source[position])) { position++; continue; }
            if (source.Substring(position).StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = source.IndexOf("-->", position + 4, StringComparison.Ordinal);
                if (end < 0)
                {
                    if (tolerant)
                    {
                        position = source.Length;
                        break;
                    }
                    throw new SqxParseException("Unclosed Vue comment", position, "SQV0001");
                }
                position = end + 3;
                continue;
            }
            break;
        }
    }

    private static int FindTagEnd(string source, int start)
    {
        var quote = '\0';
        for (var i = start; i < source.Length; i++)
        {
            var c = source[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            if (c == '\'' || c == '"') quote = c;
            else if (c == '>') return i;
        }
        return -1;
    }

    /// <summary>查找与起始 &lt;name&gt; 配对的闭合标签，跳过同名嵌套标签。</summary>
    private static int FindMatchingCloseTag(string source, string name, int start)
    {
        var openTag = "<" + name;
        var closeTag = "</" + name;
        var depth = 1;
        var i = start;
        while (i < source.Length)
        {
            var openAt = IndexOfTag(source, openTag, i);
            var closeAt = source.IndexOf(closeTag, i, StringComparison.OrdinalIgnoreCase);
            if (closeAt < 0) return -1;
            if (openAt >= 0 && openAt < closeAt)
            {
                depth++;
                i = openAt + openTag.Length;
            }
            else
            {
                depth--;
                if (depth == 0) return closeAt;
                i = closeAt + closeTag.Length;
            }
        }
        return -1;
    }

    private static int IndexOfTag(string source, string prefix, int start)
    {
        var i = start;
        while (i < source.Length)
        {
            var at = source.IndexOf(prefix, i, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            var after = at + prefix.Length;
            if (after >= source.Length || source[after] == '>' || source[after] == '/' || char.IsWhiteSpace(source[after]))
                return at;
            i = at + 1;
        }
        return -1;
    }

    private sealed class Section
    {
        public string OpeningTag { get; }
        public string Content { get; }
        public int Start { get; }
        public int ContentStart { get; }
        public Section(string openingTag, string content, int start, int contentStart)
        {
            OpeningTag = openingTag;
            Content = content;
            Start = start;
            ContentStart = contentStart;
        }
    }

    private sealed class ScriptMetadata
    {
        public string Language { get; }
        public string Namespace { get; }
        public string ComponentName { get; }
        public string Access { get; }
        public ScriptMetadata(string language, string ns, string componentName, string access)
        { Language = language; Namespace = ns; ComponentName = componentName; Access = access; }
    }
}
