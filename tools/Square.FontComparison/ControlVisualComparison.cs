using SkiaSharp;

namespace Square.FontComparison;

public sealed record ControlVisualThresholds(
    float MinimumMaskIoU,
    float MaximumMeanColorDelta,
    float MaximumHighDeltaRatio,
    float MaximumCornerMeanDelta = 40f,
    float MaximumCornerHighDeltaRatio = 0.45f)
{
    public static ControlVisualThresholds Button { get; } = new(0.72f, 18f, 0.13f);
    public static ControlVisualThresholds Input { get; } = new(0.65f, 18f, 0.13f, 60f, 0.80f);
    public static ControlVisualThresholds TextArea { get; } = new(0.60f, 18f, 0.13f, 60f, 0.80f);
    public static ControlVisualThresholds Select { get; } = new(0.60f, 26f, 0.15f, 100f, 0.80f);
    public static ControlVisualThresholds CheckBox { get; } = new(0.65f, 27f, 0.19f, 100f, 0.80f);
    public static ControlVisualThresholds Radio { get; } = new(0.65f, 27f, 0.19f, 100f, 0.80f);
}

public sealed class ControlVisualCaseResult
{
    public required string Id { get; init; }
    public required string Renderer { get; init; }
    public required bool Passed { get; init; }
    public required string ChromiumScreenshot { get; init; }
    public required string SquareScreenshot { get; init; }
    public required string DiffScreenshot { get; init; }
    public required List<ControlVisualRegionResult> Regions { get; init; }
}

public sealed class ControlVisualRegionResult
{
    public required string Name { get; init; }
    public required bool Passed { get; init; }
    public required float MaskIoU { get; init; }
    public required float MeanColorDelta { get; init; }
    public required float HighDeltaRatio { get; init; }
    public required bool MaskIsBlocking { get; init; }
    public required float MinimumMaskIoU { get; init; }
    public required float MaximumMeanColorDelta { get; init; }
    public required float MaximumHighDeltaRatio { get; init; }
    public required string[] Failures { get; init; }
}

public static class ControlVisualComparer
{
    public static ControlVisualCaseResult CompareButton(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect borderBox,
        ControlVisualThresholds thresholds,
        string id = "button",
        string renderer = "Square")
    {
        using var chromium = SKBitmap.Decode(chromiumPath)
            ?? throw new InvalidOperationException($"Unable to decode '{chromiumPath}'.");
        using var square = SKBitmap.Decode(squarePath)
            ?? throw new InvalidOperationException($"Unable to decode '{squarePath}'.");
        if (chromium.Width != square.Width || chromium.Height != square.Height)
            throw new InvalidOperationException($"Button screenshots have different sizes: {chromium.Width}x{chromium.Height} and {square.Width}x{square.Height}.");

        var box = PixelBox.Create(borderBox, chromium.Width, chromium.Height);
        var regions = CreateButtonRegions(box)
            .Select(region => CompareRegion(chromium, square, region, thresholds))
            .ToList();
        WriteDiff(chromium, square, diffPath, box);
        return new ControlVisualCaseResult
        {
            Id = id,
            Renderer = renderer,
            Passed = regions.All(region => region.Passed),
            ChromiumScreenshot = chromiumPath,
            SquareScreenshot = squarePath,
            DiffScreenshot = diffPath,
            Regions = regions
        };
    }

    public static ControlVisualCaseResult CompareInput(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect chromiumBorderBox,
        ControlRect squareBorderBox,
        ControlState state,
        ControlVisualThresholds thresholds,
        string id = "input",
        string renderer = "Square")
    {
        using var chromium = SKBitmap.Decode(chromiumPath)
            ?? throw new InvalidOperationException($"Unable to decode '{chromiumPath}'.");
        using var square = SKBitmap.Decode(squarePath)
            ?? throw new InvalidOperationException($"Unable to decode '{squarePath}'.");
        if (chromium.Width != square.Width || chromium.Height != square.Height)
            throw new InvalidOperationException($"Input screenshots have different sizes: {chromium.Width}x{chromium.Height} and {square.Width}x{square.Height}.");

        var box = PixelBox.Create(chromiumBorderBox, chromium.Width, chromium.Height);
        var squareBox = PixelBox.Create(squareBorderBox, square.Width, square.Height);
        if (box.Width != squareBox.Width || box.Height != squareBox.Height)
            throw new InvalidOperationException($"Input border boxes have different pixel sizes: {box.Width}x{box.Height} and {squareBox.Width}x{squareBox.Height}.");
        var squareOffsetX = squareBox.Left - box.Left;
        var squareOffsetY = squareBox.Top - box.Top;
        var regions = CreateInputRegions(box, state)
            .Select(region => CompareRegion(chromium, square, region, thresholds, squareOffsetX, squareOffsetY))
            .ToList();
        WriteDiff(chromium, square, diffPath, box, squareOffsetX, squareOffsetY);
        return new ControlVisualCaseResult
        {
            Id = id,
            Renderer = renderer,
            Passed = regions.All(region => region.Passed),
            ChromiumScreenshot = chromiumPath,
            SquareScreenshot = squarePath,
            DiffScreenshot = diffPath,
            Regions = regions
        };
    }

    public static ControlVisualCaseResult CompareTextArea(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect chromiumBorderBox,
        ControlRect squareBorderBox,
        ControlState state,
        ControlVisualThresholds thresholds,
        string id = "textarea",
        string renderer = "Square")
        => CompareTextArea(
            chromiumPath, squarePath, diffPath,
            chromiumBorderBox, squareBorderBox,
            Inset(chromiumBorderBox, 1), Inset(squareBorderBox, 1),
            state, thresholds, id, renderer);

    public static ControlVisualCaseResult CompareTextArea(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect chromiumBorderBox,
        ControlRect squareBorderBox,
        ControlRect chromiumContentBox,
        ControlRect squareContentBox,
        ControlState state,
        ControlVisualThresholds thresholds,
        string id = "textarea",
        string renderer = "Square")
    {
        using var chromium = SKBitmap.Decode(chromiumPath)
            ?? throw new InvalidOperationException($"Unable to decode '{chromiumPath}'.");
        using var square = SKBitmap.Decode(squarePath)
            ?? throw new InvalidOperationException($"Unable to decode '{squarePath}'.");
        if (chromium.Width != square.Width || chromium.Height != square.Height)
            throw new InvalidOperationException($"TextArea screenshots have different sizes: {chromium.Width}x{chromium.Height} and {square.Width}x{square.Height}.");

        var box = PixelBox.Create(chromiumBorderBox, chromium.Width, chromium.Height);
        var squareBox = PixelBox.Create(squareBorderBox, square.Width, square.Height);
        if (box.Width != squareBox.Width || box.Height != squareBox.Height)
            throw new InvalidOperationException($"TextArea border boxes have different pixel sizes: {box.Width}x{box.Height} and {squareBox.Width}x{squareBox.Height}.");
        var squareOffsetX = squareBox.Left - box.Left;
        var squareOffsetY = squareBox.Top - box.Top;
        var contentBox = PixelBox.Create(chromiumContentBox, chromium.Width, chromium.Height);
        var squareContent = PixelBox.Create(squareContentBox, square.Width, square.Height);
        if (contentBox.Width != squareContent.Width || contentBox.Height != squareContent.Height)
            throw new InvalidOperationException($"TextArea content boxes have different pixel sizes: {contentBox.Width}x{contentBox.Height} and {squareContent.Width}x{squareContent.Height}.");
        var contentOffsetX = squareContent.Left - contentBox.Left;
        var contentOffsetY = squareContent.Top - contentBox.Top;
        var regionDefinitions = CreateTextAreaRegions(box, contentBox, state);
        var regions = regionDefinitions
            .Select(region => region.Name is "corner" or "border"
                ? CompareRegion(chromium, square, region, thresholds, squareOffsetX, squareOffsetY)
                : CompareRegion(chromium, square, region, thresholds, contentOffsetX, contentOffsetY))
            .ToList();
        WriteDiff(
            chromium, square, diffPath, box, regionDefinitions,
            squareOffsetX, squareOffsetY, contentOffsetX, contentOffsetY);
        return new ControlVisualCaseResult
        {
            Id = id,
            Renderer = renderer,
            Passed = regions.All(region => region.Passed),
            ChromiumScreenshot = chromiumPath,
            SquareScreenshot = squarePath,
            DiffScreenshot = diffPath,
            Regions = regions
        };
    }

    public static ControlVisualCaseResult CompareSelect(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect chromiumBorderBox,
        ControlRect squareBorderBox,
        ControlVisualThresholds thresholds,
        string id = "select",
        string renderer = "Square")
    {
        using var chromium = SKBitmap.Decode(chromiumPath)
            ?? throw new InvalidOperationException($"Unable to decode '{chromiumPath}'.");
        using var square = SKBitmap.Decode(squarePath)
            ?? throw new InvalidOperationException($"Unable to decode '{squarePath}'.");
        if (chromium.Width != square.Width || chromium.Height != square.Height)
            throw new InvalidOperationException($"Select screenshots have different sizes: {chromium.Width}x{chromium.Height} and {square.Width}x{square.Height}.");

        var box = PixelBox.Create(chromiumBorderBox, chromium.Width, chromium.Height);
        var squareBox = PixelBox.Create(squareBorderBox, square.Width, square.Height);
        if (box.Width != squareBox.Width || box.Height != squareBox.Height)
            throw new InvalidOperationException($"Select border boxes have different pixel sizes: {box.Width}x{box.Height} and {squareBox.Width}x{squareBox.Height}.");
        var squareOffsetX = squareBox.Left - box.Left;
        var squareOffsetY = squareBox.Top - box.Top;
        var regions = CreateSelectRegions(box)
            .Select(region => CompareRegion(chromium, square, region, thresholds, squareOffsetX, squareOffsetY))
            .ToList();
        WriteDiff(chromium, square, diffPath, box, squareOffsetX, squareOffsetY);
        return new ControlVisualCaseResult
        {
            Id = id,
            Renderer = renderer,
            Passed = regions.All(region => region.Passed),
            ChromiumScreenshot = chromiumPath,
            SquareScreenshot = squarePath,
            DiffScreenshot = diffPath,
            Regions = regions
        };
    }

    public static ControlVisualCaseResult CompareCheckBox(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect chromiumBorderBox,
        ControlRect squareBorderBox,
        ControlVisualThresholds thresholds,
        string id = "checkbox",
        string renderer = "Square")
    {
        using var chromium = SKBitmap.Decode(chromiumPath)
            ?? throw new InvalidOperationException($"Unable to decode '{chromiumPath}'.");
        using var square = SKBitmap.Decode(squarePath)
            ?? throw new InvalidOperationException($"Unable to decode '{squarePath}'.");
        if (chromium.Width != square.Width || chromium.Height != square.Height)
            throw new InvalidOperationException($"CheckBox screenshots have different sizes: {chromium.Width}x{chromium.Height} and {square.Width}x{square.Height}.");

        var box = PixelBox.Create(chromiumBorderBox, chromium.Width, chromium.Height);
        var squareBox = PixelBox.Create(squareBorderBox, square.Width, square.Height);
        if (box.Width != squareBox.Width || box.Height != squareBox.Height)
            throw new InvalidOperationException($"CheckBox border boxes have different pixel sizes: {box.Width}x{box.Height} and {squareBox.Width}x{squareBox.Height}.");
        var squareOffsetX = squareBox.Left - box.Left;
        var squareOffsetY = squareBox.Top - box.Top;
        var regions = CreateCheckBoxRegions(box)
            .Select(region => CompareRegion(chromium, square, region, thresholds, squareOffsetX, squareOffsetY))
            .ToList();
        WriteDiff(chromium, square, diffPath, box, squareOffsetX, squareOffsetY);
        return new ControlVisualCaseResult
        {
            Id = id,
            Renderer = renderer,
            Passed = regions.All(region => region.Passed),
            ChromiumScreenshot = chromiumPath,
            SquareScreenshot = squarePath,
            DiffScreenshot = diffPath,
            Regions = regions
        };
    }

    public static ControlVisualCaseResult CompareRadio(
        string chromiumPath,
        string squarePath,
        string diffPath,
        ControlRect chromiumBorderBox,
        ControlRect squareBorderBox,
        ControlVisualThresholds thresholds,
        string id = "radio",
        string renderer = "Square")
    {
        using var chromium = SKBitmap.Decode(chromiumPath)
            ?? throw new InvalidOperationException($"Unable to decode '{chromiumPath}'.");
        using var square = SKBitmap.Decode(squarePath)
            ?? throw new InvalidOperationException($"Unable to decode '{squarePath}'.");
        if (chromium.Width != square.Width || chromium.Height != square.Height)
            throw new InvalidOperationException($"Radio screenshots have different sizes: {chromium.Width}x{chromium.Height} and {square.Width}x{square.Height}.");

        var box = PixelBox.Create(chromiumBorderBox, chromium.Width, chromium.Height);
        var squareBox = PixelBox.Create(squareBorderBox, square.Width, square.Height);
        if (box.Width != squareBox.Width || box.Height != squareBox.Height)
            throw new InvalidOperationException($"Radio border boxes have different pixel sizes: {box.Width}x{box.Height} and {squareBox.Width}x{squareBox.Height}.");
        var squareOffsetX = squareBox.Left - box.Left;
        var squareOffsetY = squareBox.Top - box.Top;
        var regions = CreateRadioRegions(box)
            .Select(region => CompareRegion(chromium, square, region, thresholds, squareOffsetX, squareOffsetY))
            .ToList();
        WriteDiff(chromium, square, diffPath, box, squareOffsetX, squareOffsetY);
        return new ControlVisualCaseResult
        {
            Id = id,
            Renderer = renderer,
            Passed = regions.All(region => region.Passed),
            ChromiumScreenshot = chromiumPath,
            SquareScreenshot = squarePath,
            DiffScreenshot = diffPath,
            Regions = regions
        };
    }

    private static IReadOnlyList<PixelRegion> CreateButtonRegions(PixelBox box)
    {
        var corner = Math.Clamp(Math.Min(box.Width, box.Height) / 4, 2, 5);
        const int border = 1;
        var textMargin = box.Width < 100 ? 8 : box.Width / 5;
        var textLeft = box.Left + textMargin;
        var textRight = box.Right - textMargin;
        var textTop = box.Top + box.Height / 4;
        var textBottom = box.Bottom - box.Height / 4;
        return
        [
            new("corner", (x, y) =>
                Inside(box, x, y) &&
                (x < box.Left + corner || x >= box.Right - corner) &&
                (y < box.Top + corner || y >= box.Bottom - corner)),
            new("border-top", (x, y) => Inside(box, x, y) && y < box.Top + border && x >= box.Left + corner && x < box.Right - corner),
            new("border-right", (x, y) => Inside(box, x, y) && x >= box.Right - border && y >= box.Top + corner && y < box.Bottom - corner),
            new("border-bottom", (x, y) => Inside(box, x, y) && y >= box.Bottom - border && x >= box.Left + corner && x < box.Right - corner),
            new("border-left", (x, y) => Inside(box, x, y) && x < box.Left + border && y >= box.Top + corner && y < box.Bottom - corner),
            new("text", (x, y) => x >= textLeft && x < textRight && y >= textTop && y < textBottom),
            new("background", (x, y) =>
                x >= box.Left + border && x < box.Right - border &&
                y >= box.Top + border && y < box.Bottom - border &&
                !(x >= textLeft && x < textRight && y >= textTop && y < textBottom))
        ];
    }

    private static IReadOnlyList<PixelRegion> CreateInputRegions(PixelBox box, ControlState state)
    {
        const int border = 2;
        const int corner = 2;
        var contentLeft = box.Left + border;
        var contentRight = box.Right - border;
        var textRight = contentLeft + Math.Max(1, (contentRight - contentLeft) / 2);
        var caretLeft = state == ControlState.Focus ? Math.Min(box.Left + 32, contentRight) : contentLeft;
        var caretRight = state == ControlState.Focus ? Math.Min(box.Left + 50, contentRight) : contentLeft;
        return
        [
            new("corner", (x, y) => Inside(box, x, y) &&
                (x < box.Left + corner || x >= box.Right - corner) &&
                (y < box.Top + corner || y >= box.Bottom - corner)),
            new("border", (x, y) => Inside(box, x, y) &&
                (x < box.Left + border || x >= box.Right - border || y < box.Top + border || y >= box.Bottom - border) &&
                !((x < box.Left + corner || x >= box.Right - corner) &&
                  (y < box.Top + corner || y >= box.Bottom - corner))),
            new("text", (x, y) => x >= contentLeft && x < textRight &&
                y >= box.Top + border && y < box.Bottom - border),
            new("caret", (x, y) => x >= caretLeft && x < caretRight &&
                y >= box.Top + border && y < box.Bottom - border),
            new("background", (x, y) => x >= textRight && x < contentRight &&
                y >= box.Top + border && y < box.Bottom - border)
        ];
    }

    private static IReadOnlyList<PixelRegion> CreateTextAreaRegions(PixelBox box, PixelBox contentBox, ControlState state)
    {
        const int border = 1;
        const int corner = 2;
        var contentLeft = contentBox.Left;
        var contentRight = contentBox.Right;
        var textRight = contentLeft + Math.Max(1, (contentRight - contentLeft) / 2);
        var middle = contentBox.Top + contentBox.Height / 2;
        var caretLeft = state == ControlState.Focus ? textRight + 1 : contentLeft;
        var caretRight = state == ControlState.Focus ? Math.Min(caretLeft + 5, contentRight) : contentLeft;
        var backgroundLeft = state == ControlState.Focus ? caretRight : textRight;
        return
        [
            new("corner", (x, y) => Inside(box, x, y) &&
                (x < box.Left + corner || x >= box.Right - corner) &&
                (y < box.Top + corner || y >= box.Bottom - corner)),
            new("border", (x, y) => Inside(box, x, y) &&
                (x < box.Left + border || x >= box.Right - border || y < box.Top + border || y >= box.Bottom - border) &&
                !((x < box.Left + corner || x >= box.Right - corner) &&
                  (y < box.Top + corner || y >= box.Bottom - corner))),
            new("text-line-1", (x, y) => x >= contentLeft && x < textRight && y >= contentBox.Top && y < middle),
            new("text-line-2", (x, y) => x >= contentLeft && x < textRight && y >= middle && y < contentBox.Bottom),
            new("caret", (x, y) => x >= caretLeft && x < caretRight && y >= contentBox.Top && y < contentBox.Bottom),
            new("background", (x, y) => x >= backgroundLeft && x < contentRight && y >= contentBox.Top && y < contentBox.Bottom)
        ];
    }

    private static IReadOnlyList<PixelRegion> CreateSelectRegions(PixelBox box)
    {
        const int border = 1;
        const int corner = 2;
        var contentLeft = box.Left + border;
        var contentRight = box.Right - border;
        var textRight = box.Width < 100
            ? Math.Max(contentLeft + 1, contentRight - 13)
            : contentLeft + Math.Max(1, (contentRight - contentLeft) / 2);
        var arrowLeft = Math.Max(textRight, contentRight - 9);
        return
        [
            new("corner", (x, y) => Inside(box, x, y) &&
                (x < box.Left + corner || x >= box.Right - corner) &&
                (y < box.Top + corner || y >= box.Bottom - corner)),
            new("border", (x, y) => Inside(box, x, y) &&
                (x < box.Left + border || x >= box.Right - border || y < box.Top + border || y >= box.Bottom - border) &&
                !((x < box.Left + corner || x >= box.Right - corner) &&
                  (y < box.Top + corner || y >= box.Bottom - corner))),
            new("text", (x, y) => x >= contentLeft && x < textRight &&
                y >= box.Top + border && y < box.Bottom - border),
            new("arrow", (x, y) => x >= arrowLeft && x < contentRight &&
                y >= box.Top + border && y < box.Bottom - border),
            new("background", (x, y) => x >= textRight && x < arrowLeft &&
                y >= box.Top + border && y < box.Bottom - border)
        ];
    }

    private static IReadOnlyList<PixelRegion> CreateCheckBoxRegions(PixelBox box)
    {
        var corner = Math.Clamp(Math.Min(box.Width, box.Height) / 4, 2, 4);
        const int border = 2;
        var checkInset = Math.Clamp(Math.Min(box.Width, box.Height) / 5, 3, 5);
        return
        [
            new("corner", (x, y) => Inside(box, x, y) &&
                (x < box.Left + corner || x >= box.Right - corner) &&
                (y < box.Top + corner || y >= box.Bottom - corner)),
            new("border", (x, y) => Inside(box, x, y) &&
                (x < box.Left + border || x >= box.Right - border || y < box.Top + border || y >= box.Bottom - border) &&
                !((x < box.Left + corner || x >= box.Right - corner) &&
                  (y < box.Top + corner || y >= box.Bottom - corner))),
            new("check", (x, y) => x >= box.Left + checkInset && x < box.Right - checkInset &&
                y >= box.Top + checkInset && y < box.Bottom - checkInset),
            new("background", (x, y) => Inside(box, x, y) &&
                x >= box.Left + border && x < box.Right - border &&
                y >= box.Top + border && y < box.Bottom - border &&
                !(x >= box.Left + checkInset && x < box.Right - checkInset &&
                  y >= box.Top + checkInset && y < box.Bottom - checkInset))
        ];
    }

    private static IReadOnlyList<PixelRegion> CreateRadioRegions(PixelBox box)
    {
        var radius = Math.Min(box.Width, box.Height) / 2f;
        var centerX = box.Left + box.Width / 2f;
        var centerY = box.Top + box.Height / 2f;
        var dotRadius = Math.Max(2f, radius * 0.55f);
        return
        [
            new("corner", (x, y) => Inside(box, x, y) &&
                DistanceSquared(x, y, centerX, centerY) > (radius - 1) * (radius - 1)),
            new("border", (x, y) => Inside(box, x, y) &&
                DistanceSquared(x, y, centerX, centerY) <= radius * radius &&
                DistanceSquared(x, y, centerX, centerY) > (radius - 2) * (radius - 2)),
            new("dot", (x, y) => DistanceSquared(x, y, centerX, centerY) <= (dotRadius - 1) * (dotRadius - 1)),
            new("background", (x, y) => Inside(box, x, y) &&
                DistanceSquared(x, y, centerX, centerY) > (dotRadius + 1) * (dotRadius + 1) &&
                DistanceSquared(x, y, centerX, centerY) <= (radius - 1.5f) * (radius - 1.5f))
        ];
    }

    private static float DistanceSquared(int x, int y, float centerX, float centerY)
    {
        var dx = x + 0.5f - centerX;
        var dy = y + 0.5f - centerY;
        return dx * dx + dy * dy;
    }

    private static ControlRect Inset(ControlRect rect, float inset) => new(
        rect.X + inset,
        rect.Y + inset,
        Math.Max(0, rect.Width - inset * 2),
        Math.Max(0, rect.Height - inset * 2));

    private static bool Inside(PixelBox box, int x, int y) =>
        x >= box.Left && x < box.Right && y >= box.Top && y < box.Bottom;

    private static ControlVisualRegionResult CompareRegion(
        SKBitmap chromium,
        SKBitmap square,
        PixelRegion region,
        ControlVisualThresholds thresholds,
        int squareOffsetX = 0,
        int squareOffsetY = 0)
    {
        long samples = 0;
        long highDelta = 0;
        double totalDelta = 0;
        var chromiumPixels = new bool[chromium.Width * chromium.Height];
        var squarePixels = new bool[square.Width * square.Height];
        long chromiumMask = 0;
        long squareMask = 0;
        var chromiumBackground = DominantColor(chromium, region);
        var squareRegion = new PixelRegion(region.Name,
            (x, y) => region.Contains(x - squareOffsetX, y - squareOffsetY));
        var squareBackground = DominantColor(square, squareRegion);
        for (var y = 0; y < chromium.Height; y++)
        {
            for (var x = 0; x < chromium.Width; x++)
            {
                if (!region.Contains(x, y)) continue;
                var expected = chromium.GetPixel(x, y);
                var actual = square.GetPixel(x + squareOffsetX, y + squareOffsetY);
                var delta = ColorDelta(expected, actual);
                totalDelta += delta;
                if (delta > 48) highDelta++;
                samples++;
                var expectedInk = ColorDelta(expected, chromiumBackground) > 12;
                var actualInk = ColorDelta(actual, squareBackground) > 12;
                var index = y * chromium.Width + x;
                chromiumPixels[index] = expectedInk;
                squarePixels[index] = actualInk;
                if (expectedInk) chromiumMask++;
                if (actualInk) squareMask++;
            }
        }

        var mean = samples == 0 ? 0 : (float)(totalDelta / samples);
        var highRatio = samples == 0 ? 0 : (float)highDelta / samples;
        long matchedChromium = 0;
        long matchedSquare = 0;
        for (var y = 0; y < chromium.Height; y++)
        {
            for (var x = 0; x < chromium.Width; x++)
            {
                if (!region.Contains(x, y)) continue;
                var index = y * chromium.Width + x;
                if (chromiumPixels[index] && HasNeighbor(squarePixels, chromium.Width, chromium.Height, region, x, y)) matchedChromium++;
                if (squarePixels[index] && HasNeighbor(chromiumPixels, chromium.Width, chromium.Height, region, x, y)) matchedSquare++;
            }
        }
        var maskSamples = chromiumMask + squareMask;
        var iou = maskSamples == 0 ? 1f : (float)(matchedChromium + matchedSquare) / maskSamples;
        var isText = region.Name == "text" || region.Name.StartsWith("text-line-", StringComparison.Ordinal);
        var maximumMeanDelta = region.Name switch
        {
            "caret" => 85f,
            "corner" => thresholds.MaximumCornerMeanDelta,
            _ when isText => 85f,
            _ => thresholds.MaximumMeanColorDelta
        };
        var maximumHighDeltaRatio = region.Name switch
        {
            "caret" => 0.50f,
            "corner" => thresholds.MaximumCornerHighDeltaRatio,
            _ when isText => 0.50f,
            _ => thresholds.MaximumHighDeltaRatio
        };
        var maskIsBlocking = isText || region.Name is "caret" or "corner" or "arrow" or "check";
        var failures = new List<string>();
        if (maskIsBlocking && iou < thresholds.MinimumMaskIoU)
            failures.Add($"mask IoU {iou:0.####} < {thresholds.MinimumMaskIoU:0.####}");
        if (mean > maximumMeanDelta) failures.Add($"mean color delta {mean:0.###} > {maximumMeanDelta:0.###}");
        if (highRatio > maximumHighDeltaRatio)
            failures.Add($"high delta ratio {highRatio:P2} > {maximumHighDeltaRatio:P2}");
        return new ControlVisualRegionResult
        {
            Name = region.Name,
            Passed = failures.Count == 0,
            MaskIoU = iou,
            MeanColorDelta = mean,
            HighDeltaRatio = highRatio,
            MaskIsBlocking = maskIsBlocking,
            MinimumMaskIoU = thresholds.MinimumMaskIoU,
            MaximumMeanColorDelta = maximumMeanDelta,
            MaximumHighDeltaRatio = maximumHighDeltaRatio,
            Failures = failures.ToArray()
        };
    }

    private static bool HasNeighbor(
        bool[] mask,
        int width,
        int height,
        PixelRegion region,
        int x,
        int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var candidateY = y + dy;
            if ((uint)candidateY >= (uint)height) continue;
            for (var dx = -1; dx <= 1; dx++)
            {
                var candidateX = x + dx;
                if ((uint)candidateX >= (uint)width || !region.Contains(candidateX, candidateY)) continue;
                if (mask[candidateY * width + candidateX]) return true;
            }
        }
        return false;
    }

    private static SKColor DominantColor(SKBitmap bitmap, PixelRegion region)
    {
        var counts = new Dictionary<uint, int>();
        uint dominant = 0;
        var dominantCount = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (!region.Contains(x, y)) continue;
                var color = bitmap.GetPixel(x, y);
                var key = (uint)(color.Red << 16 | color.Green << 8 | color.Blue);
                var count = counts.TryGetValue(key, out var current) ? current + 1 : 1;
                counts[key] = count;
                if (count <= dominantCount) continue;
                dominant = key;
                dominantCount = count;
            }
        }
        return new SKColor((byte)(dominant >> 16), (byte)(dominant >> 8), (byte)dominant);
    }

    private static void WriteDiff(
        SKBitmap chromium,
        SKBitmap square,
        string path,
        PixelBox box,
        int squareOffsetX = 0,
        int squareOffsetY = 0)
        => WriteDiff(
            chromium, square, path, box, null,
            squareOffsetX, squareOffsetY, squareOffsetX, squareOffsetY);

    private static void WriteDiff(
        SKBitmap chromium,
        SKBitmap square,
        string path,
        PixelBox box,
        IReadOnlyList<PixelRegion>? regions,
        int borderOffsetX,
        int borderOffsetY,
        int contentOffsetX,
        int contentOffsetY)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var diff = new SKBitmap(chromium.Width, chromium.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        diff.Erase(new SKColor(12, 15, 20));
        for (var y = box.Top; y < box.Bottom; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                var region = regions?.FirstOrDefault(candidate => candidate.Contains(x, y));
                var useContentOffset = region != null && region.Name is not ("corner" or "border");
                var offsetX = useContentOffset ? contentOffsetX : borderOffsetX;
                var offsetY = useContentOffset ? contentOffsetY : borderOffsetY;
                var delta = ColorDelta(chromium.GetPixel(x, y), square.GetPixel(x + offsetX, y + offsetY));
                diff.SetPixel(x, y, new SKColor((byte)Math.Clamp(delta * 3, 0, 255), 30, (byte)Math.Clamp(80 + delta, 0, 255)));
            }
        }
        using var image = SKImage.FromBitmap(diff);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static int ColorDelta(SKColor left, SKColor right)
    {
        var (leftRed, leftGreen, leftBlue) = CompositeOverWhite(left);
        var (rightRed, rightGreen, rightBlue) = CompositeOverWhite(right);
        return (Math.Abs(leftRed - rightRed) + Math.Abs(leftGreen - rightGreen) + Math.Abs(leftBlue - rightBlue) + 1) / 3;
    }

    private static (int Red, int Green, int Blue) CompositeOverWhite(SKColor color)
    {
        var inverseAlpha = 255 - color.Alpha;
        return (
            (color.Red * color.Alpha + 255 * inverseAlpha + 127) / 255,
            (color.Green * color.Alpha + 255 * inverseAlpha + 127) / 255,
            (color.Blue * color.Alpha + 255 * inverseAlpha + 127) / 255);
    }

    private sealed record PixelRegion(string Name, Func<int, int, bool> Contains);

    private readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;

        public static PixelBox Create(ControlRect rect, int imageWidth, int imageHeight)
        {
            var left = Math.Clamp((int)MathF.Round(rect.X, MidpointRounding.AwayFromZero), 0, imageWidth);
            var top = Math.Clamp((int)MathF.Round(rect.Y, MidpointRounding.AwayFromZero), 0, imageHeight);
            var right = Math.Clamp((int)MathF.Round(rect.X + rect.Width, MidpointRounding.AwayFromZero), left, imageWidth);
            var bottom = Math.Clamp((int)MathF.Round(rect.Y + rect.Height, MidpointRounding.AwayFromZero), top, imageHeight);
            if (right <= left || bottom <= top) throw new InvalidOperationException("Button visual comparison requires a non-empty border box.");
            return new PixelBox(left, top, right, bottom);
        }
    }
}
