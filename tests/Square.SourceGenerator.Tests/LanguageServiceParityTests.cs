using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Square.Compiler;
using Square.Compiler.LanguageServices;
using Square.Runtime.Binding;
using Xunit;
using MarkupSqxParseException = Square.Markup.SqxParseException;
using MarkupSqxParser = Square.Markup.Parser.SqxParser;

namespace Square.Compiler.Tests;

public sealed class LanguageServiceParityTests
{
    [Fact]
    public void SourceRangeMapsAbsoluteOffsetAndLengthThroughSourceText()
    {
        var source = SourceText.From("first\r\nsecond\r\nthird");
        var range = new SquareSourceRange(7, 3);
        var diagnostic = new SquareDiagnostic(
            "SQX0001",
            SquareDiagnosticSeverity.Error,
            "Invalid markup",
            range,
            "Broken.sqx");

        var lineSpan = diagnostic.GetLinePositionSpan(source);

        Assert.Equal(1, lineSpan.Start.Line);
        Assert.Equal(0, lineSpan.Start.Character);
        Assert.Equal(1, lineSpan.End.Line);
        Assert.Equal(3, lineSpan.End.Character);
        Assert.Equal(7, range.Offset);
        Assert.Equal(3, range.Length);
    }

    [Fact]
    public void ParseResultPreservesMultipleDiagnosticsInSourceOrder()
    {
        var source = SourceText.From("<template>\n  <View>\n");
        var diagnostics = new[]
        {
            new SquareDiagnostic(
                "SQX0001",
                SquareDiagnosticSeverity.Error,
                "Unclosed template",
                new SquareSourceRange(0, 0),
                "Broken.sqx"),
            new SquareDiagnostic(
                "SQX0002",
                SquareDiagnosticSeverity.Warning,
                "Unknown control",
                new SquareSourceRange(13, 4),
                "Broken.sqx")
        };

        var result = new SquareParseResult("Broken.sqx", source, diagnostics);

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.Diagnostics.Length);
        Assert.Equal("SQX0001", result.Diagnostics[0].Id);
        Assert.Equal("SQX0002", result.Diagnostics[1].Id);
        Assert.Same(source, result.SourceText);
        Assert.Equal("Broken.sqx", result.SourcePath);
    }

    [Fact]
    public void SquareDocumentServiceReturnsStructuredSqxSyntaxDiagnostic()
    {
        var result = SquareDocumentService.Parse("<template><View>", "Broken.sqx");

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("SQX0001", diagnostic.Id);
        Assert.Equal(SquareDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Broken.sqx", diagnostic.SourcePath);
        Assert.InRange(diagnostic.Range.Offset, 0, result.SourceText.Length);
    }

    [Fact]
    public void SquareDocumentServiceReturnsStructuredSqvDiagnosticId()
    {
        const string source = "<template><View /></template><script lang=\"typescript\"></script>";

        var result = SquareDocumentService.Parse(source, "Broken.sqv");

        var diagnostic = Assert.Single(result.Diagnostics);

        Assert.Equal("SQV0001", diagnostic.Id);
        Assert.Equal(SquareDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Broken.sqv", diagnostic.SourcePath);
    }

    [Fact]
    public void MarkupFacadeAndGeneratorShareSqxDiagnosticIdAndSourceRange()
    {
        const string source = "<template><View /></template>\r\n<script lang=\"csharp\"></script>\r\n<script lang=\"csharp\"></script>";
        const string path = "Duplicate.sqx";

        var serviceResult = SquareDocumentService.Parse(source, path);
        var serviceDiagnostic = Assert.Single(serviceResult.Diagnostics);
        var generatorDiagnostic = Assert.Single(
            RunGenerator(new InMemoryAdditionalText(path, source))
                .Where(diagnostic => diagnostic.Id == serviceDiagnostic.Id));
        var markupException = Assert.Throws<MarkupSqxParseException>(() =>
            new MarkupSqxParser().Parse(source, path));

        Assert.Equal(serviceDiagnostic.Id, generatorDiagnostic.Id);
        Assert.Equal(serviceDiagnostic.Message, generatorDiagnostic.GetMessage());
        Assert.Equal(serviceDiagnostic.Id, markupException.DiagnosticId);
        Assert.Equal(serviceDiagnostic.Message, markupException.DiagnosticMessage);
        Assert.Equal(serviceDiagnostic.Range.Offset, generatorDiagnostic.Location.SourceSpan.Start);
        Assert.Equal(serviceDiagnostic.Range.Offset, markupException.Offset);
        Assert.Equal(
            serviceDiagnostic.GetLinePositionSpan(serviceResult.SourceText).Start,
            generatorDiagnostic.Location.GetLineSpan().StartLinePosition);
        Assert.Equal(
            serviceDiagnostic.GetLinePositionSpan(serviceResult.SourceText).Start.Line + 1,
            markupException.Line);
        Assert.Equal(
            serviceDiagnostic.GetLinePositionSpan(serviceResult.SourceText).Start.Character + 1,
            markupException.Column);
    }

    [Fact]
    public void SharedServiceGeneratorAndMarkupShareDirectiveDiagnostic()
    {
        const string source = "<template><Show><Text /></Show></template>";
        const string path = "InvalidShow.sqx";

        var serviceResult = SquareDocumentService.Parse(source, path);
        var serviceDiagnostic = Assert.Single(serviceResult.Diagnostics);
        var allGeneratorDiagnostics = RunGenerator(new InMemoryAdditionalText(path, source));
        Assert.True(
            allGeneratorDiagnostics.Length > 0,
            string.Join("; ", allGeneratorDiagnostics.Select(diagnostic => diagnostic.Id + ": " + diagnostic.GetMessage())));
        var generatorDiagnostic = Assert.Single(
            allGeneratorDiagnostics.Where(diagnostic => diagnostic.Id == serviceDiagnostic.Id));
        var markupException = Assert.Throws<MarkupSqxParseException>(() =>
            new MarkupSqxParser().Parse(source, path));

        Assert.Equal("SQXD002", serviceDiagnostic.Id);
        Assert.Equal(serviceDiagnostic.Id, generatorDiagnostic.Id);
        Assert.Equal(serviceDiagnostic.Message, generatorDiagnostic.GetMessage());
        Assert.Equal(serviceDiagnostic.Id, markupException.DiagnosticId);
        Assert.Equal(serviceDiagnostic.Message, markupException.DiagnosticMessage);
        Assert.Equal(serviceDiagnostic.Range.Offset, generatorDiagnostic.Location.SourceSpan.Start);
        Assert.Equal(serviceDiagnostic.Range.Offset, markupException.Offset);
    }

    private static ImmutableArray<Diagnostic> RunGenerator(params AdditionalText[] files)
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = (trustedPlatformAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path))
            .GroupBy(reference => reference.Display, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (references.All(reference => reference.Display != typeof(PropAttribute).Assembly.Location))
            references.Add(MetadataReference.CreateFromFile(typeof(PropAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "Parity",
            [CSharpSyntaxTree.ParseText("public class Consumer { }")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SqxGenerator().AsSourceGenerator()],
            files,
            (CSharpParseOptions?)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult().Diagnostics;
    }

    private sealed class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(content, Encoding.UTF8);
    }
}
