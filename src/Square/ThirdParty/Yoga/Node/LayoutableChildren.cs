using System;
using System.Collections;
using System.Collections.Generic;

namespace Square.Rendering.Yoga
{
    internal class LayoutableChildren<T> : IEnumerable<T> where T : Node
    {
        private readonly T _node;

        public LayoutableChildren(T node)
        {
            _node = node ?? throw new ArgumentNullException(nameof(node));
        }

        public Iterator GetEnumerator()
        {
            return new Iterator(_node);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Iterator : IEnumerator<T>
        {
            // Small inline stack to avoid LinkedList allocation for shallow nesting
            private const int InlineCapacity = 4;
            private (T node, int childIndex) _stack0, _stack1, _stack2, _stack3;
            private int _stackCount;

            // Overflow for deep nesting (rare)
            private List<(T node, int childIndex)>? _overflow;

            private T _node;
            private int _childIndex;
            private T _current;
            private bool _initialized;

            internal Iterator(T rootNode)
            {
                _node = rootNode;
                _childIndex = 0;
                _stackCount = 0;
                _stack0 = _stack1 = _stack2 = _stack3 = default;
                _overflow = null;
                _current = null!;
                _initialized = false;
            }

            public T Current => _current;
            object IEnumerator.Current => _current;

            private void PushBacktrack(T node, int childIndex)
            {
                if (_stackCount < InlineCapacity)
                {
                    switch (_stackCount)
                    {
                        case 0: _stack0 = (node, childIndex); break;
                        case 1: _stack1 = (node, childIndex); break;
                        case 2: _stack2 = (node, childIndex); break;
                        case 3: _stack3 = (node, childIndex); break;
                    }
                    _stackCount++;
                }
                else
                {
                    _overflow ??= new List<(T, int)>();
                    _overflow.Add((node, childIndex));
                    _stackCount++;
                }
            }

            private bool TryPopBacktrack(out T node, out int childIndex)
            {
                if (_stackCount == 0)
                {
                    node = null!;
                    childIndex = 0;
                    return false;
                }

                _stackCount--;
                if (_stackCount >= InlineCapacity && _overflow != null)
                {
                    int idx = _stackCount - InlineCapacity;
                    (node, childIndex) = _overflow[idx];
                    _overflow.RemoveAt(idx);
                }
                else
                {
                    switch (_stackCount)
                    {
                        case 0: (node, childIndex) = _stack0; break;
                        case 1: (node, childIndex) = _stack1; break;
                        case 2: (node, childIndex) = _stack2; break;
                        case 3: (node, childIndex) = _stack3; break;
                        default: node = null!; childIndex = 0; break;
                    }
                }
                return true;
            }

            public bool MoveNext()
            {
                if (!_initialized)
                {
                    _initialized = true;
                    if (_node == null || (int)_node.GetChildCount() == 0)
                    {
                        _node = null!;
                        return false;
                    }

                    _childIndex = 0;
                    if (_node.GetChild(0)!.Style.Display == Display.Contents)
                    {
                        SkipContentsNodes();
                    }

                    if (_node == null)
                        return false;

                    _current = (T)_node.GetChild((nuint)_childIndex)!;
                    return true;
                }
                else
                {
                    if (_node == null)
                        return false;

                    Next();

                    if (_node == null)
                        return false;

                    _current = (T)_node.GetChild((nuint)_childIndex)!;
                    return true;
                }
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }

            private void Next()
            {
                if (_childIndex + 1 >= (int)_node.GetChildCount())
                {
                    if (!TryPopBacktrack(out var parentNode, out var parentIndex))
                    {
                        _node = null!;
                        _childIndex = 0;
                    }
                    else
                    {
                        _node = parentNode;
                        _childIndex = parentIndex;
                        Next();
                    }
                }
                else
                {
                    _childIndex++;
                    var child = _node.GetChild((nuint)_childIndex);
                    if (child!.Style.Display == Display.Contents)
                    {
                        SkipContentsNodes();
                    }
                }
            }

            private void SkipContentsNodes()
            {
                var currentNode = _node.GetChild((nuint)_childIndex)!;
                while (currentNode.Style.Display == Display.Contents && currentNode.GetChildCount() > 0)
                {
                    PushBacktrack(_node, _childIndex);
                    _node = (T)currentNode;
                    _childIndex = 0;
                    currentNode = currentNode.GetChild(0)!;
                }

                if (currentNode.Style.Display == Display.Contents)
                {
                    Next();
                }
            }
        }
    }
}
