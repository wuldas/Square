using System.Globalization;
using Square.Graphics;
using Square.CSS.Values;

namespace Square.CSS.Properties;

internal static class CssBorderRadiusParser
{
    public static bool IsValid(string value)
    {
        if (!TrySplit(value, out var horizontal, out var vertical)) return false;
        return horizontal.All(IsRadiusToken) && vertical.All(IsRadiusToken);
    }

    public static bool IsCornerValid(string value)
    {
        if (!CssValueSyntax.TrySplitWhitespace(value, out var tokens) || tokens.Length is < 1 or > 2)
            return false;
        return tokens.All(IsRadiusToken);
    }

    public static bool TryExpandCorners(string value, out string[] corners)
    {
        corners = [];
        if (!TrySplit(value, out var horizontal, out var vertical) ||
            !horizontal.All(IsRadiusToken) || !vertical.All(IsRadiusToken))
            return false;

        var horizontalValues = Expand(horizontal);
        var verticalValues = Expand(vertical);
        corners = new string[4];
        for (var i = 0; i < corners.Length; i++)
            corners[i] = string.Equals(horizontalValues[i], verticalValues[i], StringComparison.OrdinalIgnoreCase)
                ? horizontalValues[i]
                : $"{horizontalValues[i]} {verticalValues[i]}";
        return true;
    }

    public static bool TryResolve(string? value, Rect box, out RoundedRectGeometry geometry)
    {
        geometry = new RoundedRectGeometry(box, 0, 0);
        if (!TrySplit(value, out var horizontal, out var vertical)) return false;

        var horizontalValues = Expand(horizontal);
        var verticalValues = Expand(vertical);
        var radii = new[]
        {
            new CornerRadius(ParseRadius(horizontalValues[0], box.Width), ParseRadius(verticalValues[0], box.Height)),
            new CornerRadius(ParseRadius(horizontalValues[1], box.Width), ParseRadius(verticalValues[1], box.Height)),
            new CornerRadius(ParseRadius(horizontalValues[2], box.Width), ParseRadius(verticalValues[2], box.Height)),
            new CornerRadius(ParseRadius(horizontalValues[3], box.Width), ParseRadius(verticalValues[3], box.Height))
        };
        Normalize(radii, box.Width, box.Height);
        geometry = new RoundedRectGeometry(box, radii[0], radii[1], radii[2], radii[3]);
        return true;
    }

    public static bool TryResolve(
        IReadOnlyDictionary<string, string> declarations,
        Func<string, string?> getValue,
        Rect box,
        out RoundedRectGeometry geometry)
    {
        if (!TryResolve(getValue("border-radius"), box, out geometry)) return false;

        var names = new[] { "top-left", "top-right", "bottom-right", "bottom-left" };
        var radii = new[] { geometry.TopLeft, geometry.TopRight, geometry.BottomRight, geometry.BottomLeft };
        for (var i = 0; i < names.Length; i++)
        {
            var property = $"border-{names[i]}-radius";
            if (declarations.ContainsKey(property) && TryResolveCorner(getValue(property), box, out var radius))
                radii[i] = radius;
        }

        Normalize(radii, box.Width, box.Height);
        geometry = new RoundedRectGeometry(box, radii[0], radii[1], radii[2], radii[3]);
        return true;
    }

    private static bool TryResolveCorner(string? value, Rect box, out CornerRadius radius)
    {
        radius = default;
        if (!CssValueSyntax.TrySplitWhitespace(value ?? "", out var tokens) || tokens.Length is < 1 or > 2 ||
            !tokens.All(IsRadiusToken)) return false;
        radius = new CornerRadius(
            ParseRadius(tokens[0], box.Width),
            ParseRadius(tokens.Length == 2 ? tokens[1] : tokens[0], box.Height));
        return true;
    }

    private static bool TrySplit(string? value, out string[] horizontal, out string[] vertical)
    {
        horizontal = [];
        vertical = [];
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 ||
            !CssValueSyntax.TrySplitWhitespace(parts[0], out horizontal) || horizontal.Length is < 1 or > 4)
            return false;
        if (parts.Length == 1) vertical = horizontal;
        else if (!CssValueSyntax.TrySplitWhitespace(parts[1], out vertical) || vertical.Length is < 1 or > 4)
            return false;
        return true;
    }

    private static string[] Expand(IReadOnlyList<string> values) => values.Count switch
    {
        1 => [values[0], values[0], values[0], values[0]],
        2 => [values[0], values[1], values[0], values[1]],
        3 => [values[0], values[1], values[2], values[1]],
        _ => [values[0], values[1], values[2], values[3]]
    };

    private static bool IsRadiusToken(string value)
    {
        var token = value.Trim();
        if (token == "0") return true;
        var unit = token.EndsWith('%') ? "%" : token.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? "px" : "";
        return unit.Length > 0 &&
            float.TryParse(token[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            float.IsFinite(number) && number >= 0;
    }

    private static float ParseRadius(string value, float reference)
    {
        var token = value.Trim();
        if (token == "0") return 0;
        if (token.EndsWith('%'))
            return Math.Max(0, float.Parse(token[..^1], CultureInfo.InvariantCulture) * reference / 100f);
        return Math.Max(0, float.Parse(token[..^2], CultureInfo.InvariantCulture));
    }

    private static void Normalize(CornerRadius[] radii, float width, float height)
    {
        var scale = MathF.Min(1,
            MathF.Min(
                MathF.Min(Scale(width, radii[0].X, radii[1].X), Scale(width, radii[3].X, radii[2].X)),
                MathF.Min(Scale(height, radii[0].Y, radii[3].Y), Scale(height, radii[1].Y, radii[2].Y))));
        if (!float.IsFinite(scale) || scale >= 1) return;
        for (var i = 0; i < radii.Length; i++)
            radii[i] = new CornerRadius(radii[i].X * scale, radii[i].Y * scale);
    }

    private static float Scale(float reference, float first, float second)
    {
        var sum = first + second;
        return sum <= 0 ? float.PositiveInfinity : reference / sum;
    }
}