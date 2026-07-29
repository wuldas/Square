using System.Text;
using Square.Graphics;

namespace Square.Platform.X11;

internal static class X11InputPolicy
{
    internal const int MaxEventsPerPump = 128;
    internal const long PreferredStyle = X11Api.XIMPreeditPosition | X11Api.XIMStatusNothing;
    internal const long FallbackStyle = X11Api.XIMPreeditNothing | X11Api.XIMStatusNothing;

    internal static long SelectStyle(IEnumerable<long> supportedStyles)
    {
        ArgumentNullException.ThrowIfNull(supportedStyles);

        var fallbackSupported = false;
        foreach (var style in supportedStyles)
        {
            if (style == PreferredStyle) return PreferredStyle;
            fallbackSupported |= style == FallbackStyle;
        }

        return fallbackSupported ? FallbackStyle : 0;
    }

    internal static string? DecodeCommittedText(byte[] buffer, int byteCount, int lookupStatus)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (byteCount <= 0 || byteCount > buffer.Length) return null;
        if (lookupStatus is not X11Api.XLookupChars and not X11Api.XLookupBoth) return null;

        return Encoding.UTF8.GetString(buffer, 0, byteCount);
    }

    internal static bool HasDispatchableText(string? text)
        => !string.IsNullOrEmpty(text) && text.EnumerateRunes().Any(static rune => !Rune.IsControl(rune));

    internal static bool ShouldYieldEventPump(int processedEvents)
        => processedEvents >= MaxEventsPerPump;

    internal static X11CaretSpot ToClientPhysicalSpot(Rect clientLogicalRect, float dpiScale)
    {
        if (!float.IsFinite(dpiScale) || dpiScale <= 0f) dpiScale = 1f;

        return new X11CaretSpot(
            ToXPointCoordinate(clientLogicalRect.X * dpiScale),
            ToXPointCoordinate(clientLogicalRect.Bottom * dpiScale));
    }

    private static short ToXPointCoordinate(float value)
    {
        if (!float.IsFinite(value)) return 0;
        return (short)Math.Clamp((int)MathF.Round(value), short.MinValue, short.MaxValue);
    }
}

internal readonly record struct X11CaretSpot(short X, short Y);
