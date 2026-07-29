namespace Square.Controls;

internal readonly record struct VirtualizingStackRange(int FirstIndex, int LastIndex)
{
    public static readonly VirtualizingStackRange Empty = new(0, -1);
    public int Count => LastIndex < FirstIndex ? 0 : LastIndex - FirstIndex + 1;
}

internal static class VirtualizingStackRangeCalculator
{
    public static VirtualizingStackRange Calculate(
        int itemCount,
        float verticalOffset,
        float viewportHeight,
        float itemHeight,
        int overscanCount)
    {
        if (itemCount <= 0) return VirtualizingStackRange.Empty;
        itemHeight = NormalizeItemHeight(itemHeight);
        overscanCount = Math.Max(0, overscanCount);
        verticalOffset = Math.Max(0, float.IsFinite(verticalOffset) ? verticalOffset : 0);
        viewportHeight = Math.Max(itemHeight, float.IsFinite(viewportHeight) ? viewportHeight : itemHeight);

        var first = Math.Max(0, (int)MathF.Floor(verticalOffset / itemHeight) - overscanCount);
        var visibleLast = Math.Max(0, (int)MathF.Ceiling((verticalOffset + viewportHeight) / itemHeight) - 1);
        var last = Math.Min(itemCount - 1, visibleLast + overscanCount);
        return new VirtualizingStackRange(first, last);
    }

    public static float NormalizeItemHeight(float value) =>
        float.IsFinite(value) && value > 0 ? value : 28f;
}
