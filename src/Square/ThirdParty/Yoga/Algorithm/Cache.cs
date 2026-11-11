// This file is ported from C++ source code in the Yoga project.
// License: MIT

namespace Square.Rendering.Yoga
{
    internal static class Cache
    {
        private static bool SizeIsExactAndMatchesOldMeasuredSize(
            SizingMode sizeMode,
            float size,
            float lastComputedSize)
        {
            return sizeMode == SizingMode.StretchFit &&
                Comparison.InexactEquals(size, lastComputedSize);
        }

        private static bool OldSizeIsMaxContentAndStillFits(
            SizingMode sizeMode,
            float size,
            SizingMode lastSizeMode,
            float lastComputedSize)
        {
            return sizeMode == SizingMode.FitContent &&
                lastSizeMode == SizingMode.MaxContent &&
                (size >= lastComputedSize || Comparison.InexactEquals(size, lastComputedSize));
        }

        private static bool NewSizeIsStricterAndStillValid(
            SizingMode sizeMode,
            float size,
            SizingMode lastSizeMode,
            float lastSize,
            float lastComputedSize)
        {
            return lastSizeMode == SizingMode.FitContent &&
                sizeMode == SizingMode.FitContent &&
                Comparison.IsDefined(lastSize) &&
                Comparison.IsDefined(size) &&
                Comparison.IsDefined(lastComputedSize) &&
                lastSize > size &&
                (lastComputedSize <= size || Comparison.InexactEquals(size, lastComputedSize));
        }

        public static bool CanUseCachedMeasurement(
            SizingMode widthMode,
            float availableWidth,
            SizingMode heightMode,
            float availableHeight,
            SizingMode lastWidthMode,
            float lastAvailableWidth,
            SizingMode lastHeightMode,
            float lastAvailableHeight,
            float lastComputedWidth,
            float lastComputedHeight,
            float marginRow,
            float marginColumn,
            Config? config)
        {
            if ((Comparison.IsDefined(lastComputedHeight) && lastComputedHeight < 0) ||
                (Comparison.IsDefined(lastComputedWidth) && lastComputedWidth < 0))
            {
                return false;
            }

            float pointScaleFactor = config != null ? config.GetPointScaleFactor() : 0;

            bool useRoundedComparison = config != null && !Comparison.InexactEquals(pointScaleFactor, 0);
            float effectiveWidth = useRoundedComparison
                ? PixelGrid.RoundValueToPixelGrid(availableWidth, pointScaleFactor, false, false)
                : availableWidth;
            float effectiveHeight = useRoundedComparison
                ? PixelGrid.RoundValueToPixelGrid(availableHeight, pointScaleFactor, false, false)
                : availableHeight;
            float effectiveLastWidth = useRoundedComparison
                ? PixelGrid.RoundValueToPixelGrid(lastAvailableWidth, pointScaleFactor, false, false)
                : lastAvailableWidth;
            float effectiveLastHeight = useRoundedComparison
                ? PixelGrid.RoundValueToPixelGrid(lastAvailableHeight, pointScaleFactor, false, false)
                : lastAvailableHeight;

            bool hasSameWidthSpec = lastWidthMode == widthMode &&
                Comparison.InexactEquals(effectiveLastWidth, effectiveWidth);
            bool hasSameHeightSpec = lastHeightMode == heightMode &&
                Comparison.InexactEquals(effectiveLastHeight, effectiveHeight);

            bool widthIsCompatible =
                hasSameWidthSpec ||
                SizeIsExactAndMatchesOldMeasuredSize(
                    widthMode, availableWidth - marginRow, lastComputedWidth) ||
                OldSizeIsMaxContentAndStillFits(
                    widthMode,
                    availableWidth - marginRow,
                    lastWidthMode,
                    lastComputedWidth) ||
                NewSizeIsStricterAndStillValid(
                    widthMode,
                    availableWidth - marginRow,
                    lastWidthMode,
                    lastAvailableWidth,
                    lastComputedWidth);

            bool heightIsCompatible = hasSameHeightSpec ||
                SizeIsExactAndMatchesOldMeasuredSize(
                    heightMode,
                    availableHeight - marginColumn,
                    lastComputedHeight) ||
                OldSizeIsMaxContentAndStillFits(
                    heightMode,
                    availableHeight - marginColumn,
                    lastHeightMode,
                    lastComputedHeight) ||
                NewSizeIsStricterAndStillValid(
                    heightMode,
                    availableHeight - marginColumn,
                    lastHeightMode,
                    lastAvailableHeight,
                    lastComputedHeight);

            return widthIsCompatible && heightIsCompatible;
        }
    }
}

