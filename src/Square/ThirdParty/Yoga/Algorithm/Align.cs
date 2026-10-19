// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.

namespace Square.Rendering.Yoga
{
    internal static class AlignHelper
    {
        public static Align ResolveChildAlignment(Node node, Node child)
    {
        Align align = child.Style.AlignSelf == Align.Auto
            ? node.Style.AlignItems
            : child.Style.AlignSelf;

        if (node.Style.Display == Display.Flex && align == Align.Baseline &&
            node.Style.FlexDirection.IsColumn())
        {
            return Align.FlexStart;
        }

        return align;
    }

    public static Justify ResolveChildJustification(Node node, Node child)
    {
        return child.Style.JustifySelf == Justify.Auto
            ? node.Style.JustifyItems
            : child.Style.JustifySelf;
    }

    /// <summary>
    /// Fallback alignment to use on overflow
    /// https://www.w3.org/TR/css-align-3/#distribution-values
    /// </summary>
    public static Align FallbackAlignment(Align align)
    {
        switch (align)
        {
            // Fallback to flex-start
            case Align.SpaceBetween:
            case Align.Stretch:
                return Align.FlexStart;

            // Fallback to safe center. TODO (T208209388): This should be aligned to
            // Start instead of FlexStart (for row-reverse containers)
            case Align.SpaceAround:
            case Align.SpaceEvenly:
                return Align.FlexStart;
            default:
                return align;
        }
    }

    /// <summary>
    /// Fallback alignment to use on overflow
    /// https://www.w3.org/TR/css-align-3/#distribution-values
    /// </summary>
    public static Justify FallbackAlignment(Justify align)
    {
        switch (align)
        {
            // Fallback to flex-start
            case Justify.SpaceBetween:
                // TODO: Support `justify-content: stretch`
                // case Justify.Stretch:
                return Justify.FlexStart;

            // Fallback to safe center. TODO (T208209388): This should be aligned to
            // Start instead of FlexStart (for row-reverse containers)
            case Justify.SpaceAround:
            case Justify.SpaceEvenly:
                return Justify.FlexStart;
            default:
                return align;
        }
    }
}
}

