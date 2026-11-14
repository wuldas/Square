using Square.Backends;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using Square.Hosting;
using Square.Platform;
using Square.Runtime;
using Square.Runtime.Binding;
using Square.UI;
using Xunit;

namespace Square.UI.Tests;

public sealed class TriStateCheckBoxTests
{
    [Fact]
    public void DefaultsToUncheckedAndPreservesExplicitNull()
    {
        var check = new TriStateCheckBox();

        Assert.Equal(false, check.IsChecked);
        Assert.Equal("", check.TextContent);
        Assert.False(check.HasState(ElementState.Checked));
        Assert.False(check.HasState(ElementState.Indeterminate));

        check.IsChecked = null;
        Assert.Null(check.IsChecked);
        Assert.True(check.HasState(ElementState.Indeterminate));
        Assert.False(check.HasState(ElementState.Checked));

        check.IsChecked = true;
        Assert.Equal(true, check.IsChecked);
        Assert.True(check.HasState(ElementState.Checked));
        Assert.False(check.HasState(ElementState.Indeterminate));

        check.RemoveProperty(nameof(TriStateCheckBox.IsChecked));
        Assert.Equal(false, check.IsChecked);
        Assert.False(check.HasState(ElementState.Checked));
        Assert.False(check.HasState(ElementState.Indeterminate));
    }

    [Fact]
    public void ClickCyclesStatesAndRaisesOneBubblingChangePerActivation()
    {
        var parent = new View();
        var check = new TriStateCheckBox();
        parent.Children.Add(check);
        var changes = new List<bool?>();
        var inputs = 0;
        parent.AddEventListener(StandardEvents.Change, e =>
        {
            Assert.Same(check, e.Target);
            Assert.True(e.Bubbles);
            Assert.False(e.Cancelable);
            changes.Add(check.IsChecked);
        });
        parent.AddEventListener(StandardEvents.Input, () => inputs++);

        for (var i = 0; i < 3; i++) check.DispatchEvent(StandardEvents.CreateClick());

        Assert.Equal(new bool?[] { true, null, false }, changes);
        Assert.Equal(0, inputs);
    }

    [Fact]
    public void ProgrammaticAndBoundValuesDoNotRaiseChange()
    {
        var check = new TriStateCheckBox();
        var changes = 0;
        check.AddEventListener(StandardEvents.Change, () => changes++);
        check.IsChecked = true;
        check.IsChecked = null;
        check.IsChecked = false;
        var source = new ObservableValue<bool?>(null);
        check.BindProperty(nameof(TriStateCheckBox.IsChecked), source);
        Assert.Null(check.IsChecked);

        source.Value = true;
        Assert.Equal(true, check.IsChecked);
        source.Value = null;
        Assert.Null(check.IsChecked);
        Assert.True(check.HasState(ElementState.Indeterminate));
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(null)]
    public void DisabledControlIgnoresClickAndSpace(bool? initial)
    {
        var check = new TriStateCheckBox { IsChecked = initial, IsDisabled = true };
        var changes = 0;
        check.AddEventListener(StandardEvents.Change, () => changes++);

        check.DispatchEvent(StandardEvents.CreateClick());
        PressSpace(check);

        Assert.Equal(initial, check.IsChecked);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void SpaceCyclesOncePerPressAndIgnoresRepeatedKeyDown()
    {
        var check = new TriStateCheckBox();
        check.Focus();
        var changes = new List<bool?>();
        var clicks = 0;
        check.AddEventListener(StandardEvents.Change, () => changes.Add(check.IsChecked));
        check.AddEventListener(StandardEvents.Click, () => clicks++);

        for (var i = 0; i < 3; i++)
        {
            var down = StandardEvents.CreateKeyDown(32);
            check.DispatchEvent(down);
            Assert.True(down.DefaultPrevented);
            check.DispatchEvent(StandardEvents.CreateKeyDown(32));
            check.DispatchEvent(StandardEvents.CreateKeyDown(32));
            Assert.Equal(i + 1, changes.Count);
            check.DispatchEvent(StandardEvents.CreateKeyUp(32));
            Assert.Equal(i + 1, changes.Count);
        }

        Assert.Equal(new bool?[] { true, null, false }, changes);
        Assert.Equal(3, clicks);
    }

    [Fact]
    public void PreventDefaultCancelsClickAndSpaceActivation()
    {
        var check = new TriStateCheckBox();
        var changes = 0;
        check.AddEventListener(StandardEvents.Change, () => changes++);
        using (check.Listen(StandardEvents.Click, e => e.PreventDefault()))
            check.DispatchEvent(StandardEvents.CreateClick());
        using (check.Listen(StandardEvents.KeyDown, e => e.PreventDefault()))
            PressSpace(check);

        Assert.Equal(false, check.IsChecked);
        Assert.Equal(0, changes);
        PressSpace(check);
        Assert.Equal(true, check.IsChecked);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ParentCanCancelClickBeforeDefaultActivation()
    {
        var parent = new View();
        var check = new TriStateCheckBox();
        parent.Children.Add(check);
        parent.AddEventListener(StandardEvents.Click, e => e.PreventDefault());

        check.DispatchEvent(StandardEvents.CreateClick());
        PressSpace(check);

        Assert.Equal(false, check.IsChecked);
    }

    [Fact]
    public void SpaceDoesNotReachAncestorKeyboardHandlers()
    {
        var parent = new View();
        var check = new TriStateCheckBox();
        parent.Children.Add(check);
        var ancestorKeys = 0;
        parent.AddEventListener(StandardEvents.KeyDown, () => ancestorKeys++);
        parent.AddEventListener(StandardEvents.KeyUp, () => ancestorKeys++);

        PressSpace(check);
        Assert.Equal(0, ancestorKeys);
        Assert.Equal(true, check.IsChecked);

        check.DispatchEvent(StandardEvents.CreateKeyDown(65));
        Assert.Equal(1, ancestorKeys);
    }

    [Theory]
    [InlineData(13, false, false)]
    [InlineData(32, true, false)]
    [InlineData(32, false, true)]
    public void OtherKeysAndModifiedSpaceDoNotActivate(int keyCode, bool control, bool alt)
    {
        var check = new TriStateCheckBox();

        check.DispatchEvent(StandardEvents.CreateKeyDown(keyCode, controlKey: control, altKey: alt));
        check.DispatchEvent(StandardEvents.CreateKeyUp(keyCode, controlKey: control, altKey: alt));

        Assert.Equal(false, check.IsChecked);
    }

    [Fact]
    public void FocusLossAndDisablingResetSpaceLatch()
    {
        var check = new TriStateCheckBox();
        check.Focus();
        check.DispatchEvent(StandardEvents.CreateKeyDown(32));
        Assert.Equal(true, check.IsChecked);
        check.Unfocus();
        check.Focus();
        check.DispatchEvent(StandardEvents.CreateKeyDown(32));
        Assert.Null(check.IsChecked);

        check.IsDisabled = true;
        check.DispatchEvent(StandardEvents.CreateKeyDown(32));
        Assert.Null(check.IsChecked);
        check.IsDisabled = false;
        check.DispatchEvent(StandardEvents.CreateKeyDown(32));
        Assert.Equal(false, check.IsChecked);
    }

    [Fact]
    public void DetachingResetsSpaceLatch()
    {
        var document = new UIDocument();
        ((IComponentLifecycle)document.Ui).OnAttached();
        var check = new TriStateCheckBox();
        document.Body.Children.Add(check);
        Assert.True(check.IsAttached);
        check.DispatchEvent(StandardEvents.CreateKeyDown(32));
        Assert.Equal(true, check.IsChecked);

        document.Body.Children.Remove(check);
        Assert.False(check.IsAttached);
        document.Body.Children.Add(check);
        PressSpace(check);

        Assert.Null(check.IsChecked);
    }

    [Fact]
    public void IndeterminateBitPreservesExistingFocusAndDisabledBits()
    {
        var check = new TriStateCheckBox { IsChecked = null };
        check.SetState(ElementState.FocusVisible, true);
        check.IsDisabled = true;

        Assert.True(check.HasState(ElementState.Indeterminate));
        Assert.True(check.HasState(ElementState.FocusVisible));
        Assert.True(check.HasState(ElementState.Disabled));
        check.IsChecked = true;
        Assert.False(check.HasState(ElementState.Indeterminate));
        Assert.True(check.HasState(ElementState.FocusVisible));
        Assert.True(check.HasState(ElementState.Disabled));
    }

    [Fact]
    public void DesktopMouseFocusThenSpaceUsesSingleActivationPath()
    {
        var check = new TriStateCheckBox();
        var changes = 0;
        check.AddEventListener(StandardEvents.Change, () => changes++);
        var window = new AppWindow("TriStateCheckBox input", 100, 80);
        window.Load(check);
        var application = new DesktopApplication(window);
        var host = new TestHost();
        try
        {
            application.PrepareSession();
            application.AttachSessionHost(host);
            Assert.True(application.ProcessSessionFrame());

            host.RaiseMouse(check.Geometry.Center, MouseAction.Down);
            host.RaiseMouse(check.Geometry.Center, MouseAction.Up);
            Assert.True(check.IsFocused);
            Assert.Equal(true, check.IsChecked);
            Assert.Equal(1, changes);

            host.RaiseKey(32, KeyAction.Down);
            host.RaiseKey(32, KeyAction.Down);
            host.RaiseKey(32, KeyAction.Up);
            Assert.Null(check.IsChecked);
            Assert.Equal(2, changes);
        }
        finally
        {
            application.DetachSession();
        }
    }

    [Fact]
    public void DialogCanChooseTriStateCheckBoxForInitialFocus()
    {
        var dialog = new Dialog();
        var check = new TriStateCheckBox();
        dialog.Children.Add(check);

        dialog.Open();

        Assert.True(check.IsFocused);
        Assert.True(check.HasState(ElementState.FocusVisible));
        dialog.Close();
    }

    [Fact]
    public void TextContentExposesSelectableLabel()
    {
        var check = new TriStateCheckBox { TextContent = "All items" };

        Assert.Equal("All items", ((ITextSelectable)check).SelectableText);
    }

    private static void PressSpace(TriStateCheckBox check)
    {
        check.DispatchEvent(StandardEvents.CreateKeyDown(32));
        check.DispatchEvent(StandardEvents.CreateKeyUp(32));
    }

    private sealed class TestHost : IPlatformHost
    {
        public Size ClientSize => new(100, 80);
        public float DpiScale => 1;
        public bool IsRunning => true;
        public string Title { get; set; } = "TriStateCheckBox input";
        public CursorKind Cursor { get; set; }
        public KeyModifiers Modifiers => KeyModifiers.None;
        public event Action<Size>? SizeChanged { add { } remove { } }
        public event Action<Point, MouseAction, MouseButton>? MouseEvent;
        public event Action<WheelInput>? WheelEvent { add { } remove { } }
        public event Action<int, KeyAction>? KeyEvent;
        public event Action<string>? TextInput { add { } remove { } }
        public event Action? Tick { add { } remove { } }
        public void RaiseMouse(Point point, MouseAction action) => MouseEvent?.Invoke(point, action, MouseButton.Left);
        public void RaiseKey(int keyCode, KeyAction action) => KeyEvent?.Invoke(keyCode, action);
        public void Show() { }
        public void Close() { }
        public IRenderContext CreateRenderContext() => new RenderBackendFactory().CreateContext(new RenderContextCreateInfo
        {
            CanvasSize = ClientSize
        });
        public void PumpEvents() { }
        public void SetTextInputRect(Rect rect) { }
        public string GetClipboardText() => "";
        public void SetClipboardText(string text) { }
        public void Dispose() { }
    }
}
