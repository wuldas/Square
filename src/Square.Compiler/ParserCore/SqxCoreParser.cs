using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;

namespace Square.Compiler.ParserCore
{
    internal sealed class CoreParseException : Exception
    {
        public int Position { get; private set; }
        public int Length { get; private set; }
        public int Line { get; private set; }
        public int Column { get; private set; }
        public string DiagnosticId { get; private set; }

        public CoreParseException(
            string message,
            int position,
            int line,
            int column,
            int length = 0,
            string diagnosticId = null)
            : base(message)
        {
            Position = position;
            Length = length;
            Line = line;
            Column = column;
            DiagnosticId = diagnosticId;
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

            var sections = ReadSections(
                source,
                options.CaseSensitiveSectionNames,
                options.Tolerant,
                out var syntax);
            Section templateSection;
            if (!sections.TryGetValue("template", out templateSection))
                throw Error(source, 0, "Missing required <template> section");

            if (options.StrictTemplate) ThrowTemplateErrors(source, syntax.Template);
            var document = new CoreDocument
            {
                Syntax = syntax,
                FileName = string.IsNullOrEmpty(fileName) ? "Component" : Path.GetFileNameWithoutExtension(fileName),
                SourcePath = fileName ?? ""
            };

            Section scriptSection;
            if (sections.TryGetValue("script", out scriptSection))
            {
                var metadata = syntax.Script.Metadata;
                var metadataDiagnostic = metadata.Diagnostics.FirstOrDefault();
                if (!options.Tolerant && metadataDiagnostic != null)
                    throw Error(
                        source,
                        metadataDiagnostic.Range.Offset,
                        metadataDiagnostic.Message,
                        metadataDiagnostic.Range.Length);
                var csharpDiagnostic = syntax.Script.CSharp.Diagnostics.FirstOrDefault();
                if (!options.Tolerant && csharpDiagnostic != null)
                    throw Error(
                        source,
                        csharpDiagnostic.Range.Offset,
                        csharpDiagnostic.Message,
                        csharpDiagnostic.Range.Length);
                document.Script = new CoreScript
                {
                    Language = metadata.Language,
                    Code = syntax.Script.ContentText.Trim(),
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

        private static Dictionary<string, Section> ReadSections(
            string source,
            bool caseSensitive,
            bool tolerant,
            out ComponentDocumentSyntax syntax)
        {
            var scan = ComponentSectionScanner.Scan(
                source,
                string.Empty,
                caseSensitive ? ComponentDialect.Sqx : ComponentDialect.Sqv,
                tolerant);
            syntax = scan.Document;
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
                syntax.ContentText,
                syntax.ContentRange.Offset,
                GetLine(source, syntax.ContentRange.Offset)));
        }

        /// <summary>严格模式下把模板段的失败事实（由语法解析器记录）重新抛出。</summary>
        private static void ThrowTemplateErrors(string source, TemplateSectionSyntax template)
        {
            var diagnostic = template.Diagnostics.FirstOrDefault();
            if (diagnostic == null) return;
            throw Error(source, diagnostic.Range.Offset, diagnostic.Message, diagnostic.Range.Length, diagnostic.Id);
        }


        private static CoreParseException Error(
            string source,
            int position,
            string message,
            int length = 0,
            string diagnosticId = null)
        {
            position = Math.Max(0, Math.Min(position, source.Length));
            var line = GetLine(source, position);
            var lastNewLine = position > 0 ? source.LastIndexOf('\n', Math.Min(position - 1, source.Length - 1)) : -1;
            return new CoreParseException(message, position, line, position - lastNewLine, length, diagnosticId);
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
            public string Content { get; private set; }
            public int ContentLine { get; private set; }

            public Section(string content, int contentStart, int contentLine)
            {
                Content = content;
                ContentLine = contentLine;
            }
        }
    }
}
