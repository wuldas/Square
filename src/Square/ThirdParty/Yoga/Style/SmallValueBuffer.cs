using System;
using System.Collections.Generic;

namespace Square.Rendering.Yoga
{
    internal class SmallValueBuffer<BufferSize> where BufferSize : struct, IConstant
    {
        private sealed class Overflow
        {
            public List<uint> Buffer = new();
            public List<bool> WideElements = new();
        }

        private const int MaxBufferSize = 4096;

        private ushort _count;
        private readonly uint[] _buffer;
        private readonly bool[] _wideElements;
        private Overflow? _overflow;

        private int BufferSizeValue => _buffer.Length;

        public SmallValueBuffer()
        {
            int size = default(BufferSize).Value;
            _buffer = new uint[size];
            _wideElements = new bool[size];
        }

        private SmallValueBuffer(SmallValueBuffer<BufferSize> other)
        {
            _count = other._count;
            int size = default(BufferSize).Value;
            _buffer = new uint[size];
            _wideElements = new bool[size];
            Array.Copy(other._buffer, _buffer, size);
            Array.Copy(other._wideElements, _wideElements, size);

            if (other._overflow != null)
            {
                _overflow = new Overflow
                {
                    Buffer = new List<uint>(other._overflow.Buffer),
                    WideElements = new List<bool>(other._overflow.WideElements)
                };
            }
        }

        public ushort Push(uint value)
        {
            var index = _count++;
            if (index >= MaxBufferSize)
            {
                throw new InvalidOperationException("SmallValueBuffer can only hold up to 4096 chunks");
            }

            if (index < BufferSizeValue)
            {
                _buffer[index] = value;
                return index;
            }

            _overflow ??= new Overflow();

            _overflow.Buffer.Add(value);
            _overflow.WideElements.Add(false);
            return index;
        }

        public ushort Push(ulong value)
        {
            var lsb = (uint)(value & 0xFFFFFFFF);
            var msb = (uint)(value >> 32);

            var lsbIndex = Push(lsb);
            var msbIndex = Push(msb);

            if (msbIndex >= MaxBufferSize)
            {
                throw new InvalidOperationException("SmallValueBuffer can only hold up to 4096 chunks");
            }

            if (lsbIndex < BufferSizeValue)
            {
                _wideElements[lsbIndex] = true;
            }
            else
            {
                _overflow!.WideElements[lsbIndex - BufferSizeValue] = true;
            }

            return lsbIndex;
        }

        public ushort Replace(ushort index, uint value)
        {
            if (index < BufferSizeValue)
            {
                _buffer[index] = value;
            }
            else
            {
                _overflow!.Buffer[index - BufferSizeValue] = value;
            }

            return index;
        }

        public ushort Replace(ushort index, ulong value)
        {
            bool isWide = index < BufferSizeValue
                ? _wideElements[index]
                : _overflow!.WideElements[index - BufferSizeValue];

            if (isWide)
            {
                var lsb = (uint)(value & 0xFFFFFFFF);
                var msb = (uint)(value >> 32);

                Replace(index, lsb);
                Replace((ushort)(index + 1), msb);
                return index;
            }
            else
            {
                return Push(value);
            }
        }

        public uint Get32(ushort index)
        {
            if (index < BufferSizeValue)
            {
                return _buffer[index];
            }
            else
            {
                return _overflow!.Buffer[index - BufferSizeValue];
            }
        }

        public ulong Get64(ushort index)
        {
            var lsb = Get32(index);
            var msb = Get32((ushort)(index + 1));
            return ((ulong)msb << 32) | lsb;
        }

        public SmallValueBuffer<BufferSize> Clone()
        {
            return new SmallValueBuffer<BufferSize>(this);
        }
    }

    internal interface IConstant
    {
        int Value { get; }
    }
}

