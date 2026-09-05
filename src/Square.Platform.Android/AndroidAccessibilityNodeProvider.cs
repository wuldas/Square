#pragma warning disable CA1416, CA1422
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Views.Accessibility;
using SquareButton = Square.Controls.Button;
using SquareCheckBox = Square.Controls.CheckBox;
using SquareInput = Square.Controls.Input;
using SquareLink = Square.Controls.Link;
using SquareListItem = Square.Controls.ListItem;
using SquareSelect = Square.Controls.Select;
using SquareScrollViewer = Square.Controls.ScrollViewer;
using SquareText = Square.Controls.Text;
using SquareTreeItem = Square.Controls.TreeItem;
using SquareTextInputClient = Square.Controls.ITextInputClient;
using Square.Events;
using Square.UI;
using AndroidRect = Android.Graphics.Rect;
using AndroidAccessibilityAction = Android.Views.Accessibility.Action;
using SquareRect = Square.Graphics.Rect;

namespace Square.Platform.Android;

/// <summary>将 Square Element Tree 暴露为 Android 虚拟可访问性节点树。</summary>
internal sealed class AndroidAccessibilityNodeProvider : AccessibilityNodeProvider
{
    private readonly SquareView _view;
    private readonly AndroidPlatformHost _host;
    private readonly Dictionary<int, Element> _elements = [];
    private readonly Dictionary<Element, int> _ids = new(ReferenceEqualityComparer.Instance);
    private int _nextId;
    private int _accessibilityFocusedId = HostViewId;

    internal AndroidAccessibilityNodeProvider(SquareView view, AndroidPlatformHost host)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(host);
        _view = view;
        _host = host;
    }

    internal void Refresh()
    {
        _elements.Clear();
        _ids.Clear();
        _nextId = 0;
        var root = _host.AccessibilityRootQuery?.Invoke();
        if (root != null && root.IsEffectivelyVisible)
            AddElement(root);

        if (_accessibilityFocusedId != HostViewId && !_elements.ContainsKey(_accessibilityFocusedId))
            _accessibilityFocusedId = HostViewId;
    }

    /// <inheritdoc />
    public override AccessibilityNodeInfo? CreateAccessibilityNodeInfo(int virtualViewId)
    {
        Refresh();
        if (virtualViewId == HostViewId)
            return CreateHostInfo();
        if (!_elements.TryGetValue(virtualViewId, out var element))
            return null;
        return CreateElementInfo(virtualViewId, element);
    }

    /// <inheritdoc />
    public override IList<AccessibilityNodeInfo> FindAccessibilityNodeInfosByText(string? text, int virtualViewId)
    {
        Refresh();
        var result = new List<AccessibilityNodeInfo>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var query = text.Trim();
        foreach (var pair in _elements)
        {
            var label = GetLabel(pair.Value);
            if (label.Contains(query, StringComparison.OrdinalIgnoreCase))
                result.Add(CreateElementInfo(pair.Key, pair.Value));
        }
        return result;
    }

    /// <inheritdoc />
    public override AccessibilityNodeInfo? FindFocus(NodeFocus focus)
    {
        Refresh();
        return focus == NodeFocus.Accessibility && _accessibilityFocusedId != HostViewId
            && _elements.TryGetValue(_accessibilityFocusedId, out var element)
            ? CreateElementInfo(_accessibilityFocusedId, element)
            : null;
    }

    /// <inheritdoc />
    public override bool PerformAction(int virtualViewId, AndroidAccessibilityAction action, Bundle? arguments)
    {
        Refresh();
        var actionId = action;
        if (virtualViewId == HostViewId)
            return _view.PerformAccessibilityAction(action, arguments);
        if (!_elements.TryGetValue(virtualViewId, out var element)) return false;

        var handled = actionId switch
        {
            AndroidAccessibilityAction.Click => PerformClick(element),
            AndroidAccessibilityAction.Focus => PerformFocus(virtualViewId, element),
            AndroidAccessibilityAction.ClearFocus => PerformClearFocus(element),
            AndroidAccessibilityAction.AccessibilityFocus => SetAccessibilityFocus(virtualViewId),
            AndroidAccessibilityAction.ClearAccessibilityFocus => ClearAccessibilityFocus(virtualViewId),
            AndroidAccessibilityAction.ScrollForward => Scroll(element, forward: true),
            AndroidAccessibilityAction.ScrollBackward => Scroll(element, forward: false),
            AndroidAccessibilityAction.SetText => SetText(element, arguments),
            AndroidAccessibilityAction.SetSelection => SetSelection(element, arguments),
            _ => false
        };
        if (handled)
        {
            _host.RequestRenderFrame();
            _view.PostInvalidateOnAnimation();
        }
        return handled;
    }

    private AccessibilityNodeInfo CreateHostInfo()
    {
        var info = new AccessibilityNodeInfo(_view);
        info.PackageName = _view.Context?.PackageName;
        info.ClassName = "android.view.View";
        info.ContentDescription = _host.Title;
        info.Enabled = true;
        info.VisibleToUser = true;
        if (OperatingSystem.IsAndroidVersionAtLeast(28)) info.ScreenReaderFocusable = true;
        foreach (var id in _elements.Keys.Where(id => GetParentId(_elements[id]) == HostViewId))
            info.AddChild(_view, id);
        return info;
    }

    private AccessibilityNodeInfo CreateElementInfo(int id, Element element)
    {
        var info = new AccessibilityNodeInfo(_view, id);
        var parentId = GetParentId(element);
        if (parentId == HostViewId)
            info.SetParent(_view);
        else
            info.SetParent(_view, parentId);
        info.PackageName = _view.Context?.PackageName;
        info.ClassName = GetAndroidClass(element);
        info.Text = GetText(element);
        info.ContentDescription = GetContentDescription(element);
        info.Enabled = element is not UIElement ui || ui.IsEnabled;
        info.VisibleToUser = element.IsEffectivelyVisible && !element.Geometry.IsEmpty;
        info.Focusable = element is UIElement && ((UIElement)element).IsEnabled;
        info.Focused = element is UIElement focused && focused.IsFocused;
        info.AccessibilityFocused = id == _accessibilityFocusedId;
        info.Clickable = IsClickable(element);
        info.Scrollable = IsScrollable(element);
        info.Editable = element is SquareTextInputClient;
        info.Password = element is SquareInput { Type: "password" };
        info.MultiLine = element is SquareTextInputClient { IsMultiline: true };
        info.Checkable = element is SquareCheckBox;
        info.Checked = element is SquareCheckBox { IsChecked: true };
        info.Selected = element is SquareListItem { IsSelected: true };
        if (OperatingSystem.IsAndroidVersionAtLeast(28)) info.ScreenReaderFocusable = HasSemanticContent(element);
        if (element is SquareTextInputClient input)
        {
            info.SetTextSelection(input.SelectionStart, input.SelectionEnd);
        }

        var bounds = _host.ToPhysicalRect(element.Geometry);
        info.SetBoundsInParent(bounds);
        info.SetBoundsInScreen(ToScreenRect(bounds));
        if (IsClickable(element)) info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClick);
        if (info.Focusable == true) info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionFocus);
        if (info.Focused == true) info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionClearFocus);
        if (IsScrollable(element))
        {
            var scroll = (SquareScrollViewer)element;
            if (scroll.VerticalOffset > 0 || scroll.HorizontalOffset > 0)
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollBackward);
            if (scroll.ScrollableHeight > scroll.VerticalOffset || scroll.ScrollableWidth > scroll.HorizontalOffset)
                info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionScrollForward);
        }
        if (element is SquareTextInputClient)
        {
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionSetText);
            info.AddAction(AccessibilityNodeInfo.AccessibilityAction.ActionSetSelection);
        }

        foreach (var child in element.Children)
            if (_ids.TryGetValue(child, out var childId)) info.AddChild(_view, childId);
        return info;
    }

    private int AddElement(Element element)
    {
        if (_ids.TryGetValue(element, out var existing)) return existing;
        var id = ++_nextId;
        _ids[element] = id;
        _elements[id] = element;
        foreach (var child in element.Children)
            if (child.IsEffectivelyVisible && !child.Geometry.IsEmpty) AddElement(child);
        return id;
    }

    private int GetParentId(Element element)
        => element.Parent is { } parent && _ids.TryGetValue(parent, out var id) ? id : HostViewId;

    private bool PerformClick(Element element)
    {
        if (!IsClickable(element) || element is UIElement { IsEnabled: false }) return false;
        element.DispatchEvent(StandardEvents.CreateClick());
        return true;
    }

    private bool PerformFocus(int id, Element element)
    {
        if (element is not UIElement ui || !ui.IsEnabled) return false;
        ui.Focus();
        if (element is SquareTextInputClient) _host.RequestTextInputSurface();
        _accessibilityFocusedId = id;
        return true;
    }

    private static bool PerformClearFocus(Element element)
    {
        if (element is not UIElement ui || !ui.IsFocused) return false;
        ui.Unfocus();
        return true;
    }

    private bool SetAccessibilityFocus(int id)
    {
        _accessibilityFocusedId = id;
        return true;
    }

    private bool ClearAccessibilityFocus(int id)
    {
        if (_accessibilityFocusedId != id) return false;
        _accessibilityFocusedId = HostViewId;
        return true;
    }

    private static bool Scroll(Element element, bool forward)
    {
        if (element is not SquareScrollViewer scroll) return false;
        var beforeX = scroll.HorizontalOffset;
        var beforeY = scroll.VerticalOffset;
        var deltaX = Math.Max(1, scroll.ViewportWidth);
        var deltaY = Math.Max(1, scroll.ViewportHeight);
        scroll.ScrollTo(
            beforeX + (forward ? deltaX : -deltaX),
            beforeY + (forward ? deltaY : -deltaY));
        return Math.Abs(beforeX - scroll.HorizontalOffset) > 0.01f ||
               Math.Abs(beforeY - scroll.VerticalOffset) > 0.01f;
    }

    private static bool SetText(Element element, Bundle? arguments)
    {
        if (element is not SquareTextInputClient input || arguments == null) return false;
        var text = arguments.GetCharSequence(AccessibilityNodeInfo.ActionArgumentSetTextCharsequence)?.ToString();
        if (text == null) return false;
        input.SetSelection(0, input.Text.Length);
        input.CommitText(text);
        return true;
    }

    private static bool SetSelection(Element element, Bundle? arguments)
    {
        if (element is not SquareTextInputClient input || arguments == null) return false;
        var start = arguments.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionStartInt, -1);
        var end = arguments.GetInt(AccessibilityNodeInfo.ActionArgumentSelectionEndInt, -1);
        if (start < 0 || end < 0) return false;
        input.SetSelection(start, end);
        return true;
    }

    private bool ShowOnScreen(Element element)
    {
        var changed = false;
        for (Element? current = element.Parent; current != null; current = current.Parent)
        {
            if (current is SquareScrollViewer scroll)
            {
                var before = scroll.VerticalOffset;
                scroll.ScrollIntoView(element);
                changed |= Math.Abs(before - scroll.VerticalOffset) > 0.01f;
            }
        }
        return changed;
    }

    private AndroidRect ToScreenRect(AndroidRect bounds)
    {
        var location = new int[2];
        _view.GetLocationOnScreen(location);
        bounds.Offset(location[0], location[1]);
        return bounds;
    }

    private static string GetAndroidClass(Element element) =>
        element switch
        {
            SquareButton => "android.widget.Button",
            SquareCheckBox => "android.widget.CheckBox",
            SquareTextInputClient => "android.widget.EditText",
            SquareScrollViewer => "android.widget.ScrollView",
            _ => "android.view.View"
        };

    private static bool IsClickable(Element element) =>
        element is SquareButton or SquareCheckBox or SquareLink or SquareSelect or SquareListItem or SquareTreeItem ||
        element.GetProperty<bool>("IsClickable");

    private static bool IsScrollable(Element element) =>
        element is SquareScrollViewer scroll &&
        (scroll.ScrollableHeight > 0 || scroll.ScrollableWidth > 0);

    private static bool HasSemanticContent(Element element) =>
        IsClickable(element) || IsScrollable(element) || element is SquareTextInputClient ||
        !string.IsNullOrWhiteSpace(GetLabel(element));

    private static string GetLabel(Element element)
    {
        foreach (var name in new[] { "AccessibilityLabel", "aria-label", "AriaLabel", "Label" })
            if (element.GetProperty<string>(name) is { Length: > 0 } value) return value;

        return element switch
        {
            SquareButton button => button.TextContent,
            SquareCheckBox checkBox => checkBox.TextContent,
            SquareLink link => link.TextContent,
            SquareListItem item => item.TextContent,
            SquareTreeItem item => item.TextContent,
            SquareText text => text.TextContent,
            SquareInput { Type: "password" } password => password.Placeholder,
            SquareTextInputClient input when input.Text.Length > 0 => input.Text,
            _ => string.Join(" ", element.Children.Select(GetLabel).Where(value => value.Length > 0))
        };
    }

    private static string? GetText(Element element)
    {
        var label = GetLabel(element);
        if (element is not SquareInput { Type: "password" } &&
            element is SquareTextInputClient input && input.Text.Length == 0)
            return null;
        return label.Length == 0 ? null : label;
    }

    private static string? GetContentDescription(Element element)
    {
        foreach (var name in new[] { "AccessibilityHint", "aria-description", "AriaDescription", "Tooltip" })
            if (element.GetProperty<string>(name) is { Length: > 0 } value) return value;
        return null;
    }
}
#pragma warning restore CA1416, CA1422
