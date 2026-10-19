using System.Globalization;
using System.Numerics;

namespace Square.CSS.Properties;

/// <summary>解析 CSS <c>transform</c> 属性的 2D 函数子集为变换矩阵。</summary>
internal static class CssTransformParser
{
    /// <summary>校验声明值是否为 "none" 或受支持的变换函数列表。</summary>
    public static bool IsValid(string value) => TryParse(value, out _);

    /// <summary>解析变换函数列表；"none" 与空值解析为单位矩阵。函数按 CSS 语义从右向左复合。</summary>
    public static bool TryParse(string? value, out Matrix3x2 matrix)
    {
        matrix = Matrix3x2.Identity;
        if (string.IsNullOrWhiteSpace(value)) return true;
        value = value.Trim();
        if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase)) return true;
        var index = 0;
        var sawFunction = false;
        while (index < value.Length)
        {
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            if (index >= value.Length) break;
            var start = index;
            while (index < value.Length && (char.IsLetter(value[index]) || value[index] == '-')) index++;
            if (start == index) return false;
            var name = value[start..index].ToLowerInvariant();
            while (index < value.Length && char.IsWhiteSpace(value[index])) index++;
            if (index >= value.Length || value[index] != '(') return false;
            var end = value.IndexOf(')', index);
            if (end < 0) return false;
            if (!TryParseArguments(value[(index + 1)..end], out var arguments)) return false;
            if (!TryCreateFunction(name, arguments, out var function)) return false;
            matrix = function * matrix;
            sawFunction = true;
            index = end + 1;
        }
        return sawFunction;
    }

    private static bool TryParseArguments(string text, out (float Value, string Unit)[] arguments)
    {
        arguments = [];
        var parsed = new List<(float, string)>();
        var index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ',')) index++;
            if (index >= text.Length) break;
            var start = index;
            while (index < text.Length &&
                (char.IsDigit(text[index]) || text[index] is '+' or '-' or '.' or 'e' or 'E')) index++;
            if (!float.TryParse(text[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return false;
            var unitStart = index;
            while (index < text.Length && char.IsLetter(text[index])) index++;
            var unit = text[unitStart..index].ToLowerInvariant();
            if (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != ',') return false;
            parsed.Add((number, unit));
        }
        if (parsed.Count == 0) return false;
        arguments = parsed.ToArray();
        return true;
    }

    private static bool TryCreateFunction(string name, (float Value, string Unit)[] args, out Matrix3x2 matrix)
    {
        matrix = Matrix3x2.Identity;
        switch (name)
        {
            case "matrix":
                if (args.Length != 6 || !args.All(IsNumber)) return false;
                matrix = new Matrix3x2(args[0].Value, args[1].Value, args[2].Value, args[3].Value, args[4].Value, args[5].Value);
                return true;
            case "translate":
                if (args.Length is < 1 or > 2 || !args.All(IsLength)) return false;
                matrix = Matrix3x2.CreateTranslation(args[0].Value, args.Length > 1 ? args[1].Value : 0);
                return true;
            case "translatex":
                if (args.Length != 1 || !IsLength(args[0])) return false;
                matrix = Matrix3x2.CreateTranslation(args[0].Value, 0);
                return true;
            case "translatey":
                if (args.Length != 1 || !IsLength(args[0])) return false;
                matrix = Matrix3x2.CreateTranslation(0, args[0].Value);
                return true;
            case "scale":
                if (args.Length is < 1 or > 2 || !args.All(IsNumber)) return false;
                matrix = Matrix3x2.CreateScale(args[0].Value, args.Length > 1 ? args[1].Value : args[0].Value);
                return true;
            case "scalex":
                if (args.Length != 1 || !IsNumber(args[0])) return false;
                matrix = Matrix3x2.CreateScale(args[0].Value, 1);
                return true;
            case "scaley":
                if (args.Length != 1 || !IsNumber(args[0])) return false;
                matrix = Matrix3x2.CreateScale(1, args[0].Value);
                return true;
            case "rotate":
                if (args.Length != 1 || !IsAngle(args[0])) return false;
                matrix = Matrix3x2.CreateRotation(ToRadians(args[0]));
                return true;
            case "skew":
                if (args.Length is < 1 or > 2 || !args.All(IsAngle)) return false;
                matrix = Matrix3x2.CreateSkew(ToRadians(args[0]), args.Length > 1 ? ToRadians(args[1]) : 0);
                return true;
            case "skewx":
                if (args.Length != 1 || !IsAngle(args[0])) return false;
                matrix = Matrix3x2.CreateSkew(ToRadians(args[0]), 0);
                return true;
            case "skewy":
                if (args.Length != 1 || !IsAngle(args[0])) return false;
                matrix = Matrix3x2.CreateSkew(0, ToRadians(args[0]));
                return true;
            default:
                return false;
        }
    }

    private static bool IsNumber((float Value, string Unit) argument) => argument.Unit.Length == 0;

    private static bool IsLength((float Value, string Unit) argument) =>
        argument.Unit.Length == 0 || argument.Unit == "px";

    private static bool IsAngle((float Value, string Unit) argument) =>
        argument.Unit is "" or "deg" or "rad" or "grad" or "turn";

    private static float ToRadians((float Value, string Unit) argument) => argument.Unit switch
    {
        "rad" => argument.Value,
        "grad" => argument.Value * MathF.PI / 200f,
        "turn" => argument.Value * 2f * MathF.PI,
        _ => argument.Value * MathF.PI / 180f
    };
}
