using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler.Diagnostics;
using Square.Compiler.LanguageServices;
using Square.Compiler.Parser;

namespace Square.Compiler.Directives;

/// <summary>
/// Shared structural-directive analysis used by the generator and language tooling.
/// </summary>
internal static class DirectiveValidator
{
    public static bool Validate(
        SourceProductionContext context,
        string filePath,
        string content,
        SqxDocument document,
        DirectiveCatalog catalog)
    {
        var result = Analyze(filePath, content, document, catalog);
        foreach (var report in result.Reports)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                report.Descriptor,
                CreateLocation(filePath, content, report.Range),
                report.MessageArgs));
        }

        return result.CanEmit;
    }

    internal static ImmutableArray<SquareDiagnostic> CollectDiagnostics(
        string filePath,
        string content,
        SqxDocument document,
        DirectiveCatalog catalog)
    {
        var result = Analyze(filePath, content, document, catalog);
        return result.Reports
            .Select(report => ToSquareDiagnostic(filePath, content, report))
            .ToImmutableArray();
    }

    private static ValidationResult Analyze(
        string filePath,
        string content,
        SqxDocument document,
        DirectiveCatalog catalog)
    {
        var reports = new List<DirectiveValidationReport>();
        var canEmit = true;
        ValidateNodes(
            content,
            document.Template.Roots,
            parentDirectiveId: null,
            catalog,
            reports,
            ref canEmit);
        return new ValidationResult(
            canEmit && reports.All(report => !report.PreventsEmission),
            reports.ToImmutableArray());
    }

    private static void ValidateNodes(
        string content,
        IEnumerable<SqxNode> nodes,
        string parentDirectiveId,
        DirectiveCatalog catalog,
        List<DirectiveValidationReport> reports,
        ref bool canEmit)
    {
        foreach (var node in nodes)
        {
            if (node is not SqxElement element) continue;

            if (catalog.TryGet(element.TagName, out var descriptor))
            {
                var id = descriptor.TagName;

                if (descriptor.Pattern == "ControlFlowAttach" &&
                    !descriptor.SkipStandaloneEmit &&
                    !IsBuiltInControlFlow(id) &&
                    (string.IsNullOrWhiteSpace(descriptor.RuntimeTypeName) ||
                     string.IsNullOrWhiteSpace(descriptor.FieldPrefix) ||
                     string.IsNullOrWhiteSpace(descriptor.PrimaryAttribute)))
                {
                    Report(reports, content, element,
                        SqxDiagnostics.SQXD007_UnsupportedControlFlowShape, true, id);
                    canEmit = false;
                }

                if (!IsKnownPattern(descriptor.Pattern) &&
                    !IsKnownTagFallback(id))
                {
                    Report(reports, content, element,
                        SqxDiagnostics.SQXD004_UnknownPattern, false, id, descriptor.Pattern ?? "");
                }

                if (!string.IsNullOrEmpty(descriptor.PrimaryAttribute) &&
                    RequiresPrimaryAttribute(id, descriptor))
                {
                    var attr = FindAttr(element, descriptor.PrimaryAttribute);
                    if (attr == null || string.IsNullOrWhiteSpace(attr.RawValue))
                    {
                        Report(reports, content, element,
                            SqxDiagnostics.SQXD002_MissingRequiredAttribute,
                            false,
                            id,
                            descriptor.PrimaryAttribute);
                    }
                }

                if (!IsValidParent(id, descriptor, parentDirectiveId))
                {
                    if (parentDirectiveId == null && descriptor.SkipStandaloneEmit)
                    {
                        Report(reports, content, element,
                            SqxDiagnostics.SQXD005_IllegalStandalone, false, id);
                    }
                    else
                    {
                        var expected = DescribeExpectedParent(id, descriptor);
                        Report(reports, content, element,
                            SqxDiagnostics.SQXD003_InvalidParent, false, id, expected);
                    }
                }

                if (!descriptor.AllowedChildTags.IsDefaultOrEmpty)
                {
                    foreach (var child in element.Children.OfType<SqxElement>())
                    {
                        var childId = child.TagName;
                        if (catalog.TryGet(child.TagName, out var childDescriptor))
                            childId = childDescriptor.TagName;
                        if (descriptor.AllowedChildTags.Any(allowed =>
                            string.Equals(allowed, childId, StringComparison.Ordinal))) continue;
                        Report(reports, content, child,
                            SqxDiagnostics.SQXD006_InvalidChild,
                            false,
                            id,
                            string.Join(", ", descriptor.AllowedChildTags.Select(tag => "<" + tag + ">")),
                            child.TagName);
                    }
                }

                ValidateNodes(content, element.Children, id, catalog, reports, ref canEmit);
            }
            else
            {
                ValidateNodes(content, element.Children, parentDirectiveId: null, catalog, reports, ref canEmit);
            }
        }
    }

    private static bool RequiresPrimaryAttribute(string directiveId, DirectiveDescriptor descriptor) =>
        directiveId is "Show" or "For" or "Index" ||
        descriptor.Pattern == "ControlFlowAttach" && !IsBuiltInControlFlow(directiveId);

    private static bool IsBuiltInControlFlow(string directiveId) =>
        directiveId is "Show" or "For" or "Index" or "Switch";

    private static bool IsKnownPattern(string pattern) =>
        pattern is "ControlFlowAttach" or "SlotOutlet";

    private static bool IsKnownTagFallback(string id) =>
        id is "Show" or "For" or "Index" or "Switch" or "Match" or "Slot";

    private static bool IsValidParent(string id, DirectiveDescriptor descriptor, string parentDirectiveId)
    {
        if (string.IsNullOrEmpty(descriptor.ParentTag) && !descriptor.SkipStandaloneEmit)
            return true;

        if (id == "Match")
            return parentDirectiveId == "Switch";

        if (!string.IsNullOrEmpty(descriptor.ParentTag))
            return string.Equals(parentDirectiveId, descriptor.ParentTag, StringComparison.Ordinal);

        return parentDirectiveId != null || !descriptor.SkipStandaloneEmit;
    }

    private static string DescribeExpectedParent(string id, DirectiveDescriptor descriptor)
    {
        if (id == "Match") return "Switch";
        return descriptor.ParentTag ?? "(非根)";
    }

    private static SqxAttribute FindAttr(SqxElement element, string name) =>
        element.Attributes.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void Report(
        List<DirectiveValidationReport> reports,
        string content,
        SqxElement element,
        DiagnosticDescriptor descriptor,
        bool preventsEmission,
        params object[] messageArgs)
    {
        var position = Math.Max(0, Math.Min(element.Position, content.Length));
        reports.Add(new DirectiveValidationReport(
            descriptor,
            messageArgs,
            new SquareSourceRange(position, 0),
            preventsEmission));
    }

    private static Location CreateLocation(string filePath, string content, SquareSourceRange range)
    {
        var source = SourceText.From(content, System.Text.Encoding.UTF8);
        var span = range.ToTextSpan(source);
        return Location.Create(filePath, span, source.Lines.GetLinePositionSpan(span));
    }

    private static SquareDiagnostic ToSquareDiagnostic(
        string filePath,
        string content,
        DirectiveValidationReport report)
    {
        var diagnostic = Diagnostic.Create(
            report.Descriptor,
            CreateLocation(filePath, content, report.Range),
            report.MessageArgs);
        return new SquareDiagnostic(
            report.Descriptor.Id,
            ToSeverity(report.Descriptor.DefaultSeverity),
            diagnostic.GetMessage(),
            report.Range,
            filePath);
    }

    private static SquareDiagnosticSeverity ToSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Warning => SquareDiagnosticSeverity.Warning,
        DiagnosticSeverity.Info => SquareDiagnosticSeverity.Information,
        DiagnosticSeverity.Hidden => SquareDiagnosticSeverity.Hint,
        _ => SquareDiagnosticSeverity.Error
    };

    private sealed class ValidationResult
    {
        public ValidationResult(bool canEmit, ImmutableArray<DirectiveValidationReport> reports)
        {
            CanEmit = canEmit;
            Reports = reports;
        }

        public bool CanEmit { get; }

        public ImmutableArray<DirectiveValidationReport> Reports { get; }
    }

    private sealed class DirectiveValidationReport
    {
        public DirectiveValidationReport(
            DiagnosticDescriptor descriptor,
            object[] messageArgs,
            SquareSourceRange range,
            bool preventsEmission)
        {
            Descriptor = descriptor;
            MessageArgs = messageArgs;
            Range = range;
            PreventsEmission = preventsEmission;
        }

        public DiagnosticDescriptor Descriptor { get; }

        public object[] MessageArgs { get; }

        public SquareSourceRange Range { get; }

        public bool PreventsEmission { get; }
    }
}
