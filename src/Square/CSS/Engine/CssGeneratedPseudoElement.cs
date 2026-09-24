namespace Square.CSS.Engine;

internal sealed class CssGeneratedPseudoElement : Square.Controls.Text
{
    public CssGeneratedPseudoElement(string pseudoElementName)
    {
        PseudoElementName = pseudoElementName;
        if (pseudoElementName == "marker")
            Style.SetCascaded("display", "inline", new Square.UI.ElementApi.CssSpecificity(0, 0, 0),
                important: false, persistent: true, origin: Square.UI.ElementApi.CssCascadeOrigin.UserAgent);
    }

    public string PseudoElementName { get; }
    public bool IsNew { get; set; } = true;
}
