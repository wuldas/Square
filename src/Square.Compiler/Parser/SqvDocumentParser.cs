using System.Text.RegularExpressions;
using Square.Compiler.Syntax;

namespace Square.Compiler.Parser;

/// <summary>
/// Vue 文档分区解析器：拆分 &lt;template&gt; / &lt;script&gt; / &lt;style&gt;，提取脚本元数据，
/// 并把模板内容交给 <see cref="SqvTemplateParser"/>。不依赖 <c>SqxCoreParser</c>。
/// </summary>
internal static class SqvDocumentParser
{
    public static SqxDocument Parse(string source, string fileName, bool tolerant = false)
    {
        var sections = ReadSections(source, tolerant);
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

    private static Dictionary<string, Section> ReadSections(string source, bool tolerant)
    {
        var scan = ComponentSectionScanner.Scan(
            source,
            string.Empty,
            ComponentDialect.Sqv,
            tolerant);
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
            source.Substring(syntax.OpeningTagRange.Offset, syntax.OpeningTagRange.Length),
            syntax.ContentText,
            syntax.FullRange.Offset,
            syntax.ContentRange.Offset));
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
