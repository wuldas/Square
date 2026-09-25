using Square.Controls;
using Square.Html;
using Square.CSS;
using Square.CSS.Engine;

namespace Square.UI;

/// <summary>
/// Square 应用文档：固定 <c>UI</c> / <c>Head</c> / <c>Body</c> 壳。
/// <see cref="Document.DocumentElement"/> 为只读的 <c>UI</c> 根；应用内容挂在 <see cref="Body"/> 下。
/// </summary>
public sealed class UIDocument : Document
{
    /// <summary>文档根元素 <c>UI</c>（即 documentElement）。</summary>
    public UIRootElement Ui { get; }

    /// <summary>文档头（元数据 / 标题栏扩展点；本阶段高度为 0）。</summary>
    public UIHeadElement Head { get; }

    /// <summary>文档体：窗口客户区内容宿主（对齐 HTML <c>body</c>）。</summary>
    public UIBodyElement Body { get; }

    /// <summary>文档独立的调度、协调和 Store 上下文。</summary>
    public UIContext Context { get; } = new();
    /// <summary>Immutable preferred resolution URI; independent of the C# root namespace.</summary>
    public string DefaultElementNamespaceUri { get; }

    /// <summary>Definitions and callback diagnostics unique to this document.</summary>
    public HtmlCustomElementRegistry CustomElements { get; }

    internal CssEngine GlobalCssEngine { get; } = new();

    private DocumentStyleSheetLoader? _styleSheetLoader;

    /// <summary>承载此文档的应用窗口；未绑定到桌面宿主时为 null。</summary>
    public Square.Hosting.AppWindow? AppWindow { get; internal set; }

    /// <summary>创建带 UI/Head/Body 壳、固定默认元素 URI 的空文档。</summary>
    public UIDocument(string defaultElementNamespaceUri = ElementRegistry.HtmlNamespaceUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultElementNamespaceUri);
        if (!Uri.TryCreate(defaultElementNamespaceUri, UriKind.Absolute, out _))
            throw new ArgumentException("The default element namespace must be an absolute URI.", nameof(defaultElementNamespaceUri));
        DefaultElementNamespaceUri = defaultElementNamespaceUri;
        ControlRegistration.RegisterDefaults();
        CustomElements = new HtmlCustomElementRegistry();
        Ui = new UIRootElement();
        Head = new UIHeadElement();
        Body = new UIBodyElement();
        Ui.Children.Add(Head);
        Ui.Children.Add(Body);
        SetDocumentElement(Ui);
    }

    /// <summary>按默认 URI 优先、否则唯一候选创建元素；定制内建用 isName 指定定义。</summary>
    public Element CreateElement(string localName, string? isName = null)
    {
        ElementRegistry.ValidateLocalName(localName);
        Element element = isName == null
            ? ElementRegistry.Create(DefaultElementNamespaceUri, localName,
                CustomElements.FindAutonomousFactory(localName))
            : CustomElements.CreateCustomizedBuiltIn(isName, localName.ToLowerInvariant());
        AssignOwnerDocument(element);
        return element;
    }

    /// <summary>只接受真实 DOM XHTML/SVG URI，包解析 URI 使用 CreateComponentElement。</summary>
    public Element CreateElementNS(string namespaceUri, string localName, string? isName = null)
    {
        ElementRegistry.ValidateLocalName(localName);
        if (namespaceUri is not (ElementRegistry.HtmlNamespaceUri or ElementRegistry.SvgNamespaceUri))
            throw new ArgumentException("CreateElementNS accepts only XHTML or SVG DOM namespace URIs.", nameof(namespaceUri));
        if (isName != null && namespaceUri != ElementRegistry.HtmlNamespaceUri)
            throw new ArgumentException("Customized built-ins require the XHTML namespace.", nameof(isName));
        var element = isName == null
            ? ElementRegistry.CreateNamespace(namespaceUri, localName,
                namespaceUri == ElementRegistry.HtmlNamespaceUri ? CustomElements.FindAutonomousFactory(localName) : null)
            : CustomElements.CreateCustomizedBuiltIn(isName, localName.ToLowerInvariant());
        AssignOwnerDocument(element);
        return element;
    }

    /// <summary>用 Square UI 或包解析 URI 显式创建组件，不把包 URI 写作 DOM namespaceURI。</summary>
    public Element CreateComponentElement(string namespaceUri, string localName)
    {
        if (namespaceUri is ElementRegistry.HtmlNamespaceUri or ElementRegistry.SvgNamespaceUri)
            throw new ArgumentException("Use CreateElementNS for XHTML and SVG DOM elements.", nameof(namespaceUri));
        var element = ElementRegistry.CreateNamespace(namespaceUri, localName);
        AssignOwnerDocument(element);
        return element;
    }

    /// <summary>强类型创建元素并设置 OwnerDocument。</summary>
    public T CreateElement<T>() where T : Element, new()
    {
        var element = new T();
        AssignOwnerDocument(element);
        return element;
    }

    /// <summary>构建 Body 下应用内容树（对子节点调用 <see cref="Element.BuildElementTree"/>）。</summary>
    public void Build()
    {
        AssignOwnerDocument(Ui);
        foreach (var child in Head.Children)
            child.BuildElementTree();
        foreach (var child in Body.Children)
            child.BuildElementTree();
    }

    /// <summary>
    /// 执行当前文档待处理的调度、结构协调与样式更新。
    /// 无窗口宿主调用时必须自行保证同一文档不会被并发访问。
    /// </summary>
    public void FlushPendingUpdates()
    {
        Context.Dispatcher.RunPending();
        Context.Reconciler.Flush();
        CssStyleReconciler.Flush(Ui);
    }

    internal void LoadGlobalCss(string path)
    {
        var styleSheet = GetStyleSheetLoader().LoadFile(path);
        AddStyleSheet(styleSheet);
    }

    internal void LoadGlobalCssText(string css)
    {
        var styleSheet = GetStyleSheetLoader().LoadText(css);
        AddStyleSheet(styleSheet);
    }

    internal void InheritGlobalStylesFrom(UIDocument source)
    {
        foreach (var styleSheet in source.StyleSheets)
        {
            LoadStyleSheetTree(styleSheet);
            AddStyleSheet(styleSheet);
        }
    }

    private void LoadStyleSheetTree(DocumentStyleSheet styleSheet)
    {
        foreach (var import in styleSheet.Imports)
            LoadStyleSheetTree(import);
        GlobalCssEngine.LoadStyleSheet(styleSheet.ParsedSheet);
    }

    private DocumentStyleSheetLoader GetStyleSheetLoader() =>
        _styleSheetLoader ??= new DocumentStyleSheetLoader(GlobalCssEngine);
}
