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
            ["polygon"] = "Square.UI.Svg.SVGPolygonElement",
            ["Show"] = "Show",
            ["For"] = "For",
            ["Index"] = "Index",
            ["Switch"] = "Switch",
            ["Match"] = "Match",
            ["Slot"] = "Slot",
            ["Outlet"] = "Slot"
        };

    private static readonly IReadOnlyDictionary<string, string> PropertyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Id",
            ["class"] = "class",
            ["style"] = "style",
            ["ref"] = "ref",
            ["slot"] = "slot",
            ["key"] = "key",
            ["when"] = "when",
            ["each"] = "each",
            ["fallback"] = "fallback",
            ["name"] = "name",
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

    private static readonly string[] CommonPropertyNames =
        { "id", "class", "style", "ref", "slot" };

    private static readonly string[] UiElementPropertyNames =
        { "width", "height", "disabled" };

    private static readonly HashSet<string> NonUiElementTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Show", "For", "Index", "Switch", "Match", "Slot", "Outlet",
        "svg", "g", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon"
    };

    private static readonly IReadOnlyDictionary<string, string[]> TagPropertyNames =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Text"] = new[] { "text", "color" },
            ["FontIcon"] = new[] { "glyph", "color", "font-family" },
            ["Button"] = new[] { "text", "background" },
            ["Input"] = new[] { "value", "placeholder", "type" },
            ["TextArea"] = new[] { "value", "placeholder" },
            ["CheckBox"] = new[] { "text", "checked" },
            ["Radio"] = new[] { "text", "checked", "group" },
            ["Select"] = new[] { "options", "value", "placeholder" },
            ["Image"] = new[] { "source", "image" },
            ["Splitter"] = new[] { "minimum", "maximum", "value", "vertical", "reversed" },
            ["SplitContainer"] = new[]
                { "minimum", "maximum", "value", "vertical", "splitter-thickness", "seamless" },
            ["List"] = new[] { "items", "selected-index" },
            ["VirtualList"] = new[] { "selected-index", "item-height", "overscan-count" },
            ["VirtualTree"] = new[]
                { "item-height", "overscan-count", "indent-size" },
            ["TreeItem"] = new[] { "text", "expanded", "color" },
            ["ListItem"] = new[] { "text", "marker", "color" },
            ["Swiper"] = new[] { "selected-index", "loop" },
            ["MenuItem"] = new[]
                { "text", "icon", "group", "shortcut", "checkable", "checked", "stays-open-on-click", "disabled" },
            ["Link"] = new[] { "text", "href", "underline", "color" },
            ["Show"] = new[] { "when", "fallback" },
            ["For"] = new[] { "each", "key", "fallback" },
            ["Index"] = new[] { "each", "fallback" },
            ["Switch"] = new[] { "fallback" },
            ["Match"] = new[] { "when" },
            ["Slot"] = new[] { "name", "fallback" },
            ["Outlet"] = new[] { "name", "fallback" },
            ["svg"] = new[] { "viewbox", "width", "height", "fill", "stroke", "opacity" },
            ["g"] = new[] { "transform", "fill", "stroke", "opacity" },
            ["path"] = new[] { "d", "transform", "fill", "stroke", "stroke-width", "opacity" },
            ["rect"] = new[] { "x", "y", "width", "height", "rx", "ry", "fill", "stroke", "opacity" },
            ["circle"] = new[] { "cx", "cy", "r", "fill", "stroke", "opacity" },
            ["ellipse"] = new[] { "cx", "cy", "rx", "ry", "fill", "stroke", "opacity" },
            ["line"] = new[] { "x1", "y1", "x2", "y2", "stroke", "stroke-width", "opacity" },
            ["polyline"] = new[] { "points", "fill", "stroke", "stroke-width", "opacity" },
            ["polygon"] = new[] { "points", "fill", "stroke", "stroke-width", "opacity" }
        };

    private static readonly HashSet<string> TextContentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Text", "Button", "Link", "ListItem", "TreeItem"
    };

    private static readonly HashSet<string> BooleanPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "seamless", "vertical", "reversed", "checked", "disabled", "checkable",
        "stays-open-on-click", "expanded", "loop", "replace", "underline"
    };

    private static readonly (string Name, string CanonicalName)[] StandardEvents =
    {
        ("pointerdown", "onPointerDown"),
        ("pointerup", "onPointerUp"),
        ("pointermove", "onPointerMove"),
        ("wheel", "onWheel"),
        ("scroll", "onScroll"),
        ("keydown", "onKeyDown"),
        ("keyup", "onKeyUp"),
        ("textinput", "onTextInput"),
        ("focusin", "onFocusIn"),
        ("focusout", "onFocusOut"),
        ("focus", "onFocus"),
        ("blur", "onBlur"),
        ("click", "onClick"),
        ("contextmenu", "onContextMenu"),
        ("change", "onChange"),
        ("selectionchange", "onSelectionChange"),
        ("input", "onInput"),
        ("requestframe", "onRequestFrame")
    };

    private readonly IReadOnlyDictionary<string, TemplateComponentDescriptor> _components;

    private TemplateCatalog(IReadOnlyDictionary<string, TemplateComponentDescriptor> components)
    {
        _components = components;
    }

    public static TemplateCatalog BuiltIn { get; } = CreateBuiltIn();

    public IReadOnlyCollection<TemplateEventDescriptor> Events { get; } =
        new ReadOnlyCollection<TemplateEventDescriptor>(StandardEvents
            .Select(item => new TemplateEventDescriptor(item.Name, item.CanonicalName))
            .ToArray());

    public IReadOnlyCollection<TemplatePropertyDescriptor> Properties { get; } =
        new ReadOnlyCollection<TemplatePropertyDescriptor>(PropertyAliases
            .Select(pair => new TemplatePropertyDescriptor(
                pair.Key,
                pair.Value,
                GetPropertyValueKind(pair.Key)))
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

    public IReadOnlyCollection<TemplatePropertyDescriptor> GetPropertiesForTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return Properties;
        var names = new HashSet<string>(CommonPropertyNames, StringComparer.OrdinalIgnoreCase);
        if (!NonUiElementTags.Contains(tagName)) names.UnionWith(UiElementPropertyNames);
        if (!_components.ContainsKey(tagName))
            return Properties.Where(property => names.Contains(property.Name)).ToArray();
        if (TagPropertyNames.TryGetValue(tagName, out var tagProperties)) names.UnionWith(tagProperties);
        return Properties.Where(property => names.Contains(property.Name)).ToArray();
    }

    public TemplatePropertyDescriptor GetProperty(string name) =>
        Properties.FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static TemplatePropertyValueKind GetPropertyValueKind(string name)
    {
        if (name.Equals("class", StringComparison.OrdinalIgnoreCase)) return TemplatePropertyValueKind.CssClass;
        return BooleanPropertyNames.Contains(name)
            ? TemplatePropertyValueKind.Boolean
            : TemplatePropertyValueKind.String;
    }

    private static TemplateCatalog CreateBuiltIn()
    {
        var components = new Dictionary<string, TemplateComponentDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in BuiltInTypeNames)
        {
            var isTitleBar = pair.Key.Equals("TitleBar", StringComparison.OrdinalIgnoreCase);
            var isDirective = pair.Key is "Show" or "For" or "Index" or "Switch" or "Match" or "Slot" or "Outlet";
            components[pair.Key] = new TemplateComponentDescriptor(
                pair.Key,
                pair.Value,
                !isDirective,
                isTitleBar,
                TextContentTags.Contains(pair.Key));
        }

        return new TemplateCatalog(components);
    }
}
