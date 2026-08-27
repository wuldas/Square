using System.Text;
using Square.Compiler.Syntax;

namespace Square.Compiler.Emit;

internal static class StyleAstRuntimeEmitter
{
    public static string Emit(CssStyleSheetSyntax style)
    {
        var imports = style.AtRules.Where(rule => Is(rule, "import")).Select(EmitImport);
        var media = style.AtRules.Where(rule => Is(rule, "media")).Select(EmitMedia);
        var keyframes = style.AtRules.Where(rule => Is(rule, "keyframes")).Select(EmitKeyframes);
        var regularAtRules = style.AtRules
            .Where(rule => !Is(rule, "import") && !Is(rule, "media") && !Is(rule, "keyframes"))
            .Select(EmitAtRule);
        return "new Square.CSS.Ast.CssStyleSheet(" +
               List("Square.CSS.Ast.CssRule", EmitRules(style.Rules)) + ", " +
               List("Square.CSS.Ast.CssAtRule", regularAtRules) + ") { " +
               "Imports = " + List("Square.CSS.Ast.CssImportRule", imports) + ", " +
               "KeyFrames = " + List("Square.CSS.Ast.KeyFramesRule", keyframes) + ", " +
               "MediaRules = " + List("Square.CSS.Ast.CssMediaRule", media) + " }";
    }

    private static IEnumerable<string> EmitRules(IEnumerable<CssRuleSyntax> rules)
    {
        foreach (var rule in rules)
        {
            foreach (var selector in rule.Selectors)
            {
                yield return "new Square.CSS.Ast.CssRule(" + EmitSelector(selector) + ", " +
                             EmitDeclarations(rule.Declarations) + ")";
            }
        }
    }

    private static string EmitSelector(CssSelectorSyntax selector) =>
        "new Square.CSS.Ast.ComplexSelector(" +
        List("Square.CSS.Ast.CompoundStep", selector.Steps.Select(EmitStep)) + ")";

    private static string EmitStep(CssCompoundStepSyntax step) =>
        "new Square.CSS.Ast.CompoundStep(new Square.CSS.Ast.CompoundSelector(" +
        List("Square.CSS.Ast.SimpleSelector", step.Parts.Select(EmitSimpleSelector)) + "), " +
        "Square.CSS.Ast.Combinator." + step.Combinator + ")";

    private static string EmitSimpleSelector(CssSimpleSelectorSyntax selector) =>
        "new Square.CSS.Ast.SimpleSelector(" +
        "Square.CSS.Ast.SimpleSelectorKind." + selector.Kind + ", " + Quote(selector.Name) + ", " +
        "Square.CSS.Ast.AttributeSelectorOperator." + selector.AttributeOperator + ", " +
        (selector.AttributeValue == null ? "null" : Quote(selector.AttributeValue)) + ", " +
        "Square.CSS.Ast.AttributeCaseSensitivity." + selector.AttributeCaseSensitivity + ")";

    private static string EmitDeclarations(IEnumerable<CssDeclarationSyntax> declarations) =>
        List("Square.CSS.Ast.Declaration", declarations.Select(declaration =>
            "new Square.CSS.Ast.Declaration(" + Quote(declaration.Property) + ", " +
            Quote(declaration.Value) + ", " + (declaration.Important ? "true" : "false") + ")"));

    private static string EmitAtRule(CssAtRuleSyntax rule) =>
        "new Square.CSS.Ast.CssAtRule(" + Quote(rule.Name) + ", " + Quote(rule.Prelude) + ", " +
        EmitDeclarations(rule.Declarations) + ")";

    private static string EmitMedia(CssAtRuleSyntax rule) =>
        "new Square.CSS.Ast.CssMediaRule(" +
        List("string", rule.Prelude.Split(',')
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Select(Quote)) + ", " +
        List("Square.CSS.Ast.CssRule", EmitRules(rule.Rules)) + ")";

    private static string EmitKeyframes(CssAtRuleSyntax rule) =>
        "new Square.CSS.Ast.KeyFramesRule(" + Quote(rule.Prelude) + ", " +
        List("Square.CSS.Ast.KeyFrameStop", rule.Rules.Select(stop =>
            "new Square.CSS.Ast.KeyFrameStop(" +
            Quote(stop.Selectors.FirstOrDefault()?.Text ?? string.Empty) + ", " +
            EmitDeclarations(stop.Declarations) + ")")) + ")";

    private static string EmitImport(CssAtRuleSyntax rule)
    {
        var prelude = rule.Prelude.Trim();
        var href = string.Empty;
        var conditions = string.Empty;
        if (prelude.Length > 1 && prelude[0] is '"' or '\'')
        {
            var quote = prelude[0];
            var end = prelude.IndexOf(quote, 1);
            if (end > 0)
            {
                href = prelude.Substring(1, end - 1);
                conditions = prelude.Substring(end + 1).Trim();
            }
        }
        else if (prelude.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var end = prelude.IndexOf(')');
            if (end > 4)
            {
                href = prelude.Substring(4, end - 4).Trim().Trim('"', '\'');
                conditions = prelude.Substring(end + 1).Trim();
            }
        }
        if (href.Length == 0) href = prelude;
        return "new Square.CSS.Ast.CssImportRule(" + Quote(href) + ", " + Quote(conditions) + ")";
    }

    private static bool Is(CssAtRuleSyntax rule, string name) =>
        rule.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static string List(string typeName, IEnumerable<string> values) =>
        "new System.Collections.Generic.List<" + typeName + "> { " + string.Join(", ", values) + " }";

    private static string Quote(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (var character in value)
        {
            result.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => character.ToString()
            });
        }
        return result.Append('"').ToString();
    }
}
