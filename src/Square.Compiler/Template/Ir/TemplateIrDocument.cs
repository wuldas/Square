namespace Square.Compiler.Template.Ir;

internal sealed class TemplateIrDocument
{
    public TemplateIrDocument(IReadOnlyList<TemplateIrNode> roots)
    {
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
    }

    public IReadOnlyList<TemplateIrNode> Roots { get; }
}
