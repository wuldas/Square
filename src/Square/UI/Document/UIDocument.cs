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

    internal CssEngine GlobalCssEngine { get; } = new();

    private DocumentStyleSheetLoader? _styleSheetLoader;

    /// <summary>承载此文档的应用窗口；未绑定到桌面宿主时为 null。</summary>
    public Square.Hosting.AppWindow? AppWindow { get; internal set; }

    /// <summary>创建带 UI/Head/Body 壳的空文档。</summary>
    public UIDocument()
    {
        Ui = new UIRootElement();
        Head = new UIHeadElement();
        Body = new UIBodyElement();
        Ui.Children.Add(Head);
        Ui.Children.Add(Body);
        SetDocumentElement(Ui);
    }

    /// <summary>
    /// 注册标签名到工厂（AOT 友好；供 <see cref="CreateElement(string)"/> 使用）。
    /// </summary>
    /// <summary>按标签名创建元素（对齐 <c>document.createElement</c>；须先注册）。</summary>
    public Element CreateElement(string tagName)
    {
        var element = ElementRegistry.Create(tagName);
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
