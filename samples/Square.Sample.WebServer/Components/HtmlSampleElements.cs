using Square.Html;
using Square.UI;

[assembly: HtmlCustomElementExport("acme-badge", typeof(Square.Sample.WebServer.Components.AcmeBadgeElement),
    ObservedAttributes = new[] { "status" })]
[assembly: HtmlCustomElementExport("acme-button", typeof(Square.Sample.WebServer.Components.AcmeButtonElement),
    ExtendsTag = "button")]
[assembly: ElementExport("urn:acme:widgets", "element", typeof(Square.Sample.WebServer.Components.AcmeBadgeElement))]

namespace Square.Sample.WebServer.Components;

public class AcmeBadgeElement : HTMLElement, IHtmlStaticRepresentation
{
    private readonly Square.UI.Text _label;

    public AcmeBadgeElement() : base("acme-badge")
    {
        _label = new Square.UI.Text(BadgeText);
        ChildNodes.Add(_label);
    }

    private string BadgeText => "Badge: " + (GetAttribute("status") ?? "");

    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == "status") _label.Data = BadgeText;
    }

    public HTMLElement CreateHtmlRepresentation()
    {
        var span = new HTMLSpanElement();
        span.ChildNodes.Add(new Square.UI.Text(BadgeText));
        return span;
    }
}

public class AcmeButtonElement : HTMLButtonElement, IHtmlStaticRepresentation
{
    public HTMLElement CreateHtmlRepresentation()
    {
        var button = new HTMLButtonElement();
        foreach (var (name, value) in GetAttributes())
            if (name is "id" or "class" or "style" or "title" or "type" or "name" or "value" or "disabled")
                button.SetAttribute(name, value);
        foreach (var child in ChildNodes)
            if (child is Square.UI.Text text)
                button.ChildNodes.Add(new Square.UI.Text(text.Data));
        return button;
    }
}
