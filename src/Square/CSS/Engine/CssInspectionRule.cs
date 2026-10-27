using Square.CSS.Ast;
using Square.UI.ElementApi;

namespace Square.CSS.Engine;

/// <summary>供运行时检查器读取的匹配 CSS 规则快照。</summary>
/// <param name="Selector">格式化后的选择器文本。</param>
/// <param name="Declarations">检查器视角的有效声明（含运行时检查器覆盖）。</param>
public sealed record CssInspectionRule(
    string Selector,
    IReadOnlyList<Declaration> Declarations)
{
    /// <summary>该快照的真实来源（引擎、原始 AST 规则与级联来源）；仅由引擎填充。</summary>
    internal CssInspectionRuleSource? Source { get; init; }
}

/// <summary>检查器规则快照的真实来源：产生它的引擎、原始规则与级联来源。</summary>
/// <param name="Engine">拥有该规则的引擎；运行时覆盖存储在引擎内。</param>
/// <param name="Rule">引擎中当前加载的原始 AST 规则；从不被检查器改写。</param>
/// <param name="Origin">规则的级联来源。</param>
internal readonly record struct CssInspectionRuleSource(CssEngine Engine, CssRule Rule, CssCascadeOrigin Origin);
