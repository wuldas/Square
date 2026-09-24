using Square.Controls;
using Square.Graphics;

namespace Square.UI.Html;

/// <summary>
/// Paints disclosure markers, text decoration, gauges, and static disabled-content placeholders.
/// </summary>
internal static class HtmlElementPainting
{

    /// <summary>Disclosure triangle for a summary inside a details element.</summary>
    public static void PaintDisclosureMarker(IRenderContext ctx, HtmlElement summary)
    {
        bool open = false, inDetails = false;
        for (Element? current = summary.Parent; current != null; current = current.Parent)
            if (current is HtmlElement { TagName: "details" } details)
            {
                inDetails = true;
                open = details.HasAttribute("open");
                break;
            }
        if (!inDetails) return;
        var font = ControlDrawing.ResolveFont(summary, 16f);
        var x = summary.Geometry.X + font.Size * 0.35f;
        var y = summary.Geometry.Y + summary.Geometry.Height / 2f;
        var path = open
            ? PathGeometry.Create().MoveTo(new Point(x, y - 3f)).LineTo(new Point(x + 6f, y - 3f)).LineTo(new Point(x + 3f, y + 3f))
            : PathGeometry.Create().MoveTo(new Point(x, y - 4f)).LineTo(new Point(x + 5f, y)).LineTo(new Point(x, y + 4f));
        ctx.FillPath(path, new SolidColorBrush(Color.FromRgb(96, 96, 96)));
    }

    /// <summary>
    /// Underline (a, u, ins) or line-through (s, del) drawn under each laid-out fragment of the
    /// descendant text runs; runs without fragment geometry keep their own painting.
    /// </summary>
    public static void PaintRunDecoration(IRenderContext ctx, HtmlElement host, bool lineThrough)
    {
        foreach (var run in DescendantTextRuns(host))
        {
            if (!ElementLayoutStore.TryGet(run, out var data) || data.CssTextFragments == null) continue;
            var color = ControlDrawing.GetStyledColor(run, "color", Color.Black);
            foreach (var fragment in data.CssTextFragments)
            {
                var bounds = fragment.Bounds;
                if (bounds.Width <= 0) continue;
                var y = lineThrough ? bounds.Y + bounds.Height / 2f : bounds.Bottom - 1f;
                ctx.FillRect(new Rect(bounds.X, y, bounds.Width, 1f), new SolidColorBrush(color));
            }
        }
    }

    /// <summary>Typographic quotes drawn around the q element's laid-out content box.</summary>
    public static void PaintQuotes(IRenderContext ctx, HtmlElement host)
    {
        if (host.Geometry.Width <= 0 && host.Geometry.Height <= 0) return;
        var font = ControlDrawing.ResolveFont(host, 16f);
        var color = ControlDrawing.GetStyledColor(host, "color", Color.Black);
        ControlDrawing.DrawText(ctx, host, "\u201C", host.Geometry.Position, color, font.Size);
        const string closeQuote = "\u201D";
        var closeWidth = ControlDrawing.MeasureText(host, closeQuote, font.Size).Width;
        ControlDrawing.DrawText(ctx, host, closeQuote,
            new Point(host.Geometry.Right - closeWidth, host.Geometry.Y), color, font.Size);
    }

    /// <summary>
    /// Labeled static region for content Square does not decode or load: audio/video playback,
    /// and iframe/object/embed documents. <paramref name="dark"/> switches the media chrome look.
    /// </summary>
    public static void PaintUnavailableRegion(IRenderContext ctx, HtmlElement host, string message, bool dark)
    {
        var rect = host.Geometry;
        ctx.FillRect(rect, new SolidColorBrush(dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(242, 242, 242)));
        if (!dark) ctx.DrawRect(rect, Pen.FromColor(Color.FromRgb(200, 200, 200)));
        var color = dark ? Color.FromRgb(200, 200, 200) : Color.FromRgb(118, 118, 118);
        DrawCenteredText(ctx, host, message, rect, color, 13.3333f);
    }

    /// <summary>Broken/absent image box showing the alt text when no source is present.</summary>
    public static void PaintAltText(IRenderContext ctx, HtmlElement img)
    {
        var rect = img.Geometry;
        ctx.DrawRect(rect, Pen.FromColor(Color.FromRgb(192, 192, 192)));
        var alt = img.GetAttribute("alt");
        if (string.IsNullOrEmpty(alt)) return;
        ControlDrawing.DrawText(ctx, img, alt, new Point(rect.X + 4f, rect.Y + 2f),
            Color.FromRgb(118, 118, 118), 13.3333f);
    }

    /// <summary>Static progress gauge; without a value attribute the fill stays empty (no animation).</summary>
    public static void PaintProgress(IRenderContext ctx, HtmlElement host)
    {
        var rect = host.Geometry;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var track = new RoundedRectGeometry(rect, 2, 2);
        ctx.FillGeometry(track, new SolidColorBrush(Color.FromRgb(238, 238, 238)));
        ctx.DrawGeometry(track, Pen.FromColor(Color.FromRgb(180, 180, 180)));
        var fraction = NumberFraction(host.GetAttribute("value"), host.GetAttribute("max"), fallbackMax: 1d);
        if (fraction <= 0) return;
        var inner = rect.Inflate(-1, -1);
        var fill = new Rect(inner.X, inner.Y, Math.Max(0, inner.Width * (float)fraction), inner.Height);
        ctx.FillGeometry(new RoundedRectGeometry(fill, 1, 1), new SolidColorBrush(Color.FromRgb(0, 117, 255)));
    }

    /// <summary>Static bounded meter gauge colored by the optimum zone of the current value.</summary>
    public static void PaintMeter(IRenderContext ctx, HtmlElement host)
    {
        var rect = host.Geometry;
        if (rect.Width <= 0 || rect.Height <= 0) return;
        var min = ReadNumber(host.GetAttribute("min"), 0d);
        var max = ReadNumber(host.GetAttribute("max"), 1d);
        if (!(max > min)) max = min + 1d;
        var low = Math.Clamp(ReadNumber(host.GetAttribute("low"), min), min, max);
        var high = Math.Clamp(ReadNumber(host.GetAttribute("high"), max), min, max);
        if (high < low) high = low;
        var optimum = ReadNumber(host.GetAttribute("optimum"), (min + max) / 2d);
        var value = ReadNumber(host.GetAttribute("value"), min);

        var fill = Color.FromRgb(60, 160, 60);
        if (InBand(value, low, high) != InBand(optimum, low, high))
            fill = Math.Abs(InBand(value, low, high) - InBand(optimum, low, high)) == 1
                ? Color.FromRgb(230, 170, 40)
                : Color.FromRgb(210, 60, 60);

        var track = new RoundedRectGeometry(rect, 2, 2);
        ctx.FillGeometry(track, new SolidColorBrush(Color.FromRgb(238, 238, 238)));
        ctx.DrawGeometry(track, Pen.FromColor(Color.FromRgb(180, 180, 180)));
        var fraction = Math.Clamp((value - min) / (max - min), 0d, 1d);
        if (fraction <= 0) return;
        var inner = rect.Inflate(-1, -1);
        var bar = new Rect(inner.X, inner.Y, Math.Max(0, inner.Width * (float)fraction), inner.Height);
        ctx.FillGeometry(new RoundedRectGeometry(bar, 1, 1), new SolidColorBrush(fill));
    }

    private static int InBand(double value, double low, double high) =>
        value < low ? 0 : value <= high ? 1 : 2;

    /// <summary>Parses value/max pairs for the progress gauge; malformed inputs keep the fill empty.</summary>
    private static double NumberFraction(string? rawValue, string? rawMax, double fallbackMax)
    {
        if (rawValue == null || !double.TryParse(rawValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) return 0d;
        var max = ReadNumber(rawMax, fallbackMax);
        return max > 0 ? Math.Clamp(value / max, 0d, 1d) : 0d;
    }

    private static double ReadNumber(string? raw, double fallback) =>
        double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static void DrawCenteredText(IRenderContext ctx, Element host, string message, Rect rect,
        Color color, float fontSize)
    {
        var font = ControlDrawing.ResolveFont(host, fontSize);
        var size = ControlDrawing.MeasureText(host, message, font.Size);
        var x = Math.Max(rect.X + 2f, rect.X + (rect.Width - size.Width) / 2f);
        var y = rect.Y + (rect.Height - size.Height) / 2f;
        ControlDrawing.DrawText(ctx, host, message, new Point(x, y), color, font.Size);
    }

    private static IEnumerable<Square.Controls.Text> DescendantTextRuns(Element element)
    {
        foreach (var child in element.Children)
        {
            if (!child.IsVisible) continue;
            if (child is Square.Controls.Text run) yield return run;
            foreach (var nested in DescendantTextRuns(child)) yield return nested;
        }
    }
}
