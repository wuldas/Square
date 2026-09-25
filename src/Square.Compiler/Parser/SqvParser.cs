using Square.Compiler.LanguageServices;
namespace Square.Compiler.Parser;

/// <summary>
/// Vue (.sqv) 解析入口。完全独立于 <c>SqxParser</c> / <c>SqxCoreParser</c>，
/// 通过 <see cref="SqvDocumentParser"/> 完成分区与模板解析。
/// </summary>
internal static class SqvParser
{
    public static SqxDocument Parse(string source, string fileName, bool parseTemplateBody = true,
        TemplateCatalog catalog = null, TemplateResolutionContext context = null) =>
        SqvDocumentParser.Parse(source, fileName, parseTemplateBody: parseTemplateBody, catalog: catalog, context: context);

    public static SqxDocument ParseTolerant(string source, string fileName) =>
        SqvDocumentParser.Parse(source, fileName, tolerant: true);
}