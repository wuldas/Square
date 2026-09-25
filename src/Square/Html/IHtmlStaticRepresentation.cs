namespace Square.Html;

/// <summary>Supplies a detached HTML tree that the safe Web exporter normalizes and encodes.</summary>
public interface IHtmlStaticRepresentation
{
    HTMLElement CreateHtmlRepresentation();
}
