using Square.Controls;
using Square.Html;
using Square.Runtime;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public class HtmlCustomElementLifecycleTests
{
    // Custom element definitions require public concrete types with parameterless
    // constructors, so these helpers are public and nested; the shared log carries the
    // cross-element callback order for the nesting test only.
    private static List<string>? _sharedLog;

    private static void Record(string entry) => _sharedLog?.Add(entry);

    public sealed class StatusBadge : HTMLElement
    {
        public List<(string Name, string? OldValue, string? NewValue)> Changes { get; } = [];
        public int Connected;
        public int Disconnected;

        public StatusBadge() : base("acme-badge") { }

        protected override void ConnectedCallback() => Connected++;
        protected override void DisconnectedCallback() => Disconnected++;
        protected override void AttributeChangedCallback(string name, string? oldValue, string? newValue) =>
            Changes.Add((name, oldValue, newValue));
    }

    public sealed class LoggedPanel : HTMLElement
    {
        public LoggedPanel() : base("acme-panel") { }

        protected override void ConnectedCallback() => Record("connect:panel");
        protected override void DisconnectedCallback() => Record("disconnect:panel");
    }

    public sealed class LoggedBadge : HTMLElement
    {
        public LoggedBadge() : base("acme-badge") { }

        protected override void ConnectedCallback() => Record("connect:badge");
        protected override void DisconnectedCallback() => Record("disconnect:badge");
    }

    public sealed class FailingBadge : HTMLElement
    {
        public FailingBadge() : base("acme-failing") { }

        protected override void ConnectedCallback() => throw new InvalidOperationException("connect exploded");
    }

    public sealed class FailingObservedBadge : HTMLElement
    {
        public FailingObservedBadge() : base("acme-badge") { }

        protected override void AttributeChangedCallback(string name, string? oldValue, string? newValue) =>
            throw new InvalidOperationException("status callback exploded");
    }

    public sealed class LoggedButton : HTMLButtonElement
    {
        public int Connected;

        protected override void ConnectedCallback() => Connected++;
    }

    [Fact]
    public void ObservedAttributesReportOldAndNewValuesAroundAttachTransitions()
    {
        var document = new UIDocument();
        document.CustomElements.DefineAutonomous("acme-badge", static () => new StatusBadge(), "status");
        var badge = Assert.IsType<StatusBadge>(document.CreateElement("acme-badge"));

        Assert.True(badge.IsDefinedCustomElement);
        Assert.Equal("acme-badge", badge.CustomElementName);
        Assert.Equal("acme-badge", badge.TagName);
        Assert.Equal("http://www.w3.org/1999/xhtml", badge.NamespaceURI);
        Assert.Same(document, badge.OwnerDocument);

        // Attributes set before connection still observe with their previous value.
        badge.SetAttribute("status", "ready");
        Assert.Equal(new (string Name, string? OldValue, string? NewValue)[] { ("status", null, "ready") }, badge.Changes);

        ((IComponentLifecycle)badge).OnAttached();
        Assert.Equal(1, badge.Connected);
        Assert.Equal(0, badge.Disconnected);

        badge.SetAttribute("status", "done");
        Assert.Equal(new (string Name, string? OldValue, string? NewValue)[]
        {
            ("status", null, "ready"),
            ("status", "ready", "done")
        }, badge.Changes);
        // Writing the same value or an unobserved attribute never notifies.
        badge.SetAttribute("status", "done");
        badge.SetAttribute("title", "badge");
        Assert.Equal(2, badge.Changes.Count);

        ((IComponentLifecycle)badge).OnDetached();
        Assert.Equal(1, badge.Disconnected);

        // Reattaching runs the connection callback again, exactly once per transition.
        ((IComponentLifecycle)badge).OnAttached();
        Assert.Equal(2, badge.Connected);
        Assert.Equal(1, badge.Disconnected);
        Assert.Empty(document.CustomElements.Diagnostics);
    }

    [Fact]
    public void NestedCustomElementsConnectParentFirstAndDisconnectChildFirst()
    {
        var document = new UIDocument();
        var log = new List<string>();
        document.CustomElements.DefineAutonomous("acme-panel", static () => new LoggedPanel());
        document.CustomElements.DefineAutonomous("acme-badge", static () => new LoggedBadge());
        var panel = Assert.IsType<LoggedPanel>(document.CreateElement("acme-panel"));
        var badge = Assert.IsType<LoggedBadge>(document.CreateElement("acme-badge"));
        panel.Children.Add(badge);

        _sharedLog = log;
        try
        {
            ((IComponentLifecycle)panel).OnAttached();
            Assert.Equal(["connect:panel", "connect:badge"], log);

            log.Clear();
            ((IComponentLifecycle)panel).OnDetached();
            Assert.Equal(["disconnect:badge", "disconnect:panel"], log);
        }
        finally
        {
            _sharedLog = null;
        }
    }

    [Fact]
    public void RemovingAndReinsertingIntoAttachedParentTogglesCallbacks()
    {
        var document = new UIDocument();
        document.CustomElements.DefineAutonomous("acme-badge", static () => new StatusBadge());
        var badge = Assert.IsType<StatusBadge>(document.CreateElement("acme-badge"));
        var host = new View();
        host.Children.Add(badge);

        ((IComponentLifecycle)host).OnAttached();
        Assert.Equal(1, badge.Connected);
        Assert.Equal(0, badge.Disconnected);

        host.Children.Remove(badge);
        Assert.Equal(1, badge.Disconnected);

        host.Children.Add(badge);
        Assert.Equal(2, badge.Connected);
        Assert.Equal(1, badge.Disconnected);
    }

    [Fact]
    public void CallbackFailuresAreReportedPerDocumentWithoutBlockingSiblings()
    {
        var document = new UIDocument();
        document.CustomElements.DefineAutonomous("acme-failing", static () => new FailingBadge());
        document.CustomElements.DefineAutonomous("acme-badge", static () => new StatusBadge());
        var failing = Assert.IsType<FailingBadge>(document.CreateElement("acme-failing"));
        var badge = Assert.IsType<StatusBadge>(document.CreateElement("acme-badge"));
        Assert.Empty(document.CustomElements.Diagnostics);

        ((IComponentLifecycle)failing).OnAttached();
        ((IComponentLifecycle)badge).OnAttached();

        var diagnostic = Assert.Single(document.CustomElements.Diagnostics);
        Assert.Contains("acme-failing", diagnostic.Message);
        Assert.IsType<InvalidOperationException>(diagnostic.Exception);
        Assert.Null(diagnostic.DebugInfo);
        Assert.Equal(1, badge.Connected);
    }

    [Fact]
    public void DetachedAttributeCallbackErrorsJoinDocumentOnAttachWithSourceLocation()
    {
        var document = new UIDocument();
        document.CustomElements.DefineAutonomous("acme-badge", static () => new FailingObservedBadge(), "status");
        var badge = Assert.IsType<FailingObservedBadge>(document.CreateElement("acme-badge"));
        var debugInfo = ElementDebugInfo.Create(42, 2, 3, 2, 13, "acme-badge", "Badge");
        badge.SetDebugInfo(debugInfo);
        badge.SetAttribute("status", "ready");
        Assert.Empty(document.CustomElements.Diagnostics);

        ((IComponentLifecycle)badge).OnAttached();
        var diagnostic = Assert.Single(document.CustomElements.Diagnostics);
        Assert.Same(debugInfo, diagnostic.DebugInfo);
        Assert.Contains("status callback exploded", diagnostic.Message);
    }

    [Fact]
    public void CustomElementDefinitionsStayDocumentLocal()
    {
        var first = new UIDocument();
        first.CustomElements.DefineAutonomous("acme-badge", static () => new StatusBadge());
        Assert.IsType<StatusBadge>(first.CreateElement("acme-badge"));

        var second = new UIDocument();
        Assert.Throws<InvalidOperationException>(() => second.CreateElement("acme-badge"));
        Assert.Throws<InvalidOperationException>(() =>
            first.CustomElements.DefineAutonomous("acme-badge", static () => new StatusBadge()));
    }

    [Fact]
    public void CustomDefinitionsRejectInvalidNamesAndUnrelatedBuiltInTypes()
    {
        var document = new UIDocument();
        Assert.Throws<ArgumentException>(() =>
            document.CustomElements.DefineAutonomous("badname", static () => new StatusBadge()));
        Assert.Throws<ArgumentException>(() =>
            document.CustomElements.DefineAutonomous("annotation-xml", static () => new StatusBadge()));
        Assert.Throws<ArgumentException>(() =>
            document.CustomElements.DefineCustomizedBuiltIn("acme-button", "param", static () => new LoggedButton()));
        Assert.Throws<ArgumentException>(() =>
            document.CustomElements.DefineCustomizedBuiltIn("acme-button", "button", static () => new StatusBadge()));
    }

    [Fact]
    public void CustomizedBuiltInUpgradesTheBuiltInTagExactlyOnce()
    {
        var document = new UIDocument();
        document.CustomElements.DefineCustomizedBuiltIn("acme-button", "button", static () => new LoggedButton());

        var button = Assert.IsType<LoggedButton>(document.CreateElement("button", "acme-button"));
        Assert.Equal("button", button.TagName);
        Assert.Equal("acme-button", button.CustomElementName);
        Assert.Equal("button", button.CustomElementExtendsTag);
        Assert.True(button.HasAttribute("is"));
        Assert.Equal("acme-button", button.GetAttribute("is"));

        ((IComponentLifecycle)button).OnAttached();
        Assert.Equal(1, button.Connected);

        // Mutating is after creation never re-upgrades or replaces the element.
        button.SetAttribute("is", "acme-other");
        Assert.Equal(1, button.Connected);
        Assert.Equal("acme-other", button.GetAttribute("is"));
    }
}
