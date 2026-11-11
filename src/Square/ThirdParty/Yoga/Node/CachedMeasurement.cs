namespace Square.Rendering.Yoga
{
    internal static class NumericComparison
    {
        public static bool IsUndefined(float value)
        {
            return float.IsNaN(value);
        }
    }

    internal struct CachedMeasurement : IEquatable<CachedMeasurement>
    {
        public float AvailableWidth;
        public float AvailableHeight;
        public SizingMode WidthSizingMode;
        public SizingMode HeightSizingMode;

        public float ComputedWidth;
        public float ComputedHeight;

        public CachedMeasurement()
        {
            AvailableWidth = -1;
            AvailableHeight = -1;
            WidthSizingMode = SizingMode.MaxContent;
            HeightSizingMode = SizingMode.MaxContent;
            ComputedWidth = -1;
            ComputedHeight = -1;
        }

        public bool Equals(CachedMeasurement measurement)
        {
            bool isEqual = WidthSizingMode == measurement.WidthSizingMode &&
                           HeightSizingMode == measurement.HeightSizingMode;

            if (!NumericComparison.IsUndefined(AvailableWidth) ||
                !NumericComparison.IsUndefined(measurement.AvailableWidth))
            {
                isEqual = isEqual && AvailableWidth == measurement.AvailableWidth;
            }

            if (!NumericComparison.IsUndefined(AvailableHeight) ||
                !NumericComparison.IsUndefined(measurement.AvailableHeight))
            {
                isEqual = isEqual && AvailableHeight == measurement.AvailableHeight;
            }

            if (!NumericComparison.IsUndefined(ComputedWidth) ||
                !NumericComparison.IsUndefined(measurement.ComputedWidth))
            {
                isEqual = isEqual && ComputedWidth == measurement.ComputedWidth;
            }

            if (!NumericComparison.IsUndefined(ComputedHeight) ||
                !NumericComparison.IsUndefined(measurement.ComputedHeight))
            {
                isEqual = isEqual && ComputedHeight == measurement.ComputedHeight;
            }

            return isEqual;
        }

        public static bool operator ==(CachedMeasurement left, CachedMeasurement right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CachedMeasurement left, CachedMeasurement right)
        {
            return !left.Equals(right);
        }

        public override bool Equals(object? obj)
        {
            return obj is CachedMeasurement measurement && Equals(measurement);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                AvailableWidth, AvailableHeight,
                WidthSizingMode, HeightSizingMode,
                ComputedWidth, ComputedHeight
            );
        }
    }
}

