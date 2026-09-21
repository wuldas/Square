using Microsoft.CodeAnalysis;
using Square.Compiler.LanguageServices;

namespace Square.Compiler.Diagnostics;

public static class ElementDiagnostics
{
    public const string Category = "Square.Elements";

    public static readonly DiagnosticDescriptor SQXE001_InvalidDeclaration = new(
        "SQXE001", "Invalid element declaration", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXE002_UnknownPrefix = new(
        "SQXE002", "Unknown element prefix", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXE003_UnknownElement = new(
        "SQXE003", "Unknown element", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXE004_AmbiguousElement = new(
        "SQXE004", "Ambiguous element", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXE005_DuplicateIdentity = new(
        "SQXE005", "Duplicate element identity", "{0}", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor SQXE006_InvalidEventContract = new(
        "SQXE006", "Invalid element event contract", "{0}", Category, DiagnosticSeverity.Error, true);

    public static DiagnosticDescriptor Get(string id) => id switch
    {
        "SQXE002" => SQXE002_UnknownPrefix,
        "SQXE003" => SQXE003_UnknownElement,
        "SQXE004" => SQXE004_AmbiguousElement,
        "SQXE005" => SQXE005_DuplicateIdentity,
        "SQXE006" => SQXE006_InvalidEventContract,
        _ => SQXE001_InvalidDeclaration
    };

    /// <summary>元素解析失败时统一的诊断 ID。</summary>
    public static string GetId(TemplateElementResolutionStatus status) => status switch
    {
        TemplateElementResolutionStatus.UnknownPrefix => "SQXE002",
        TemplateElementResolutionStatus.UnknownElement => "SQXE003",
        TemplateElementResolutionStatus.Ambiguous => "SQXE004",
        _ => "SQXE001"
    };

    /// <summary>元素解析失败时统一的诊断（ID、消息与范围由本方法集中决定）。</summary>
    public static SquareDiagnostic ForResolution(
        string tagName,
        TemplateElementResolution resolution,
        TemplateCatalog catalog,
        SquareSourceRange range,
        string sourcePath) =>
        new(GetId(resolution.Status),
            SquareDiagnosticSeverity.Error,
            Describe(tagName, resolution, catalog),
            range,
            sourcePath);

    private static string Describe(
        string tagName,
        TemplateElementResolution resolution,
        TemplateCatalog catalog)
    {
        var candidates = resolution.Candidates.Count == 0
            ? string.Empty
            : " Candidates: " + string.Join(", ", resolution.Candidates.Select(candidate =>
                string.Join("/", catalog.GetQualifiedNames(candidate)) + " (" + candidate.TypeName + ")"));
        var namespaces = resolution.CandidateNamespaceUris.Count == 0
            ? string.Empty
            : " Namespaces: " + string.Join(", ", resolution.CandidateNamespaceUris);
        var guidance = resolution.Status == TemplateElementResolutionStatus.Ambiguous
            ? " Configure ElementNamespaceOrder/ElementNamespaceAlias or use an explicit prefix."
            : string.Empty;
        return "Element '" + tagName + "' could not be resolved." + candidates + namespaces + guidance;
    }
}
