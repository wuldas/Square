#nullable enable

using System.Collections.ObjectModel;

namespace Square.Html;

/// <summary>
/// SVG attribute → CLR property alias table shared by the template compiler descriptors
/// (<c>TemplateCatalog</c>, <c>SqvValidator</c>) and the runtime static HTML importer.
/// Pure string data: compiled into the netstandard2.0 analyzer via Compile Link, so it must
/// not reference <c>typeof</c>, delegates, or any other Square runtime type.
/// </summary>
public static class SvgAttributeCatalog
{
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
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

    /// <summary>SVG attribute names mapped to native property names.</summary>
    public static IReadOnlyDictionary<string, string> PropertyAliases { get; } =
        new ReadOnlyDictionary<string, string>(Aliases);

    /// <summary>Resolves an SVG attribute name to its CLR property name, ignoring case.</summary>
    public static bool TryGetPropertyName(string attributeName, out string propertyName)
    {
        if (string.IsNullOrEmpty(attributeName))
        {
            propertyName = null!;
            return false;
        }
        return Aliases.TryGetValue(attributeName, out propertyName!);
    }
}
