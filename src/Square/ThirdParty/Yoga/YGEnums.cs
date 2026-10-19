// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGEnums.h, yoga/YGEnums.cpp

namespace Square.Rendering.Yoga
{
    // YG-prefixed enums for C-style public API compatibility.
    // These mirror the internal enums (Direction, Align, etc.) with the same ordinal values.

    internal enum YGAlign
    {
        Auto,
        FlexStart,
        Center,
        FlexEnd,
        Stretch,
        Baseline,
        SpaceBetween,
        SpaceAround,
        SpaceEvenly,
        Start,
        End
    }

    // YGBoxSizing is defined in enums\BoxSizing.cs

    internal enum YGDimension
    {
        Width,
        Height
    }

    internal enum YGDirection
    {
        Inherit,
        LTR,
        RTL
    }

    internal enum YGDisplay
    {
        Flex,
        None,
        Contents,
        Grid
    }

    internal enum YGEdge
    {
        Left,
        Top,
        Right,
        Bottom,
        Start,
        End,
        Horizontal,
        Vertical,
        All
    }

    // YGErrata is defined in enums\Errata.cs

    internal enum YGExperimentalFeature
    {
        WebFlexBasis,
        FixFlexBasisFitContent
    }

    internal enum YGFlexDirection
    {
        Column,
        ColumnReverse,
        Row,
        RowReverse
    }

    internal enum YGGridTrackType
    {
        Auto,
        Points,
        Percent,
        Fr,
        Minmax
    }

    internal enum YGGutter
    {
        Column,
        Row,
        All
    }

    internal enum YGJustify
    {
        Auto,
        FlexStart,
        Center,
        FlexEnd,
        SpaceBetween,
        SpaceAround,
        SpaceEvenly,
        Stretch,
        Start,
        End
    }

    internal enum YGLogLevel
    {
        Error,
        Warn,
        Info,
        Debug,
        Verbose,
        Fatal
    }

    // YGMeasureMode is defined in event\event.cs

    internal enum YGNodeType
    {
        Default,
        Text
    }

    internal enum YGOverflow
    {
        Visible,
        Hidden,
        Scroll
    }

    internal enum YGPositionType
    {
        Static,
        Relative,
        Absolute
    }

    internal enum YGUnit
    {
        Undefined,
        Point,
        Percent,
        Auto,
        MaxContent,
        FitContent,
        Stretch
    }

    internal enum YGWrap
    {
        NoWrap,
        Wrap,
        WrapReverse
    }

    /// <summary>
    /// Extension methods for fast string conversion and enum conversions between YG* and internal enums.
    /// </summary>
    internal static class YGEnumExtensions
    {
        // Conversion: YG* enum <-> internal enum (same ordinal values, safe to cast)

        public static Direction ToInternal(this YGDirection value) => (Direction)(byte)value;
        public static YGDirection ToYG(this Direction value) => (YGDirection)(byte)value;

        public static Align ToInternal(this YGAlign value) => (Align)(byte)value;
        public static YGAlign ToYG(this Align value) => (YGAlign)(byte)value;

        public static FlexDirection ToInternal(this YGFlexDirection value) => (FlexDirection)(byte)value;
        public static YGFlexDirection ToYG(this FlexDirection value) => (YGFlexDirection)(byte)value;

        public static Justify ToInternal(this YGJustify value) => (Justify)(byte)value;
        public static YGJustify ToYG(this Justify value) => (YGJustify)(byte)value;

        public static Wrap ToInternal(this YGWrap value) => (Wrap)(byte)value;
        public static YGWrap ToYG(this Wrap value) => (YGWrap)(byte)value;

        public static Overflow ToInternal(this YGOverflow value) => (Overflow)(byte)value;
        public static YGOverflow ToYG(this Overflow value) => (YGOverflow)(byte)value;

        public static Display ToInternal(this YGDisplay value) => (Display)(byte)value;
        public static YGDisplay ToYG(this Display value) => (YGDisplay)(byte)value;

        public static PositionType ToInternal(this YGPositionType value) => (PositionType)(byte)value;
        public static YGPositionType ToYG(this PositionType value) => (YGPositionType)(byte)value;

        public static BoxSizing ToInternal(this YGBoxSizing value) => (BoxSizing)(byte)value;
        public static YGBoxSizing ToYG(this BoxSizing value) => (YGBoxSizing)(byte)value;

        public static Edge ToInternal(this YGEdge value) => (Edge)(byte)value;
        public static YGEdge ToYG(this Edge value) => (YGEdge)(byte)value;

        public static Gutter ToInternal(this YGGutter value) => (Gutter)(byte)value;
        public static YGGutter ToYG(this Gutter value) => (YGGutter)(byte)value;

        public static Dimension ToInternal(this YGDimension value) => (Dimension)(byte)value;
        public static YGDimension ToYG(this Dimension value) => (YGDimension)(byte)value;

        public static Errata ToInternal(this YGErrata value) => (Errata)(int)value;
        public static YGErrata ToYG(this Errata value) => (YGErrata)(int)value;

        public static ExperimentalFeature ToInternal(this YGExperimentalFeature value) => (ExperimentalFeature)(byte)value;
        public static YGExperimentalFeature ToYG(this ExperimentalFeature value) => (YGExperimentalFeature)(byte)value;

        public static LogLevel ToInternal(this YGLogLevel value) => (LogLevel)(byte)value;
        public static YGLogLevel ToYG(this LogLevel value) => (YGLogLevel)(byte)value;

        public static MeasureMode ToInternal(this YGMeasureMode value) => (MeasureMode)(byte)value;
        public static YGMeasureMode ToYG(this MeasureMode value) => (YGMeasureMode)(byte)value;

        public static NodeType ToInternal(this YGNodeType value) => (NodeType)(byte)value;
        public static YGNodeType ToYG(this NodeType value) => (YGNodeType)(byte)value;

        public static Unit ToInternal(this YGUnit value) => (Unit)(byte)value;
        public static YGUnit ToYG(this Unit value) => (YGUnit)(byte)value;

        // Fast string conversion methods

        public static string ToStringFast(this YGAlign value)
        {
            return value switch
            {
                YGAlign.Auto => "auto",
                YGAlign.FlexStart => "flex-start",
                YGAlign.Center => "center",
                YGAlign.FlexEnd => "flex-end",
                YGAlign.Stretch => "stretch",
                YGAlign.Baseline => "baseline",
                YGAlign.SpaceBetween => "space-between",
                YGAlign.SpaceAround => "space-around",
                YGAlign.SpaceEvenly => "space-evenly",
                YGAlign.Start => "start",
                YGAlign.End => "end",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGBoxSizing value)
        {
            return value switch
            {
                YGBoxSizing.BorderBox => "border-box",
                YGBoxSizing.ContentBox => "content-box",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGDimension value)
        {
            return value switch
            {
                YGDimension.Width => "width",
                YGDimension.Height => "height",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGDirection value)
        {
            return value switch
            {
                YGDirection.Inherit => "inherit",
                YGDirection.LTR => "ltr",
                YGDirection.RTL => "rtl",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGDisplay value)
        {
            return value switch
            {
                YGDisplay.Flex => "flex",
                YGDisplay.None => "none",
                YGDisplay.Contents => "contents",
                YGDisplay.Grid => "grid",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGEdge value)
        {
            return value switch
            {
                YGEdge.Left => "left",
                YGEdge.Top => "top",
                YGEdge.Right => "right",
                YGEdge.Bottom => "bottom",
                YGEdge.Start => "start",
                YGEdge.End => "end",
                YGEdge.Horizontal => "horizontal",
                YGEdge.Vertical => "vertical",
                YGEdge.All => "all",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGErrata value)
        {
            return value switch
            {
                YGErrata.None => "none",
                YGErrata.StretchFlexBasis => "stretch-flex-basis",
                YGErrata.AbsolutePositionWithoutInsetsExcludesPadding => "absolute-position-without-insets-excludes-padding",
                YGErrata.AbsolutePercentAgainstInnerSize => "absolute-percent-against-inner-size",
                YGErrata.All => "all",
                YGErrata.Classic => "classic",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGExperimentalFeature value)
        {
            return value switch
            {
                YGExperimentalFeature.WebFlexBasis => "web-flex-basis",
                YGExperimentalFeature.FixFlexBasisFitContent => "fix-flex-basis-fit-content",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGFlexDirection value)
        {
            return value switch
            {
                YGFlexDirection.Column => "column",
                YGFlexDirection.ColumnReverse => "column-reverse",
                YGFlexDirection.Row => "row",
                YGFlexDirection.RowReverse => "row-reverse",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGGridTrackType value)
        {
            return value switch
            {
                YGGridTrackType.Auto => "auto",
                YGGridTrackType.Points => "points",
                YGGridTrackType.Percent => "percent",
                YGGridTrackType.Fr => "fr",
                YGGridTrackType.Minmax => "minmax",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGGutter value)
        {
            return value switch
            {
                YGGutter.Column => "column",
                YGGutter.Row => "row",
                YGGutter.All => "all",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGJustify value)
        {
            return value switch
            {
                YGJustify.Auto => "auto",
                YGJustify.FlexStart => "flex-start",
                YGJustify.Center => "center",
                YGJustify.FlexEnd => "flex-end",
                YGJustify.SpaceBetween => "space-between",
                YGJustify.SpaceAround => "space-around",
                YGJustify.SpaceEvenly => "space-evenly",
                YGJustify.Stretch => "stretch",
                YGJustify.Start => "start",
                YGJustify.End => "end",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGLogLevel value)
        {
            return value switch
            {
                YGLogLevel.Error => "error",
                YGLogLevel.Warn => "warn",
                YGLogLevel.Info => "info",
                YGLogLevel.Debug => "debug",
                YGLogLevel.Verbose => "verbose",
                YGLogLevel.Fatal => "fatal",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGMeasureMode value)
        {
            return value switch
            {
                YGMeasureMode.Undefined => "undefined",
                YGMeasureMode.Exactly => "exactly",
                YGMeasureMode.AtMost => "at-most",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGNodeType value)
        {
            return value switch
            {
                YGNodeType.Default => "default",
                YGNodeType.Text => "text",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGOverflow value)
        {
            return value switch
            {
                YGOverflow.Visible => "visible",
                YGOverflow.Hidden => "hidden",
                YGOverflow.Scroll => "scroll",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGPositionType value)
        {
            return value switch
            {
                YGPositionType.Static => "static",
                YGPositionType.Relative => "relative",
                YGPositionType.Absolute => "absolute",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGUnit value)
        {
            return value switch
            {
                YGUnit.Undefined => "undefined",
                YGUnit.Point => "point",
                YGUnit.Percent => "percent",
                YGUnit.Auto => "auto",
                YGUnit.MaxContent => "max-content",
                YGUnit.FitContent => "fit-content",
                YGUnit.Stretch => "stretch",
                _ => "unknown"
            };
        }

        public static string ToStringFast(this YGWrap value)
        {
            return value switch
            {
                YGWrap.NoWrap => "no-wrap",
                YGWrap.Wrap => "wrap",
                YGWrap.WrapReverse => "wrap-reverse",
                _ => "unknown"
            };
        }
    }
}
