namespace Square.Compiler.LanguageServices;

public enum TemplateElementResolutionStatus
{
    Resolved,
    UnknownElement,
    UnknownPrefix,
    Ambiguous,
    InvalidName
}

public sealed class TemplateElementResolution
{
    private TemplateElementResolution(
        TemplateElementResolutionStatus status,
        TemplateComponentDescriptor component,
        IReadOnlyList<TemplateComponentDescriptor> candidates,
        IReadOnlyList<string> candidateNamespaceUris)
    {
        Status = status;
        Component = component;
        Candidates = Array.AsReadOnly((candidates ?? Array.Empty<TemplateComponentDescriptor>()).ToArray());
        CandidateNamespaceUris = Array.AsReadOnly((candidateNamespaceUris ?? Array.Empty<string>()).ToArray());
    }

    public TemplateElementResolutionStatus Status { get; }
    public TemplateComponentDescriptor Component { get; }
    public IReadOnlyList<TemplateComponentDescriptor> Candidates { get; }
    /// <summary>失败时可供选择的命名空间 URI（前缀歧义或未限定名歧义）。</summary>
    public IReadOnlyList<string> CandidateNamespaceUris { get; }

    public static TemplateElementResolution Resolved(TemplateComponentDescriptor component) =>
        new(TemplateElementResolutionStatus.Resolved, component, new[] { component }, Array.Empty<string>());

    public static TemplateElementResolution Failed(
        TemplateElementResolutionStatus status,
        IEnumerable<TemplateComponentDescriptor> candidates = null,
        IEnumerable<string> candidateNamespaceUris = null) =>
        new(status, null,
            candidates?.ToArray() ?? Array.Empty<TemplateComponentDescriptor>(),
            candidateNamespaceUris?.ToArray() ?? Array.Empty<string>());
}
