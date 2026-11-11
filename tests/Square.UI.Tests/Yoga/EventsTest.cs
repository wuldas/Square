// Copyright (c) Meta Platforms, Inc. and affiliates.
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
// Ported from yoga/tests/EventsTest.cpp

using Xunit;
using Square.Rendering.Yoga;
using static Square.Rendering.Yoga.YGNodeAPI;
using static Square.Rendering.Yoga.YGNodeStyleAPI;
using static Square.Rendering.Yoga.YGNodeLayoutAPI;
using static Square.Rendering.Yoga.YGConfigAPI;

namespace Square.Rendering.Yoga.Tests;

public class EventsTest : IDisposable
{
    private sealed class EventRecord
    {
        public Node? Node { get; }
        public EventType Type { get; }
        public Event.Data Data { get; }

        /// <summary>
        /// For LayoutPassEnd events, a snapshot copy of the LayoutData at the
        /// time the event was received (mirrors the C++ TypedEventTestData copy).
        /// </summary>
        public LayoutData? LayoutDataCopy { get; init; }

        public EventRecord(Node? node, EventType type, Event.Data data)
        {
            Node = node;
            Type = type;
            Data = data;
        }
    }

    private readonly List<EventRecord> _events = new();

    public EventsTest()
    {
        Event.Subscribe(Listen);
    }

    void IDisposable.Dispose()
    {
        Event.Reset();
        _events.Clear();
    }

    private void Listen(Node? node, EventType type, Event.Data data)
    {
        LayoutData? layoutDataCopy = null;
        if (type == EventType.LayoutPassEnd)
        {
            layoutDataCopy = data.GetData<Event.LayoutPassEndData>()?.LayoutData;
        }

        _events.Add(new EventRecord(node, type, data)
        {
            LayoutDataCopy = layoutDataCopy
        });
    }

    private EventRecord LastEvent() => _events[_events.Count - 1];

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, new_node_has_event)
    // -----------------------------------------------------------------------
    [Fact]
    public void New_node_has_event()
    {
        var c = YGConfigGetDefault();
        var n = YGNodeNew();

        Assert.Same(n, LastEvent().Node);
        Assert.Equal(EventType.NodeAllocation, LastEvent().Type);
        Assert.Same(c, LastEvent().Data.GetData<Event.NodeAllocationData>()?.Config);

        YGNodeFree(n);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, new_node_with_config_event)
    // -----------------------------------------------------------------------
    [Fact]
    public void New_node_with_config_event()
    {
        var c = YGConfigNew();
        var n = YGNodeNewWithConfig(c);

        Assert.Same(n, LastEvent().Node);
        Assert.Equal(EventType.NodeAllocation, LastEvent().Type);
        Assert.Same(c, LastEvent().Data.GetData<Event.NodeAllocationData>()?.Config);

        YGNodeFree(n);
        YGConfigFree(c);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, clone_node_event)
    // -----------------------------------------------------------------------
    [Fact]
    public void Clone_node_event()
    {
        var c = YGConfigNew();
        var n = YGNodeNewWithConfig(c);
        var clone = YGNodeClone(n);

        Assert.Same(clone, LastEvent().Node);
        Assert.Equal(EventType.NodeAllocation, LastEvent().Type);
        Assert.Same(c, LastEvent().Data.GetData<Event.NodeAllocationData>()?.Config);

        YGNodeFree(n);
        YGNodeFree(clone);
        YGConfigFree(c);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, free_node_event)
    // -----------------------------------------------------------------------
    [Fact]
    public void Free_node_event()
    {
        var c = YGConfigNew();
        var n = YGNodeNewWithConfig(c);
        YGNodeFree(n);

        Assert.Same(n, LastEvent().Node);
        Assert.Equal(EventType.NodeDeallocation, LastEvent().Type);
        Assert.Same(c, LastEvent().Data.GetData<Event.NodeDeallocationData>()?.Config);

        YGConfigFree(c);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, layout_events)
    // -----------------------------------------------------------------------
    [Fact]
    public void Layout_events()
    {
        var root = YGNodeNew();
        var child = YGNodeNew();
        YGNodeInsertChild(root, child, 0);

        YGNodeCalculateLayout(root, 123, 456, YGDirection.LTR);

        Assert.Same(root, _events[2].Node);
        Assert.Equal(EventType.LayoutPassStart, _events[2].Type);

        Assert.Same(child, _events[3].Node);
        Assert.Equal(EventType.NodeLayout, _events[3].Type);

        Assert.Same(child, _events[4].Node);
        Assert.Equal(EventType.NodeLayout, _events[4].Type);

        Assert.Same(child, _events[5].Node);
        Assert.Equal(EventType.NodeLayout, _events[5].Type);

        Assert.Same(root, _events[6].Node);
        Assert.Equal(EventType.NodeLayout, _events[6].Type);

        Assert.Same(root, _events[7].Node);
        Assert.Equal(EventType.LayoutPassEnd, _events[7].Type);

        YGNodeFreeRecursive(root);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, layout_events_single_node)
    // -----------------------------------------------------------------------
    [Fact]
    public void Layout_events_single_node()
    {
        var root = YGNodeNew();
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Same(root, _events[1].Node);
        Assert.Equal(EventType.LayoutPassStart, _events[1].Type);

        Assert.Same(root, _events[2].Node);
        Assert.Equal(EventType.NodeLayout, _events[2].Type);

        Assert.Same(root, _events[3].Node);
        Assert.Equal(EventType.LayoutPassEnd, _events[3].Type);

        var layoutData = _events[3].LayoutDataCopy!.Value;

        Assert.Equal(1, layoutData.Layouts);
        Assert.Equal(0, layoutData.Measures);
        Assert.Equal(1u, layoutData.MaxMeasureCache);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, layout_events_counts_multi_node_layout)
    // -----------------------------------------------------------------------
    [Fact]
    public void Layout_events_counts_multi_node_layout()
    {
        var root = YGNodeNew();
        var childA = YGNodeNew();
        YGNodeInsertChild(root, childA, 0);
        var childB = YGNodeNew();
        YGNodeInsertChild(root, childB, 1);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Same(root, _events[3].Node);
        Assert.Equal(EventType.LayoutPassStart, _events[3].Type);

        Assert.Same(root, _events[11].Node);
        Assert.Equal(EventType.LayoutPassEnd, _events[11].Type);

        var layoutData = _events[11].LayoutDataCopy!.Value;

        Assert.Equal(3, layoutData.Layouts);
        Assert.Equal(4, layoutData.Measures);
        Assert.Equal(3u, layoutData.MaxMeasureCache);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, layout_events_counts_cache_hits_single_node_layout)
    // -----------------------------------------------------------------------
    [Fact]
    public void Layout_events_counts_cache_hits_single_node_layout()
    {
        var root = YGNodeNew();

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);
        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Same(root, _events[4].Node);
        Assert.Equal(EventType.LayoutPassStart, _events[4].Type);

        Assert.Same(root, _events[6].Node);
        Assert.Equal(EventType.LayoutPassEnd, _events[6].Type);

        var layoutData = _events[6].LayoutDataCopy!.Value;

        Assert.Equal(0, layoutData.Layouts);
        Assert.Equal(0, layoutData.Measures);
        Assert.Equal(1, layoutData.CachedLayouts);
        Assert.Equal(0, layoutData.CachedMeasures);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, layout_events_counts_cache_hits_multi_node_layout)
    // -----------------------------------------------------------------------
    [Fact]
    public void Layout_events_counts_cache_hits_multi_node_layout()
    {
        var root = YGNodeNew();
        var childA = YGNodeNew();
        YGNodeInsertChild(root, childA, 0);
        var childB = YGNodeNew();
        YGNodeInsertChild(root, childB, 1);

        YGNodeCalculateLayout(root, 987, 654, YGDirection.LTR);
        YGNodeCalculateLayout(root, 123, 456, YGDirection.LTR);

        YGNodeCalculateLayout(root, 987, 654, YGDirection.LTR);

        Assert.Same(root, LastEvent().Node);
        Assert.Equal(EventType.LayoutPassEnd, LastEvent().Type);

        var layoutData = LastEvent().LayoutDataCopy!.Value;

        Assert.Equal(3, layoutData.Layouts);
        Assert.Equal(0, layoutData.Measures);
        Assert.Equal(5u, layoutData.MaxMeasureCache);
        Assert.Equal(0, layoutData.CachedLayouts);
        Assert.Equal(4, layoutData.CachedMeasures);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, layout_events_has_max_measure_cache)
    // -----------------------------------------------------------------------
    [Fact]
    public void Layout_events_has_max_measure_cache()
    {
        var root = YGNodeNew();
        var a = YGNodeNew();
        YGNodeInsertChild(root, a, 0);
        var b = YGNodeNew();
        YGNodeInsertChild(root, b, 1);
        YGNodeStyleSetFlexBasis(a, 10.0f);

        foreach (var s in new[] { 20.0f, 30.0f, 40.0f })
        {
            YGNodeCalculateLayout(root, s, s, YGDirection.LTR);
        }

        Assert.Same(root, LastEvent().Node);
        Assert.Equal(EventType.LayoutPassEnd, LastEvent().Type);

        var layoutData = LastEvent().LayoutDataCopy!.Value;

        Assert.Equal(3, layoutData.Layouts);
        Assert.Equal(3, layoutData.Measures);
        Assert.Equal(7u, layoutData.MaxMeasureCache);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, measure_functions_get_wrapped)
    // -----------------------------------------------------------------------
    [Fact]
    public void Measure_functions_get_wrapped()
    {
        var root = YGNodeNew();
        YGNodeSetMeasureFunc(root,
            (Node node, float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
            {
                return new YGSize();
            });

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Same(root, _events[2].Node);
        Assert.Equal(EventType.MeasureCallbackStart, _events[2].Type);

        Assert.Same(root, _events[_events.Count - 1].Node);
        Assert.Equal(EventType.LayoutPassEnd, _events[_events.Count - 1].Type);
    }

    // -----------------------------------------------------------------------
    // TEST_F(EventTest, baseline_functions_get_wrapped)
    // -----------------------------------------------------------------------
    [Fact]
    public void Baseline_functions_get_wrapped()
    {
        var root = YGNodeNew();
        var child = YGNodeNew();
        YGNodeInsertChild(root, child, 0);

        YGNodeSetBaselineFunc(child,
            (Node node, float width, float height) => 0.0f);
        YGNodeStyleSetFlexDirection(root, YGFlexDirection.Row);
        YGNodeStyleSetAlignItems(root, YGAlign.Baseline);

        YGNodeCalculateLayout(root, float.NaN, float.NaN, YGDirection.LTR);

        Assert.Same(child, _events[5].Node);
        Assert.Equal(EventType.NodeBaselineStart, _events[5].Type);

        Assert.Same(root, _events[_events.Count - 1].Node);
        Assert.Equal(EventType.LayoutPassEnd, _events[_events.Count - 1].Type);
    }

    // -----------------------------------------------------------------------
    // Real node event delivery across zero -> subscribe -> unsubscribe/reset
    // -> resubscribe transitions. While nobody is subscribed, node
    // allocation, layout and deallocation must produce no events; delivery
    // must resume after each resubscription.
    // -----------------------------------------------------------------------
    [Fact]
    public void Node_events_follow_subscribe_transitions()
    {
        // Subscribed via the test constructor: allocation is delivered.
        var first = YGNodeNew();
        Assert.Equal(EventType.NodeAllocation, LastEvent().Type);
        Assert.Same(first, LastEvent().Node);
        Assert.Single(_events);

        // Unsubscribe: no events for subsequent node lifecycle or layout.
        Event.Unsubscribe(Listen);
        var silent = YGNodeNew();
        YGNodeCalculateLayout(silent, 100.0f, 100.0f, YGDirection.LTR);
        YGNodeFree(silent);
        Assert.Single(_events);
        Assert.Same(first, LastEvent().Node);

        // Resubscribe: delivery resumes, including the full layout pass.
        Event.Subscribe(Listen);
        var second = YGNodeNew();
        Assert.Equal(EventType.NodeAllocation, LastEvent().Type);
        Assert.Same(second, LastEvent().Node);

        YGNodeCalculateLayout(second, 100.0f, 100.0f, YGDirection.LTR);
        Assert.Equal(EventType.LayoutPassEnd, LastEvent().Type);
        Assert.Same(second, LastEvent().Node);
        Assert.Equal(1, LastEvent().LayoutDataCopy!.Value.Layouts);

        // Unsubscribe again: still silent.
        Event.Unsubscribe(Listen);
        var silentAgain = YGNodeNew();
        YGNodeFree(silentAgain);
        Assert.Equal(EventType.LayoutPassEnd, LastEvent().Type);
        Assert.Same(second, LastEvent().Node);

        // Reset clears everything; a fresh subscription receives events again.
        Event.Reset();
        Event.Subscribe(Listen);
        var third = YGNodeNew();
        Assert.Equal(EventType.NodeAllocation, LastEvent().Type);
        Assert.Same(third, LastEvent().Node);

        YGNodeFree(first);
        YGNodeFree(second);
        YGNodeFree(third);
        Assert.Equal(EventType.NodeDeallocation, LastEvent().Type);
        Assert.Same(third, LastEvent().Node);
    }
}
