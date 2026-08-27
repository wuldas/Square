using Square.Compiler.Syntax;
using Square.Compiler.Template.Compatibility;

namespace Square.Compiler.Parser;

/// <summary>
/// Vue 文档分区解析器：拆分 &lt;template&gt; / &lt;script&gt; / &lt;style&gt;，提取脚本元数据，
/// 并把模板内容交给 <see cref="SqvTemplateParser"/>。不依赖 <c>SqxCoreParser</c>。
/// </summary>
internal static class SqvDocumentParser
{
    public static SqxDocument Parse(string source, string fileName, bool tolerant = false)
    {
        var sections = ReadSections(source, tolerant, out var syntax);
        if (!sections.TryGetValue("template", out var templateSection))
        {
            if (tolerant)
            {
                return new SqxDocument
                {
                    Syntax = syntax,
                    Name = string.IsNullOrEmpty(fileName) ? "Component" : Path.GetFileNameWithoutExtension(fileName),
                    SourcePath = fileName,
                    Template = new SqxTemplate()
                };
            }
            throw new SqxParseException("Missing required <template> section", 0);
        }

        var validationRoots = SqvTemplateParser.Parse(templateSection.Content, templateSection.ContentStart, tolerant);
        if (!tolerant)
            SqvValidator.Validate(validationRoots);
        var roots = TemplateIrCompatibilityAdapter.ToSqxNodes(
            syntax.Template.Ir,
            syntax.SourceText,
            syntax.Dialect,
            syntax.Template.ContentRange.Offset);

        var document = new SqxDocument
        {
            Syntax = syntax,
            Name = string.IsNullOrEmpty(fileName) ? "Component" : Path.GetFileNameWithoutExtension(fileName),
            SourcePath = fileName,
            Template = new SqxTemplate { Roots = roots }
        };

        if (sections.TryGetValue("script", out var scriptSection))
        {
            var meta = syntax.Script.Metadata;
            var metadataDiagnostic = meta.Diagnostics.FirstOrDefault();
            if (!tolerant && metadataDiagnostic != null)
                throw new SqxParseException(
                    metadataDiagnostic.Message,
                    metadataDiagnostic.Range.Offset,
                    "SQV0001",
                    metadataDiagnostic.Range.Length);
            var csharpDiagnostic = syntax.Script.CSharp.Diagnostics.FirstOrDefault();
            if (!tolerant && csharpDiagnostic != null)
                throw new SqxParseException(
                    csharpDiagnostic.Message,
                    csharpDiagnostic.Range.Offset,
                    "SQV0001",
                    csharpDiagnostic.Range.Length);
            document.Namespace = meta.Namespace;
            document.Access = meta.Access;
            if (!string.IsNullOrEmpty(meta.ComponentName)) document.Name = meta.ComponentName;
        }

        return document;
    }

    private static Dictionary<string, Section> ReadSections(
        string source,
        bool tolerant,
        out ComponentDocumentSyntax syntax)
    {
        var scan = ComponentSectionScanner.Scan(
            source,
            string.Empty,
            ComponentDialect.Sqv,
            tolerant);
        syntax = scan.Document;
        var diagnostic = scan.Diagnostics.FirstOrDefault(item =>
            !tolerant || !CanRecover(item.Kind));
        if (diagnostic != null)
            throw new SqxParseException(diagnostic.Message, diagnostic.Range.Offset, "SQV0001");

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
            syntax.ContentRange.Offset));
    }

    private sealed class Section
    {
        public string Content { get; }
        public int ContentStart { get; }
        public Section(string content, int contentStart)
        {
            Content = content;
            ContentStart = contentStart;
        }
    }
}
