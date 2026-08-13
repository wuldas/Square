using Square.Graphics;
using Square.UI;

namespace Square.Hosting;

/// <summary>元素检查快照：以根节点形式承载整个文档的检查结果。</summary>
public sealed record ElementInspectionSnapshot(ElementInspectionNode Root);

/// <summary>元素检查节点：承载单个元素的可视化与调试信息。</summary>
public sealed record ElementInspectionNode(
    int Id,
    string TagName,
    string? ElementId,
    string? ComponentName,
    Rect Bounds,
    ElementInspectionState State,
    ElementInspectionSource? Source,
    string? Text,
    int ChildCount,
    IReadOnlyList<ElementInspectionNode> Children,
    IReadOnlyList<string>? ClassNames = null,
    ElementInspectionBoxModel? BoxModel = null);

/// <summary>元素检查中的真实 CSS 盒模型四层几何。</summary>
public sealed record ElementInspectionBoxModel(
    Rect Content,
    Rect Padding,
    Rect Border,
    Rect Margin);

/// <summary>元素检查来源：承载生成该元素的源码位置信息。</summary>
public sealed record ElementInspectionSource(
    int SourceId,
    string? File,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Kind);

/// <summary>元素检查状态：承载悬停、焦点、激活、禁用等交互态。</summary>
public sealed record ElementInspectionState(
    bool Hover,
    bool Focus,
    bool Active,
    bool Disabled);

/// <summary>元素样式检查快照：承载最终应用值和内联声明。</summary>
public sealed record ElementInspectionStyleSnapshot(
    IReadOnlyDictionary<string, string> Computed,
    string InlineCssText,
    IReadOnlyList<ElementInspectionStyleRule>? MatchedRules = null);

/// <summary>元素检查中的匹配 CSS 规则。</summary>
public sealed record ElementInspectionStyleRule(
    string Selector,
    IReadOnlyList<ElementInspectionStyleDeclaration> Declarations);

/// <summary>元素检查中的 CSS 声明。</summary>
public sealed record ElementInspectionStyleDeclaration(
    string Property,
    string Value,
    bool Important);
