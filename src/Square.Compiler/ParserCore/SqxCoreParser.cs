using System.Text.RegularExpressions;

namespace Square.Compiler.ParserCore
{
    internal sealed class CoreParseException : Exception
    {
        public int Position { get; private set; }
        public int Line { get; private set; }
        public int Column { get; private set; }

        public CoreParseException(string message, int position, int line, int column)
            : base(message)
        {
            Position = position;
            Line = line;
            Column = column;
        }
    }

    internal sealed class SqxCoreParserOptions
    {
        public bool StrictTemplate { get; set; }
        public bool CaseSensitiveSectionNames { get; set; }
        public bool Tolerant { get; set; }
    }

    internal static class SqxCoreParser
    {
        public static CoreDocument Parse(string source, string fileName, SqxCoreParserOptions options)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var sections = SplitSections(source, options.CaseSensitiveSectionNames, options.Tolerant);
            Section templateSection;
            if (!sections.TryGetValue("template", out templateSection))
                throw Error(source, 0, "Missing required <template> section");

            var document = new CoreDocument
            {
                FileName = string.IsNullOrEmpty(fileName) ? "Component" : Path.GetFileNameWithoutExtension(fileName),
                SourcePath = fileName ?? "",
                Template = ParseTemplate(source, templateSection, options.StrictTemplate)
            };

            Section scriptSection;
            if (sections.TryGetValue("script", out scriptSection))
            {
                var metadata = ParseScriptMetadata(source, scriptSection);
                document.Script = new CoreScript
                {
                    Language = metadata.Language,
                    Code = scriptSection.Content.Trim(),
                    Namespace = metadata.Namespace,
                    ComponentName = metadata.ComponentName,
                    Access = metadata.Access,
                    Line = scriptSection.ContentLine,
                    Column = 1
                };
            }

            Section styleSection;
            if (sections.TryGetValue("style", out styleSection))
            {
                document.Style = new CoreStyle
                {
                    Css = styleSection.Content.Trim(),
                    Line = styleSection.ContentLine,
                    Column = 1
                };
            }

            return document;
        }

        private static Dictionary<string, Section> SplitSections(string source, bool caseSensitive, bool tolerant)
        {
            var sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
            var position = 0;
            while (position < source.Length)
            {
                SkipTrivia(source, ref position);
                if (position >= source.Length) break;

                if (StartsWithTag(source, position, "sqx"))
                    throw Error(source, position, "The <sqx> document root is no longer supported");
                if (source[position] != '<')
                    throw Error(source, position, "Unexpected content outside a top-level section");

                var nameStart = position + 1;
                var nameEnd = nameStart;
                while (nameEnd < source.Length && char.IsLetter(source[nameEnd])) nameEnd++;
                if (nameEnd == nameStart)
                    throw Error(source, position, "Invalid top-level section");

                var sourceName = source.Substring(nameStart, nameEnd - nameStart);
                var name = caseSensitive ? sourceName : sourceName.ToLowerInvariant();
                if (name != "template" && name != "script" && name != "style")
                    throw Error(source, position, "Unknown top-level section <" + sourceName + ">");
                if (sections.ContainsKey(name))
                    throw Error(source, position, "Duplicate <" + name + "> section");

                var openingEnd = FindTagEnd(source, nameEnd);
                if (openingEnd < 0)
                {
                    if (!tolerant) throw Error(source, position, "Unclosed <" + name + "> opening tag");
                    sections.Add(name, new Section(
                        position,
                        source.Substring(position),
                        string.Empty,
                        source.Length,
                        GetLine(source, source.Length)));
                    break;
                }

                var closeStart = source.IndexOf("</" + name, openingEnd + 1, StringComparison.OrdinalIgnoreCase);
                var closeEnd = closeStart < 0 ? -1 : FindTagEnd(source, closeStart + name.Length + 2);
                var contentStart = openingEnd + 1;
                if (closeStart < 0 || closeEnd < 0)
                {
                    if (!tolerant) throw Error(source, closeStart < 0 ? position : closeStart,
                        closeStart < 0 ? "Unclosed <" + name + "> section" : "Unclosed </" + name + "> tag");
                    sections.Add(name, new Section(
                        position,
                        source.Substring(position, openingEnd - position + 1),
                        source.Substring(contentStart),
                        contentStart,
                        GetLine(source, contentStart)));
                    break;
                }
                sections.Add(name, new Section(
                    position,
                    source.Substring(position, openingEnd - position + 1),
                    source.Substring(contentStart, closeStart - contentStart),
                    contentStart,
                    GetLine(source, contentStart)));
                position = closeEnd + 1;
            }
            return sections;
        }

        private static CoreTemplate ParseTemplate(string source, Section section, bool strict)
        {
            try
            {
                var tokens = new SqxCoreLexer(section.Content, !strict).Tokenize();
                var roots = new SqxCoreTemplateParser(tokens, strict).ParseRoots();
                OffsetPositions(roots, section.ContentStart);
                return new CoreTemplate { Roots = roots, Line = section.ContentLine, Column = 1 };
            }
            catch (CoreParseException exception)
            {
                var position = Math.Min(source.Length, section.ContentStart + exception.Position);
                throw Error(source, position, exception.Message);
            }
        }

        private static void OffsetPositions(IEnumerable<CoreNode> nodes, int offset)
        {
            foreach (var node in nodes)
            {
                node.Position += offset;
                if (node is not CoreElement element) continue;
                foreach (var attribute in element.Attributes)
                {
                    attribute.Position += offset;
                    if (attribute.FragmentNodes != null) OffsetPositions(attribute.FragmentNodes, offset);
                }
                OffsetPositions(element.Children, offset);
            }
        }

        private static ScriptMetadata ParseScriptMetadata(string source, Section section)
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var tagNameEnd = section.OpeningTag.IndexOf("script", StringComparison.OrdinalIgnoreCase) + 6;
            var attributeText = section.OpeningTag.Substring(tagNameEnd, section.OpeningTag.Length - tagNameEnd - 1);
            var matches = Regex.Matches(attributeText, @"([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*(?:""([^""]*)""|'([^']*)')");
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                if (name != "lang" && name != "namespace" && name != "name" && name != "access")
                    throw Error(source, section.Start + tagNameEnd + match.Index, "Unknown script metadata '" + name + "'");
                if (attributes.ContainsKey(name))
                    throw Error(source, section.Start + tagNameEnd + match.Index, "Duplicate script metadata '" + name + "'");
                attributes.Add(name, match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
            }

            string language;
            if (!attributes.TryGetValue("lang", out language)) language = "csharp";
            if (!string.Equals(language, "csharp", StringComparison.OrdinalIgnoreCase))
                throw Error(source, section.Start, "Unsupported script language '" + language + "'");

            string access;
            if (!attributes.TryGetValue("access", out access)) access = "public";
            if (access != "public" && access != "internal")
                throw Error(source, section.Start, "Script access must be 'public' or 'internal'");

            string namespaceName;
            string componentName;
            attributes.TryGetValue("namespace", out namespaceName);
            attributes.TryGetValue("name", out componentName);
            return new ScriptMetadata(language, namespaceName, componentName, access);
        }

        private static void SkipTrivia(string source, ref int position)
        {
            while (position < source.Length)
            {
                if (char.IsWhiteSpace(source[position]))
                {
                    position++;
                    continue;
                }
                if (source.Substring(position).StartsWith("<!--", StringComparison.Ordinal))
                {
                    var end = source.IndexOf("-->", position + 4, StringComparison.Ordinal);
                    if (end < 0) throw Error(source, position, "Unclosed top-level comment");
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
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '\'' || c == '"') quote = c;
                else if (c == '>') return i;
            }
            return -1;
        }

        private static bool StartsWithTag(string source, int position, string name)
        {
            var text = "<" + name;
            if (position + text.Length > source.Length ||
                !source.Substring(position, text.Length).Equals(text, StringComparison.OrdinalIgnoreCase)) return false;
            var boundary = position + text.Length;
            return boundary >= source.Length || char.IsWhiteSpace(source[boundary]) || source[boundary] == '>' || source[boundary] == '/';
        }

        private static CoreParseException Error(string source, int position, string message)
        {
            position = Math.Max(0, Math.Min(position, source.Length));
            var line = GetLine(source, position);
            var lastNewLine = position > 0 ? source.LastIndexOf('\n', Math.Min(position - 1, source.Length - 1)) : -1;
            return new CoreParseException(message, position, line, position - lastNewLine);
        }

        private static int GetLine(string source, int position)
        {
            var line = 1;
            for (var i = 0; i < position && i < source.Length; i++)
                if (source[i] == '\n') line++;
            return line;
        }

        private sealed class Section
        {
            public int Start { get; private set; }
            public string OpeningTag { get; private set; }
            public string Content { get; private set; }
            public int ContentStart { get; private set; }
            public int ContentLine { get; private set; }

            public Section(int start, string openingTag, string content, int contentStart, int contentLine)
            {
                Start = start;
                OpeningTag = openingTag;
                Content = content;
                ContentStart = contentStart;
                ContentLine = contentLine;
            }
        }

        private sealed class ScriptMetadata
        {
            public string Language { get; private set; }
            public string Namespace { get; private set; }
            public string ComponentName { get; private set; }
            public string Access { get; private set; }

            public ScriptMetadata(string language, string namespaceName, string componentName, string access)
            {
                Language = language;
                Namespace = namespaceName;
                ComponentName = componentName;
                Access = access;
            }
        }
    }

    internal sealed class SqxCoreTemplateParser
    {
        private readonly List<CoreToken> _tokens;
        private readonly bool _strict;
        private int _index;

        public SqxCoreTemplateParser(List<CoreToken> tokens, bool strict)
        {
            _tokens = tokens;
            _strict = strict;
        }

        public List<CoreNode> ParseRoots()
        {
            var roots = new List<CoreNode>();
            while (Peek().Type != CoreTokenType.Eof)
            {
                var node = ParseNode();
                if (node != null) roots.Add(node);
            }
            return roots;
        }

        private CoreNode ParseNode()
        {
            var token = Peek();
            switch (token.Type)
            {
                case CoreTokenType.OpenTag:
                    return ParseElement();
                case CoreTokenType.Text:
                    _index++;
                    return new CoreText { Text = token.Text, Line = token.Line, Column = token.Column, Position = token.Offset };
                case CoreTokenType.OpenBraceExpr:
                    _index++;
                    return new CoreExpression { Expression = token.Text, Line = token.Line, Column = token.Column, Position = token.Offset };
                default:
                    if (_strict && token.Type == CoreTokenType.EndTag)
                        throw Error(token, "Unexpected closing tag </" + token.Text + ">");
                    _index++;
                    return null;
            }
        }

        private CoreElement ParseElement()
        {
            var open = Expect(CoreTokenType.OpenTag);
            var nameToken = Expect(CoreTokenType.Identifier);
            var attributes = new List<CoreAttribute>();
            while (Peek().Type != CoreTokenType.CloseTag &&
                   Peek().Type != CoreTokenType.CloseSelfTag &&
                   Peek().Type != CoreTokenType.Eof)
            {
                var attribute = ParseAttribute();
                if (attribute != null) attributes.Add(attribute);
            }

            if (Peek().Type == CoreTokenType.CloseSelfTag)
            {
                _index++;
                return NewElement(nameToken.Text, attributes, open, new List<CoreNode>());
            }

            Expect(CoreTokenType.CloseTag);
            var children = new List<CoreNode>();
            while (true)
            {
                var token = Peek();
                if (token.Type == CoreTokenType.Eof)
                {
                    if (_strict) throw Error(token, "Unclosed <" + nameToken.Text + "> element");
                    break;
                }
                if (token.Type == CoreTokenType.EndTag)
                {
                    _index++;
                    if (_strict && !string.Equals(token.Text, nameToken.Text, StringComparison.Ordinal))
                        throw Error(token, "Expected </" + nameToken.Text + "> but got </" + token.Text + ">");
                    break;
                }
                var child = ParseNode();
                if (child != null) children.Add(child);
            }
            return NewElement(nameToken.Text, attributes, open, children);
        }

        private static CoreElement NewElement(
            string tagName,
            List<CoreAttribute> attributes,
            CoreToken open,
            List<CoreNode> children)
        {
            return new CoreElement
            {
                TagName = tagName,
                Attributes = attributes,
                Children = children,
                Line = open.Line,
                Column = open.Column,
                Position = open.Offset
            };
        }

        private CoreAttribute ParseAttribute()
        {
            var nameToken = Peek();
            if (nameToken.Type != CoreTokenType.Identifier)
            {
                _index++;
                return null;
            }
            _index++;

            if (Peek().Type != CoreTokenType.Equals)
            {
                return new CoreAttribute
                {
                    Name = nameToken.Text,
                    Line = nameToken.Line,
                    Column = nameToken.Column,
                    Position = nameToken.Offset
                };
            }

            _index++;
            var valueToken = Peek();
            string rawValue = null;
            var isExpression = false;
            if (valueToken.Type == CoreTokenType.StringLiteral)
            {
                _index++;
                rawValue = valueToken.Text;
            }
            else if (valueToken.Type == CoreTokenType.OpenBraceExpr)
            {
                _index++;
                rawValue = valueToken.Text;
                isExpression = true;
            }
            else if (_strict)
            {
                throw Error(valueToken, "Expected attribute value");
            }

            List<CoreNode> fragmentNodes = null;
            if (isExpression && nameToken.Text.Equals("fallback", StringComparison.OrdinalIgnoreCase) &&
                rawValue != null && rawValue.TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                var fragment = rawValue.Trim();
                if (fragment.StartsWith("<>", StringComparison.Ordinal) && fragment.EndsWith("</>", StringComparison.Ordinal))
                    fragment = "<Fragment>" + fragment.Substring(2, fragment.Length - 5) + "</Fragment>";
                fragmentNodes = new SqxCoreTemplateParser(new SqxCoreLexer(fragment).Tokenize(), true).ParseRoots();
            }

            return new CoreAttribute
            {
                Name = nameToken.Text,
                RawValue = rawValue,
                IsExpression = isExpression,
                FragmentNodes = fragmentNodes,
                Line = nameToken.Line,
                Column = nameToken.Column,
                Position = nameToken.Offset
            };
        }

        private CoreToken Peek()
        {
            return _index < _tokens.Count ? _tokens[_index] : _tokens[_tokens.Count - 1];
        }

        private CoreToken Expect(CoreTokenType type)
        {
            var token = Peek();
            if (_strict && token.Type != type)
                throw Error(token, "Expected " + type + " but got " + token.Type);
            _index++;
            return token;
        }

        private static CoreParseException Error(CoreToken token, string message)
        {
            return new CoreParseException(message, token.Offset, token.Line, token.Column);
        }
    }
}
