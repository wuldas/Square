using System.Globalization;

namespace Square.Graphics;

/// <summary>表示 BGRA 颜色（不透明度为 0–255）。</summary>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>红色通道（0–255）。</summary>
    public readonly byte R, G, B, A;

    /// <summary>用 RGBA 分量构造颜色，<paramref name="a"/> 默认为 255。</summary>
    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }

    /// <summary>从 RGBA 分量构造颜色。</summary>
    public static Color FromRgba(byte r, byte g, byte b, byte a) => new(r, g, b, a);
    /// <summary>从 RGB 分量构造不透明颜色。</summary>
    public static Color FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);

    /// <summary>完全透明（RGBA = 0,0,0,0）。</summary>
    public static readonly Color Transparent = new(0, 0, 0, 0);
    /// <summary>黑色（RGBA = 0,0,0,255）。</summary>
    public static readonly Color Black = new(0, 0, 0, 255);
    /// <summary>白色（RGBA = 255,255,255,255）。</summary>
    public static readonly Color White = new(255, 255, 255, 255);
    /// <summary>红色（RGBA = 255,0,0,255）。</summary>
    public static readonly Color Red = new(255, 0, 0, 255);
    /// <summary>绿色（RGBA = 0,255,0,255）。</summary>
    public static readonly Color Green = new(0, 255, 0, 255);
    /// <summary>蓝色（RGBA = 0,0,255,255）。</summary>
    public static readonly Color Blue = new(0, 0, 255, 255);

    /// <summary>打包为 32 位 BGRA 值（A 在最高字节，B 在最低字节）。</summary>
    public uint ToPackedBgra() => (uint)(A << 24 | R << 16 | G << 8 | B);

    /// <summary>解析 CSS 十六进制颜色字符串（#RGB / #RRGGBB / #AARRGGBB）。</summary>
    /// <exception cref="FormatException"><paramref name="hex"/> 不是合法颜色。</exception>
    public static Color Parse(string hex)
    {
        if (TryParse(hex, out var color)) return color;
        throw new FormatException($"Invalid color hex: {hex}");
    }

    /// <summary>尝试解析 CSS 十六进制颜色字符串。</summary>
    /// <returns>解析成功返回 true；失败返回 false。</returns>
    public static bool TryParse(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        if (TryParseNamed(text, out color)) return true;

        var span = text.AsSpan();
        if (!span.IsEmpty && span[0] == '#') span = span[1..];
        switch (span.Length)
        {
            case 3:
                if (!TryHex(span[0], out var r) ||
                    !TryHex(span[1], out var g) ||
                    !TryHex(span[2], out var b)) return false;
                color = new Color((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                return true;
            case 6:
                if (!TryHexByte(span, 0, out var red) ||
                    !TryHexByte(span, 2, out var green) ||
                    !TryHexByte(span, 4, out var blue)) return false;
                color = new Color(red, green, blue);
                return true;
            case 8:
                if (!TryHexByte(span, 0, out var alpha) ||
                    !TryHexByte(span, 2, out red) ||
                    !TryHexByte(span, 4, out green) ||
                    !TryHexByte(span, 6, out blue)) return false;
                color = new Color(red, green, blue, alpha);
                return true;
            default:
                return TryParseRgb(text, out color);
        }
    }

    private static bool TryParseNamed(string value, out Color color)
    {
        color = value.ToLowerInvariant() switch
        {
            "transparent" => Transparent,
            "black" => Black,
            "white" => White,
            "red" => Red,
            "green" => Green,
            "blue" => Blue,
            // Chrome CSS Color 4 system colors, light scheme (Win11 default).
            "buttonface" => FromRgb(240, 240, 240),
            "buttontext" => FromRgb(0, 0, 0),
            "buttonborder" => FromRgb(118, 118, 118),
            "field" => FromRgb(255, 255, 255),
            "fieldtext" => FromRgb(0, 0, 0),
            "canvas" => FromRgb(255, 255, 255),
            "canvastext" => FromRgb(0, 0, 0),
            "graytext" => FromRgb(109, 109, 109),
            "highlight" => FromRgb(0, 120, 215),
            "highlighttext" => White,
            "threedface" => FromRgb(240, 240, 240),
            _ => default
        };
        return color != default || value.Equals("transparent", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseRgb(string text, out Color color)
    {
        color = default;
        var rgba = text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')');
        var rgb = text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')');
        if (!rgba && !rgb) return false;
        var parts = text[(rgba ? 5 : 4)..^1].Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != (rgba ? 4 : 3) ||
            !TryParseChannel(parts[0], out var red) ||
            !TryParseChannel(parts[1], out var green) ||
            !TryParseChannel(parts[2], out var blue))
            return false;
        var alpha = (byte)255;
        if (rgba && !TryParseAlpha(parts[3], out alpha)) return false;
        color = FromRgba(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseChannel(string value, out byte result)
    {
        result = 0;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !float.IsFinite(parsed) || parsed is < 0 or > 255) return false;
        result = (byte)MathF.Round(parsed);
        return true;
    }

    private static bool TryParseAlpha(string value, out byte result)
    {
        result = 0;
        var text = value.Trim();
        var percent = text.EndsWith('%');
        if (percent) text = text[..^1];
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !float.IsFinite(parsed) || parsed < 0) return false;
        if (percent)
        {
            if (parsed > 100) return false;
            parsed /= 100f;
        }
        else if (parsed > 1) return false;
        result = (byte)Math.Clamp(MathF.Round(parsed * 255), 0, 255);
        return true;
    }

    private static bool TryHexByte(ReadOnlySpan<char> value, int index, out byte result)
    {
        result = 0;
        if (!TryHex(value[index], out var high) || !TryHex(value[index + 1], out var low)) return false;
        result = (byte)(high * 16 + low);
        return true;
    }

    private static bool TryHex(char value, out byte result)
    {
        if (value is >= '0' and <= '9')
        {
            result = (byte)(value - '0');
            return true;
        }
        if (value is >= 'a' and <= 'f')
        {
            result = (byte)(value - 'a' + 10);
            return true;
        }
        if (value is >= 'A' and <= 'F')
        {
            result = (byte)(value - 'A' + 10);
            return true;
        }
        result = 0;
        return false;
    }

    /// <summary>按 RGBA 分量比较相等。</summary>
    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Color c && Equals(c);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    /// <summary>相等运算符。</summary>
    public static bool operator ==(Color a, Color b) => a.Equals(b);
    /// <summary>不相等运算符。</summary>
    public static bool operator !=(Color a, Color b) => !a.Equals(b);

    /// <summary>返回 #AARRGGBB 形式的字符串。</summary>
    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}