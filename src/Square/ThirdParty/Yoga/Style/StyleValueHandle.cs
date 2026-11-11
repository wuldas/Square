using System;

namespace Square.Rendering.Yoga
{
    internal struct StyleValueHandle
    {
        private const ushort KHandleTypeMask = 0b0000_0000_0000_0111;
        private const ushort KHandleIndexedMask = 0b0000_0000_0000_1000;
        private const ushort KHandleValueMask = 0b1111_1111_1111_0000;

        public enum HandleTypeEnum : byte
        {
            Undefined,
            Point,
            Percent,
            Number,
            Auto,
            Keyword
        }

        public enum KeywordEnum : byte
        {
            MaxContent,
            FitContent,
            Stretch
        }

        private ushort _repr;

        public static StyleValueHandle OfAuto()
        {
            return new StyleValueHandle { _repr = (ushort)HandleTypeEnum.Auto };
        }

        public readonly bool IsUndefined => HandleType == HandleTypeEnum.Undefined;

        public readonly bool IsDefined => !IsUndefined;

        public readonly bool IsAuto => HandleType == HandleTypeEnum.Auto;

        public readonly bool IsKeyword(KeywordEnum keyword)
        {
            return HandleType == HandleTypeEnum.Keyword && GetValue() == (ushort)keyword;
        }

        public readonly HandleTypeEnum HandleType => (HandleTypeEnum)(_repr & KHandleTypeMask);

        public void SetType(HandleTypeEnum handleType)
        {
            _repr = (ushort)((_repr & ~KHandleTypeMask) | (ushort)handleType);
        }

        public readonly ushort GetValue() => (ushort)(_repr >> 4);

        public void SetValue(ushort value)
        {
            _repr = (ushort)((_repr & ~KHandleValueMask) | (value << 4));
        }

        public readonly bool IsValueIndexed() => (_repr & KHandleIndexedMask) != 0;

        public void SetValueIsIndexed()
        {
            _repr = (ushort)(_repr | KHandleIndexedMask);
        }
    }
}
