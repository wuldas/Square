using Square.Controls;
using Square.Html;
using Square.UI;

namespace Square.Controls;

/// <summary>注册内置控件类型到 <see cref="ElementRegistry"/>，幂等。</summary>
public static class ControlRegistration
{
    private static bool _registered;
    private static readonly object Gate = new();

    /// <summary>注册所有默认控件、SVG 与 113 个 HTML 元素。</summary>
    public static void RegisterDefaults()
    {
        lock (Gate)
        {
            if (_registered) return;

            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "View", static () => new View());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "ScrollViewer", static () => new ScrollViewer());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Popup", static () => new Popup());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Dialog", static () => new Dialog());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "MenuBar", static () => new MenuBar());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Menu", static () => new Menu());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "ContextMenu", static () => new ContextMenu());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "MenuItem", static () => new MenuItem());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "MenuSeparator", static () => new MenuSeparator());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Text", static () => new Controls.Text());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "FontIcon", static () => new FontIcon());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Splitter", static () => new Splitter());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "SplitContainer", static () => new SplitContainer());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "List", static () => new Controls.List());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "VirtualList", static () => new VirtualList());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "ListItem", static () => new ListItem());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Tree", static () => new Tree());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "VirtualTree", static () => new VirtualTree());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TreeItem", static () => new TreeItem());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Swiper", static () => new Swiper());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Link", static () => new Controls.Link());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Button", static () => new Button());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Input", static () => new Input());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TextArea", static () => new TextArea());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "CheckBox", static () => new CheckBox());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Radio", static () => new Radio());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Select", static () => new Select());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Image", static () => new Controls.Image());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Canvas", static () => new Canvas());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TitleBar", static () => new TitleBar());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Table", static () => new Table());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "InlineTable", static () => new InlineTable());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TableRowGroup", static () => new TableRowGroup());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TableHeaderGroup", static () => new TableHeaderGroup());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TableFooterGroup", static () => new TableFooterGroup());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TableRow", static () => new TableRow());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TableCell", static () => new TableCell());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "TableCaption", static () => new TableCaption());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "UI", static () => new UIRootElement());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Head", static () => new UIHeadElement());
            ElementRegistry.Register(ElementRegistry.SquareNamespaceUri, "Body", static () => new UIBodyElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "svg", static () => new Square.UI.Svg.SVGSVGElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "g", static () => new Square.UI.Svg.SVGGElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "path", static () => new Square.UI.Svg.SVGPathElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "rect", static () => new Square.UI.Svg.SVGRectElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "circle", static () => new Square.UI.Svg.SVGCircleElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "ellipse", static () => new Square.UI.Svg.SVGEllipseElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "line", static () => new Square.UI.Svg.SVGLineElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "polyline", static () => new Square.UI.Svg.SVGPolylineElement());
            ElementRegistry.Register(ElementRegistry.SvgNamespaceUri, "polygon", static () => new Square.UI.Svg.SVGPolygonElement());
            foreach (var (tag, factory) in HtmlElementFactory.Registrations)
                ElementRegistry.Register(ElementRegistry.HtmlNamespaceUri, tag, factory);
            _registered = true;
        }
    }
}
