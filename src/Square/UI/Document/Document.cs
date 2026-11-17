using Square.CSS;
using Square.Events;
using Square.Text.Fonts;
using Square.UI.ElementApi;

namespace Square.UI;

/// <summary>
/// 文档根（对齐 DOM <c>Document</c>）。
/// 继承：<see cref="EventTarget"/> → <see cref="Node"/> → <see cref="Document"/>（与 <see cref="Element"/> 并列）。
/// </summary>
public abstract class Document : Node
{
    private Element? _documentElement;
    private string _title = "";
    private FontFaceSet? _fonts;
    private Selection? _selection;
    private readonly List<DocumentStyleSheet> _styleSheets = [];

    /// <summary>
    /// 文档元素根，只读（对齐 <c>document.documentElement</c>）。
    /// UI 文档中为 <c>UI</c> 根元素。
    /// </summary>
    public Element DocumentElement =>
        _documentElement ?? throw new InvalidOperationException("Document has no documentElement.");

    /// <summary>文档标题（对齐 <c>document.title</c>；可同步到平台窗口标题）。</summary>
    public string Title
    {
        get => _title;
        set => _title = value ?? "";
    }

    /// <summary>
    /// 文档字体集（对齐 <c>document.fonts</c> / CSS Font Loading）。
    /// 默认与进程级 <see cref="FontFaceSet.Default"/> 共享，便于全局自定义字体。
    /// </summary>
    public FontFaceSet Fonts
    {
        get => _fonts ??= FontFaceSet.Default;
        set => _fonts = value ?? FontFaceSet.Default;
    }

    /// <summary>当前文档选择（对齐 <c>getSelection()</c>）。</summary>
    public Selection Selection => _selection ??= new Selection(this);

    /// <summary>文档按加载顺序持有的顶层全局样式表。</summary>
    public IReadOnlyList<DocumentStyleSheet> StyleSheets => _styleSheets;

    private readonly Dictionary<int, Element> _pointerCaptures = [];

    /// <summary>
    /// 把指针捕获到指定元素（对齐 <c>setPointerCapture</c>）：后续 pointermove/pointerup
    /// 派发到该元素而不是命中测试结果。元素脱离文档后捕获自动失效。
    /// </summary>
    internal void SetPointerCapture(Element element, int pointerId) =>
        _pointerCaptures[pointerId] = element;

    /// <summary>释放指定指针的捕获；只有当前持有者可以释放。</summary>
    internal void ReleasePointerCapture(Element element, int pointerId)
    {
        if (_pointerCaptures.TryGetValue(pointerId, out var current) && ReferenceEquals(current, element))
            _pointerCaptures.Remove(pointerId);
    }

    /// <summary>无条件清除指定指针的捕获（pointerup/cancel 隐式释放）。</summary>
    internal void ClearPointerCapture(int pointerId) => _pointerCaptures.Remove(pointerId);

    /// <summary>读取指针捕获元素；已从文档树移除的捕获会被丢弃。</summary>
    internal Element? GetPointerCapture(int pointerId)
    {
        if (!_pointerCaptures.TryGetValue(pointerId, out var element)) return null;
        if (element.Parent != null || ReferenceEquals(element, _documentElement)) return element;
        _pointerCaptures.Remove(pointerId);
        return null;
    }

    /// <inheritdoc />
    public override NodeType NodeTypeValue => NodeType.Document;

    /// <inheritdoc />
    public override string NodeName => "#document";

    /// <summary>由子类在构造时设置只读的 documentElement（仅允许一次）。</summary>
    protected void SetDocumentElement(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (_documentElement != null)
            throw new InvalidOperationException("documentElement is read-only once set.");
        _documentElement = element;
        AssignOwnerDocument(element);
    }

    /// <summary>按 id 查找元素（对齐 <c>getElementById</c>；泛型为 Square 扩展）。</summary>
    public T? GetElementById<T>(string id) where T : Element
    {
        if (string.IsNullOrEmpty(id) || _documentElement == null) return null;
        return FindById<T>(_documentElement, id);
    }

    /// <summary>按 id 查找元素（对齐 <c>getElementById</c>）。</summary>
    public Element? GetElementById(string id) => GetElementById<Element>(id);

    /// <summary>按标签名收集元素（对齐 <c>getElementsByTagName</c>；返回列表快照）。</summary>
    public List<T> GetElementsByTagName<T>(string tagName) where T : Element
    {
        var result = new List<T>();
        if (_documentElement == null || string.IsNullOrEmpty(tagName)) return result;
        CollectByTag(_documentElement, tagName, result);
        return result;
    }

    /// <summary>按 class 收集元素（对齐 <c>getElementsByClassName</c>；返回列表快照）。</summary>
    public List<T> GetElementsByClassName<T>(string className) where T : Element
    {
        var result = new List<T>();
        if (_documentElement == null || string.IsNullOrEmpty(className)) return result;
        CollectByClass(_documentElement, className, result);
        return result;
    }

    /// <summary>强类型查询（委托给 documentElement）。</summary>
    public T? Query<T>(string? className = null) where T : Element =>
        _documentElement?.Query<T>(className);

    /// <summary>强类型查询全部（委托给 documentElement）。</summary>
    public List<T> QueryAll<T>(string? className = null) where T : Element =>
        _documentElement?.QueryAll<T>(className) ?? [];

    /// <summary>
    /// 按 CSS 选择器子集查找文档中第一个匹配元素（对齐 <c>document.querySelector</c>，含 documentElement）。
    /// </summary>
    public Element? QuerySelector(string selectors) =>
        _documentElement == null
            ? null
            : CssSelector.QuerySelector(_documentElement, selectors, includeRoot: true);

    /// <summary>创建空 Range（对齐 <c>document.createRange()</c>）。</summary>
    public Range CreateRange() => new(this);

    /// <summary>获取当前选择（对齐 <c>document.getSelection()</c>）。</summary>
    public Selection GetSelection() => Selection;

    /// <summary>
    /// 按 CSS 选择器子集查找文档中全部匹配元素（对齐 <c>document.querySelectorAll</c>）。
    /// </summary>
    public List<Element> QuerySelectorAll(string selectors) =>
        _documentElement == null
            ? []
            : CssSelector.QuerySelectorAll(_documentElement, selectors, includeRoot: true);

    /// <summary>递归设置子树 <see cref="Node.OwnerDocument"/>。</summary>
    internal void AssignOwnerDocument(Node node)
    {
        if (node is IEmbeddedDocumentRoot embedded && embedded.EmbeddedDocument is { } document && !ReferenceEquals(this, document))
        {
            document.AssignOwnerDocument(node);
            return;
        }
        node.OwnerDocument = this;
        if (node is not Element element) return;
        foreach (var child in element.ChildNodes)
            AssignOwnerDocument(child);
    }

    internal void AddStyleSheet(DocumentStyleSheet styleSheet) => _styleSheets.Add(styleSheet);

    internal interface IEmbeddedDocumentRoot
    {
        Document? EmbeddedDocument { get; }
    }

    private static T? FindById<T>(Element node, string id) where T : Element
    {
        if (node is T typed && string.Equals(node.Id, id, StringComparison.Ordinal))
            return typed;
        foreach (var child in node.Children)
        {
            var found = FindById<T>(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static void CollectByTag<T>(Element node, string tagName, List<T> result) where T : Element
    {
        if (node is T typed &&
            string.Equals(node.TagName, tagName, StringComparison.OrdinalIgnoreCase))
            result.Add(typed);
        foreach (var child in node.Children)
            CollectByTag(child, tagName, result);
    }

    private static void CollectByClass<T>(Element node, string className, List<T> result) where T : Element
    {
        if (node is T typed && node.ClassList.Contains(className))
            result.Add(typed);
        foreach (var child in node.Children)
            CollectByClass(child, className, result);
    }
}
