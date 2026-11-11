// Copyright (c) Meta Platforms, Inc. and affiliates.
//
// This source code is licensed under the MIT license found in the
// LICENSE file in the root directory of this source tree.
//
// Original: yoga/YGNode.h, yoga/YGNode.cpp

using System;
using System.Collections.Generic;

namespace Square.Rendering.Yoga
{
    /// <summary>
    /// Public C-style API for Yoga nodes.
    /// Delegates to the internal Node class.
    /// </summary>
    internal static class YGNodeAPI
    {
        public static Node YGNodeNew()
        {
            return YGNodeNewWithConfig(Config.Default);
        }

        public static Node YGNodeNewWithConfig(Config config)
        {
            Debug.AssertFatal.Assert(
                config != null,
                "Tried to construct YGNode with null config");

            var node = new Node(config);
            if (Event.HasSubscribers)
            {
                Event.Publish(node, EventType.NodeAllocation,
                    new Event.NodeAllocationData { Config = config });
            }
            return node;
        }

        public static Node YGNodeClone(Node oldNode)
        {
            var node = new Node(oldNode.GetConfig());
            node.MoveFrom(oldNode);
            if (Event.HasSubscribers)
            {
                Event.Publish(node, EventType.NodeAllocation,
                    new Event.NodeAllocationData { Config = node.GetConfig() });
            }
            node.SetOwner(null);
            return node;
        }

        public static void YGNodeFree(Node node)
        {
            var owner = node.GetOwner();
            if (owner != null)
            {
                owner.RemoveChild(node);
                node.SetOwner(null);
            }

            nuint childCount = node.GetChildCount();
            for (nuint i = 0; i < childCount; i++)
            {
                var child = node.GetChild(i);
                child?.SetOwner(null);
            }

            node.ClearChildren();
            if (Event.HasSubscribers)
            {
                Event.Publish(node, EventType.NodeDeallocation,
                    new Event.NodeDeallocationData { Config = node.GetConfig() });
            }
        }

        public static void YGNodeFreeRecursive(Node root)
        {
            nuint skipped = 0;
            while (root.GetChildCount() > skipped)
            {
                var child = root.GetChild(skipped);
                if (child?.GetOwner() != root)
                {
                    skipped += 1;
                }
                else
                {
                    YGNodeRemoveChild(root, child!);
                    YGNodeFreeRecursive(child!);
                }
            }
            YGNodeFree(root);
        }

        public static void YGNodeFinalize(Node node)
        {
            // In C#, GC handles deallocation. No-op beyond event publishing.
        }

        public static void YGNodeReset(Node node)
        {
            node.Reset();
        }

        public static void YGNodeCalculateLayout(
            Node node,
            float availableWidth,
            float availableHeight,
            YGDirection ownerDirection)
        {
            LayoutAlgorithm.CalculateLayout(
                node, availableWidth, availableHeight, ownerDirection.ToInternal());
        }

        public static bool YGNodeGetHasNewLayout(Node node)
        {
            return node.GetHasNewLayout();
        }

        public static void YGNodeSetHasNewLayout(Node node, bool hasNewLayout)
        {
            node.SetHasNewLayout(hasNewLayout);
        }

        public static bool YGNodeIsDirty(Node node)
        {
            return node.IsDirty();
        }

        public static void YGNodeMarkDirty(Node node)
        {
            Debug.AssertFatal.AssertWithNode(
                node,
                node.HasMeasureFunc(),
                "Only leaf nodes with custom measure functions should manually mark themselves as dirty");

            node.MarkDirtyAndPropagate();
        }

        public static void YGNodeSetDirtiedFunc(Node node, YGDirtiedFunc? dirtiedFunc)
        {
            node.SetDirtiedFunc(dirtiedFunc);
        }

        public static YGDirtiedFunc? YGNodeGetDirtiedFunc(Node node)
        {
            return node.GetDirtiedFunc();
        }

        public static void YGNodeInsertChild(Node owner, Node child, nuint index)
        {
            Debug.AssertFatal.AssertWithNode(
                owner,
                child.GetOwner() == null,
                "Child already has a owner, it must be removed first.");

            Debug.AssertFatal.AssertWithNode(
                owner,
                !owner.HasMeasureFunc(),
                "Cannot add child: Nodes with measure functions cannot have children.");

            owner.InsertChild(child, index);
            child.SetOwner(owner);
            owner.MarkDirtyAndPropagate();
        }

        public static void YGNodeSwapChild(Node owner, Node child, nuint index)
        {
            owner.ReplaceChild(child, index);
            child.SetOwner(owner);
        }

        public static void YGNodeRemoveChild(Node owner, Node excludedChild)
        {
            if (owner.GetChildCount() == 0)
            {
                return;
            }

            var childOwner = excludedChild.GetOwner();
            if (owner.RemoveChild(excludedChild))
            {
                if (owner == childOwner)
                {
                    excludedChild.SetLayout(new LayoutResults());
                    excludedChild.SetOwner(null);

                    var dirtiedFunc = excludedChild.GetDirtiedFunc();
                    excludedChild.SetDirtiedFunc(null);
                    excludedChild.SetDirty(true);
                    excludedChild.SetDirtiedFunc(dirtiedFunc);
                }
                owner.MarkDirtyAndPropagate();
            }
        }

        public static void YGNodeRemoveAllChildren(Node owner)
        {
            nuint childCount = owner.GetChildCount();
            if (childCount == 0)
            {
                return;
            }

            var firstChild = owner.GetChild(0);
            if (firstChild?.GetOwner() == owner)
            {
                for (nuint i = 0; i < childCount; i++)
                {
                    Node? oldChild = owner.GetChild(i);
                    if (oldChild != null)
                    {
                        oldChild.SetLayout(new LayoutResults());
                        oldChild.SetOwner(null);

                        var dirtiedFunc = oldChild.GetDirtiedFunc();
                        oldChild.SetDirtiedFunc(null);
                        oldChild.SetDirty(true);
                        oldChild.SetDirtiedFunc(dirtiedFunc);
                    }
                }
                owner.ClearChildren();
                owner.MarkDirtyAndPropagate();
                return;
            }

            owner.SetChildren(new List<Node>());
            owner.MarkDirtyAndPropagate();
        }

        public static void YGNodeSetChildren(Node owner, Node[] children)
        {
            if (children == null || children.Length == 0)
            {
                if (owner.GetChildCount() > 0)
                {
                    foreach (var child in owner.GetChildren())
                    {
                        child.SetLayout(new LayoutResults());
                        child.SetOwner(null);
                    }
                    owner.SetChildren(new List<Node>());
                    owner.MarkDirtyAndPropagate();
                }
            }
            else
            {
                if (owner.GetChildCount() > 0)
                {
                    var childrenSet = new HashSet<Node>(children);
                    foreach (var oldChild in owner.GetChildren())
                    {
                        if (!childrenSet.Contains(oldChild))
                        {
                            oldChild.SetLayout(new LayoutResults());
                            oldChild.SetOwner(null);
                        }
                    }
                }

                var childrenList = new List<Node>(children);
                owner.SetChildren(childrenList);
                foreach (var child in childrenList)
                {
                    child.SetOwner(owner);
                }
                owner.MarkDirtyAndPropagate();
            }
        }

        public static Node? YGNodeGetChild(Node node, nuint index)
        {
            if (index < (nuint)node.GetChildren().Count)
            {
                return node.GetChild(index);
            }
            return null;
        }

        public static nuint YGNodeGetChildCount(Node node)
        {
            return (nuint)node.GetChildren().Count;
        }

        public static Node? YGNodeGetOwner(Node node)
        {
            return node.GetOwner();
        }

        public static Node? YGNodeGetParent(Node node)
        {
            return node.GetOwner();
        }

        public static void YGNodeSetConfig(Node node, Config config)
        {
            node.SetConfig(config);
        }

        public static Config? YGNodeGetConfig(Node node)
        {
            return node.GetConfig();
        }

        public static void YGNodeSetContext(Node node, object? context)
        {
            node.SetContext(context);
        }

        public static object? YGNodeGetContext(Node node)
        {
            return node.GetContext();
        }

        public static void YGNodeSetMeasureFunc(Node node, YGMeasureFunc? measureFunc)
        {
            node.SetMeasureFunc(measureFunc);
        }

        public static bool YGNodeHasMeasureFunc(Node node)
        {
            return node.HasMeasureFunc();
        }

        public static void YGNodeSetBaselineFunc(Node node, YGBaselineFunc? baselineFunc)
        {
            node.SetBaselineFunc(baselineFunc);
        }

        public static bool YGNodeHasBaselineFunc(Node node)
        {
            return node.HasBaselineFunc();
        }

        public static void YGNodeSetIsReferenceBaseline(Node node, bool isReferenceBaseline)
        {
            if (node.IsReferenceBaseline() != isReferenceBaseline)
            {
                node.SetIsReferenceBaseline(isReferenceBaseline);
                node.MarkDirtyAndPropagate();
            }
        }

        public static bool YGNodeIsReferenceBaseline(Node node)
        {
            return node.IsReferenceBaseline();
        }

        public static void YGNodeSetNodeType(Node node, YGNodeType nodeType)
        {
            node.SetNodeType(nodeType.ToInternal());
        }

        public static YGNodeType YGNodeGetNodeType(Node node)
        {
            return node.GetNodeType().ToYG();
        }

        public static void YGNodeSetAlwaysFormsContainingBlock(Node node, bool alwaysFormsContainingBlock)
        {
            node.SetAlwaysFormsContainingBlock(alwaysFormsContainingBlock);
        }

        public static bool YGNodeGetAlwaysFormsContainingBlock(Node node)
        {
            return node.AlwaysFormsContainingBlock;
        }

        public static bool YGNodeCanUseCachedMeasurement(
            YGMeasureMode widthMode,
            float availableWidth,
            YGMeasureMode heightMode,
            float availableHeight,
            YGMeasureMode lastWidthMode,
            float lastAvailableWidth,
            YGMeasureMode lastHeightMode,
            float lastAvailableHeight,
            float lastComputedWidth,
            float lastComputedHeight,
            float marginRow,
            float marginColumn,
            Config config)
        {
            return Cache.CanUseCachedMeasurement(
                widthMode.ToInternal().ToSizingMode(),
                availableWidth,
                heightMode.ToInternal().ToSizingMode(),
                availableHeight,
                lastWidthMode.ToInternal().ToSizingMode(),
                lastAvailableWidth,
                lastHeightMode.ToInternal().ToSizingMode(),
                lastAvailableHeight,
                lastComputedWidth,
                lastComputedHeight,
                marginRow,
                marginColumn,
                config);
        }
    }
}
