// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/YGValueTest.cpp

using Xunit;
using Square.Rendering.Yoga;

namespace Square.Rendering.Yoga.Tests;

public class YGValueTest
{
    [Fact]
    public void Supports_equality()
    {
        Assert.Equal(new YGValue(12.5f, Unit.Percent), new YGValue(12.5f, Unit.Percent));
        Assert.NotEqual(new YGValue(12.5f, Unit.Percent), new YGValue(56.7f, Unit.Percent));
        Assert.NotEqual(new YGValue(12.5f, Unit.Percent), new YGValue(12.5f, Unit.Point));
        Assert.NotEqual(new YGValue(12.5f, Unit.Percent), new YGValue(12.5f, Unit.Auto));
        Assert.NotEqual(new YGValue(12.5f, Unit.Percent), new YGValue(12.5f, Unit.Undefined));

        // Undefined values compare equal regardless of value
        Assert.Equal(
            new YGValue(12.5f, Unit.Undefined),
            new YGValue(float.NaN, Unit.Undefined));

        // Auto values compare equal regardless of value
        Assert.Equal(new YGValue(0, Unit.Auto), new YGValue(-1, Unit.Auto));
    }
}
