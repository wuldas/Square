using System;

namespace Square.Rendering.Yoga
{
    internal enum YGBoxSizing
    {
        BorderBox,
        ContentBox
    }

    internal enum BoxSizing : byte
    {
        BorderBox = YGBoxSizing.BorderBox,
        ContentBox = YGBoxSizing.ContentBox,
    }

    internal static partial class YogaEnums
    {
        public static int OrdinalCount(BoxSizing type)
        {
            return 2;
        }

        public static BoxSizing ScopedEnum(YGBoxSizing unscoped)
        {
            return (BoxSizing)unscoped;
        }

        public static YGBoxSizing UnscopedEnum(BoxSizing scoped)
        {
            return (YGBoxSizing)scoped;
        }

        public static string ToString(BoxSizing e)
        {
            return UnscopedEnum(e).ToString();
        }
    }
}

