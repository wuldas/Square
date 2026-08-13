using System;
using System.Reflection;
using Square.Controls;
using Square.CSS.Engine;
using Square.CSS.Tokenizer;
using Square.Events;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Runtime.State;
using Square.Rendering;
using Square.UI;
using Square.UI.Svg;
using Xunit;

namespace Square.UI.Tests;

public class DocumentTests
{
    private static bool IsRenderRequested(DesktopApplication application)
    {
        var field = typeof(DesktopApplication).GetField(
            "_renderRequested",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<bool>(field!.GetValue(application));
    }

    private static void ClearRenderRequest(DesktopApplication application)
    {
        var field = typeof(DesktopApplication).GetField(
            "_renderRequested",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(application, false);
    }

    private static void InvokeHandleMouse(
        DesktopApplication application,
        Square.Graphics.Point point,
        MouseAction action,
        MouseButton button = MouseButton.Left)
    {
        var method = typeof(DesktopApplication).GetMethod(
            "HandleMouse",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(application, [point, action, button]);
    }

    private static void SetPrivateField<T>(DesktopApplication application, string name, T value)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(application, value);
    }

    private static T? GetPrivateField<T>(DesktopApplication application, string name)
    {
        var field = typeof(DesktopApplication).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T?)field!.GetValue(application);
    }

    private static bool HasVisualInvalidation(Element element)
    {
        var method = typeof(DesktopApplication).GetMethod(
            "HasVisualInvalidation",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, [element]));
    }

    [Theory]
    [InlineData(885f, 943f, 885.3333f, 942.6667f, true)]
    [InlineData(885f, 943f, 887f, 943f, false)]
    public void LayoutSizeComparisonAllowsSubpixelDpiRounding(
        float actualWidth, float actualHeight, float requestedWidth, float requestedHeight, bool expected)
    {
        var method = typeof(DesktopApplication).GetMethod(
            "AreLayoutSizesEquivalent",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null,
            [new Square.Graphics.Size(actualWidth, actualHeight), new Square.Graphics.Size(requestedWidth, requestedHeight)]));
    }

    public DocumentTests()
    {
        ControlRegistration.RegisterDefaults();
    }

    [Fact]
    public void UIDocumentHasReadonlyShell()
    {
        var doc = new UIDocument();

        Assert.Same(doc.Ui, doc.DocumentElement);
        Assert.Equal("UI", doc.DocumentElement.TagName);
        Assert.Equal("Head", doc.Head.TagName);
        Assert.Equal("Body", doc.Body.TagName);
        Assert.Contains(doc.Head, doc.Ui.Children);
        Assert.Contains(doc.Body, doc.Ui.Children);
        Assert.Same(doc, doc.Body.OwnerDocument);
    }

    [Fact]
    public void FontIconUsesRequestedFamilyAndGlyph()
    {
        var icon = new FontIcon("Product Icons", "\uE000");

        Assert.Equal("\uE000", icon.Glyph);
        Assert.Equal("\uE000", icon.TextContent);
        Assert.Equal("Product Icons", icon.FontFamily);
        Assert.Equal("'Product Icons'", icon.Style.Get("font-family"));
        Assert.Equal("400", icon.Style.Get("font-weight"));
        Assert.Equal("normal", icon.Style.Get("font-style"));
        Assert.Equal("none", icon.Style.Get("user-select"));

        icon.Glyph = "\uE001";

        Assert.Equal("\uE001", icon.TextContent);
    }

    [Fact]
    public void SplitterTracksDragAndRaisesInputAndChange()
    {
        var splitter = new Splitter { Value = 300, Minimum = 240, Maximum = 420 };
        var inputCount = 0;
        var changeCount = 0;
        splitter.AddEventListener("input", () => inputCount++);
        splitter.AddEventListener("change", () => changeCount++);

        splitter.HandlePointerDown(new Square.Graphics.Point(100, 0));
        splitter.HandlePointerMove(new Square.Graphics.Point(170, 0));
        splitter.HandlePointerUp(new Square.Graphics.Point(200, 0));

        Assert.Equal(400, splitter.Value);
        Assert.Equal(2, inputCount);
        Assert.Equal(1, changeCount);
    }

    [Fact]
    public void SplitterReadsIntegerPropertiesAsFloatValues()
    {
        var splitter = new Splitter();
        splitter.SetProperty("Value", 320);
        splitter.SetProperty("Minimum", 250);
        splitter.SetProperty("Maximum", 390);

        Assert.Equal(320, splitter.Value);
        Assert.Equal(250, splitter.Minimum);
        Assert.Equal(390, splitter.Maximum);

        splitter.HandlePointerDown(new Square.Graphics.Point(450, 550));
        splitter.HandlePointerMove(new Square.Graphics.Point(500, 550));

        Assert.Equal(370, splitter.Value);
    }

    [Fact]
    public void SplitContainerResizesPanesOnSplitterDrag()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        var container = new SplitContainer { Value = 300, Minimum = 200, Maximum = 500, SplitterThickness = 8 };
        root.Children.Add(container);

        Layout(root, new Size(800, 600));

        Assert.Equal(300, container.First.Geometry.Width, precision: 1);
        Assert.Equal(8, container.Splitter.Geometry.Width, precision: 1);
        // 面板各向分隔条延伸一半厚度（4px），在接缝处无缝衔接；
        // Second 延伸到分隔条区域，宽度 = 容器 - First。
        Assert.Equal(800 - 300, container.Second.Geometry.Width, precision: 1);
        Assert.Equal(container.First.Geometry.Right, container.Second.Geometry.X, precision: 1);
        Assert.Equal(container.Splitter.Geometry.X, container.First.Geometry.Right - 4, precision: 1);
        Assert.Equal(container.Splitter.Geometry.Right, container.Second.Geometry.X + 4, precision: 1);

        container.Splitter.HandlePointerDown(new Square.Graphics.Point(400, 300));
        container.Splitter.HandlePointerMove(new Square.Graphics.Point(450, 300));
        container.Splitter.HandlePointerUp(new Square.Graphics.Point(450, 300));

        Assert.Equal(350, container.Value);

        Layout(root, new Size(800, 600));

        Assert.Equal(350, container.First.Geometry.Width, precision: 1);
        Assert.Equal(800 - 350, container.Second.Geometry.Width, precision: 1);
        Assert.Equal(container.First.Geometry.Right, container.Second.Geometry.X, precision: 1);
        Assert.Equal(container.Splitter.Geometry.X, container.First.Geometry.Right - 4, precision: 1);
        Assert.Equal(container.Splitter.Geometry.Right, container.Second.Geometry.X + 4, precision: 1);
    }

    [Fact]
    public void SplitContainerKeepsSplitterValueWhenMaximumIsSetAfterValue()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        var container = new SplitContainer
        {
            Value = 780,
            Minimum = 480,
            Maximum = 980,
            SplitterThickness = 1
        };
        root.Children.Add(container);

        Layout(root, new Size(1280, 760));

        Assert.Equal(780, container.Value);
        Assert.Equal(780, container.Splitter.Value);
        Assert.Equal(780, container.First.Geometry.Width, precision: 1);

        container.Splitter.HandlePointerDown(new Point(780, 100));
        container.Splitter.HandlePointerMove(new Point(790, 100));

        Assert.Equal(790, container.Value);
        Assert.Equal(790, container.Splitter.Value);
    }

    [Fact]
    public void SplitContainerNonSeamlessLeavesVisibleGapBetweenPanes()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "row");
        var container = new SplitContainer { Value = 300, Minimum = 200, Maximum = 500, SplitterThickness = 8, IsSeamless = false };
        root.Children.Add(container);

        Layout(root, new Size(800, 600));

        // 有缝模式：分隔条独立显示在面板之间（First 右缘 = Splitter 左缘）。
        Assert.Equal(300, container.First.Geometry.Width, precision: 1);
        Assert.Equal(8, container.Splitter.Geometry.Width, precision: 1);
        Assert.Equal(800 - 300 - 8, container.Second.Geometry.Width, precision: 1);
        Assert.Equal(container.First.Geometry.Right, container.Splitter.Geometry.X, precision: 1);
        Assert.Equal(container.Splitter.Geometry.Right, container.Second.Geometry.X, precision: 1);

        // 切换为无缝：面板延伸覆盖分隔条，接缝闭合。
        container.IsSeamless = true;
        Layout(root, new Size(800, 600));

        Assert.Equal(container.First.Geometry.Right, container.Second.Geometry.X, precision: 1);
        Assert.Equal(container.Splitter.Geometry.X, container.First.Geometry.Right - 4, precision: 1);

        // 切回有缝：恢复间隙。
        container.IsSeamless = false;
        Layout(root, new Size(800, 600));

        Assert.Equal(container.First.Geometry.Right, container.Splitter.Geometry.X, precision: 1);
    }

    [Fact]
    public void SplitContainerHorizontalModeAdjustsHeights()
    {
        var root = new View();
        root.Style.Set("display", "flex");
        root.Style.Set("flex-direction", "column");
        var container = new SplitContainer { IsVertical = false, Value = 200, Minimum = 100, Maximum = 400, SplitterThickness = 6 };
        root.Children.Add(container);

        Layout(root, new Size(600, 500));

        Assert.Equal(200, container.First.Geometry.Height, precision: 1);
        Assert.Equal(500 - 200, container.Second.Geometry.Height, precision: 1);
        Assert.Equal(container.First.Geometry.Bottom, container.Second.Geometry.Y, precision: 1);

        container.Splitter.HandlePointerDown(new Square.Graphics.Point(300, 300));
        container.Splitter.HandlePointerMove(new Square.Graphics.Point(300, 350));
        container.Splitter.HandlePointerUp(new Square.Graphics.Point(300, 350));

        Assert.Equal(250, container.Value);

        Layout(root, new Size(600, 500));

        Assert.Equal(250, container.First.Geometry.Height, precision: 1);
        Assert.Equal(500 - 250, container.Second.Geometry.Height, precision: 1);
        Assert.Equal(container.First.Geometry.Bottom, container.Second.Geometry.Y, precision: 1);
    }

    [Fact]
    public void SplitContainerRegistersFromElementRegistry()
    {
        var container = Assert.IsType<SplitContainer>(ElementRegistry.Create("SplitContainer"));
        Assert.NotNull(container.First);
        Assert.NotNull(container.Second);
        Assert.NotNull(container.Splitter);
        Assert.Equal(3, container.Children.Count);
    }

    private static void Layout(View root, Size size)
    {
        var layout = new LayoutEngine();
        layout.Measure(root, size);
        layout.Arrange(root, new Rect(0, 0, size.Width, size.Height));
    }

    [Fact]
    public void ReversedSplitterClampsValue()
    {
        var splitter = new Splitter
        {
            Value = 320,
            Minimum = 260,
            Maximum = 400,
            IsReversed = true
        };

        splitter.HandlePointerDown(new Square.Graphics.Point(100, 0));
        splitter.HandlePointerMove(new Square.Graphics.Point(200, 0));

        Assert.Equal(260, splitter.Value);
    }

    [Fact]
    public void SplitterDoesNotRaiseInputWhenClampedValueDoesNotChange()
    {
        var splitter = new Splitter { Value = 240, Minimum = 240, Maximum = 420 };
        var inputCount = 0;
        splitter.AddEventListener("input", () => inputCount++);

        splitter.HandlePointerDown(new Square.Graphics.Point(100, 0));
        splitter.HandlePointerMove(new Square.Graphics.Point(20, 0));
        splitter.HandlePointerMove(new Square.Graphics.Point(10, 0));

        Assert.Equal(240, splitter.Value);
        Assert.Equal(0, inputCount);
    }

    [Fact]
    public void DesktopApplicationCoalescesSplitterMovesUntilRender()
    {
        var window = new AppWindow("Splitter");
        var splitter = new Splitter { Value = 300, Minimum = 240, Maximum = 420 };
        window.Load(splitter);
        var application = new DesktopApplication(window);
        SetPrivateField<IPlatformHost>(application, "_host", new SplitterTestHost());
        SetPrivateField(application, "_draggingSplitter", splitter);
        splitter.HandlePointerDown(new Square.Graphics.Point(100, 0));

        InvokeHandleMouse(application, new Square.Graphics.Point(120, 0), MouseAction.Move);
        InvokeHandleMouse(application, new Square.Graphics.Point(170, 0), MouseAction.Move);

        Assert.Equal(300, splitter.Value);
        Assert.Equal(new Square.Graphics.Point(170, 0),
            GetPrivateField<Square.Graphics.Point?>(application, "_pendingSplitterPoint"));
        Assert.True(IsRenderRequested(application));

        var flush = typeof(DesktopApplication).GetMethod(
            "FlushPendingSplitterMove",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(flush);
        flush!.Invoke(application, null);

        Assert.Equal(370, splitter.Value);
        Assert.Null(GetPrivateField<Square.Graphics.Point?>(application, "_pendingSplitterPoint"));
        SetPrivateField<IPlatformHost?>(application, "_host", null);
    }

    [Fact]
    public void BodyHostsApplicationContent()
    {
        var doc = new UIDocument();
        var view = new View();
        doc.Body.Children.Add(view);

        Assert.Same(doc.Body, view.Parent);
        Assert.Same(doc, view.OwnerDocument);
        Assert.Same(doc.Context.Reconciler, view.Reconciler);
        Assert.Same(doc.Context.Stores, view.Stores);
    }

    [Fact]
    public void DocumentsOwnIndependentUiContexts()
    {
        var first = new UIDocument();
        var second = new UIDocument();

        Assert.NotSame(first.Context.Dispatcher, second.Context.Dispatcher);
        Assert.NotSame(first.Context.Reconciler, second.Context.Reconciler);
        Assert.NotSame(first.Context.Stores, second.Context.Stores);
    }

    [Fact]
    public void DesktopApplicationExposesWindowToDocumentAndElements()
    {
        var window = new AppWindow("Initial", 800, 600);
        var content = new View();
        window.Load(content);
        var application = new DesktopApplication(window);

        Assert.Same(application.MainWindow, content.AppWindow);
        Assert.Same(application.MainWindow, content.AppWindow);
        Assert.Equal("Initial", application.MainWindow.Title);

        application.MainWindow.Title = "Updated";

        Assert.Equal("Updated", window.Document.Title);
    }

    [Fact]
    public void DesktopApplicationShowsTooltipForHoveredElement()
    {
        var window = new AppWindow("Tooltip", 320, 200);
        var root = new View { Geometry = new Square.Graphics.Rect(0, 0, 320, 200) };
        var button = new Button("Icon")
        {
            Tooltip = "按钮提示",
            Geometry = new Square.Graphics.Rect(10, 10, 80, 30)
        };
        root.Children.Add(button);
        window.Load(root);
        var application = new DesktopApplication(window);
        SetPrivateField<IPlatformHost>(application, "_host", new SplitterTestHost());
        window.WindowDocument.Build();
        GetPrivateField<DisplayTree>(application, "_displayTree")!.BuildFrom(application.Document.DocumentElement);

        InvokeHandleMouse(application, new Square.Graphics.Point(20, 20), MouseAction.Move);

        var tooltip = GetPrivateField<Popup>(application, "_tooltipPopup");
        Assert.NotNull(tooltip);
        Assert.True(tooltip!.IsOpen);
        Assert.Same(button, tooltip.Anchor);
        Assert.Equal("按钮提示", Assert.IsType<Square.Controls.Text>(Assert.Single(tooltip.Children)).TextContent);

        InvokeHandleMouse(application, new Square.Graphics.Point(200, 150), MouseAction.Move);

        Assert.False(tooltip.IsOpen);
        SetPrivateField<IPlatformHost?>(application, "_host", null);
    }

    [Fact]
    public void PopupAnchorBoundsFollowScrolledAncestor()
    {
        var scroll = new ScrollViewer
        {
            Geometry = new Square.Graphics.Rect(0, 0, 200, 100)
        };
        scroll.SetScrollContentSize(new Square.Graphics.Size(200, 300));
        scroll.ScrollTop = 60;
        var anchor = new Button("Icon")
        {
            Geometry = new Square.Graphics.Rect(20, 120, 30, 30)
        };
        scroll.Children.Add(anchor);
        var popup = new Popup
        {
            Anchor = anchor,
            Geometry = new Square.Graphics.Rect(0, 0, 80, 20),
            VerticalOffset = 4
        };

        var bounds = popup.PopupBounds;

        Assert.Equal(20, bounds.X);
        Assert.Equal(94, bounds.Y);
    }

    [Fact]
    public void AttachedLayoutInvalidationRequestsApplicationFrame()
    {
        var window = new AppWindow("Layout");
        var root = new View();
        var text = new Square.Controls.Text("Initial");
        root.Children.Add(text);
        window.Load(root);
        var application = new DesktopApplication(window);
        window.WindowDocument.Build();
        ClearRenderRequest(application);

        text.TextContent = "Updated";

        Assert.True(root.IsLayoutDirty);
        Assert.True(IsRenderRequested(application));
    }

    [Fact]
    public void AttachedPaintInvalidationRequestsApplicationFrame()
    {
        var window = new AppWindow("Paint");
        var view = new View();
        window.Load(view);
        var application = new DesktopApplication(window);
        window.WindowDocument.Build();
        ClearRenderRequest(application);

        view.InvalidatePaint();

        Assert.True(view.NeedsPaint);
        Assert.True(IsRenderRequested(application));
    }

    [Fact]
    public void RightMouseUpDispatchesContextMenuWithClientCoordinates()
    {
        var window = new AppWindow("Context menu", 200, 100);
        var target = new View { Geometry = new Square.Graphics.Rect(0, 0, 200, 100) };
        window.Load(target);
        var application = new DesktopApplication(window);
        SetPrivateField<IPlatformHost>(application, "_host", new SplitterTestHost());
        Assert.IsType<DisplayTree>(GetPrivateField<DisplayTree>(application, "_displayTree")).BuildFrom(target);
        PointerEvent? received = null;
        target.AddEventListener<PointerEvent>(StandardEvents.ContextMenu, e => received = e);

        InvokeHandleMouse(application, new Square.Graphics.Point(32, 18), MouseAction.Up, MouseButton.Right);

        Assert.NotNull(received);
        Assert.Equal(32, received.ClientX);
        Assert.Equal(18, received.ClientY);
        Assert.Equal(2, received.Button);
        Assert.True(received.IsTrusted);
    }

    [Fact]
    public void InvalidationBeforeApplicationBindingDoesNotThrow()
    {
        var window = new AppWindow("Before binding");
        var text = new Square.Controls.Text("Initial");
        window.Load(text);

        var exception = Record.Exception(() => text.TextContent = "Updated");

        Assert.Null(exception);
        Assert.True(text.IsLayoutDirty);
    }

    [Fact]
    public void HiddenDirtySubtreeDoesNotCountAsVisualWork()
    {
        var root = new View();
        var hidden = new View { IsVisible = false };
        hidden.Children.Add(new Square.Controls.Text("hidden"));
        root.Children.Add(hidden);
        root.ClearLayoutDirty();
        root.ClearPaintDirty();

        Assert.True(hidden.IsLayoutDirty);
        Assert.True(hidden.NeedsPaint);
        Assert.False(HasVisualInvalidation(root));
    }

    [Fact]
    public void DisplayNoneDirtySubtreeDoesNotCountAsVisualWork()
    {
        var root = new View();
        var hidden = new View();
        hidden.Style.Set("display", "none");
        hidden.Children.Add(new Square.Controls.Text("hidden"));
        root.Children.Add(hidden);
        root.ClearLayoutDirty();
        root.ClearPaintDirty();

        Assert.True(hidden.IsLayoutDirty);
        Assert.True(hidden.NeedsPaint);
        Assert.False(HasVisualInvalidation(root));
    }

    [Fact]
    public void AppWindowOwnsContentAndReplacesItBeforeRun()
    {
        var window = new AppWindow("Window", 640, 480);
        var first = new View();
        var second = new Button();

        window.Load(first);
        window.Load(second);

        Assert.Same(second, window.Content);
        Assert.Null(first.Parent);
        Assert.Same(window.Document, second.OwnerDocument);
        Assert.Same(window, second.AppWindow);
        Assert.Equal(new Square.Graphics.Size(640, 480), window.ClientSize);
        Assert.Equal(IntPtr.Zero, window.NativeWindow);
    }

    [Fact]
    public void AppWindowLoadsMultipleGlobalCssSourcesInOrder()
    {
        var window = new AppWindow("Styles");
        var button = new Button("Styled");
        window.Load(button);
        window.LoadGlobalCssText(
            "Button { color: red; background: white; }",
            "Button { color: blue; }");

        ApplyWindowStyles(window);

        Assert.Equal("blue", button.Style.Get("color"));
        Assert.Equal("white", button.Style.Get("background"));
        CssStyleReconciler.UnregisterScopesForTree(window.WindowDocument.DocumentElement);
    }

    [Fact]
    public void AppWindowLoadsMultipleGlobalCssFilesRelativeToApplicationDirectory()
    {
        var window = new AppWindow("Styles");
        var button = new Button("Styled");
        window.Load(button);

        window.LoadGlobalCss("global-base.css", "global-overrides.css");
        ApplyWindowStyles(window);

        Assert.Equal("18px", button.Style.Get("font-size"));
        Assert.Equal("green", button.Style.Get("color"));
        CssStyleReconciler.UnregisterScopesForTree(window.WindowDocument.DocumentElement);
    }

    [Fact]
    public void ComponentCssOverridesGlobalCssAtEqualSpecificity()
    {
        var window = new AppWindow("Styles");
        var button = new Button("Styled");
        window.Load(button);
        window.LoadGlobalCssText("Button { color: red; background: white; }");
        window.RegisterGlobalCssScope(window.WindowDocument.DocumentElement);

        var componentEngine = new CssEngine();
        componentEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button { color: blue; }").Tokenize()).Parse());
        componentEngine.ApplyStylesToTree(button);
        CssStyleReconciler.ReapplyScopesToTree(window.WindowDocument.DocumentElement);

        Assert.Equal("blue", button.Style.Get("color"));
        Assert.Equal("white", button.Style.Get("background"));
        CssStyleReconciler.UnregisterScopesForTree(window.WindowDocument.DocumentElement);
    }

    [Fact]
    public void HoverFlushDoesNotReapplySiblingComponentScope()
    {
        var root = new View();
        var left = new View();
        var right = new View();
        var button = new Button();
        root.Children.Add(left);
        root.Children.Add(right);
        left.Children.Add(button);

        var leftEngine = new CssEngine();
        leftEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "Button:hover { color: red; }").Tokenize()).Parse());
        leftEngine.ApplyStylesToTree(left);

        var rightEngine = new CssEngine();
        rightEngine.LoadStyleSheet(new CssParser(new CssTokenizer(
            "View { color: blue; }").Tokenize()).Parse());
        rightEngine.ApplyStylesToTree(right);
        right.Style.SetCascaded("color", "sentinel", int.MaxValue);

        button.SetState(ElementState.Hover, true);
        CssStyleReconciler.Flush();

        Assert.Equal("red", button.Style.Get("color"));
        Assert.Equal("sentinel", right.Style.Get("color"));
        CssStyleReconciler.UnregisterScopesForTree(root);
    }

    [Fact]
    public void AppWindowLoadsNestedCssImportsRelativeToEachStyleSheet()
    {
        var window = new AppWindow("Imports");
        var button = new Button("Styled");
        window.Load(button);

        window.LoadGlobalCss("css-imports/root.css");
        ApplyWindowStyles(window);

        Assert.Equal("blue", button.Style.Get("color"));
        Assert.Equal("19px", button.Style.Get("font-size"));
        Assert.Equal("white", button.Style.Get("background"));
        var rootSheet = Assert.Single(window.Document.StyleSheets);
        Assert.EndsWith(Path.Combine("css-imports", "root.css"), rootSheet.Href);
        var baseSheet = Assert.Single(rootSheet.Imports);
        var paletteSheet = Assert.Single(baseSheet.Imports);
        Assert.EndsWith(Path.Combine("css-imports", "palette.css"), paletteSheet.Href);
        CssStyleReconciler.UnregisterScopesForTree(window.WindowDocument.DocumentElement);
    }

    [Fact]
    public void AppWindowRejectsCircularCssImports()
    {
        var window = new AppWindow("Imports");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            window.LoadGlobalCss("css-imports/cycle-a.css"));

        Assert.Contains("Circular CSS @import", exception.Message);
        Assert.Empty(window.Document.StyleSheets);
    }

    [Fact]
    public void AppWindowRejectsUnsupportedConditionalCssImports()
    {
        var window = new AppWindow("Imports");

        var exception = Assert.Throws<NotSupportedException>(() =>
            window.LoadGlobalCssText("@import \"theme.css\" screen;"));

        Assert.Contains("Conditional CSS @import", exception.Message);
    }

    [Fact]
    public void RelativeCssImportRequiresFileBackedStyleSheet()
    {
        var window = new AppWindow("Imports");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            window.LoadGlobalCssText("@import \"theme.css\";"));

        Assert.Contains("requires a stylesheet loaded from a file", exception.Message);
    }

    private static void ApplyWindowStyles(AppWindow window)
    {
        var root = window.WindowDocument.DocumentElement;
        window.RegisterGlobalCssScope(root);
        window.WindowDocument.Build();
        CssStyleReconciler.ReapplyScopesToTree(root);
    }

    [Fact]
    public void AppWindowRejectsInvalidDimensionsAndMultipleApplications()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppWindow("Window", 0, 480));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppWindow("Window", 640, 0));

        var window = new AppWindow("Window");
        window.Load(new View());
        _ = new DesktopApplication(window);

        Assert.Throws<InvalidOperationException>(() => new DesktopApplication(window));
    }

    [Fact]
    public void CustomTitleBarIsLoadedIntoWindowHead()
    {
        var window = new AppWindow("Window");
        var titleBar = new TitleBar { PreferredHeight = 42 };

        window.LoadCustomTitleBar(titleBar);

        Assert.Equal(TitleStyle.Custom, window.TitleStyle);
        Assert.Same(titleBar, window.CustomTitleBar);
        Assert.Same(window.Document, titleBar.OwnerDocument);
        Assert.Same(window, titleBar.AppWindow);
    }

    [Fact]
    public void AppWindowUsesResizableBorderByDefaultAndPassesConfiguredStyleToHost()
    {
        var window = new AppWindow("Window")
        {
            BorderStyle = BorderStyle.Fixed
        };

        var hostInfo = window.CreateHostInfo();

        Assert.Equal(BorderStyle.Fixed, hostInfo.BorderStyle);
        Assert.Equal(BorderStyle.Resizable, new AppWindow("Default").BorderStyle);
    }

    [Fact]
    public void AppWindowPassesOwnerAndModalStateToHost()
    {
        var window = new AppWindow("Dialog", 480, 320)
        {
            OwnerHandle = new IntPtr(42),
            IsModal = true
        };

        var hostInfo = window.CreateHostInfo();

        Assert.Equal(new IntPtr(42), hostInfo.OwnerHandle);
        Assert.True(hostInfo.IsModal);
    }

    [Fact]
    public async Task AppWindowRejectsChildWindowsWithoutRunningOwner()
    {
        var window = new AppWindow("Owner");

        Assert.Throws<InvalidOperationException>(() => window.Open(new View()));
        Assert.Throws<InvalidOperationException>(() => window.Open(new View(), customTitleBar: new TitleBar()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => window.OpenDialog(new View()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            window.OpenDialog(new View(), customTitleBar: new TitleBar()));
    }

    [Fact]
    public void TitleBarRendersIconTitleAndControlSlots()
    {
        var window = new AppWindow("Window");
        var titleBar = new TitleBar();
        var icon = new Square.Controls.Text("I");
        var title = new Square.Controls.Text("Custom title");
        var control = new Button("Menu");
        titleBar.Slots.Set("icon", parent => parent.Children.Add(icon));
        titleBar.Slots.Set("", parent => parent.Children.Add(title));
        titleBar.Slots.Set("control", parent => parent.Children.Add(control));
        window.LoadCustomTitleBar(titleBar);

        titleBar.BuildElementTree();

        Assert.Equal(3, titleBar.Children.Count);
        Assert.Same(icon, titleBar.Children[0].Children[0]);
        Assert.Same(title, titleBar.Children[1].Children[0]);
        Assert.Same(control, titleBar.Children[2].Children[0]);
    }

    [Fact]
    public void TitleBarProvidesDefaultTitleAndWindowControls()
    {
        var window = new AppWindow("Default title");
        var titleBar = new TitleBar();
        window.LoadCustomTitleBar(titleBar);

        titleBar.BuildElementTree();

        var titleHost = titleBar.Children[1];
        var controlHost = titleBar.Children[2];
        Assert.Equal("Default title",
            Assert.IsType<Square.Controls.Text>(Assert.Single(titleHost.Children)).TextContent);
        Assert.Equal(3, controlHost.Children.Count);
        Assert.Equal(new[] { "最小化", "最大化", "关闭" },
            controlHost.Children.Cast<Button>().Select(button => button.Tooltip ?? "").ToArray());
        Assert.All(controlHost.Children, child =>
        {
            var button = Assert.IsAssignableFrom<Button>(child);
            var icon = Assert.IsType<Square.Controls.Text>(Assert.Single(button.Children));
            Assert.Equal("'Square Iconfont'", icon.Style.Get("font-family"));
            Assert.Single(icon.TextContent);
            Assert.NotNull(new Square.Text.Glyph.SystemGlyphRasterizer().Rasterize(
                new Square.Graphics.Font("Square Iconfont", 16), icon.TextContent[0]));
        });
        Assert.NotEqual(
            "Square Iconfont",
            Square.Text.Glyph.FontCollection.Shared.Resolve("Missing family", 'A')?.Family);
    }

    [Fact]
    public void ReactiveBindingUsesDocumentDispatcher()
    {
        var document = new UIDocument();
        var text = new Square.Controls.Text();
        document.Body.Children.Add(text);
        using var store = new Store<string>("initial");

        text.BindProperty("TextContent", store);
        var worker = new Thread(() => store.Set("updated"));
        worker.Start();
        worker.Join();

        document.Context.Dispatcher.Run();
        Assert.Equal("updated", text.TextContent);
    }

    [Fact]
    public void DocumentSelectionDispatchesSelectionChange()
    {
        var document = new UIDocument();
        var text = new Square.UI.Text("hello");
        document.Body.ChildNodes.Add(text);
        var changes = 0;
        document.AddEventListener(StandardEvents.SelectionChange, () => changes++);
        var range = document.CreateRange();
        range.SetStart(text, 1);
        range.SetEnd(text, 4);

        document.GetSelection().AddRange(range);
        document.GetSelection().RemoveAllRanges();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void CreateElementUsesRegistry()
    {
        var doc = new UIDocument();
        var text = doc.CreateElement("Text");

        Assert.IsType<Square.Controls.Text>(text);
        Assert.Same(doc, text.OwnerDocument);
    }

    [Fact]
    public void CreateElementRegistersScrollViewer()
    {
        var doc = new UIDocument();

        var scroller = doc.CreateElement("ScrollViewer");

        Assert.IsType<ScrollViewer>(scroller);
        Assert.Same(doc, scroller.OwnerDocument);
    }

    [Theory]
    [InlineData("VirtualList", typeof(VirtualList))]
    [InlineData("VirtualTree", typeof(VirtualTree))]
    public void CreateElementRegistersVirtualizedControls(string tag, Type expectedType)
    {
        var doc = new UIDocument();

        var element = doc.CreateElement(tag);

        Assert.IsType(expectedType, element);
        Assert.Same(doc, element.OwnerDocument);
    }

    [Fact]
    public void CreateElementRegistersPopup()
    {
        var doc = new UIDocument();

        var popup = doc.CreateElement("Popup");

        Assert.IsType<Popup>(popup);
        Assert.Same(doc, popup.OwnerDocument);
    }

    [Fact]
    public void CreateElementRegistersDialog()
    {
        var doc = new UIDocument();

        var dialog = doc.CreateElement("Dialog");

        Assert.IsType<Dialog>(dialog);
        Assert.Same(doc, dialog.OwnerDocument);
    }

    [Theory]
    [InlineData("MenuBar", typeof(MenuBar))]
    [InlineData("Menu", typeof(Menu))]
    [InlineData("ContextMenu", typeof(ContextMenu))]
    [InlineData("MenuItem", typeof(MenuItem))]
    [InlineData("MenuSeparator", typeof(MenuSeparator))]
    public void CreateElementRegistersMenuControls(string tag, Type expectedType)
    {
        var doc = new UIDocument();

        var element = doc.CreateElement(tag);

        Assert.Equal(expectedType, element.GetType());
        Assert.Same(doc, element.OwnerDocument);
    }

    [Fact]
    public void CreateElementUnknownTagThrows()
    {
        var doc = new UIDocument();
        Assert.Throws<InvalidOperationException>(() => doc.CreateElement("NoSuchTag"));
    }

    [Fact]
    public void EmbeddedSvgUsesItsOwnXmlDocument()
    {
        var uiDocument = new UIDocument();
        var svg = Assert.IsType<SVGSVGElement>(uiDocument.CreateElement("svg"));
        var group = new SVGGElement();
        var path = new SVGPathElement { Id = "shape" };
        group.Children.Add(path);
        svg.Children.Add(group);
        uiDocument.Body.Children.Add(svg);

        Assert.IsAssignableFrom<XMLDocument>(svg.SvgDocument);
        Assert.Equal("image/svg+xml", svg.SvgDocument.ContentType);
        Assert.Same(svg.SvgDocument, svg.OwnerDocument);
        Assert.Same(svg.SvgDocument, group.OwnerDocument);
        Assert.Same(svg.SvgDocument, path.OwnerDocument);
        Assert.Same(path, svg.SvgDocument.GetElementById("shape"));
        Assert.Same(svg, svg.SvgDocument.DocumentElement);
    }

    [Fact]
    public void GetElementByIdFindsDescendant()
    {
        var doc = new UIDocument();
        var item = new ListItem { Id = "item-1", TextContent = "One" };
        doc.Body.Children.Add(item);

        Assert.Same(item, doc.GetElementById("item-1"));
        Assert.Same(item, doc.GetElementById<ListItem>("item-1"));
    }

    [Fact]
    public void TitleRoundTrips()
    {
        var doc = new UIDocument { Title = "Hello" };
        Assert.Equal("Hello", doc.Title);
    }

    [Fact]
    public void EventFromBodyBubblesToDocument()
    {
        var doc = new UIDocument();
        var button = new Button();
        doc.Body.Children.Add(button);
        var seen = 0;
        doc.AddEventListener("click", _ => seen++);

        button.DispatchEvent(Square.Events.StandardEvents.CreateClick());

        Assert.Equal(1, seen);
    }

    [Fact]
    public void AppendChildAndRemoveChildMatchDomSemantics()
    {
        var parent = new View();
        var a = new Square.Controls.Text("a");
        var b = new Square.Controls.Text("b");

        Assert.Same(a, parent.AppendChild(a));
        parent.AppendChild(b);
        Assert.Equal(2, parent.ChildElementCount);
        Assert.Same(a, parent.FirstElementChild);
        Assert.Same(b, parent.LastElementChild);
        Assert.Same(parent, a.ParentNode);
        Assert.Same(parent, a.ParentElement);

        Assert.Same(a, parent.RemoveChild(a));
        Assert.Null(a.Parent);
        Assert.Equal(1, parent.ChildElementCount);
        var ex = Assert.Throws<InvalidOperationException>(() => parent.RemoveChild(a));
        Assert.Contains("not a child", ex.Message);
    }

    [Fact]
    public void InsertBeforeAndReplaceChildrenWork()
    {
        var parent = new View();
        var a = new Square.Controls.Text("a");
        var b = new Square.Controls.Text("b");
        var c = new Square.Controls.Text("c");
        parent.AppendChild(b);
        parent.InsertBefore(a, b);
        Assert.Equal(2, parent.Children.Count);
        Assert.Same(a, parent.Children[0]);
        Assert.Same(b, parent.Children[1]);

        parent.ReplaceChildren(c);
        Assert.Single(parent.Children);
        Assert.Same(c, parent.FirstElementChild);
        Assert.Null(a.Parent);
        Assert.Null(b.Parent);
    }

    [Fact]
    public void GetBoundingClientRectReturnsGeometry()
    {
        var view = new View { Geometry = new Square.Graphics.Rect(10, 20, 30, 40) };
        Assert.Equal(view.Geometry, view.GetBoundingClientRect());
    }

    [Fact]
    public void NodeInheritanceForkIsDocumentAndElement()
    {
        var doc = new UIDocument();
        var view = new View();
        doc.Body.AppendChild(view);

        Assert.IsAssignableFrom<Node>(doc);
        Assert.IsAssignableFrom<Node>(view);
        Assert.Equal(Node.NodeType.Document, doc.NodeTypeValue);
        Assert.Equal(Node.NodeType.Element, view.NodeTypeValue);
        Assert.Equal("#document", doc.NodeName);
        Assert.Equal("View", view.NodeName);
        Assert.Same(doc, view.OwnerDocument);
        Assert.Same(doc.Body, view.ParentNode);
        Assert.Same(doc.Body, view.ParentElement);
        Assert.Null(doc.ParentNode);
        Assert.Null(doc.OwnerDocument);

        // 事件：Parent 为空时经 OwnerDocument 冒泡到 Document
        var hops = 0;
        doc.AddEventListener("click", _ => hops++);
        view.DispatchEvent(Square.Events.StandardEvents.CreateClick());
        Assert.Equal(1, hops);
    }

    [Fact]
    public void DomTextNodeCoexistsWithTextControl()
    {
        var domText = new Square.UI.Text("hello");
        var controlText = new Square.Controls.Text("hello");

        Assert.Equal(Node.NodeType.Text, domText.NodeTypeValue);
        Assert.Equal("#text", domText.NodeName);
        Assert.Equal("hello", domText.Data);
        Assert.Equal(5, domText.Length);

        domText.AppendData(" world");
        domText.ReplaceData(0, 5, "Hi");

        Assert.Equal("Hi world", domText.Data);
        Assert.Equal(Node.NodeType.Element, controlText.NodeTypeValue);
        Assert.Equal("Text", controlText.NodeName);
        Assert.Equal("hello", controlText.TextContent);
    }

    [Fact]
    public void ElementChildNodesCanContainDomTextWithoutChangingChildrenView()
    {
        var doc = new UIDocument();
        var parent = new View();
        var textNode = new Square.UI.Text("hello");
        var childElement = new Button("button");

        doc.Body.AppendChild(parent);
        parent.AppendChild(textNode);
        parent.AppendChild(childElement);

        Assert.Equal(2, parent.ChildNodes.Count);
        Assert.Single(parent.Children);
        Assert.Same(textNode, parent.ChildNodes[0]);
        Assert.Same(childElement, parent.ChildNodes[1]);
        Assert.Same(childElement, parent.Children[0]);
        Assert.Same(parent, textNode.ParentNode);
        Assert.Same(parent, textNode.ParentElement);
        Assert.Same(doc, textNode.OwnerDocument);
        Assert.Same(doc, childElement.OwnerDocument);

        Assert.Same(textNode, parent.RemoveChild(textNode));
        Assert.Null(textNode.ParentNode);
        Assert.Single(parent.ChildNodes);
        Assert.Single(parent.Children);
    }

    [Fact]
    public void RangeExtractsTextAcrossDomTextNodes()
    {
        var doc = new UIDocument();
        var parent = new View();
        var first = new Square.UI.Text("hello ");
        var middle = new View();
        var second = new Square.UI.Text("world");
        var third = new Square.UI.Text("!");

        doc.Body.AppendChild(parent);
        parent.AppendChild(first);
        parent.AppendChild(middle);
        middle.AppendChild(second);
        parent.AppendChild(third);

        var range = doc.CreateRange();
        range.SetStart(first, 3);
        range.SetEnd(second, 2);

        Assert.False(range.Collapsed);
        Assert.Equal("lo wo", range.ToString());
    }

    [Fact]
    public void SelectionStoresSingleRangeAndReturnsSelectedText()
    {
        var doc = new UIDocument();
        var text = new Square.UI.Text("hello");
        doc.Body.AppendChild(text);

        var range = doc.CreateRange();
        range.SetStart(text, 1);
        range.SetEnd(text, 4);

        var selection = doc.GetSelection();
        selection.AddRange(range);

        Assert.Equal(1, selection.RangeCount);
        Assert.False(selection.IsCollapsed);
        Assert.Same(text, selection.AnchorNode);
        Assert.Equal(1, selection.AnchorOffset);
        Assert.Same(text, selection.FocusNode);
        Assert.Equal(4, selection.FocusOffset);
        Assert.Equal("ell", selection.ToString());

        selection.RemoveAllRanges();

        Assert.Equal(0, selection.RangeCount);
        Assert.True(selection.IsCollapsed);
        Assert.Equal(string.Empty, selection.ToString());
    }

    [Fact]
    public void SelectionRejectsRangeFromDifferentDocument()
    {
        var first = new UIDocument();
        var second = new UIDocument();
        var firstText = new Square.UI.Text("first");
        var secondText = new Square.UI.Text("second");
        first.Body.AppendChild(firstText);
        second.Body.AppendChild(secondText);

        var current = first.CreateRange();
        current.SelectNodeContents(firstText);
        first.GetSelection().AddRange(current);
        var foreign = second.CreateRange();
        foreign.SelectNodeContents(secondText);

        Assert.Throws<InvalidOperationException>(() => first.GetSelection().AddRange(foreign));
        Assert.Same(current, first.GetSelection().GetRangeAt(0));
        Assert.Equal("first", first.GetSelection().ToString());
    }

    [Fact]
    public void TextControlMaintainsDomTextChildNode()
    {
        var doc = new UIDocument();
        var text = new Square.Controls.Text("hello");

        doc.Body.AppendChild(text);

        var textNode = Assert.IsType<Square.UI.Text>(Assert.Single(text.ChildNodes));
        Assert.Empty(text.Children);
        Assert.Equal("hello", textNode.Data);
        Assert.Same(text, textNode.ParentNode);
        Assert.Same(doc, textNode.OwnerDocument);

        text.TextContent = "hello world";

        Assert.Equal("hello world", textNode.Data);
        var range = doc.CreateRange();
        range.SetStart(textNode, 6);
        range.SetEnd(textNode, 11);
        Assert.Equal("world", range.ToString());
    }

    [Fact]
    public void LinkAndButtonMaintainDomTextChildNodes()
    {
        var doc = new UIDocument();
        var link = new Link("docs", "/docs");
        var button = new Button("submit");

        doc.Body.AppendChild(link);
        doc.Body.AppendChild(button);

        var linkText = Assert.IsType<Square.UI.Text>(Assert.Single(link.ChildNodes));
        var buttonText = Assert.IsType<Square.UI.Text>(Assert.Single(button.ChildNodes));
        Assert.Empty(link.Children);
        Assert.Empty(button.Children);
        Assert.Equal("docs", linkText.Data);
        Assert.Equal("submit", buttonText.Data);
        Assert.Same(doc, linkText.OwnerDocument);
        Assert.Same(doc, buttonText.OwnerDocument);

        link.TextContent = "documentation";
        button.TextContent = "submit form";

        Assert.Equal("documentation", linkText.Data);
        Assert.Equal("submit form", buttonText.Data);

        var range = doc.CreateRange();
        range.SetStart(linkText, 0);
        range.SetEnd(buttonText, 6);
        Assert.Equal("documentationsubmit", range.ToString());
    }

    private sealed class SplitterTestHost : IPlatformHost
    {
        public Square.Graphics.Size ClientSize => new(800, 600);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Square.Graphics.Size>? SizeChanged { add { } remove { } }
        public event Action<Square.Graphics.Point, MouseAction, MouseButton>? MouseEvent { add { } remove { } }
        public event Action<Square.Graphics.Point, int>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent { add { } remove { } }
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void Show() { }
        public void Close() { }
        public Square.Graphics.IRenderContext CreateRenderContext() => throw new NotSupportedException();
        public void PumpEvents() { }
        public void SetTextInputRect(Square.Graphics.Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }
}
