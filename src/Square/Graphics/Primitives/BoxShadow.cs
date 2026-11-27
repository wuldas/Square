using System.Globalization;

namespace Square.Graphics;

/// <summary>盒阴影（偏移、模糊、扩展、颜色）。</summary>
public readonly record struct BoxShadow(float OffsetX, float OffsetY, float BlurRadius, float SpreadRadius, Color Color)
{
    /// <summary>尝试解析 CSS box-shadow 字符串。</summary>
    /// <returns>解析成功返回 true；失败返回 false。</returns>
    public static bool TryParse(string? value, out BoxShadow shadow)
    {
        shadow = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("inset", StringComparison.OrdinalIgnoreCase) ||
            HasTopLevelComma(text)) return false;

        var tokens = Tokenize(text);
        var lengths = new List<float>(4);
        var color = Color.FromRgba(0, 0, 0, 64);
        foreach (var token in tokens)
        {
            if (TryParseLength(token, out var length))
            {
                lengths.Add(length);
                continue;
            }
            if (!TryParseColor(token, out color)) return false;
        }

        if (lengths.Count is < 2 or > 4) return false;
        shadow = new BoxShadow(
            lengths[0], lengths[1],
            Math.Max(0, lengths.Count > 2 ? lengths[2] : 0),
            lengths.Count > 3 ? lengths[3] : 0,
            color);
        return true;
    }

    /// <summary>尝试解析逗号分隔的 CSS 外阴影列表。</summary>
    /// <returns>解析成功返回 true；<c>none</c> 返回空列表；任一阴影无效时返回 false。</returns>
    public static bool TryParseList(string? value, out IReadOnlyList<BoxShadow> shadows)
    {
        shadows = Array.Empty<BoxShadow>();
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase)) return true;
        if (!TrySplitList(text, out var items)) return false;

        var parsed = new List<BoxShadow>(items.Count);
        foreach (var item in items)
        {
            if (!TryParse(item, out var shadow)) return false;
            parsed.Add(shadow);
        }
        shadows = parsed;
        return true;
    }

    private static bool HasTopLevelComma(string text)
    {
        var depth = 0;
        foreach (var character in text)
        {
            if (character == '(') depth++;
            else if (character == ')') depth--;
            else if (character == ',' && depth == 0) return true;
        }
        return false;
    }

    private static bool TrySplitList(string text, out List<string> items)
    {
        items = [];
        var start = 0;
        var depth = 0;
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }
            if (character == '(') depth++;
            else if (character == ')')
            {
                if (--depth < 0) return false;
            }
            else if (character == ',' && depth == 0)
            {
                var item = text[start..i].Trim();
                if (item.Length == 0) return false;
                items.Add(item);
                start = i + 1;
            }
        }

        if (depth != 0 || quote != '\0') return false;
        var last = text[start..].Trim();
        if (last.Length == 0) return false;
        items.Add(last);
        return true;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = 0;
        var depth = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')') depth--;
                if (!char.IsWhiteSpace(text[i]) || depth > 0) continue;
            }
            if (i > start) tokens.Add(text[start..i]);
            start = i + 1;
        }
        return tokens;
    }

    private static bool TryParseLength(string token, out float value)
    {
        var text = token.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseColor(string token, out Color color)
    {
        var text = token.Trim();
        try
        {
            if (text.StartsWith('#'))
            {
                color = Color.Parse(text);
                return true;
            }
            if (text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
            {
                var parts = text[5..^1].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 4 || !byte.TryParse(parts[0], out var r) || !byte.TryParse(parts[1], out var g) ||
                    !byte.TryParse(parts[2], out var b) ||
                    !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
                {
                    color = default;
                    return false;
                }
                color = Color.FromRgba(r, g, b, (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255));
                return true;
            }
            if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')'))
            {
                var parts = text[4..^1].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 3 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
                {
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
            }
        }
        catch (FormatException) { }
        color = default;
        return false;
    }
}

/// <summary>盒阴影渲染辅助方法。</summary>
public static class BoxShadowRendering
{
    /// <summary>计算盒阴影的视觉包围盒。</summary>
    public static Rect GetVisualBounds(Rect box, BoxShadow shadow)
    {
        if (box.IsEmpty || shadow.Color.A == 0) return box;
        var shadowBounds = box.Offset(shadow.OffsetX, shadow.OffsetY)
            .Inflate(shadow.SpreadRadius + Math.Max(0, shadow.BlurRadius), shadow.SpreadRadius + Math.Max(0, shadow.BlurRadius));
        return shadowBounds.IsEmpty ? box : Rect.Union(box, shadowBounds);
    }

    /// <summary>计算全部盒阴影的视觉包围盒。</summary>
    public static Rect GetVisualBounds(Rect box, IReadOnlyList<BoxShadow> shadows)
    {
        var bounds = box;
        foreach (var shadow in shadows)
            bounds = Rect.Union(bounds, GetVisualBounds(box, shadow));
        return bounds;
    }

    /// <summary>绘制盒阴影。</summary>
    public static void Draw(IRenderContext context, Rect box, float cornerRadius, BoxShadow shadow)
        => Draw(context, box, cornerRadius == 0 ? null : new RoundedRectGeometry(box, cornerRadius, cornerRadius), shadow);

    private static void Draw(IRenderContext context, Rect box, RoundedRectGeometry? corners, BoxShadow shadow)
    {
        if (box.IsEmpty || shadow.Color.A == 0) return;
        var baseRect = box.Offset(shadow.OffsetX, shadow.OffsetY).Inflate(shadow.SpreadRadius, shadow.SpreadRadius);
        if (baseRect.IsEmpty) return;
        var blur = Math.Max(0, shadow.BlurRadius);
        if (blur <= 0)
        {
            DrawLayer(context, baseRect, corners, shadow.SpreadRadius, 0, shadow.Color);
            return;
        }

        var steps = Math.Clamp((int)MathF.Ceiling(blur), 2, 24);
        var accumulatedAlpha = 0f;
        for (var i = steps; i >= 0; i--)
        {
            var distanceRatio = i / (float)steps;
            var expansion = blur * distanceRatio;
            var coverage = 0.5f * MathF.Exp(-3f * distanceRatio * distanceRatio);
            var targetAlpha = shadow.Color.A / 255f * coverage;
            var layerAlpha = (targetAlpha - accumulatedAlpha) / Math.Max(0.0001f, 1f - accumulatedAlpha);
            var alpha = (byte)Math.Clamp(MathF.Round(layerAlpha * 255f), 0, 255);
            if (alpha == 0) continue;
            var rect = baseRect.Inflate(expansion, expansion);
            var color = Color.FromRgba(shadow.Color.R, shadow.Color.G, shadow.Color.B, alpha);
            DrawLayer(context, rect, corners, shadow.SpreadRadius, expansion, color);
            accumulatedAlpha += alpha / 255f * (1f - accumulatedAlpha);
        }
    }

    private static void DrawLayer(IRenderContext context, Rect rect, RoundedRectGeometry? corners,
        float spread, float expansion, Color color)
    {
        var brush = new SolidColorBrush(color);
        CornerRadius Expand(CornerRadius corner)
        {
            var x = corner.X + spread;
            var y = corner.Y + spread;
            if (corner.X <= 0 || corner.Y <= 0 || x <= 0 || y <= 0)
                return new CornerRadius(expansion);
            return new CornerRadius(x + expansion, y + expansion);
        }

        var tl = Expand(corners?.TopLeft ?? default);
        var tr = Expand(corners?.TopRight ?? default);
        var br = Expand(corners?.BottomRight ?? default);
        var bl = Expand(corners?.BottomLeft ?? default);
        // A negative spread can shrink a side past its remaining corner radii.
        var maxWidth = Math.Max(tl.X + tr.X, bl.X + br.X);
        var maxHeight = Math.Max(tl.Y + bl.Y, tr.Y + br.Y);
        var scale = Math.Min(maxWidth > rect.Width ? rect.Width / maxWidth : 1f,
            maxHeight > rect.Height ? rect.Height / maxHeight : 1f);
        if (scale < 1f)
        {
            CornerRadius Scale(CornerRadius c) => new(c.X * scale, c.Y * scale);
            tl = Scale(tl);
            tr = Scale(tr);
            br = Scale(br);
            bl = Scale(bl);
        }

        if (tl == default && tr == default && br == default && bl == default)
        {
            context.FillRect(rect, brush);
            return;
        }
        var shape = new RoundedRectGeometry(rect, tl, tr, br, bl);
        if (shape.IsUniform) context.FillGeometry(shape, brush);
        else context.FillPath(shape.ToPath(), brush);
    }

    /// <summary>按 CSS 堆叠顺序绘制全部盒阴影，列表首项位于最上层。</summary>
    public static void Draw(IRenderContext context, Rect box, float cornerRadius, IReadOnlyList<BoxShadow> shadows)
    {
        for (var i = shadows.Count - 1; i >= 0; i--)
            Draw(context, box, cornerRadius, shadows[i]);
    }

    public static void Draw(IRenderContext context, Rect box, RoundedRectGeometry? roundedGeometry, IReadOnlyList<BoxShadow> shadows)
    {
        for (var i = shadows.Count - 1; i >= 0; i--)
            Draw(context, box, roundedGeometry, shadows[i]);
    }
}
