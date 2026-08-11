using Square.Controls;
using Square.UI;

namespace Square.Controls;

/// <summary>注册内置控件类型到 <see cref="ElementRegistry"/>，幂等。</summary>
public static class ControlRegistration
{
    private static bool _registered;

    /// <summary>注册所有默认控件与 SVG 元素。</summary>
    public static void RegisterDefaults()
    {
        if (_registered) return;
        _registered = true;

        ElementRegistry.Register("View", static () => new View());
        ElementRegistry.Register("ScrollViewer", static () => new ScrollViewer());
        ElementRegistry.Register("Popup", static () => new Popup());
        ElementRegistry.Register("Dialog", static () => new Dialog());
        ElementRegistry.Register("MenuBar", static () => new MenuBar());
        ElementRegistry.Register("Menu", static () => new Menu());
        ElementRegistry.Register("ContextMenu", static () => new ContextMenu());
        ElementRegistry.Register("MenuItem", static () => new MenuItem());
        ElementRegistry.Register("MenuSeparator", static () => new MenuSeparator());
        ElementRegistry.Register("Text", static () => new Controls.Text());
        ElementRegistry.Register("FontIcon", static () => new FontIcon());
        ElementRegistry.Register("Splitter", static () => new Splitter());
        ElementRegistry.Register("SplitContainer", static () => new SplitContainer());
        ElementRegistry.Register("List", static () => new Controls.List());
        ElementRegistry.Register("VirtualList", static () => new VirtualList());
        ElementRegistry.Register("ListItem", static () => new ListItem());
        ElementRegistry.Register("Tree", static () => new Tree());
        ElementRegistry.Register("VirtualTree", static () => new VirtualTree());
        ElementRegistry.Register("TreeItem", static () => new TreeItem());
        ElementRegistry.Register("Swiper", static () => new Swiper());
        ElementRegistry.Register("Link", static () => new Controls.Link());
        ElementRegistry.Register("Button", static () => new Button());
        ElementRegistry.Register("Input", static () => new Input());
        ElementRegistry.Register("TextArea", static () => new TextArea());
        ElementRegistry.Register("CheckBox", static () => new CheckBox());
        ElementRegistry.Register("Radio", static () => new Radio());
        ElementRegistry.Register("Select", static () => new Select());
        ElementRegistry.Register("Image", static () => new Controls.Image());
        ElementRegistry.Register("Canvas", static () => new Canvas());
        ElementRegistry.Register("TitleBar", static () => new TitleBar());
        ElementRegistry.Register("Table", static () => new Table());
        ElementRegistry.Register("InlineTable", static () => new InlineTable());
        ElementRegistry.Register("TableRowGroup", static () => new TableRowGroup());
        ElementRegistry.Register("TableHeaderGroup", static () => new TableHeaderGroup());
        ElementRegistry.Register("TableFooterGroup", static () => new TableFooterGroup());
        ElementRegistry.Register("TableRow", static () => new TableRow());
        ElementRegistry.Register("TableCell", static () => new TableCell());
        ElementRegistry.Register("TableCaption", static () => new TableCaption());
        ElementRegistry.Register("UI", static () => new UIRootElement());
        ElementRegistry.Register("Head", static () => new UIHeadElement());
        ElementRegistry.Register("Body", static () => new UIBodyElement());
        ElementRegistry.Register("svg", static () => new Square.UI.Svg.SVGSVGElement());
        ElementRegistry.Register("g", static () => new Square.UI.Svg.SVGGElement());
        ElementRegistry.Register("path", static () => new Square.UI.Svg.SVGPathElement());
        ElementRegistry.Register("rect", static () => new Square.UI.Svg.SVGRectElement());
        ElementRegistry.Register("circle", static () => new Square.UI.Svg.SVGCircleElement());
        ElementRegistry.Register("ellipse", static () => new Square.UI.Svg.SVGEllipseElement());
        ElementRegistry.Register("line", static () => new Square.UI.Svg.SVGLineElement());
        ElementRegistry.Register("polyline", static () => new Square.UI.Svg.SVGPolylineElement());
        ElementRegistry.Register("polygon", static () => new Square.UI.Svg.SVGPolygonElement());
    }
}
