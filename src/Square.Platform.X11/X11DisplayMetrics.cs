using System.Globalization;
using Square.Graphics;

namespace Square.Platform.X11;

internal static class X11DisplayMetrics
{
    internal const double DefaultDpi = 96d;
    internal const double DefaultRefreshRate = 60d;

    internal static bool TryParseXftDpi(string? resources, out double dpi)
    {
        dpi = 0;
        if (string.IsNullOrWhiteSpace(resources)) return false;

        foreach (var line in resources.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0) continue;
            var name = line[..separator].Trim();
            if (!name.Equals("Xft.dpi", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Xft*dpi", StringComparison.OrdinalIgnoreCase))
                continue;

            if (double.TryParse(line[(separator + 1)..].Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed)
                && IsUsableDpi(parsed))
            {
                dpi = parsed;
                return true;
            }
        }

        return false;
    }

    internal static double CalculatePhysicalDpi(
        int pixelWidth, int pixelHeight, int millimeterWidth, int millimeterHeight)
    {
        var horizontal = millimeterWidth > 0 ? pixelWidth * 25.4 / millimeterWidth : double.NaN;
        var vertical = millimeterHeight > 0 ? pixelHeight * 25.4 / millimeterHeight : double.NaN;
        var horizontalValid = IsUsableDpi(horizontal);
        var verticalValid = IsUsableDpi(vertical);
        if (horizontalValid && verticalValid) return (horizontal + vertical) / 2d;
        if (horizontalValid) return horizontal;
        if (verticalValid) return vertical;
        return DefaultDpi;
    }

    internal static double ResolveDpi(
        string? resources, int pixelWidth, int pixelHeight, int millimeterWidth, int millimeterHeight)
        => TryParseXftDpi(resources, out var dpi)
            ? dpi
            : CalculatePhysicalDpi(pixelWidth, pixelHeight, millimeterWidth, millimeterHeight);

    internal static int SelectMonitor(Rect windowBounds, IReadOnlyList<X11MonitorMetrics> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0) return -1;

        var bestIndex = 0;
        var bestIntersection = IntersectionArea(windowBounds, monitors[0].Bounds);
        var bestDistance = DistanceSquared(windowBounds.Center, monitors[0].Bounds);
        for (var i = 1; i < monitors.Count; i++)
        {
            var intersection = IntersectionArea(windowBounds, monitors[i].Bounds);
            var distance = DistanceSquared(windowBounds.Center, monitors[i].Bounds);
            if (intersection > bestIntersection
                || (intersection == bestIntersection && intersection > 0 && monitors[i].IsPrimary && !monitors[bestIndex].IsPrimary)
                || (bestIntersection == 0 && intersection == 0 && distance < bestDistance)
                || (bestIntersection == 0 && intersection == 0 && distance == bestDistance
                    && monitors[i].IsPrimary && !monitors[bestIndex].IsPrimary))
            {
                bestIndex = i;
                bestIntersection = intersection;
                bestDistance = distance;
            }
        }

        return bestIndex;
    }

    internal static double ResolveMonitorDpi(string? resources, X11MonitorMetrics monitor)
        => ResolveDpi(resources, (int)monitor.Bounds.Width, (int)monitor.Bounds.Height,
            monitor.MillimeterWidth, monitor.MillimeterHeight);

    internal static double ResolveMonitorRefreshRate(X11MonitorMetrics monitor)
        => NormalizeRefreshRate(monitor.RefreshRate);

    internal static float DpiToScale(double dpi)
        => IsUsableDpi(dpi) ? (float)(dpi / DefaultDpi) : 1f;

    internal static double NormalizeRefreshRate(double refreshRate)
        => double.IsFinite(refreshRate) && refreshRate is >= 24d and <= 360d
            ? refreshRate
            : DefaultRefreshRate;

    internal static long FrameIntervalTicks(double refreshRate, long stopwatchFrequency)
    {
        if (stopwatchFrequency <= 0) throw new ArgumentOutOfRangeException(nameof(stopwatchFrequency));
        refreshRate = NormalizeRefreshRate(refreshRate);
        return Math.Max(1, (long)Math.Round(stopwatchFrequency / refreshRate));
    }

    internal static long NextFrameDeadline(long currentDeadline, long now, long interval)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval));
        if (currentDeadline <= 0) return checked(now + interval);
        if (currentDeadline > now) return currentDeadline;

        var elapsedIntervals = (now - currentDeadline) / interval + 1;
        return checked(currentDeadline + elapsedIntervals * interval);
    }

    private static bool IsUsableDpi(double dpi)
        => double.IsFinite(dpi) && dpi is >= 48d and <= 768d;

    private static double IntersectionArea(Rect first, Rect second)
    {
        var width = Math.Max(0d, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));
        var height = Math.Max(0d, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return width * height;
    }

    private static double DistanceSquared(Point point, Rect bounds)
    {
        var dx = point.X < bounds.Left ? bounds.Left - point.X : point.X > bounds.Right ? point.X - bounds.Right : 0d;
        var dy = point.Y < bounds.Top ? bounds.Top - point.Y : point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0d;
        return dx * dx + dy * dy;
    }
}

internal readonly record struct X11MonitorMetrics(
    Rect Bounds,
    int MillimeterWidth,
    int MillimeterHeight,
    double RefreshRate,
    bool IsPrimary = false);
