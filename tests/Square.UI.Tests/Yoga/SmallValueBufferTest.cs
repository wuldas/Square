// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/SmallValueBufferTest.cpp

using Xunit;
using Square.Rendering.Yoga;

namespace Square.Rendering.Yoga.Tests;

// Buffer size 4 for testing (matches C++ kBufferSize = 4)
internal struct BufferSize4 : IConstant
{
    public int Value => 4;
}

public class SmallValueBufferTest
{
    [Fact]
    public void Copy_assignment_with_overflow()
    {
        var handles = new ushort[5]; // kBufferSize + 1

        var buffer1 = new SmallValueBuffer<BufferSize4>();
        for (int i = 0; i < 5; i++)
        {
            handles[i] = buffer1.Push((uint)i);
        }

        var buffer2 = buffer1.Clone();
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal((uint)i, buffer2.Get32(handles[i]));
        }

        var handle = buffer1.Push(42u);
        Assert.Equal(42u, buffer1.Get32(handle));

        // buffer2 should not have the new value - accessing it should throw
        Assert.ThrowsAny<Exception>(() => buffer2.Get32(handle));
    }

    [Fact]
    public void Push_32()
    {
        uint magic = 88567114u;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle = buffer.Push(magic);
        Assert.Equal(magic, buffer.Get32(handle));
    }

    [Fact]
    public void Push_overflow()
    {
        uint magic1 = 88567114u;
        uint magic2 = 351012214u;
        uint magic3 = 146122128u;
        uint magic4 = 2171092154u;
        uint magic5 = 2269016953u;

        var buffer = new SmallValueBuffer<BufferSize4>();
        Assert.Equal(magic1, buffer.Get32(buffer.Push(magic1)));
        Assert.Equal(magic2, buffer.Get32(buffer.Push(magic2)));
        Assert.Equal(magic3, buffer.Get32(buffer.Push(magic3)));
        Assert.Equal(magic4, buffer.Get32(buffer.Push(magic4)));
        Assert.Equal(magic5, buffer.Get32(buffer.Push(magic5)));
    }

    [Fact]
    public void Push_64()
    {
        ulong magic = 118138934255546108UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle = buffer.Push(magic);
        Assert.Equal(magic, buffer.Get64(handle));
    }

    [Fact]
    public void Push_64_overflow()
    {
        ulong magic1 = 1401612388342512UL;
        ulong magic2 = 118712305386210UL;
        ulong magic3 = 752431801563359011UL;
        ulong magic4 = 118138934255546108UL;
        ulong magic5 = 237115443124116111UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        Assert.Equal(magic1, buffer.Get64(buffer.Push(magic1)));
        Assert.Equal(magic2, buffer.Get64(buffer.Push(magic2)));
        Assert.Equal(magic3, buffer.Get64(buffer.Push(magic3)));
        Assert.Equal(magic4, buffer.Get64(buffer.Push(magic4)));
        Assert.Equal(magic5, buffer.Get64(buffer.Push(magic5)));
    }

    [Fact]
    public void Push_64_after_32()
    {
        uint magic32 = 88567114u;
        ulong magic64 = 118712305386210UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle32 = buffer.Push(magic32);
        Assert.Equal(magic32, buffer.Get32(handle32));

        var handle64 = buffer.Push(magic64);
        Assert.Equal(magic64, buffer.Get64(handle64));
    }

    [Fact]
    public void Push_32_after_64()
    {
        uint magic32 = 88567114u;
        ulong magic64 = 118712305386210UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle64 = buffer.Push(magic64);
        Assert.Equal(magic64, buffer.Get64(handle64));

        var handle32 = buffer.Push(magic32);
        Assert.Equal(magic32, buffer.Get32(handle32));
    }

    [Fact]
    public void Replace_32_with_32()
    {
        uint magic1 = 88567114u;
        uint magic2 = 351012214u;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle = buffer.Push(magic1);

        Assert.Equal(magic2, buffer.Get32(buffer.Replace(handle, magic2)));
    }

    [Fact]
    public void Replace_32_with_64()
    {
        uint magic32 = 88567114u;
        ulong magic64 = 118712305386210UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle = buffer.Push(magic32);

        Assert.Equal(magic64, buffer.Get64(buffer.Replace(handle, magic64)));
    }

    [Fact]
    public void Replace_32_with_64_causes_overflow()
    {
        uint magic1 = 88567114u;
        uint magic2 = 351012214u;
        uint magic3 = 146122128u;
        uint magic4 = 2171092154u;

        ulong magic64 = 118712305386210UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle1 = buffer.Push(magic1);
        buffer.Push(magic2);
        buffer.Push(magic3);
        buffer.Push(magic4);

        Assert.Equal(magic64, buffer.Get64(buffer.Replace(handle1, magic64)));
    }

    [Fact]
    public void Replace_64_with_32()
    {
        uint magic32 = 88567114u;
        ulong magic64 = 118712305386210UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle = buffer.Push(magic64);

        Assert.Equal(magic32, buffer.Get32(buffer.Replace(handle, magic32)));
    }

    [Fact]
    public void Replace_64_with_64()
    {
        ulong magic1 = 1401612388342512UL;
        ulong magic2 = 118712305386210UL;

        var buffer = new SmallValueBuffer<BufferSize4>();
        var handle = buffer.Push(magic1);

        Assert.Equal(magic2, buffer.Get64(buffer.Replace(handle, magic2)));
    }
}
