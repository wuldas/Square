// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/StyleValuePoolTest.cpp

using Xunit;
using Square.Rendering.Yoga;

namespace Square.Rendering.Yoga.Tests;

public class StyleValuePoolTest
{
    [Fact]
    public void Undefined_at_init()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        Assert.True(handle.IsUndefined);
        Assert.False(handle.IsDefined);
        Assert.Equal(StyleLength.Undefined(), pool.GetLength(handle));
        Assert.Equal(FloatOptional.Undefined, pool.GetNumber(handle));
    }

    [Fact]
    public void Auto_at_init()
    {
        var pool = new StyleValuePool();
        var handle = StyleValueHandle.OfAuto();

        Assert.True(handle.IsAuto);
        Assert.Equal(StyleLength.OfAuto(), pool.GetLength(handle));
    }

    [Fact]
    public void Store_small_int_points()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Points(10));

        Assert.Equal(StyleLength.Points(10), pool.GetLength(handle));
    }

    [Fact]
    public void Store_small_negative_int_points()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Points(-10));

        Assert.Equal(StyleLength.Points(-10), pool.GetLength(handle));
    }

    [Fact]
    public void Store_small_int_percent()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Percent(10));

        Assert.Equal(StyleLength.Percent(10), pool.GetLength(handle));
    }

    [Fact]
    public void Store_large_int_percent()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Percent(262144));

        Assert.Equal(StyleLength.Percent(262144), pool.GetLength(handle));
    }

    [Fact]
    public void Store_large_int_after_small_int()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Percent(10));
        pool.Store(ref handle, StyleLength.Percent(262144));

        Assert.Equal(StyleLength.Percent(262144), pool.GetLength(handle));
    }

    [Fact]
    public void Store_small_int_after_large_int()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Percent(262144));
        pool.Store(ref handle, StyleLength.Percent(10));

        Assert.Equal(StyleLength.Percent(10), pool.GetLength(handle));
    }

    [Fact]
    public void Store_small_int_number()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, new FloatOptional(10.0f));

        Assert.Equal(new FloatOptional(10.0f), pool.GetNumber(handle));
    }

    [Fact]
    public void Store_undefined()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Undefined());

        Assert.True(handle.IsUndefined);
        Assert.False(handle.IsDefined);
        Assert.Equal(StyleLength.Undefined(), pool.GetLength(handle));
    }

    [Fact]
    public void Store_undefined_after_small_int()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Points(10));
        pool.Store(ref handle, StyleLength.Undefined());

        Assert.True(handle.IsUndefined);
        Assert.False(handle.IsDefined);
        Assert.Equal(StyleLength.Undefined(), pool.GetLength(handle));
    }

    [Fact]
    public void Store_undefined_after_large_int()
    {
        var pool = new StyleValuePool();
        var handle = new StyleValueHandle();

        pool.Store(ref handle, StyleLength.Points(262144));
        pool.Store(ref handle, StyleLength.Undefined());

        Assert.True(handle.IsUndefined);
        Assert.False(handle.IsDefined);
        Assert.Equal(StyleLength.Undefined(), pool.GetLength(handle));
    }

    [Fact]
    public void Store_keywords()
    {
        var pool = new StyleValuePool();
        var handleMaxContent = new StyleValueHandle();
        var handleFitContent = new StyleValueHandle();
        var handleStretch = new StyleValueHandle();

        pool.Store(ref handleMaxContent, StyleSizeLength.OfMaxContent());
        pool.Store(ref handleFitContent, StyleSizeLength.OfFitContent());
        pool.Store(ref handleStretch, StyleSizeLength.OfStretch());

        Assert.Equal(StyleSizeLength.OfMaxContent(), pool.GetSize(handleMaxContent));
        Assert.Equal(StyleSizeLength.OfFitContent(), pool.GetSize(handleFitContent));
        Assert.Equal(StyleSizeLength.OfStretch(), pool.GetSize(handleStretch));
    }
}
