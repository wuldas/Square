using System;

namespace Square.Rendering.Yoga
{
    internal enum NodeType : byte
    {
        Default = 0,
        Text = 1,
    }

    internal static partial class YogaEnums
    {
        public static int OrdinalCount(NodeType _)
        {
            return 2;
        }

        public static string ToString(NodeType nodeType)
        {
            return nodeType switch
            {
                NodeType.Default => nameof(NodeType.Default),
                NodeType.Text => nameof(NodeType.Text),
                _ => throw new ArgumentOutOfRangeException(nameof(nodeType), nodeType, "Unsupported NodeType")
            };
        }
    }
}

