using Square.CSS.Values;

namespace Square.CSS.Properties;

internal readonly record struct CssPropertyAssignment(string Property, string Value);

internal static class CssShorthandExpander
{
    private static readonly string[] Sides = ["top", "right", "bottom", "left"];

    public static bool IsShorthand(string property) => property is
        "margin" or "padding" or "border-width" or "border-color" or "border-style" or "border" or
        "border-top" or "border-right" or "border-bottom" or "border-left" or "background" or "font" or
        "outline" or "list-style";

    public static bool TryExpand(string property, string value, out CssPropertyAssignment[] assignments)
    {
        value = value.Trim();
        if (CssValueSyntax.IsGlobalKeyword(value))
            return TryExpandGlobal(property, value, out assignments);
        if (CssValueSyntax.ContainsVariable(value)) return Fail(out assignments);

        return property switch
        {
            "margin" => TryExpandBox("margin", value, out assignments),
            "padding" => TryExpandBox("padding", value, out assignments),
            "border-width" => TryExpandBox("border", value, "width", out assignments),
            "border-color" => TryExpandBox("border", value, "color", out assignments),
            "border-style" => TryExpandBox("border", value, "style", out assignments),
            "border" => TryExpandBorder(value, null, out assignments),
            "border-top" => TryExpandBorder(value, "top", out assignments),
            "border-right" => TryExpandBorder(value, "right", out assignments),
            "border-bottom" => TryExpandBorder(value, "bottom", out assignments),
            "border-left" => TryExpandBorder(value, "left", out assignments),
            "background" => TryExpandBackground(value, out assignments),
            "font" => TryExpandFont(value, out assignments),
            "outline" => TryExpandOutline(value, out assignments),
            "list-style" => TryExpandListStyle(value, out assignments),
            _ => Fail(out assignments)
        };
    }

    private static bool TryExpandGlobal(string property, string value, out CssPropertyAssignment[] assignments)
    {
        var names = property switch
        {
            "margin" => Sides.Select(side => $"margin-{side}"),
            "padding" => Sides.Select(side => $"padding-{side}"),
            "border-width" => Sides.Select(side => $"border-{side}-width"),
            "border-color" => Sides.Select(side => $"border-{side}-color"),
            "border-style" => Sides.Select(side => $"border-{side}-style"),
            "border" => Sides.SelectMany(BorderLonghands),
            "border-top" => BorderLonghands("top"),
            "border-right" => BorderLonghands("right"),
            "border-bottom" => BorderLonghands("bottom"),
            "border-left" => BorderLonghands("left"),
            "background" => ["background-color"],
            "font" => ["font-style", "font-variant", "font-weight", "font-size", "line-height", "font-family"],
            "outline" => ["outline-width", "outline-style", "outline-color"],
            "list-style" => ["list-style-type", "list-style-position", "list-style-image"],
            _ => []
        };
        assignments = names.Select(name => new CssPropertyAssignment(name, value)).ToArray();
        return assignments.Length > 0;
    }

    private static IEnumerable<string> BorderLonghands(string side) =>
        [$"border-{side}-width", $"border-{side}-style", $"border-{side}-color"];

    private static bool TryExpandBox(string prefix, string value, out CssPropertyAssignment[] assignments) =>
        TryExpandBox(prefix, value, null, out assignments);

    private static bool TryExpandBox(string prefix, string value, string? suffix, out CssPropertyAssignment[] assignments)
    {
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length is < 1 or > 4)
            return Fail(out assignments);

        var names = Sides.Select(side => suffix == null ? $"{prefix}-{side}" : $"{prefix}-{side}-{suffix}").ToArray();
        var values = tokens.Length switch
        {
            1 => [tokens[0], tokens[0], tokens[0], tokens[0]],
            2 => [tokens[0], tokens[1], tokens[0], tokens[1]],
            3 => [tokens[0], tokens[1], tokens[2], tokens[1]],
            _ => tokens
        };
        assignments = names.Select((name, index) => new CssPropertyAssignment(name, values[index])).ToArray();
        return assignments.All(assignment => CssPropertyRegistry.IsValid(assignment.Property, assignment.Value));
    }

    private static bool TryExpandBorder(string value, string? side, out CssPropertyAssignment[] assignments)
    {
        if (!TryParseBorder(value, out var width, out var style, out var color)) return Fail(out assignments);
        var sides = side == null ? Sides : [side];
        assignments = sides.SelectMany(current => new[]
        {
            new CssPropertyAssignment($"border-{current}-width", width),
            new CssPropertyAssignment($"border-{current}-style", style),
            new CssPropertyAssignment($"border-{current}-color", color)
        }).ToArray();
        return true;
    }

    private static bool TryParseBorder(string value, out string width, out string style, out string color)
    {
        width = CssPropertyRegistry.GetInitialValue("border-top-width")!;
        style = CssPropertyRegistry.GetInitialValue("border-top-style")!;
        color = CssPropertyRegistry.GetInitialValue("border-top-color")!;
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length is < 1 or > 3) return false;

        var hasWidth = false;
        var hasStyle = false;
        var hasColor = false;
        foreach (var token in tokens)
        {
            if (!hasWidth && CssPropertyRegistry.IsValid("border-top-width", token))
            {
                width = token;
                hasWidth = true;
            }
            else if (!hasStyle && CssPropertyRegistry.IsValid("border-top-style", token))
            {
                style = token;
                hasStyle = true;
            }
            else if (!hasColor && CssPropertyRegistry.IsValid("border-top-color", token))
            {
                color = token;
                hasColor = true;
            }
            else return false;
        }
        return true;
    }

    private static bool TryExpandBackground(string value, out CssPropertyAssignment[] assignments)
    {
        var color = string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) ? "transparent" : value;
        if (!CssPropertyRegistry.IsValid("background-color", color)) return Fail(out assignments);
        assignments = [new CssPropertyAssignment("background-color", color)];
        return true;
    }

    private static bool TryExpandFont(string value, out CssPropertyAssignment[] assignments)
    {
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length < 2)
            return Fail(out assignments);

        var style = "normal";
        var variant = "normal";
        var weight = "normal";
        string? size = null;
        var lineHeight = "normal";
        var familyStart = -1;
        var hasStyle = false;
        var hasVariant = false;
        var hasWeight = false;

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var slash = token.IndexOf('/');
            var sizeToken = slash >= 0 ? token[..slash] : token;
            if (CssPropertyRegistry.IsValid("font-size", sizeToken))
            {
                size = sizeToken;
                if (slash >= 0)
                {
                    lineHeight = token[(slash + 1)..];
                    if (lineHeight.Length == 0 || !CssPropertyRegistry.IsValid("line-height", lineHeight))
                        return Fail(out assignments);
                }
                else if (i + 1 < tokens.Length && tokens[i + 1] == "/")
                {
                    if (i + 2 >= tokens.Length || !CssPropertyRegistry.IsValid("line-height", tokens[i + 2]))
                        return Fail(out assignments);
                    lineHeight = tokens[i + 2];
                    i += 2;
                }
                familyStart = i + 1;
                break;
            }

            if (!hasStyle && CssPropertyRegistry.IsValid("font-style", token))
            {
                style = token;
                hasStyle = true;
            }
            else if (!hasVariant && CssPropertyRegistry.IsValid("font-variant", token))
            {
                variant = token;
                hasVariant = true;
            }
            else if (!hasWeight && CssPropertyRegistry.IsValid("font-weight", token))
            {
                weight = token;
                hasWeight = true;
            }
            else return Fail(out assignments);
        }

        if (size == null || familyStart < 0 || familyStart >= tokens.Length) return Fail(out assignments);
        var family = string.Join(' ', tokens[familyStart..]);
        if (!CssPropertyRegistry.IsValid("font-family", family)) return Fail(out assignments);
        assignments =
        [
            new CssPropertyAssignment("font-style", style),
            new CssPropertyAssignment("font-variant", variant),
            new CssPropertyAssignment("font-weight", weight),
            new CssPropertyAssignment("font-size", size),
            new CssPropertyAssignment("line-height", lineHeight),
            new CssPropertyAssignment("font-family", family)
        ];
        return true;
    }

    private static bool TryExpandOutline(string value, out CssPropertyAssignment[] assignments)
    {
        if (!TryParseBorder(value, out var width, out var style, out var color)) return Fail(out assignments);
        if (string.Equals(color, "currentcolor", StringComparison.OrdinalIgnoreCase))
            color = CssPropertyRegistry.GetInitialValue("outline-color")!;
        assignments =
        [
            new CssPropertyAssignment("outline-width", width),
            new CssPropertyAssignment("outline-style", style),
            new CssPropertyAssignment("outline-color", color)
        ];
        return assignments.All(assignment => CssPropertyRegistry.IsValid(assignment.Property, assignment.Value));
    }

    private static bool TryExpandListStyle(string value, out CssPropertyAssignment[] assignments)
    {
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length is < 1 or > 3)
            return Fail(out assignments);
        var type = "disc";
        var position = "outside";
        var image = "none";
        var hasType = false;
        var hasPosition = false;
        var hasImage = false;
        var ambiguousNone = false;

        foreach (var token in tokens)
        {
            if (string.Equals(token, "none", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasType && !hasImage)
                {
                    type = token;
                    image = token;
                    hasType = true;
                    hasImage = true;
                    ambiguousNone = true;
                }
                else if (!hasType) { type = token; hasType = true; }
                else if (!hasImage) { image = token; hasImage = true; }
                else return Fail(out assignments);
            }
            else if (!hasPosition && CssPropertyRegistry.IsValid("list-style-position", token))
            {
                position = token;
                hasPosition = true;
            }
            else if ((!hasImage || ambiguousNone) && CssPropertyRegistry.IsValid("list-style-image", token))
            {
                image = token;
                hasImage = true;
                ambiguousNone = false;
            }
            else if ((!hasType || ambiguousNone) && CssPropertyRegistry.IsValid("list-style-type", token))
            {
                type = token;
                hasType = true;
                ambiguousNone = false;
            }
            else return Fail(out assignments);
        }

        assignments =
        [
            new CssPropertyAssignment("list-style-type", type),
            new CssPropertyAssignment("list-style-position", position),
            new CssPropertyAssignment("list-style-image", image)
        ];
        return true;
    }

    private static bool Fail(out CssPropertyAssignment[] assignments)
    {
        assignments = [];
        return false;
    }
}
