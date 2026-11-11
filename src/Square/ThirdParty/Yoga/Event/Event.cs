using System;
using System.Collections.Generic;
using System.Threading;

namespace Square.Rendering.Yoga
{
    internal enum LayoutType
    {
        Layout = 0,
        Measure = 1,
        CachedLayout = 2,
        CachedMeasure = 3
    }

    internal enum LayoutPassReason
    {
        Initial = 0,
        AbsLayout = 1,
        Stretch = 2,
        MultilineStretch = 3,
        FlexLayout = 4,
        MeasureChild = 5,
        AbsMeasureChild = 6,
        FlexMeasure = 7,
        GridLayout = 8,
        Count
    }

    internal enum YGMeasureMode
    {
        Undefined,
        Exactly,
        AtMost
    }

    internal struct LayoutData
    {
        public int Layouts;
        public int Measures;
        public uint MaxMeasureCache;
        public int CachedLayouts;
        public int CachedMeasures;
        public int MeasureCallbacks;
        public int[] MeasureCallbackReasonsCount;

        public LayoutData()
        {
            MeasureCallbackReasonsCount = new int[(int)LayoutPassReason.Count];
        }
    }

    internal static class LayoutPassReasonHelper
    {
        public static string ToString(LayoutPassReason value)
        {
            return value switch
            {
                LayoutPassReason.Initial => "initial",
                LayoutPassReason.AbsLayout => "abs_layout",
                LayoutPassReason.Stretch => "stretch",
                LayoutPassReason.MultilineStretch => "multiline_stretch",
                LayoutPassReason.FlexLayout => "flex_layout",
                LayoutPassReason.MeasureChild => "measure",
                LayoutPassReason.AbsMeasureChild => "abs_measure",
                LayoutPassReason.FlexMeasure => "flex_measure",
                LayoutPassReason.GridLayout => "grid_layout",
                _ => "unknown"
            };
        }
    }

    internal enum EventType
    {
        NodeAllocation,
        NodeDeallocation,
        NodeLayout,
        LayoutPassStart,
        LayoutPassEnd,
        MeasureCallbackStart,
        MeasureCallbackEnd,
        NodeBaselineStart,
        NodeBaselineEnd
    }

    internal class Event
    {
        public class Data
        {
            private readonly object? _data;
            public readonly EventType EventType;

            internal Data(object? data, EventType eventType)
            {
                _data = data;
                EventType = eventType;
            }

            /// <summary>
            /// Retrieves the typed event data.
            /// </summary>
            public T? GetData<T>() where T : class
            {
                return _data as T;
            }

            // Legacy method kept for backward compatibility
            public TypedData<T> Get<T>() where T : EventTypedDataBase, new()
            {
                if (_data is TypedData<T> typedData)
                {
                    return typedData;
                }
                return new TypedData<T>();
            }
        }

        public class TypedData<T> : EventTypedDataBase where T : EventTypedDataBase, new()
        {
            public T Data { get; } = new T();
        }

        public abstract class EventTypedDataBase { }

        public class NodeAllocationData : EventTypedDataBase
        {
            public Config? Config { get; set; }
        }

        public class NodeDeallocationData : EventTypedDataBase
        {
            public Config? Config { get; set; }
        }

        public class LayoutPassEndData : EventTypedDataBase
        {
            public LayoutData? LayoutData { get; set; }
        }

        public class MeasureCallbackEndData : EventTypedDataBase
        {
            public float Width { get; set; }
            public MeasureMode WidthMeasureMode { get; set; }
            public float Height { get; set; }
            public MeasureMode HeightMeasureMode { get; set; }
            public float MeasuredWidth { get; set; }
            public float MeasuredHeight { get; set; }
            public LayoutPassReason Reason { get; set; }
        }

        public class NodeLayoutData : EventTypedDataBase
        {
            public LayoutType LayoutType { get; set; }
        }

        public delegate void Subscriber(Node? node, EventType eventType, Data eventData);

        private sealed class SubscriberNode
        {
            public Subscriber? Subscriber { get; }
            public SubscriberNode? Next { get; }

            public SubscriberNode(Subscriber? subscriber)
            {
                Subscriber = subscriber;
            }
        }

        private static readonly LinkedList<SubscriberNode> _subscribers = new();
        private static readonly object _lock = new();

        // Maintained under _lock; read without the lock on the publish fast
        // path. A publish racing a concurrent Subscribe/Unsubscribe is
        // dispatched according to the subscription state at the moment of the
        // check, matching the previous per-publish snapshot under the lock.
        private static int _subscriberCount;

        /// <summary>
        /// True when at least one subscriber is registered. Cheap volatile
        /// read, intended for hot paths to skip event payload construction.
        /// </summary>
        public static bool HasSubscribers => Volatile.Read(ref _subscriberCount) > 0;

        public static void Reset()
        {
            lock (_lock)
            {
                _subscribers.Clear();
                Volatile.Write(ref _subscriberCount, 0);
            }
        }

        public static void Subscribe(Subscriber? subscriber)
        {
            if (subscriber == null)
                return;

            lock (_lock)
            {
                _subscribers.AddLast(new SubscriberNode(subscriber));
                Volatile.Write(ref _subscriberCount, _subscribers.Count);
            }
        }

        public static void Unsubscribe(Subscriber? subscriber)
        {
            if (subscriber == null)
                return;

            lock (_lock)
            {
                var current = _subscribers.First;
                while (current != null)
                {
                    var next = current.Next;
                    if (current.Value.Subscriber == subscriber)
                    {
                        _subscribers.Remove(current);
                        Volatile.Write(ref _subscriberCount, _subscribers.Count);
                        break;
                    }
                    current = next;
                }
            }
        }

        public static void Publish(Node? node, EventType eventType, object? data = null)
        {
            if (!HasSubscribers) return;
            var eventData = new Data(data, eventType);
            PublishCore(node, eventType, eventData);
        }

        public static void Publish<T>(Node? node, in T? eventData = default) where T : EventTypedDataBase, new()
        {
            if (!HasSubscribers) return;
            var data = new Data(eventData, GetEventType<T>());
            PublishCore(node, GetEventType<T>(), data);
        }

        public static void Publish(Node? node, EventType eventType, in Data eventData)
        {
            if (!HasSubscribers) return;
            PublishCore(node, eventType, eventData);
        }

        [ThreadStatic]
        private static Subscriber[]? t_subscriberBuffer;

        private static void PublishCore(Node? node, EventType eventType, Data eventData)
        {
            int count;
            Subscriber[] buffer;

            if (Volatile.Read(ref _subscriberCount) == 0) return;

            lock (_lock)
            {
                int subscriberCount = _subscribers.Count;
                if (subscriberCount == 0) return;

                // Reuse thread-local buffer to avoid per-call allocation
                buffer = t_subscriberBuffer ?? Array.Empty<Subscriber>();
                if (buffer.Length < subscriberCount)
                {
                    buffer = new Subscriber[subscriberCount];
                    t_subscriberBuffer = buffer;
                }

                count = 0;
                foreach (var n in _subscribers)
                {
                    if (n.Subscriber is { } sub)
                    {
                        buffer[count++] = sub;
                    }
                }
            }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    buffer[i].Invoke(node, eventType, eventData);
                }
                catch
                {
                    // Swallow exceptions to match C++ behavior
                }
            }

            // Clear references to avoid leaking
            Array.Clear(buffer, 0, count);
        }

        private static EventType GetEventType<T>() where T : EventTypedDataBase, new()
        {
            if (typeof(T) == typeof(NodeAllocationData))
                return EventType.NodeAllocation;
            if (typeof(T) == typeof(NodeDeallocationData))
                return EventType.NodeDeallocation;
            if (typeof(T) == typeof(LayoutPassEndData))
                return EventType.LayoutPassEnd;
            if (typeof(T) == typeof(MeasureCallbackEndData))
                return EventType.MeasureCallbackEnd;
            if (typeof(T) == typeof(NodeLayoutData))
                return EventType.NodeLayout;
            
            return EventType.NodeAllocation; // Default
        }
    }
}

