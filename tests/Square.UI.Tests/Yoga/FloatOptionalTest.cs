// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/FloatOptionalTest.cpp

// Intentional self-comparison tests (testing operator== with same variable, matching C++ gtest)
#pragma warning disable CS1718

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YogaValue;

namespace Square.Rendering.Yoga.Tests;

public class FloatOptionalTest
{
    private static readonly FloatOptional Empty = FloatOptional.Undefined;
    private static readonly FloatOptional Zero = FloatOptional.Zero;
    private static readonly FloatOptional One = new FloatOptional(1.0f);
    private static readonly FloatOptional Positive = new FloatOptional(1234.5f);
    private static readonly FloatOptional Negative = new FloatOptional(-9876.5f);

    [Fact]
    public void Value()
    {
        Assert.True(YGFloatIsUndefined(Empty.Unwrap()));
        Assert.Equal(0.0f, Zero.Unwrap());
        Assert.Equal(1.0f, One.Unwrap());
        Assert.Equal(1234.5f, Positive.Unwrap());
        Assert.Equal(-9876.5f, Negative.Unwrap());

        Assert.True(Empty.IsUndefined());
        Assert.False(Zero.IsUndefined());
        Assert.False(One.IsUndefined());
        Assert.False(Positive.IsUndefined());
        Assert.False(Negative.IsUndefined());
    }

    [Fact]
    public void Equality()
    {
        Assert.True(Empty == Empty);
        Assert.True(Empty == float.NaN);
        Assert.False(Empty == Zero);
        Assert.False(Empty == Negative);
        Assert.False(Empty == 12.3f);

        Assert.True(Zero == Zero);
        Assert.True(Zero == 0.0f);
        Assert.False(Zero == Positive);
        Assert.False(Zero == -5555.5f);

        Assert.True(One == One);
        Assert.True(One == 1.0f);
        Assert.False(One == Positive);

        Assert.True(Positive == Positive);
        Assert.True(Positive == Positive.Unwrap());
        Assert.False(Positive == One);

        Assert.True(Negative == Negative);
        Assert.True(Negative == Negative.Unwrap());
        Assert.False(Negative == Zero);
    }

    [Fact]
    public void Inequality()
    {
        Assert.False(Empty != Empty);
        Assert.False(Empty != float.NaN);
        Assert.True(Empty != Zero);
        Assert.True(Empty != Negative);
        Assert.True(Empty != 12.3f);

        Assert.False(Zero != Zero);
        Assert.False(Zero != 0.0f);
        Assert.True(Zero != Positive);
        Assert.True(Zero != -5555.5f);

        Assert.False(One != One);
        Assert.False(One != 1.0f);
        Assert.True(One != Positive);

        Assert.False(Positive != Positive);
        Assert.False(Positive != Positive.Unwrap());
        Assert.True(Positive != One);

        Assert.False(Negative != Negative);
        Assert.False(Negative != Negative.Unwrap());
        Assert.True(Negative != Zero);
    }

    [Fact]
    public void Greater_than_with_undefined()
    {
        Assert.False(Empty > Empty);
        Assert.False(Empty > Zero);
        Assert.False(Empty > One);
        Assert.False(Empty > Positive);
        Assert.False(Empty > Negative);
        Assert.False(Zero > Empty);
        Assert.False(One > Empty);
        Assert.False(Positive > Empty);
        Assert.False(Negative > Empty);
    }

    [Fact]
    public void Greater_than()
    {
        Assert.True(Zero > Negative);
        Assert.False(Zero > Zero);
        Assert.False(Zero > Positive);
        Assert.False(Zero > One);

        Assert.True(One > Negative);
        Assert.True(One > Zero);
        Assert.False(One > Positive);

        Assert.True(Negative > new FloatOptional(float.NegativeInfinity));
    }

    [Fact]
    public void Less_than_with_undefined()
    {
        Assert.False(Empty < Empty);
        Assert.False(Zero < Empty);
        Assert.False(One < Empty);
        Assert.False(Positive < Empty);
        Assert.False(Negative < Empty);
        Assert.False(Empty < Zero);
        Assert.False(Empty < One);
        Assert.False(Empty < Positive);
        Assert.False(Empty < Negative);
    }

    [Fact]
    public void Less_than()
    {
        Assert.True(Negative < Zero);
        Assert.False(Zero < Zero);
        Assert.False(Positive < Zero);
        Assert.False(One < Zero);

        Assert.True(Negative < One);
        Assert.True(Zero < One);
        Assert.False(Positive < One);

        Assert.True(new FloatOptional(float.NegativeInfinity) < Negative);
    }

    [Fact]
    public void Greater_than_equals_with_undefined()
    {
        Assert.True(Empty >= Empty);
        Assert.False(Empty >= Zero);
        Assert.False(Empty >= One);
        Assert.False(Empty >= Positive);
        Assert.False(Empty >= Negative);
        Assert.False(Zero >= Empty);
        Assert.False(One >= Empty);
        Assert.False(Positive >= Empty);
        Assert.False(Negative >= Empty);
    }

    [Fact]
    public void Greater_than_equals()
    {
        Assert.True(Zero >= Negative);
        Assert.True(Zero >= Zero);
        Assert.False(Zero >= Positive);
        Assert.False(Zero >= One);

        Assert.True(One >= Negative);
        Assert.True(One >= Zero);
        Assert.False(One >= Positive);

        Assert.True(Negative >= new FloatOptional(float.NegativeInfinity));
    }

    [Fact]
    public void Less_than_equals_with_undefined()
    {
        Assert.True(Empty <= Empty);
        Assert.False(Zero <= Empty);
        Assert.False(One <= Empty);
        Assert.False(Positive <= Empty);
        Assert.False(Negative <= Empty);
        Assert.False(Empty <= Zero);
        Assert.False(Empty <= One);
        Assert.False(Empty <= Positive);
        Assert.False(Empty <= Negative);
    }

    [Fact]
    public void Less_than_equals()
    {
        Assert.True(Negative <= Zero);
        Assert.True(Zero <= Zero);
        Assert.False(Positive <= Zero);
        Assert.False(One <= Zero);

        Assert.True(Negative <= One);
        Assert.True(Zero <= One);
        Assert.False(Positive <= One);

        Assert.True(new FloatOptional(float.NegativeInfinity) <= Negative);
    }

    [Fact]
    public void Addition()
    {
        var n = Negative.Unwrap();
        var p = Positive.Unwrap();

        Assert.Equal(One, Zero + One);
        Assert.Equal(new FloatOptional(n + p), Negative + Positive);
        Assert.Equal(Empty, Empty + Zero);
        Assert.Equal(Empty, Empty + Empty);
        Assert.Equal(Empty, Negative + Empty);
    }

    [Fact]
    public void MaxOrDefined()
    {
        Assert.Equal(Empty, FloatOptional.MaxOrDefined(Empty, Empty));
        Assert.Equal(Positive, FloatOptional.MaxOrDefined(Empty, Positive));
        Assert.Equal(Negative, FloatOptional.MaxOrDefined(Negative, Empty));
        Assert.Equal(Negative, FloatOptional.MaxOrDefined(Negative, new FloatOptional(float.NegativeInfinity)));
        Assert.Equal(
            new FloatOptional(1.125f),
            FloatOptional.MaxOrDefined(new FloatOptional(1.0f), new FloatOptional(1.125f)));
    }

    [Fact]
    public void Unwrap()
    {
        Assert.True(YGFloatIsUndefined(Empty.Unwrap()));
        Assert.Equal(0.0f, Zero.Unwrap());
        Assert.Equal(123456.78f, new FloatOptional(123456.78f).Unwrap());
    }
}
