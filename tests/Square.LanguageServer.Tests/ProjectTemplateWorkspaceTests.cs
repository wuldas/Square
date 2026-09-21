using Xunit;

namespace Square.LanguageServer.Tests;

public sealed class ProjectTemplateWorkspaceTests
{
    [Fact]
    public async Task LoadsOwningProjectAndReferencedExports()
    {
        Assert.True(MsBuildLocatorInitializer.TryRegister(out var registrationError), registrationError);
        var projectDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Square.Sample.Vue"));
        var documentPath = Path.Combine(projectDirectory, "Components", "MarkdownSamplesPage.sqv");
        using var workspace = new ProjectTemplateWorkspace();
        workspace.SetRoots(new[] { projectDirectory });

        var context = await workspace.GetContextAsync(
            documentPath,
            new[]
            {
                new ProjectBufferSnapshot(documentPath, 1, "<template><markdown:MarkdownViewer /></template>")
            },
            CancellationToken.None);

        Assert.NotNull(context);
        Assert.True(context!.IsComplete, context.Warning);
        Assert.NotNull(context.Catalog);
        Assert.True(
            context.Catalog!.Components.Any(component => component.TypeName == "Square.Extensions.Markdown.MarkdownViewer"),
            string.Join(Environment.NewLine, context.Catalog.Components
                .Where(component => component.Kind == Square.Compiler.LanguageServices.TemplateElementKind.Extension)
                .Select(component => component.AssemblyName + ":" + component.TypeName)));
        var markdown = Assert.Single(context.Catalog.Components,
            component => component.TypeName == "Square.Extensions.Markdown.MarkdownViewer");
        Assert.Contains(context.Catalog.GetProps(markdown), prop => prop.Name == "Content");
    }
}
