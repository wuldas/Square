using Square.CSS.Ast;

namespace Square.CSS.Engine;

/// <summary>供运行时检查器读取的匹配 CSS 规则快照。</summary>
public sealed record CssInspectionRule(
    string Selector,
    IReadOnlyList<Declaration> Declarations);
