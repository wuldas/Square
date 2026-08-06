namespace Square.CSS.Ast;

/// <summary>简单选择器种类。</summary>
public enum SimpleSelectorKind
{
    /// <summary>类型选择器。</summary>
    Type,
    /// <summary>类选择器。</summary>
    Class,
    /// <summary>ID 选择器。</summary>
    Id,
    /// <summary>通配选择器。</summary>
    Universal,
    /// <summary>伪类选择器。</summary>
    PseudoClass,
    /// <summary>伪元素选择器。</summary>
    PseudoElement,
    /// <summary>属性选择器。</summary>
    Attribute
}

/// <summary>属性选择器操作符。</summary>
public enum AttributeSelectorOperator
{
    /// <summary>属性存在。</summary>
    Presence,
    /// <summary>值精确相等。</summary>
    Equals,
    /// <summary>空白分隔词列表包含指定词。</summary>
    Includes,
    /// <summary>值等于指定值或以“指定值-”开头。</summary>
    DashMatch,
    /// <summary>值以指定文本开头。</summary>
    PrefixMatch,
    /// <summary>值以指定文本结尾。</summary>
    SuffixMatch,
    /// <summary>值包含指定文本。</summary>
    SubstringMatch,
    /// <summary>无效属性选择器，始终不匹配。</summary>
    Invalid
}

/// <summary>表示一个简单选择器。</summary>
/// <param name="Kind">选择器种类。</param>
/// <param name="Name">选择器名称。</param>
/// <param name="AttributeOperator">属性选择器操作符。</param>
/// <param name="AttributeValue">属性选择器期望值。</param>
public sealed record SimpleSelector(
    SimpleSelectorKind Kind,
    string Name,
    AttributeSelectorOperator AttributeOperator = AttributeSelectorOperator.Presence,
    string? AttributeValue = null,
    AttributeCaseSensitivity AttributeCaseSensitivity = AttributeCaseSensitivity.Default);

/// <summary>属性选择器值比较方式。</summary>
public enum AttributeCaseSensitivity
{
    /// <summary>默认大小写敏感。</summary>
    Default,
    /// <summary><c>i</c> 修饰符，ASCII 大小写不敏感。</summary>
    Insensitive,
    /// <summary><c>s</c> 修饰符，显式大小写敏感。</summary>
    Sensitive
}

/// <summary>表示复合选择器，由多个简单选择器组合而成。</summary>
/// <param name="Parts">简单选择器列表。</param>
public sealed record CompoundSelector(List<SimpleSelector> Parts);

/// <summary>表示复杂选择器，由多个复合步骤组成。</summary>
/// <param name="Steps">复合步骤列表。</param>
public sealed record ComplexSelector(List<CompoundStep> Steps);

/// <summary>表示复杂选择器中的一个复合步骤，包含复合选择器与组合器。</summary>
/// <param name="Selector">复合选择器。</param>
/// <param name="Combinator">与下一步骤的组合器。</param>
public sealed record CompoundStep(CompoundSelector Selector, Combinator Combinator);

/// <summary>选择器组合器种类。</summary>
public enum Combinator
{
    /// <summary>后代组合器。</summary>
    Descendant,
    /// <summary>子代组合器。</summary>
    Child,
    /// <summary>相邻兄弟组合器。</summary>
    Adjacent,
    /// <summary>通用兄弟组合器。</summary>
    GeneralSibling
}

/// <summary>表示一条 CSS 声明。</summary>
/// <param name="Property">属性名。</param>
/// <param name="Value">属性值。</param>
/// <param name="Important">是否标记为 !important。</param>
public sealed record Declaration(string Property, string Value, bool Important = false);

/// <summary>表示一条 CSS 规则，包含选择器与声明列表。</summary>
/// <param name="Selector">复杂选择器。</param>
/// <param name="Declarations">声明列表。</param>
public sealed record CssRule(ComplexSelector Selector, List<Declaration> Declarations);

/// <summary>表示一个 CSS 样式表。</summary>
/// <param name="Rules">规则列表。</param>
/// <param name="AtRules">At 规则列表。</param>
public sealed record CssStyleSheet(List<CssRule> Rules, List<CssAtRule> AtRules)
{
    /// <summary>样式表顶部按源码顺序声明的导入规则。</summary>
    public List<CssImportRule> Imports { get; set; } = new();

    /// <summary>关键帧规则列表。</summary>
    public List<KeyFramesRule> KeyFrames { get; set; } = new();

    /// <summary>媒体规则列表。</summary>
    public List<CssMediaRule> MediaRules { get; set; } = new();
};

/// <summary>表示一条 CSS <c>@import</c> 规则。</summary>
/// <param name="Href">导入目标地址。</param>
/// <param name="Conditions">URL 后的 layer、supports 或 media 条件文本。</param>
public sealed record CssImportRule(string Href, string Conditions);

/// <summary>表示一条 At 规则。</summary>
/// <param name="Name">规则名称。</param>
/// <param name="Params">规则参数。</param>
/// <param name="Declarations">声明列表。</param>
public sealed record CssAtRule(string Name, string Params, List<Declaration> Declarations);

/// <summary>表示一条 CSS <c>@media</c> 规则。</summary>
/// <param name="MediaTypes">逗号分隔的媒体类型列表。</param>
/// <param name="Rules">媒体规则中包含的普通样式规则。</param>
public sealed record CssMediaRule(List<string> MediaTypes, List<CssRule> Rules);

/// <summary>表示关键帧中的一个停顿点。</summary>
/// <param name="Selector">停顿选择器（from/to/百分比）。</param>
/// <param name="Declarations">声明列表。</param>
public sealed record KeyFrameStop(string Selector, List<Declaration> Declarations);

/// <summary>表示一组关键帧动画规则。</summary>
/// <param name="Name">动画名称。</param>
/// <param name="Stops">关键帧停顿列表。</param>
public sealed record KeyFramesRule(string Name, List<KeyFrameStop> Stops);
