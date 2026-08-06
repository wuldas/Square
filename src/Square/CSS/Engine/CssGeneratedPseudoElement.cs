namespace Square.CSS.Engine;

internal sealed class CssGeneratedPseudoElement(string pseudoElementName) : Square.Controls.Text
{
    public string PseudoElementName { get; } = pseudoElementName;
    public bool IsNew { get; set; } = true;
}
