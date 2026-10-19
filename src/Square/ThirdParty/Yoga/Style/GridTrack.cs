using System;
using System.Collections.Generic;

namespace Square.Rendering.Yoga
{
    internal readonly struct GridTrackSize : IEquatable<GridTrackSize>
    {
        public StyleSizeLength MinSizingFunction { get; }
        public StyleSizeLength MaxSizingFunction { get; }
        public float BaseSize { get; }
        public float GrowthLimit { get; }
        public bool InfinitelyGrowable { get; }

        public GridTrackSize(
            StyleSizeLength minSizingFunction,
            StyleSizeLength maxSizingFunction,
            float baseSize = 0.0f,
            float growthLimit = 0.0f,
            bool infinitelyGrowable = false)
        {
            MinSizingFunction = minSizingFunction;
            MaxSizingFunction = maxSizingFunction;
            BaseSize = baseSize;
            GrowthLimit = growthLimit;
            InfinitelyGrowable = infinitelyGrowable;
        }

        public static GridTrackSize Auto() =>
            new GridTrackSize(
                StyleSizeLength.OfAuto(),
                StyleSizeLength.OfAuto());

        public static GridTrackSize Length(float points)
        {
            var len = StyleSizeLength.Points(points);
            return new GridTrackSize(len, len);
        }

        public static GridTrackSize Fr(float fraction) =>
            new GridTrackSize(
                StyleSizeLength.OfAuto(),
                StyleSizeLength.Stretch(fraction));

        public static GridTrackSize Percent(float percentage) =>
            new GridTrackSize(
                StyleSizeLength.Percent(percentage),
                StyleSizeLength.Percent(percentage));

        public static GridTrackSize MinMax(StyleSizeLength min, StyleSizeLength max) =>
            new GridTrackSize(min, max);

        public bool Equals(GridTrackSize other) =>
            MinSizingFunction.Equals(other.MinSizingFunction) &&
            MaxSizingFunction.Equals(other.MaxSizingFunction) &&
            BaseSize.Equals(other.BaseSize) &&
            GrowthLimit.Equals(other.GrowthLimit) &&
            InfinitelyGrowable == other.InfinitelyGrowable;

        public override bool Equals(object? obj) => obj is GridTrackSize other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(MinSizingFunction, MaxSizingFunction, BaseSize, GrowthLimit, InfinitelyGrowable);

        public static bool operator ==(GridTrackSize left, GridTrackSize right) => left.Equals(right);

        public static bool operator !=(GridTrackSize left, GridTrackSize right) => !left.Equals(right);
    }

    internal class GridTrackList : List<GridTrackSize>
    {
        public GridTrackList() { }

        public GridTrackList(IEnumerable<GridTrackSize> collection) : base(collection) { }

        public void Resize(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count < Count)
            {
                RemoveRange(count, Count - count);
            }
            else if (count > Count)
            {
                int itemsToAdd = count - Count;
                for (int i = 0; i < itemsToAdd; i++)
                {
                    Add(GridTrackSize.Auto());
                }
            }
        }
    }
}

