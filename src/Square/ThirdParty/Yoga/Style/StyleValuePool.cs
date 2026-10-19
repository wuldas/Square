using System;
using System.Runtime.CompilerServices;

namespace Square.Rendering.Yoga
{
    internal class StyleValuePool
    {
        private struct BufferSize : IConstant { public int Value => 43; }
        private SmallValueBuffer<BufferSize> buffer_ = new SmallValueBuffer<BufferSize>();

        public void Store(ref StyleValueHandle handle, StyleLength length)
        {
            if (length.IsUndefined())
            {
                handle.SetType(StyleValueHandle.HandleTypeEnum.Undefined);
            }
            else if (length.IsAuto())
            {
                handle.SetType(StyleValueHandle.HandleTypeEnum.Auto);
            }
            else
            {
                var type = length.IsPoints() ? StyleValueHandle.HandleTypeEnum.Point : StyleValueHandle.HandleTypeEnum.Percent;
                StoreValue(ref handle, length.Value().Unwrap(), type);
            }
        }

        public void Store(ref StyleValueHandle handle, StyleSizeLength sizeValue)
        {
            if (sizeValue.IsUndefined())
            {
                handle.SetType(StyleValueHandle.HandleTypeEnum.Undefined);
            }
            else if (sizeValue.IsAuto())
            {
                handle.SetType(StyleValueHandle.HandleTypeEnum.Auto);
            }
            else if (sizeValue.IsMaxContent())
            {
                StoreKeyword(ref handle, StyleValueHandle.KeywordEnum.MaxContent);
            }
            else if (sizeValue.IsStretch())
            {
                StoreKeyword(ref handle, StyleValueHandle.KeywordEnum.Stretch);
            }
            else if (sizeValue.IsFitContent())
            {
                StoreKeyword(ref handle, StyleValueHandle.KeywordEnum.FitContent);
            }
            else
            {
                var type = sizeValue.IsPoints() ? StyleValueHandle.HandleTypeEnum.Point : StyleValueHandle.HandleTypeEnum.Percent;
                StoreValue(ref handle, sizeValue.Value().Unwrap(), type);
            }
        }

        public void Store(ref StyleValueHandle handle, FloatOptional number)
        {
            if (number.IsUndefined())
            {
                handle.SetType(StyleValueHandle.HandleTypeEnum.Undefined);
            }
            else
            {
                StoreValue(ref handle, number.Unwrap(), StyleValueHandle.HandleTypeEnum.Number);
            }
        }

        public StyleLength GetLength(StyleValueHandle handle)
        {
            if (handle.IsUndefined)
            {
                return StyleLength.Undefined();
            }
            else if (handle.IsAuto)
            {
                return StyleLength.OfAuto();
            }
            else
            {
                System.Diagnostics.Debug.Assert(
                    handle.HandleType == StyleValueHandle.HandleTypeEnum.Point ||
                    handle.HandleType == StyleValueHandle.HandleTypeEnum.Percent);

                float value = (handle.IsValueIndexed())
                    ? BitConverter.Int32BitsToSingle((int)buffer_.Get32(handle.GetValue()))
                    : UnpackInlineInteger(handle.GetValue());

                return handle.HandleType == StyleValueHandle.HandleTypeEnum.Point
                    ? StyleLength.Points(value)
                    : StyleLength.Percent(value);
            }
        }

        public StyleSizeLength GetSize(StyleValueHandle handle)
        {
            if (handle.IsUndefined)
            {
                return StyleSizeLength.Undefined();
            }
            else if (handle.IsAuto)
            {
                return StyleSizeLength.OfAuto();
            }
            else if (handle.IsKeyword(StyleValueHandle.KeywordEnum.MaxContent))
            {
                return StyleSizeLength.OfMaxContent();
            }
            else if (handle.IsKeyword(StyleValueHandle.KeywordEnum.FitContent))
            {
                return StyleSizeLength.OfFitContent();
            }
            else if (handle.IsKeyword(StyleValueHandle.KeywordEnum.Stretch))
            {
                return StyleSizeLength.OfStretch();
            }
            else
            {
                System.Diagnostics.Debug.Assert(
                    handle.HandleType == StyleValueHandle.HandleTypeEnum.Point ||
                    handle.HandleType == StyleValueHandle.HandleTypeEnum.Percent);

                float value = (handle.IsValueIndexed())
                    ? BitConverter.Int32BitsToSingle((int)buffer_.Get32(handle.GetValue()))
                    : UnpackInlineInteger(handle.GetValue());

                return handle.HandleType == StyleValueHandle.HandleTypeEnum.Point
                    ? StyleSizeLength.Points(value)
                    : StyleSizeLength.Percent(value);
            }
        }

        public FloatOptional GetNumber(StyleValueHandle handle)
        {
            if (handle.IsUndefined)
            {
                return FloatOptional.Undefined;
            }
            else
            {
                System.Diagnostics.Debug.Assert(handle.HandleType == StyleValueHandle.HandleTypeEnum.Number);
                float value = (handle.IsValueIndexed())
                    ? BitConverter.Int32BitsToSingle((int)buffer_.Get32(handle.GetValue()))
                    : UnpackInlineInteger(handle.GetValue());
                return new FloatOptional(value);
            }
        }

        private void StoreValue(ref StyleValueHandle handle, float value, StyleValueHandle.HandleTypeEnum type)
        {
            handle.SetType(type);

            if (handle.IsValueIndexed())
            {
                var newIndex = buffer_.Replace(handle.GetValue(), (uint)BitConverter.SingleToInt32Bits(value));
                handle.SetValue(newIndex);
            }
            else if (IsIntegerPackable(value))
            {
                handle.SetValue(PackInlineInteger(value));
            }
            else
            {
                var newIndex = buffer_.Push((uint)BitConverter.SingleToInt32Bits(value));
                handle.SetValue(newIndex);
                handle.SetValueIsIndexed();
            }
        }

        private void StoreKeyword(ref StyleValueHandle handle, StyleValueHandle.KeywordEnum keyword)
        {
            handle.SetType(StyleValueHandle.HandleTypeEnum.Keyword);

            if (handle.IsValueIndexed())
            {
                var newIndex = buffer_.Replace(handle.GetValue(), (uint)keyword);
                handle.SetValue(newIndex);
            }
            else
            {
                handle.SetValue((ushort)keyword);
            }
        }

        private static bool IsIntegerPackable(float f)
        {
            const ushort MaxInlineAbsValue = (1 << 11) - 1;

            int i = (int)f;
            return (float)i == f && i >= -MaxInlineAbsValue && i <= +MaxInlineAbsValue;
        }

        private static ushort PackInlineInteger(float value)
        {
            ushort isNegative = value < 0 ? (ushort)1 : (ushort)0;
            return (ushort)(
                (isNegative << 11) |
                ((int)value * (isNegative != 0 ? -1 : 1)));
        }

        private static float UnpackInlineInteger(ushort value)
        {
            const ushort ValueSignMask = 0b0000_1000_0000_0000;
            const ushort ValueMagnitudeMask = 0b0000_0111_1111_1111;
            bool isNegative = (value & ValueSignMask) != 0;
            return (value & ValueMagnitudeMask) * (isNegative ? -1 : 1);
        }

        public StyleValuePool Clone()
        {
            var clone = new StyleValuePool();
            clone.buffer_ = buffer_.Clone();
            return clone;
        }
    }
}

