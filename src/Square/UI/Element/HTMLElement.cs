namespace Square.UI;

/// <summary>Base for XHTML namespace elements participating in Square layout and focus.</summary>
public abstract class HTMLElement : UIElement
{
    /// <inheritdoc />
    public override string? NamespaceURI => "http://www.w3.org/1999/xhtml";
}
