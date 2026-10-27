using Square.Compiler.LanguageServices;
using Square.Compiler.Syntax;
using Square.Compiler.Template.Compatibility;

namespace Square.Compiler.Parser;

/// <summary>
/// Vue 文档分区解析器：拆分 &lt;template&gt; / &lt;script&gt; / &lt;style&gt;，提取脚本元数据，
/// 模板体交给共享 section syntax 管线，保持独立的 SQV 标签词法与指令转换。
/// </summary>
internal static class SqvDocumentParser
{
    public static SqxDocument Parse(string source, string fileName, bool tolerant = false, bool parseTemplateBody = true,
        TemplateCatalog catalog = null, TemplateResolutionContext context = null)
    {
        var sections = ReadSections(source, tolerant, parseTemplateBody, catalog, context, out var syntax);
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

        if (!tolerant && syntax.Template.Diagnostics.FirstOrDefault() is { } templateError)
            throw new SqxParseException(templateError.Message, templateError.Range.Offset,
                templateError.Id, templateError.Range.Length);

        var roots = TemplateIrCompatibilityAdapter.ToSqxNodes(
            syntax.Template.Ir,
            syntax.SourceText,
            syntax.Dialect,
            syntax.Template.ContentRange.Offset);
        if (parseTemplateBody && !tolerant) SqvValidator.Validate(roots);

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
        bool parseTemplateBody,
        TemplateCatalog catalog,
        TemplateResolutionContext context,
        out ComponentDocumentSyntax syntax)
    {
        var scan = ComponentSectionScanner.Scan(
            source,
            string.Empty,
            ComponentDialect.Sqv,
            tolerant,
            parseTemplateBody,
            catalog,
            context);
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
