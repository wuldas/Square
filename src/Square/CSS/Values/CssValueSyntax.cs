namespace Square.CSS.Values;

internal static class CssValueSyntax
{
    public static bool IsGlobalKeyword(string value) => value.Trim().ToLowerInvariant() is
        "inherit" or "initial" or "unset";

    public static bool ContainsVariable(string value) =>
        value.Contains("var(", StringComparison.OrdinalIgnoreCase);

    public static bool TrySplitWhitespace(string value, out string[] tokens)
    {
        var result = new List<string>();
        var start = -1;
        var depth = 0;
        char quote = '\0';

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == '\\' && i + 1 < value.Length) i++;
                else if (c == quote) quote = '\0';
                continue;
            }

            if (c is '\'' or '"')
            {
                if (start < 0) start = i;
                quote = c;
                continue;
            }
            if (c == '(')
            {
                if (start < 0) start = i;
                depth++;
                continue;
            }
            if (c == ')')
            {
                if (depth == 0)
                {
                    tokens = [];
                    return false;
                }
                depth--;
                continue;
            }
            if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (start >= 0)
                {
                    result.Add(value[start..i]);
                    start = -1;
                }
                continue;
            }
            if (start < 0) start = i;
        }

        if (quote != '\0' || depth != 0)
        {
            tokens = [];
            return false;
        }
        if (start >= 0) result.Add(value[start..]);
        tokens = result.ToArray();
        return true;
    }
}
