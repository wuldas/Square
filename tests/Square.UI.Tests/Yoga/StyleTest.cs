// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/StyleTest.cpp

using Xunit;
using Square.Rendering.Yoga;

namespace Square.Rendering.Yoga.Tests;

public class StyleTest
{
    [Fact]
    public void Computed_padding_is_floored()
    {
        var style = new Style();
        style.SetPadding(Edge.All, StyleLength.Points(-1.0f));
        var paddingStart = style.ComputeInlineStartPadding(
            FlexDirection.Row, Direction.LTR, 0.0f);
        Assert.Equal(0.0f, paddingStart);
    }

    [Fact]
    public void Computed_border_is_floored()
    {
        var style = new Style();
        style.SetBorder(Edge.All, StyleLength.Points(-1.0f));
        var borderStart = style.ComputeInlineStartBorder(
            FlexDirection.Row, Direction.LTR);
        Assert.Equal(0.0f, borderStart);
    }

    [Fact]
    public void Computed_gap_is_floored()
    {
        var style = new Style();
        style.SetGap(Gutter.Column, StyleLength.Points(-1.0f));
        var gapBetweenColumns = style.ComputeGapForAxis(FlexDirection.Row, 0.0f);
        Assert.Equal(0.0f, gapBetweenColumns);
    }

    [Fact]
    public void Computed_margin_is_not_floored()
    {
        var style = new Style();
        style.SetMargin(Edge.All, StyleLength.Points(-1.0f));
        var marginStart = style.ComputeInlineStartMargin(
            FlexDirection.Row, Direction.LTR, 0.0f);
        Assert.Equal(-1.0f, marginStart);
    }
}
