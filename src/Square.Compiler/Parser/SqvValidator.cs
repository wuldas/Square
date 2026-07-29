using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Square.Compiler.Parser;

internal static class SqvValidator
{
    private static readonly HashSet<string> UnsupportedBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "component",
        "Teleport",
        "Transition",
        "TransitionGroup",
        "KeepAlive",
        "Suspense"
    };

    public static void Validate(IEnumerable<SqxNode> nodes)
    {
        foreach (var node in nodes)
            ValidateNode(node);
    }

    private static void ValidateNode(SqxNode node)
    {
        switch (node)
        {
            case SqxExpression expression:
                ValidateExpression(expression.Expression, expression.Position);
                break;
            case SqxElement element:
                ValidateElement(element);
                Validate(element.Children);
                break;
            case TemplateForDirective forDirective:
                ValidateExpression(forDirective.SourceExpression, forDirective.Position);
                if (!string.IsNullOrWhiteSpace(forDirective.KeyExpression))
                    ValidateExpression(forDirective.KeyExpression, forDirective.KeyPosition);
                Validate(forDirective.Children);
                break;
            case TemplateIfChainDirective ifDirective:
                foreach (var branch in ifDirective.Branches)
                {
                    if (!branch.IsElse)
                        ValidateExpression(branch.Condition, branch.Position);
                    Validate(branch.Children);
                }
                break;
        }
    }

    private static void ValidateElement(SqxElement element)
    {
        if (UnsupportedBuiltIns.Contains(element.TagName))
            throw new SqxParseException(
                "Vue built-in component <" + element.TagName + "> is not supported",
                element.Position,
                "SQV0007");

        var bindings = new Dictionary<string, SqxAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in element.Attributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.ArgumentExpression))
                ValidateExpression(attribute.ArgumentExpression, attribute.Position);

            var target = NormalizeTarget(attribute.Name);
            if (bindings.TryGetValue(target, out var existing))
            {
                if (!IsModelEventPair(existing, attribute))
                    throw new SqxParseException(
                        "Duplicate binding '" + target + "' on <" + element.TagName + ">",
                        attribute.Position != 0 ? attribute.Position : existing.Position,
                        "SQV0005");
            }
            else
            {
                bindings.Add(target, attribute);
            }

            if (attribute.IsExpression)
                ValidateExpression(attribute.RawValue, attribute.Position);
        }
    }

    private static void ValidateExpression(string expression, int position)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw InvalidExpression(expression, position);

        var syntax = SyntaxFactory.ParseExpression(expression);
        if (syntax.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw InvalidExpression(expression, position);
    }

    private static SqxParseException InvalidExpression(string expression, int position) =>
        new("Template expression '" + (expression ?? "") + "' is not valid C#", position, "SQV0009");

    private static bool IsModelEventPair(SqxAttribute first, SqxAttribute second) =>
        first.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase) &&
        first.IsModelEvent != second.IsModelEvent;

    private static string NormalizeTarget(string name) => name.ToLowerInvariant() switch
    {
        "__sqv_bind_object" => "__sqv_bind_object",
        "__sqv_on_object" => "__sqv_on_object",
        "__sqv_dynamic_property" => "__sqv_dynamic_property",
        "__sqv_dynamic_event" => "__sqv_dynamic_event",
        "text" => "TextContent",
        "value" => "Value",
        "checked" => "IsChecked",
        "disabled" => "IsDisabled",
        "source" => "Source",
        "image" => "ImageContent",
        "group" => "GroupName",
        "shortcut" => "ShortcutText",
        "checkable" => "IsCheckable",
        "stays-open-on-click" => "StaysOpenOnClick",
        "selected-index" => "SelectedIndex",
        "expanded" => "IsExpanded",
        "stroke-width" => "StrokeWidth",
        "fill-opacity" => "FillOpacity",
        "stroke-opacity" => "StrokeOpacity",
        _ => name
    };
}
