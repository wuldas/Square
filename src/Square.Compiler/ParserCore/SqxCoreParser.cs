using System.Text.RegularExpressions;
using Square.Compiler.Syntax;

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

            var sections = ReadSections(source, options.CaseSensitiveSectionNames, options.Tolerant);
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

        private static Dictionary<string, Section> ReadSections(string source, bool caseSensitive, bool tolerant)
        {
            var scan = ComponentSectionScanner.Scan(
                source,
                string.Empty,
                caseSensitive ? ComponentDialect.Sqx : ComponentDialect.Sqv,
                tolerant);
            var diagnostic = scan.Diagnostics.FirstOrDefault(item =>
                !tolerant || !CanRecover(item.Kind));
            if (diagnostic != null)
                throw Error(source, diagnostic.Range.Offset, diagnostic.Message);

            var sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
            AddSection(source, sections, "template", scan.Document.Template);
            AddSection(source, sections, "script", scan.Document.Script);
            AddSection(source, sections, "style", scan.Document.Style);
            return sections;
        }

        private static bool CanRecover(ComponentSectionDiagnosticKind kind) =>
            kind == ComponentSectionDiagnosticKind.UnclosedOpeningTag ||
            kind == ComponentSectionDiagnosticKind.UnclosedSection ||
            kind == ComponentSectionDiagnosticKind.UnclosedClosingTag ||
            kind == ComponentSectionDiagnosticKind.UnclosedComment;

        private static void AddSection(
            string source,
            Dictionary<string, Section> sections,
            string name,
            ComponentSectionSyntax syntax)
        {
            if (syntax == null) return;
            sections.Add(name, new Section(
                syntax.FullRange.Offset,
                source.Substring(syntax.OpeningTagRange.Offset, syntax.OpeningTagRange.Length),
                syntax.ContentText,
                syntax.ContentRange.Offset,
                GetLine(source, syntax.ContentRange.Offset)));
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
