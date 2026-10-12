using Square.Graphics;
using Square.UI;

namespace Square.Hosting;

/// <summary>元素检查快照：以根节点形式承载整个文档的检查结果。</summary>
public sealed record ElementInspectionSnapshot(ElementInspectionNode Root);

/// <summary>元素检查变更类别：承载检查器增量同步所需的变化来源。</summary>
[Flags]
public enum ElementInspectionChangeKind
{
    /// <summary>文档树结构变化（子节点增删移动）。</summary>
    Structure = 1,
    /// <summary>元素属性变化（id/class/style 及普通属性）。</summary>
    Attributes = 2,
    /// <summary>文本内容变化。</summary>
    Text = 4,
    /// <summary>有效样式变化（内联、级联、状态与动画）。</summary>
    Styles = 8
}

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
    ElementInspectionBoxModel? BoxModel = null,
    IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>元素检查中的真实 CSS 盒模型四层几何。</summary>
public sealed record ElementInspectionBoxModel(
    Rect Content,
    Rect Padding,
    Rect Border,
    Rect Margin);

/// <summary>Inspector 高亮配置：承载盒模型四层环与内容区域的真实绘制颜色。</summary>
public readonly record struct ElementInspectionHighlightOptions(
    Color ContentColor,
    Color PaddingColor,
    Color BorderColor,
    Color MarginColor);

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
    IReadOnlyList<ElementInspectionStyleRule>? MatchedRules = null,
    ElementInspectionStyleText? InlineStyle = null);

/// <summary>元素检查中的匹配 CSS 规则。</summary>
public sealed record ElementInspectionStyleRule(
    string Selector,
    IReadOnlyList<ElementInspectionStyleDeclaration> Declarations,
    string Origin = "regular",
    ElementInspectionStyleText? Style = null);

/// <summary>元素检查中的 CSS 声明。</summary>
public sealed record ElementInspectionStyleDeclaration(
    string Property,
    string Value,
    bool Important,
    ElementInspectionStyleRange? Range = null,
    string? Text = null,
    bool Disabled = false,
    bool ParsedOk = true);

public readonly record struct ElementInspectionStyleRange(
    int StartLine, int StartColumn, int EndLine, int EndColumn);

public sealed record ElementInspectionStyleText(
    string StyleSheetId, string Text, ElementInspectionStyleRange Range,
    bool IsInline, int? OwnerDebugId, string Origin,
    IReadOnlyList<ElementInspectionStyleDeclaration> Declarations);

public sealed record ElementInspectionStyleTextEdit(
    string StyleSheetId, ElementInspectionStyleRange Range, string Text);

public sealed record ElementInspectionComputedStyleSnapshot(
    int Id, IReadOnlyDictionary<string, string> Properties);
