using System;
using System.Threading;
using Square.Controls;
using Square.CSS.Engine;
using Square.Events;
using Square.Html;
using Square.Native.Html;
using Square.Runtime;
using Square.Runtime.Binding;
using Square.Runtime.State;
using Square.Sample.Components;
using Square.UI;
using Square.UI.ElementApi;
using Xunit;
using DomText = Square.UI.Text;
using ControlText = Square.Controls.Text;

namespace Square.UI.Tests;

public class ComponentMountTests
{
    [Fact]
    public void ReplacesAllChildNodesButPreservesContainerAndSurroundingTree()
    {
        var document = new UIDocument();
        var root = document.CreateElement("div");
        var header = document.CreateElement("header");
        var container = document.CreateElement("div");
        var footer = document.CreateElement("footer");
        container.Id = "slot";
        container.ClassList.Add("static-slot");
        container.Style.Set("padding", "12px");
        var placeholder = new DomText("placeholder");
        var oldChild = new View();
        var oldResource = new Resource();
        var staticResource = new Resource();
        oldChild.RegisterGeneratedResource(oldResource);
        container.RegisterGeneratedResource(staticResource);
        container.ChildNodes.Add(placeholder);
        container.Children.Add(oldChild);
        root.Children.Add(header);
        root.Children.Add(container);
        root.Children.Add(footer);
        document.Body.Children.Add(root);
        var component = new BoundComponent();

        using var mount = root.Mount("#slot", component);

        Assert.Same(container, root.Children[1]);
        Assert.Same(header, root.Children[0]);
        Assert.Same(footer, root.Children[2]);
        Assert.Same(component, Assert.Single(container.ChildNodes));
        Assert.Null(placeholder.ParentNode);
        Assert.Null(oldChild.ParentNode);
        Assert.Equal(1, oldResource.DisposeCount);
        Assert.Equal(0, staticResource.DisposeCount);
        Assert.Equal("slot", container.Id);
        Assert.True(container.ClassList.Contains("static-slot"));
        Assert.Equal("12px", container.Style.Get("padding"));
        Assert.Same(document, component.OwnerDocument);
        Assert.Same(document, component.Text.OwnerDocument);
        Assert.True(mount.IsMounted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlyAndLateMountHooksSeeBuiltSubtreeExactlyOnce(bool alreadyLoaded)
    {
        var document = new UIDocument();
        var container = document.CreateElement("div");
        document.Body.Children.Add(container);
        if (alreadyLoaded)
        {
            ((IComponentLifecycle)document.Ui).OnAttached();
            ((IComponentLifecycle)document.Ui).OnLoaded();
        }
        var component = new BoundComponent();
        using var mount = container.Mount(component);
        if (!alreadyLoaded)
        {
            ((IComponentLifecycle)document.Ui).OnAttached();
            ((IComponentLifecycle)document.Ui).OnLoaded();
        }

        Assert.Equal("initial", component.AttachedText);
        Assert.Equal("initial", component.LoadedText);
        Assert.Equal(1, component.AttachedCount);
        Assert.Equal(1, component.LoadedCount);
        mount.Unmount();
        mount.Dispose();
        Assert.Equal(1, component.UnloadedCount);
        Assert.Equal(1, component.DetachedCount);
        Assert.Equal(1, component.Resource.DisposeCount);
    }

    [Fact]
    public void BindingUsesDocumentDispatcherAndStopsAfterUnmount()
    {
        var document = new UIDocument();
        var container = document.CreateElement("div");
        document.Body.Children.Add(container);
        var component = new StoreComponent();
        using var mount = container.Mount(component);
        var worker = new Thread(() => component.Display.Set("queued"));
        worker.Start();
        worker.Join();
        Assert.Equal("initial", component.Text.TextContent);
        document.FlushPendingUpdates();
        Assert.Equal("queued", component.Text.TextContent);

        mount.Unmount();
        component.Display.Set("discarded");
        document.FlushPendingUpdates();
        Assert.Equal("queued", component.Text.TextContent);
        Assert.Empty(container.ChildNodes);
        Assert.False(mount.IsMounted);
    }

    [Fact]
    public void ReplacementEndsOldHandleAndRequiresFreshInstance()
    {
        var document = new UIDocument();
        var container = document.CreateElement("div");
        document.Body.Children.Add(container);
        var old = new BoundComponent();
        using var first = container.Mount(old);
        var fresh = new BoundComponent();
        using var second = container.Mount(fresh);
        first.Dispose();

        Assert.False(first.IsMounted);
        Assert.True(second.IsMounted);
        Assert.Same(fresh, Assert.Single(container.ChildNodes));
        Assert.Equal(1, old.Resource.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => container.Mount(old));
        Assert.Same(fresh, Assert.Single(container.ChildNodes));
        Assert.Equal("initial", fresh.Text.TextContent);
    }

    [Fact]
    public void FailedBuildReleasesBindingAndResourceWithoutRemovingOldContent()
    {
        var document = new UIDocument();
        var container = document.CreateElement("div");
        document.Body.Children.Add(container);
        var placeholder = new DomText("keep");
        container.ChildNodes.Add(placeholder);
        var component = new BoundComponent { FailBuild = true };

        var error = Assert.Throws<InvalidOperationException>(() => container.Mount(component));

        Assert.Equal("build failed", error.Message);
        Assert.Same(placeholder, Assert.Single(container.ChildNodes));
        Assert.Equal(1, component.Resource.DisposeCount);
        component.Display.Value = "must not update";
        document.FlushPendingUpdates();
        Assert.Equal("initial", component.Text.TextContent);
        Assert.Throws<InvalidOperationException>(() => container.Mount(component));
    }

    [Fact]
    public void MissingSelectorAndUnownedTargetDoNotRemovePlaceholder()
    {
        var document = new UIDocument();
        var container = document.CreateElement("div");
        document.Body.Children.Add(container);
        var placeholder = new DomText("keep");
        container.ChildNodes.Add(placeholder);
        Assert.Throws<InvalidOperationException>(() => container.Mount("#missing", new BoundComponent()));
        Assert.Throws<ArgumentException>(() => container.Mount(" ", new BoundComponent()));
        Assert.Same(placeholder, Assert.Single(container.ChildNodes));
        var unowned = HtmlElementFactory.Create("div");
        unowned.ChildNodes.Add(new DomText("unowned"));
        Assert.Throws<InvalidOperationException>(() => unowned.Mount(new BoundComponent()));
        Assert.Equal("unowned", Assert.IsType<DomText>(Assert.Single(unowned.ChildNodes)).Data);
    }

    [Fact]
    public void ParentAndOwnerAreNotStolenByAnotherMount()
    {
        var document = new UIDocument();
        var first = document.CreateElement("div");
        var second = document.CreateElement("div");
        document.Body.Children.Add(first);
        document.Body.Children.Add(second);
        var placeholder = new DomText("keep");
        second.ChildNodes.Add(placeholder);
        var component = new BoundComponent();
        using var mount = first.Mount(component);
        Assert.Throws<InvalidOperationException>(() => second.Mount(component));
        Assert.Same(component, Assert.Single(first.ChildNodes));
        Assert.Same(placeholder, Assert.Single(second.ChildNodes));
        first.ChildNodes.Remove(component);
        Assert.Throws<InvalidOperationException>(() => second.Mount(component));
        Assert.Same(placeholder, Assert.Single(second.ChildNodes));
    }

    [Fact]
    public void CrossDocumentMountDoesNotRebindAnExistingComponent()
    {
        var first = new UIDocument();
        var second = new UIDocument();
        var component = first.CreateElement<BoundComponent>();
        var container = second.CreateElement("div");
        var placeholder = new DomText("keep");
        container.ChildNodes.Add(placeholder);
        second.Body.Children.Add(container);

        Assert.Throws<InvalidOperationException>(() => container.Mount(component));
        Assert.Same(first, component.OwnerDocument);
        Assert.Same(placeholder, Assert.Single(container.ChildNodes));
    }

    [Fact]
    public void SelfAndDescendantMountCannotMakeTreeCycles()
    {
        var document = new UIDocument();
        var component = new BoundComponent();
        var descendant = document.CreateElement("div");
        component.Children.Add(descendant);
        document.Body.Children.Add(component);
        Assert.Throws<InvalidOperationException>(() => component.Mount(component));
        Assert.Throws<InvalidOperationException>(() => descendant.Mount(component));
        Assert.Same(descendant, Assert.Single(component.Children));
        Assert.Same(component, Assert.Single(document.Body.Children));
    }

    [Fact]
    public void WrongThreadMountAndUnmountLeaveTreeAndBindingIntact()
    {
        var document = new UIDocument();
        var container = document.CreateElement("div");
        document.Body.Children.Add(container);
        var placeholder = new DomText("keep");
        container.ChildNodes.Add(placeholder);
        Exception? failure = null;
        var worker = new Thread(() => failure = Record.Exception(() => container.Mount(new BoundComponent())));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Same(placeholder, Assert.Single(container.ChildNodes));
        var component = new BoundComponent();
        using var mount = container.Mount(component);
        worker = new Thread(() => failure = Record.Exception(mount.Unmount));
        worker.Start();
        worker.Join();
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Same(component, Assert.Single(container.ChildNodes));
        component.Display.Value = "still live";
        document.FlushPendingUpdates();
        Assert.Equal("still live", component.Text.TextContent);
    }

    [Fact]
    public void MovedComponentCannotBeRemovedFromItsNewParentByOldHandle()
    {
        var document = new UIDocument();
        var first = document.CreateElement("div");
        var second = document.CreateElement("div");
        document.Body.Children.Add(first);
        document.Body.Children.Add(second);
        var component = new BoundComponent();
        using var mount = first.Mount(component);
        first.ChildNodes.Remove(component);
        second.ChildNodes.Add(component);

        Assert.Throws<InvalidOperationException>(mount.Unmount);
        Assert.Same(component, Assert.Single(second.ChildNodes));
        Assert.Equal(0, component.Resource.DisposeCount);
        second.ChildNodes.Remove(component);
        mount.Unmount();
        Assert.Equal(1, component.Resource.DisposeCount);
        Assert.False(mount.IsMounted);
    }

    [Fact]
    public void DiscardedTargetIsNotReusedAndDiscardingAncestorEndsHandle()
    {
        var document = new UIDocument();
        var root = document.CreateElement("div");
        var container = document.CreateElement("div");
        root.Children.Add(container);
        document.Body.Children.Add(root);
        var component = new BoundComponent();
        using var mount = container.Mount(component);
        root.DiscardGeneratedSubtree();
        Assert.False(mount.IsMounted);
        Assert.Equal(1, component.Resource.DisposeCount);
        mount.Dispose();
        Assert.Equal(1, component.Resource.DisposeCount);
        Assert.Throws<InvalidOperationException>(() => container.Mount(new BoundComponent()));
        Assert.Same(component, Assert.Single(container.ChildNodes));
    }

    [Fact]
    public void HtmlShellMountReplacesPlaceholdersAndPreservesStaticShell()
    {
        var shell = LoadAttachedShell();
        try
        {
            var sqx = new HtmlMountedSqxCounter();
            var sqv = new HtmlMountedSqvCounter();

            using var sqxMount = shell.Root.Mount("#sqx-root", sqx);
            using var sqvMount = shell.Root.Mount("#sqv-root", sqv);

            Assert.Same(shell.SqxSlot, sqx.ParentNode);
            Assert.Same(shell.SqvSlot, sqv.ParentNode);
            Assert.Null(shell.SqxPlaceholder.ParentNode);
            Assert.Null(shell.SqvPlaceholder.ParentNode);
            Assert.Same(sqx, Assert.Single(shell.SqxSlot.ChildNodes));
            Assert.Same(sqv, Assert.Single(shell.SqvSlot.ChildNodes));

            Assert.Same(shell.Header, shell.Body.Children[0]);
            Assert.Same(shell.Note, shell.Body.Children[1]);
            Assert.Same(shell.SqxSlot, shell.Body.Children[2]);
            Assert.Same(shell.SqvSlot, shell.Body.Children[3]);
            Assert.Same(shell.Footer, shell.Body.Children[4]);

            Assert.Equal("sqx-root", shell.SqxSlot.Id);
            Assert.True(shell.SqxSlot.ClassList.Contains("mount-slot"));
            Assert.Equal("8px", shell.SqxSlot.Style.Get("padding"));
            Assert.Equal("sqv-root", shell.SqvSlot.Id);
            Assert.True(shell.SqvSlot.ClassList.Contains("mount-slot"));
            Assert.Equal("8px", shell.SqvSlot.Style.Get("padding"));

            Assert.Same(shell.Document, sqx.OwnerDocument);
            Assert.Same(shell.Document, sqv.OwnerDocument);
            var sqxParts = ResolveCounterParts(sqx, "SQX +1");
            var sqvParts = ResolveCounterParts(sqv, "SQV +1");
            Assert.Same(shell.Document, sqxParts.BoundText.OwnerDocument);
            Assert.Same(shell.Document, sqvParts.BoundText.OwnerDocument);

            ApplyComputedStyles(shell);
            Assert.Equal("#0b3d91", shell.Header.Style.Get("color"));
            Assert.Equal("#4a5568", shell.Footer.Style.Get("color"));
            Assert.Equal("#123a6d", shell.Note.Style.Get("color"));
            Assert.Equal("#f5f7fa", shell.SqxSlot.Style.Get("background"));
        }
        finally
        {
            ReleaseShellScopes(shell);
        }
    }

    [Fact]
    public void HtmlMountedCountersUpdateIndependentlyFromRealClicks()
    {
        var shell = LoadAttachedShell();
        try
        {
            var sqx = new HtmlMountedSqxCounter();
            var sqv = new HtmlMountedSqvCounter();
            using var sqxMount = shell.Root.Mount("#sqx-root", sqx);
            using var sqvMount = shell.Root.Mount("#sqv-root", sqv);
            var sqxParts = ResolveCounterParts(sqx, "SQX +1");
            var sqvParts = ResolveCounterParts(sqv, "SQV +1");

            Assert.Equal("SQX 0", sqx.Display.Value);
            Assert.Equal("SQV 0", sqv.Display.Value);
            Assert.Equal("SQX 0", sqxParts.BoundText.TextContent);
            Assert.Equal("SQV 0", sqvParts.BoundText.TextContent);

            sqxParts.Button.DispatchEvent(StandardEvents.CreateClick());
            sqvParts.Button.DispatchEvent(StandardEvents.CreateClick());
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQX 1", sqxParts.BoundText.TextContent);
            Assert.Equal("SQV 1", sqvParts.BoundText.TextContent);
            Assert.Equal("SQX 1", sqx.Display.Value);
            Assert.Equal("SQV 1", sqv.Display.Value);

            sqxParts.Button.DispatchEvent(StandardEvents.CreateClick());
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQX 2", sqxParts.BoundText.TextContent);
            Assert.Equal("SQV 1", sqvParts.BoundText.TextContent);
        }
        finally
        {
            ReleaseShellScopes(shell);
        }
    }

    [Fact]
    public void UnmountFreezesBoundTextWhileShellAndSiblingScopesStayIntact()
    {
        var shell = LoadAttachedShell();
        try
        {
            var sqx = new HtmlMountedSqxCounter();
            var sqv = new HtmlMountedSqvCounter();
            using var sqxMount = shell.Root.Mount("#sqx-root", sqx);
            using var sqvMount = shell.Root.Mount("#sqv-root", sqv);
            var sqxParts = ResolveCounterParts(sqx, "SQX +1");
            var sqvParts = ResolveCounterParts(sqv, "SQV +1");

            sqxParts.Button.DispatchEvent(StandardEvents.CreateClick());
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            ApplyComputedStyles(shell);
            Assert.Equal("#b45309", sqxParts.BoundText.Style.Get("color"));
            Assert.Equal("#b45309", sqvParts.BoundText.Style.Get("color"));

            sqxMount.Unmount();
            Assert.False(sqxMount.IsMounted);

            sqx.Display.Value = "SQX 99";
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQX 1", sqxParts.BoundText.TextContent);

            ApplyComputedStyles(shell);
            Assert.Equal("#b45309", sqvParts.BoundText.Style.Get("color"));
            Assert.Equal("#0b3d91", shell.Header.Style.Get("color"));
            Assert.Equal("#f5f7fa", shell.SqxSlot.Style.Get("background"));

            sqvParts.Button.DispatchEvent(StandardEvents.CreateClick());
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQV 1", sqvParts.BoundText.TextContent);

            Assert.Throws<InvalidOperationException>(() => shell.Root.Mount("#sqx-root", sqx));
            Assert.Empty(shell.SqxSlot.ChildNodes);
        }
        finally
        {
            ReleaseShellScopes(shell);
        }
    }

    [Fact]
    public void FreshReplacementStartsAtZeroAndDiscardedInstanceCannotRemount()
    {
        var shell = LoadAttachedShell();
        try
        {
            var stale = new HtmlMountedSqxCounter();
            using var staleMount = shell.Root.Mount("#sqx-root", stale);
            var staleParts = ResolveCounterParts(stale, "SQX +1");
            staleParts.Button.DispatchEvent(StandardEvents.CreateClick());
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQX 1", staleParts.BoundText.TextContent);

            var fresh = new HtmlMountedSqxCounter();
            using var freshMount = shell.Root.Mount("#sqx-root", fresh);
            staleMount.Dispose();

            Assert.False(staleMount.IsMounted);
            Assert.True(freshMount.IsMounted);
            Assert.Same(fresh, Assert.Single(shell.SqxSlot.ChildNodes));

            var freshParts = ResolveCounterParts(fresh, "SQX +1");
            Assert.Equal("SQX 0", freshParts.BoundText.TextContent);
            freshParts.Button.DispatchEvent(StandardEvents.CreateClick());
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQX 1", freshParts.BoundText.TextContent);

            stale.Display.Value = "SQX 404";
            Reconciler.Current.Flush();
            shell.Document.FlushPendingUpdates();
            Assert.Equal("SQX 1", staleParts.BoundText.TextContent);
            Assert.Equal("SQX 1", freshParts.BoundText.TextContent);

            Assert.Throws<InvalidOperationException>(() => shell.Root.Mount("#sqx-root", stale));
            Assert.Same(fresh, Assert.Single(shell.SqxSlot.ChildNodes));
        }
        finally
        {
            ReleaseShellScopes(shell);
        }
    }

    public sealed class BoundComponent : UIElement
    {
        public ObservableValue<string> Display { get; } = new("initial");
        public ControlText Text { get; } = new();
        public Resource Resource { get; } = new();
        public bool FailBuild { get; init; }
        public string? AttachedText { get; private set; }
        public string? LoadedText { get; private set; }
        public int AttachedCount { get; private set; }
        public int LoadedCount { get; private set; }
        public int UnloadedCount { get; private set; }
        public int DetachedCount { get; private set; }

        public override void BuildElementTree()
        {
            Children.Add(Text);
            Text.BindProperty("TextContent", (IReactiveValue<string>)Display);
            RegisterGeneratedResource(Resource);
            if (FailBuild) throw new InvalidOperationException("build failed");
        }

        protected override void OnAttachedCore()
        {
            AttachedCount++;
            AttachedText = Assert.IsType<ControlText>(Assert.Single(Children)).TextContent;
        }

        protected override void OnLoadedCore()
        {
            LoadedCount++;
            LoadedText = Assert.IsType<ControlText>(Assert.Single(Children)).TextContent;
        }

        protected override void OnUnloadedCore() => UnloadedCount++;
        protected override void OnDetachedCore() => DetachedCount++;
    }

    private sealed class StoreComponent : UIElement
    {
        public Store<string> Display { get; } = new("initial");
        public ControlText Text { get; } = new();

        public override void BuildElementTree()
        {
            Children.Add(Text);
            Text.BindProperty("TextContent", Display);
        }
    }

    public sealed class Resource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    private sealed record HtmlShell(
        UIDocument Document,
        HTMLElement Root,
        Element Body,
        Element Header,
        Element Note,
        Element SqxSlot,
        Element SqvSlot,
        Element Footer,
        Element SqxPlaceholder,
        Element SqvPlaceholder);

    private static HtmlShell LoadAttachedShell()
    {
        var document = new UIDocument();
        var root = HtmlLoader.LoadFile("Fixtures/html-mount/shell.html");
        document.Body.Children.Add(root);
        ((IComponentLifecycle)document.Ui).OnAttached();
        ((IComponentLifecycle)document.Ui).OnLoaded();

        var body = Required(root, "body");
        var header = Required(body, "#shell-header");
        var note = Required(body, "#slot-note");
        var sqxSlot = Required(body, "#sqx-root");
        var sqvSlot = Required(body, "#sqv-root");
        var footer = Required(body, "#shell-footer");
        return new HtmlShell(
            document,
            root,
            body,
            header,
            note,
            sqxSlot,
            sqvSlot,
            footer,
            Required(sqxSlot, ".slot-placeholder"),
            Required(sqvSlot, ".slot-placeholder"));

        static Element Required(Element scope, string selector) =>
            CssSelector.QuerySelector(scope, selector)
            ?? throw new InvalidOperationException($"Fixture element '{selector}' was not found.");
    }

    private static (Button Button, ControlText BoundText) ResolveCounterParts(Element component, string buttonLabel)
    {
        var button = Assert.Single(component.QueryAll<Button>(), item => item.TextContent == buttonLabel);
        var boundText = Assert.Single(component.QueryAll<ControlText>(), item => item.ClassList.Contains("counter-value"));
        return (button, boundText);
    }

    private static void ApplyComputedStyles(HtmlShell shell)
    {
        shell.Document.Build();
        CssStyleReconciler.ReapplyScopesToTree(shell.Document.Ui);
    }

    private static void ReleaseShellScopes(HtmlShell shell) =>
        CssStyleReconciler.UnregisterScopesForTree(shell.Document.Ui);
}
