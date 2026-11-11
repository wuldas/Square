// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/OrdinalsTest.cpp

using Xunit;
using Square.Rendering.Yoga;

namespace Square.Rendering.Yoga.Tests;

public class OrdinalsTest
{
    [Fact]
    public void Iteration()
    {
        var expectedEdges = new Queue<Edge>(new[]
        {
            Edge.Left,
            Edge.Top,
            Edge.Right,
            Edge.Bottom,
            Edge.Start,
            Edge.End,
            Edge.Horizontal,
            Edge.Vertical,
            Edge.All
        });

        foreach (var edge in Enum.GetValues<Edge>())
        {
            Assert.Equal(edge, expectedEdges.Dequeue());
        }

        Assert.Empty(expectedEdges);
    }
}
