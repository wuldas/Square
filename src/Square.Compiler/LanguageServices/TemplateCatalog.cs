using System.Collections.ObjectModel;

namespace Square.Compiler.LanguageServices;

/// <summary>
/// Shared compile-time metadata used by the generator and future language-server features.
/// </summary>
public sealed class TemplateCatalog
{
    private static readonly IReadOnlyDictionary<string, string> BuiltInTypeNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["View"] = "Square.Controls.View",
            ["ScrollViewer"] = "Square.Controls.ScrollViewer",
            ["Popup"] = "Square.Controls.Popup",
            ["Dialog"] = "Square.Controls.Dialog",
            ["MenuBar"] = "Square.Controls.MenuBar",
            ["Menu"] = "Square.Controls.Menu",
            ["ContextMenu"] = "Square.Controls.ContextMenu",
            ["MenuItem"] = "Square.Controls.MenuItem",
            ["MenuSeparator"] = "Square.Controls.MenuSeparator",
            ["Text"] = "Square.Controls.Text",
            ["FontIcon"] = "Square.Controls.FontIcon",
            ["Splitter"] = "Square.Controls.Splitter",
            ["SplitContainer"] = "Square.Controls.SplitContainer",
            ["List"] = "Square.Controls.List",
            ["VirtualList"] = "Square.Controls.VirtualList",
            ["ListItem"] = "Square.Controls.ListItem",
            ["Tree"] = "Square.Controls.Tree",
            ["VirtualTree"] = "Square.Controls.VirtualTree",
            ["TreeItem"] = "Square.Controls.TreeItem",
            ["Swiper"] = "Square.Controls.Swiper",
            ["Button"] = "Square.Controls.Button",
            ["Input"] = "Square.Controls.Input",
            ["TextArea"] = "Square.Controls.TextArea",
            ["CheckBox"] = "Square.Controls.CheckBox",
            ["Radio"] = "Square.Controls.Radio",
            ["Select"] = "Square.Controls.Select",
            ["Image"] = "Square.Controls.Image",
            ["Canvas"] = "Square.Controls.Canvas",
            ["TitleBar"] = "Square.Controls.TitleBar",
            ["Link"] = "Square.Controls.Link",
            ["svg"] = "Square.UI.Svg.SVGSVGElement",
            ["g"] = "Square.UI.Svg.SVGGElement",
            ["path"] = "Square.UI.Svg.SVGPathElement",
            ["rect"] = "Square.UI.Svg.SVGRectElement",
            ["circle"] = "Square.UI.Svg.SVGCircleElement",
            ["ellipse"] = "Square.UI.Svg.SVGEllipseElement",
            ["line"] = "Square.UI.Svg.SVGLineElement",
            ["polyline"] = "Square.UI.Svg.SVGPolylineElement",
            ["polygon"] = "Square.UI.Svg.SVGPolygonElement"
        };

    private static readonly IReadOnlyDictionary<string, string> PropertyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id",
            ["text"] = "TextContent",
            ["glyph"] = "Glyph",
            ["icon"] = "Icon",
            ["font-family"] = "FontFamily",
            ["minimum"] = "Minimum",
            ["maximum"] = "Maximum",
            ["splitter-thickness"] = "SplitterThickness",
            ["seamless"] = "IsSeamless",
            ["vertical"] = "IsVertical",
            ["reversed"] = "IsReversed",
            ["value"] = "Value",
            ["checked"] = "IsChecked",
            ["disabled"] = "IsDisabled",
            ["placeholder"] = "Placeholder",
            ["source"] = "Source",
            ["image"] = "ImageContent",
            ["group"] = "GroupName",
            ["shortcut"] = "ShortcutText",
            ["checkable"] = "IsCheckable",
            ["stays-open-on-click"] = "StaysOpenOnClick",
            ["options"] = "Options",
            ["items"] = "Items",
            ["selected-index"] = "SelectedIndex",
            ["item-height"] = "ItemHeight",
            ["overscan-count"] = "OverscanCount",
            ["indent-size"] = "IndentSize",
            ["expanded"] = "IsExpanded",
            ["loop"] = "Loop",
            ["to"] = "To",
            ["href"] = "Href",
            ["marker"] = "Marker",
            ["replace"] = "Replace",
            ["color"] = "Color",
            ["background"] = "Background",
            ["underline"] = "Underline",
            ["type"] = "Type",
            ["viewbox"] = "ViewBox",
            ["x"] = "X",
            ["y"] = "Y",
            ["width"] = "Width",
            ["height"] = "Height",
            ["rx"] = "RadiusX",
            ["ry"] = "RadiusY",
            ["cx"] = "CenterX",
            ["cy"] = "CenterY",
            ["r"] = "Radius",
            ["x1"] = "X1",
            ["y1"] = "Y1",
            ["x2"] = "X2",
            ["y2"] = "Y2",
            ["points"] = "Points",
            ["d"] = "Data",
            ["transform"] = "Transform",
            ["fill"] = "Fill",
            ["stroke"] = "Stroke",
            ["stroke-width"] = "StrokeWidth",
            ["opacity"] = "Opacity",
            ["fill-opacity"] = "FillOpacity",
            ["stroke-opacity"] = "StrokeOpacity"
        };

    private static readonly HashSet<string> TextContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "Button", "Link", "ListItem", "TreeItem"
    };

    private static readonly string[] StandardEventNames =
    {
        "pointerdown", "pointerup", "pointermove", "wheel", "scroll",
        "keydown", "keyup", "textinput", "focusin", "focusout", "focus", "blur",
        "click", "contextmenu", "change", "selectionchange", "input", "requestframe"
    };

    private readonly IReadOnlyDictionary<string, TemplateComponentDescriptor> _components;

    private TemplateCatalog(IReadOnlyDictionary<string, TemplateComponentDescriptor> components)
    {
        _components = components;
    }

    public static TemplateCatalog BuiltIn { get; } = CreateBuiltIn();

    public IReadOnlyCollection<TemplateEventDescriptor> Events { get; } =
        new ReadOnlyCollection<TemplateEventDescriptor>(StandardEventNames
            .Select(name => new TemplateEventDescriptor(name, name))
            .ToArray());

    public IReadOnlyCollection<TemplatePropertyDescriptor> Properties { get; } =
        new ReadOnlyCollection<TemplatePropertyDescriptor>(PropertyAliases
            .Select(pair => new TemplatePropertyDescriptor(pair.Key, pair.Value))
            .ToArray());

    public IReadOnlyCollection<TemplateComponentDescriptor> Components =>
        new ReadOnlyCollection<TemplateComponentDescriptor>(_components.Values
            .GroupBy(descriptor => descriptor.TagName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray());

    public TemplateComponentDescriptor GetComponent(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return new TemplateComponentDescriptor("Component", "Component", false, true, false);

        if (_components.TryGetValue(tagName, out var descriptor))
            return descriptor;

        return new TemplateComponentDescriptor(
            tagName,
            tagName,
            false,
            true,
            false);
    }

    public string MapPropertyName(string markupName)
    {
        if (string.IsNullOrWhiteSpace(markupName)) return markupName;
        return PropertyAliases.TryGetValue(markupName, out var propertyName)
            ? propertyName
            : markupName;
    }

    private static TemplateCatalog CreateBuiltIn()
    {
        var components = new Dictionary<string, TemplateComponentDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in BuiltInTypeNames)
        {
            var isTitleBar = pair.Key.Equals("TitleBar", StringComparison.OrdinalIgnoreCase);
            components[pair.Key] = new TemplateComponentDescriptor(
                pair.Key,
                pair.Value,
                true,
                isTitleBar,
                TextContentTags.Contains(pair.Key));
        }

        return new TemplateCatalog(components);
    }
}
