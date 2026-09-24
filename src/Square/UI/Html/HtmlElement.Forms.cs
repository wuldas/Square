using System.Diagnostics;
using System.Globalization;
using Square.Controls;
using Square.Events;
using Square.Graphics;
using ImageControl = Square.Controls.Image;

namespace Square.UI.Html;

/// <summary>Native proxy controls, form-state synchronization and host activation behavior.</summary>
public sealed partial class HtmlElement
{
    /// <summary>Creates listeners that give the semantic host its activation behavior.</summary>
    private void WireInteractions()
    {
        switch (_tagName)
        {
            case "button":
                AddEventListener(StandardEvents.Click, _ => Focus(focusVisible: false));
                AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
                {
                    if (e.KeyCode is 13 or 32 && IsEnabled)
                    {
                        e.PreventDefault();
                        DispatchEvent(StandardEvents.CreateClick());
                    }
                });
                break;
            case "summary":
                AddEventListener(StandardEvents.Click, _ =>
                {
                    Focus(focusVisible: false);
                    ToggleAncestorDetails();
                });
                break;
            case "label":
                AddEventListener(StandardEvents.Click, ActivateLabel);
                break;
            case "a":
                AddEventListener(StandardEvents.Click, _ =>
                {
                    Focus(focusVisible: false);
                    ActivateSafeHref(GetAttribute("href"));
                });
                AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
                {
                    if (e.KeyCode == 13 && IsEnabled) ActivateSafeHref(GetAttribute("href"));
                });
                break;
            case "img":
                AddEventListener<PointerEvent>(StandardEvents.PointerUp, ActivateImageMapArea);
                break;
        }
    }

    /// <summary>summary click toggles the open attribute of its nearest details ancestor.</summary>
    private void ToggleAncestorDetails()
    {
        for (Element? current = Parent; current != null; current = current.Parent)
        {
            if (current is not HtmlElement { TagName: "details" } details) continue;
            if (details.HasAttribute("open")) details.RemoveAttribute("open");
            else details.SetAttribute("open", "");
            return;
        }
    }

    /// <summary>
    /// label click focuses and activates its control: the for= target, else the first nested
    /// labelable proxy. Clicks that already landed on the control are not forwarded twice.
    /// </summary>
    private void ActivateLabel(Event e)
    {
        var target = FindLabeledControl();
        if (target == null || WithinSubtree(e.Target, target)) return;
        if (e.Target is HtmlElement host && host._proxies.Contains(target)) return;
        ActivateControl(target);
    }

    private UIElement? FindLabeledControl()
    {
        var forId = GetAttribute("for");
        if (!string.IsNullOrEmpty(forId) && FindRootOf(this) is { } root &&
            FindElementById(root, forId) is { } byId)
            return ResolveLabelable(byId);
        return FindLabelableDescendant(this);
    }

    /// <summary>Resolves a labeled host to its native proxy; direct controls pass through.</summary>
    private static UIElement? ResolveLabelable(Element element) => element switch
    {
        Input or TextArea or Select or CheckBox or Radio => (UIElement)element,
        HtmlElement host => host._proxies.FirstOrDefault(),
        _ => null
    };

    private static Element? FindRootOf(Element element)
    {
        Element? root = element;
        while (root.Parent != null) root = root.Parent;
        return root;
    }

    private static bool WithinSubtree(EventTarget? node, Element root)
    {
        for (EventTarget? current = node; current != null; current = current is Element element ? element.Parent : null)
            if (ReferenceEquals(current, root)) return true;
        return false;
    }

    private static Element? FindElementById(Element element, string id)
    {
        if (string.Equals(element.Id, id, StringComparison.Ordinal)) return element;
        foreach (var child in element.Children)
        {
            var found = FindElementById(child, id);
            if (found != null) return found;
        }
        return null;
    }

    private static UIElement? FindLabelableDescendant(Element element)
    {
        foreach (var child in element.Children)
        {
            if (child is Input or TextArea or Select or CheckBox or Radio) return (UIElement)child;
            if (child is HtmlElement host && host._proxies.FirstOrDefault() is { } proxy) return proxy;
            if (FindLabelableDescendant(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>Focuses the control; checkbox labels toggle, radio labels select their group entry.</summary>
    private static void ActivateControl(UIElement control)
    {
        if (!control.IsEnabled) return;
        control.Focus(focusVisible: false);
        switch (control)
        {
            case CheckBox checkBox:
                checkBox.IsChecked = !checkBox.IsChecked;
                checkBox.DispatchEvent(StandardEvents.CreateChange());
                break;
            case Radio radio when !radio.IsChecked:
                SweepRadioGroup(radio);
                radio.IsChecked = true;
                radio.DispatchEvent(StandardEvents.CreateChange());
                break;
        }
    }

    /// <summary>
    /// Opens only absolute http/https/mailto targets through the shell, mirroring the Square
    /// Link control. Relative, fragment and unknown-scheme hrefs (including javascript:) stay inert.
    /// </summary>
    private static void ActivateSafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return;
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https" or "mailto")) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Clicks on an img with usemap="#name" test the point against the referenced map's area
    /// shapes (document order) and open the first safe href. No download or navigation happens.
    /// </summary>
    private void ActivateImageMapArea(PointerEvent e)
    {
        var usemap = (GetAttribute("usemap") ?? "").Trim();
        if (usemap.Length == 0 || FindRootOf(this) is not { } root) return;
        var map = FindImageMap(root, usemap.TrimStart('#'));
        if (map == null) return;
        var relative = new Point(e.ClientX - Geometry.X, e.ClientY - Geometry.Y);
        foreach (var child in map.Children)
        {
            if (child is not HtmlElement { TagName: "area" } area) continue;
            if (!HitTestArea(area, relative, Geometry.Size)) continue;
            ActivateSafeHref(area.GetAttribute("href"));
            return;
        }
    }

    private static HtmlElement? FindImageMap(Element element, string name)
    {
        foreach (var child in element.Children)
        {
            if (child is HtmlElement { TagName: "map" } map && map.GetAttribute("name") == name) return map;
            if (FindImageMap(child, name) is { } nested) return nested;
        }
        return null;
    }

    private static bool HitTestArea(HtmlElement area, Point point, Size imageSize)
    {
        var shape = (area.GetAttribute("shape") ?? "rect").Trim().ToLowerInvariant();
        var coords = ParseCoords(area.GetAttribute("coords"));
        if (shape == "default") return imageSize.Width > 0 && imageSize.Height > 0 &&
            point.X >= 0 && point.Y >= 0 && point.X <= imageSize.Width && point.Y <= imageSize.Height;
        if (shape == "circle")
        {
            if (coords.Count < 3) return false;
            var dx = point.X - coords[0];
            var dy = point.Y - coords[1];
            return dx * dx + dy * dy <= coords[2] * coords[2];
        }
        if (shape == "poly")
        {
            if (coords.Count < 6) return false;
            var inside = false;
            for (int i = 0, j = coords.Count / 2 - 1; i < coords.Count / 2; j = i++)
            {
                var xi = coords[i * 2];
                var yi = coords[i * 2 + 1];
                var xj = coords[j * 2];
                var yj = coords[j * 2 + 1];
                if (yi > point.Y == yj > point.Y &&
                    point.X < (xj - xi) * (point.Y - yi) / (yj - yi) + xi) inside = !inside;
            }
            return inside;
        }
        // rect (the default shape): x,y,width,height
        if (coords.Count < 4) return false;
        return point.X >= coords[0] && point.Y >= coords[1] &&
               point.X <= coords[0] + coords[2] && point.Y <= coords[1] + coords[3];
    }

    private static List<float> ParseCoords(string? raw)
    {
        var result = new List<float>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        foreach (var token in raw.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                result.Add(value);
        return result;
    }

    // ----- proxy reconciliation -----

    private void SyncImage()
    {
        var src = GetAttribute("src") ?? "";
        var altOnly = src.Length == 0 && !string.IsNullOrEmpty(GetAttribute("alt"));
        var proxy = EnsureProxy<ImageControl>();
        if (proxy == null) return;
        SetProxyVisible(proxy, !altOnly);
        SetSource(proxy, src);
    }

    private void SyncPosterProxy(string? poster)
    {
        var proxy = EnsureProxy<ImageControl>();
        if (proxy == null) return;
        SetProxyVisible(proxy, !string.IsNullOrEmpty(poster));
        SetSource(proxy, poster ?? "");
    }

    private void SyncTextControl()
    {
        var proxy = EnsureFormControlProxy();
        if (proxy == null) return;
        proxy.IsEnabled = IsEnabled;
        if (_tagName == "input" && proxy is Radio radio) SetRadioGroup(radio);
        if (proxy is TextEditorBase editor)
        {
            SetValue(editor, GetAttribute("value") ?? "");
            editor.Placeholder = GetAttribute("placeholder") ?? "";
        }
        if (proxy is CheckBox checkBox) SetChecked(checkBox, HasAttribute("checked"));
        if (proxy is Radio radioChecked) SetChecked(radioChecked, HasAttribute("checked"));
        if (proxy is Square.Controls.Button formButton) SetFormButtonText(formButton);
        if (_tagName == "textarea") SyncTextAreaExtent();
    }

    private void SetFormButtonText(Square.Controls.Button formButton)
    {
        var type = (GetAttribute("type") ?? "button").Trim().ToLowerInvariant();
        var label = GetAttribute("value") ?? type switch
        {
            "submit" => "Submit",
            "reset" => "Reset",
            _ => ""
        };
        if (formButton.TextContent != label) formButton.TextContent = label;
    }

    private static void SetChecked(CheckBox checkBox, bool isChecked)
    {
        if (checkBox.IsChecked != isChecked) checkBox.IsChecked = isChecked;
    }

    private static void SetChecked(Radio radio, bool isChecked)
    {
        if (radio.IsChecked == isChecked) return;
        if (isChecked)
        {
            SweepRadioGroup(radio);
            radio.IsChecked = true;
        }
        else radio.IsChecked = false;
    }

    /// <summary>Unchecks same-group radios and clears their hosts' checked attributes.</summary>
    private static void SweepRadioGroup(Radio radio)
    {
        if (string.IsNullOrEmpty(radio.GroupName) || FindRootOf(radio) is not { } root) return;
        foreach (var other in root.QueryAll<Radio>())
        {
            if (other == radio || other.GroupName != radio.GroupName) continue;
            other.IsChecked = false;
            if (other.Parent is HtmlElement host && host.HasAttribute("checked"))
                host.RemoveAttribute("checked");
        }
    }

    private void SetRadioGroup(Radio radio)
    {
        var name = GetAttribute("name") ?? "";
        if (radio.GroupName != name) radio.GroupName = name;
    }

    /// <summary>rows/cols attributes size the host box; the widget metrics stay native.</summary>
    private void SyncTextAreaExtent()
    {
        var rows = ParsePositiveInt(GetAttribute("rows"), 0);
        var cols = ParsePositiveInt(GetAttribute("cols"), 0);
        SetExtentStyle("height", rows > 0 ? $"{MathF.Ceiling(rows * 19f + 8f)}px" : null);
        SetExtentStyle("width", cols > 0 ? $"{MathF.Ceiling(cols * 9f + 18f)}px" : null);
    }

    private void SetExtentStyle(string property, string? value)
    {
        if (value == null || Style.IsAuthorSpecified(property)) return;
        if (string.Equals(Style.Get(property), value, StringComparison.Ordinal)) return;
        SetUserAgentStyle(property, value);
    }

    private void SyncSelectControl()
    {
        var proxy = EnsureProxy<Select>();
        if (proxy == null) return;
        proxy.IsEnabled = IsEnabled;
        var options = CollectOptions();
        if (_syncedSelectOptions == null || !_syncedSelectOptions.SequenceEqual(options))
        {
            _syncedSelectOptions = [.. options];
            var values = options.Select(option => option.Value).ToArray();
            if (!proxy.Options.SequenceEqual(values)) proxy.Options = values;
        }
        var desired = options.FirstOrDefault(option => option.Selected).Value
                      ?? options.FirstOrDefault().Value ?? "";
        if (proxy.Value != desired) proxy.Value = desired;
    }

    private static void SetValue(TextEditorBase editor, string value)
    {
        if (editor.Value != value) editor.Value = value;
    }

    private static void SetSource(ImageControl image, string source)
    {
        if (image.Source != source) image.Source = source;
    }

    private static void SetProxyVisible(UIElement proxy, bool visible)
    {
        if (proxy.IsVisible != visible) proxy.IsVisible = visible;
    }

    private void SyncDisclosure()
    {
        var open = HasAttribute("open");
        foreach (var child in Children)
            SetChildVisible(child, open || child is HtmlElement { TagName: "summary" });
    }

    /// <summary>
    /// Flattened option list; each entry pairs the display/value string (value attribute preferred
    /// over text content) with the selected flag. optgroup labels are not mirrored (single-level list).
    /// </summary>
    private List<(string Value, bool Selected)> CollectOptions()
    {
        var result = new List<(string, bool)>();
        CollectOptions(this, result);
        return result;
    }

    private static void CollectOptions(Element element, List<(string, bool)> result)
    {
        foreach (var child in element.Children)
        {
            if (child is HtmlElement { TagName: "option" } option)
                result.Add((OptionString(option), option.HasAttribute("selected")));
            else CollectOptions(child, result);
        }
    }

    private static string OptionString(HtmlElement option) => option.GetAttribute("value") ?? option.Text;

    /// <summary>
    /// Creates or replaces the native proxy for the current input type. type=hidden yields no proxy
    /// (the UA display rule hides the host). Unsupported types degrade to a single-line text input.
    /// </summary>
    private UIElement? EnsureFormControlProxy()
    {
        if (_tagName == "textarea") return EnsureOnlyProxy<TextArea>();
        var type = (GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
        switch (type)
        {
            case "hidden":
                RemoveAllProxies();
                return null;
            case "checkbox":
                return EnsureOnlyProxy<CheckBox>();
            case "radio":
                return EnsureOnlyProxy<Radio>();
            case "button" or "submit" or "reset":
                return EnsureOnlyProxy<Square.Controls.Button>();
            default:
            {
                var input = EnsureOnlyProxy<Input>();
                if (input == null) return null;
                var mapped = type == "password" ? "password" : type == "number" ? "number" : "text";
                if (input.Type != mapped) input.Type = mapped;
                return input;
            }
        }
    }

    /// <summary>Returns the single proxy of the requested kind, replacing any other proxy kind.</summary>
    private T? EnsureOnlyProxy<T>() where T : UIElement, new()
    {
        RemoveNonProxies<T>();
        return EnsureProxy<T>();
    }

    private void RemoveNonProxies<TKeep>() where TKeep : UIElement
    {
        for (var i = _proxies.Count - 1; i >= 0; i--)
        {
            if (_proxies[i] is TKeep) continue;
            var proxy = _proxies[i];
            _proxies.RemoveAt(i);
            Children.Remove(proxy);
        }
    }

    private void RemoveAllProxies()
    {
        foreach (var proxy in _proxies) Children.Remove(proxy);
        _proxies.Clear();
    }

    /// <summary>Returns the proxy of the requested kind, creating it as the last child on first use.</summary>
    private T? EnsureProxy<T>() where T : UIElement, new()
    {
        foreach (var proxy in _proxies)
            if (proxy is T typed) return typed;
        var created = new T();
        PrepareProxy(created);
        _proxies.Add(created);
        Children.Add(created);
        AttachProxyListeners(created);
        return created;
    }

    /// <summary>Proxy chrome: neutral margins so the host box equals the widget box.</summary>
    private static void PrepareProxy(UIElement proxy) =>
        proxy.Style.SetCascaded("margin", "0", int.MinValue);

    private void AttachProxyListeners(UIElement proxy)
    {
        switch (proxy)
        {
            case TextEditorBase editor:
                editor.AddEventListener(StandardEvents.Input, _ => WriteBackValue(editor));
                editor.AddEventListener(StandardEvents.Change, _ => WriteBackValue(editor));
                break;
            case CheckBox checkBox:
                checkBox.AddEventListener(StandardEvents.Change, _ => WriteBackChecked("checked", checkBox.IsChecked));
                break;
            case Radio radio:
                radio.AddEventListener(StandardEvents.Change, _ => WriteBackRadioChecked(radio));
                break;
            case Select select:
                select.AddEventListener(StandardEvents.Change, () => WriteBackSelected(select));
                break;
            case Square.Controls.Button:
                // Activation handlers belong to template event bindings; desktop submit stays unavailable.
                break;
        }
    }

    private void WriteBackValue(TextEditorBase editor)
    {
        if ((GetAttribute("value") ?? "") == editor.Value) return;
        SetAttribute("value", editor.Value);
    }

    /// <summary>
    /// Radio exclusivity clears sibling proxies without change events, so the group's checked
    /// attributes are swept here before mirroring this radio's own state.
    /// </summary>
    private void WriteBackRadioChecked(Radio radio)
    {
        if (radio.IsChecked) SweepRadioGroup(radio);
        WriteBackChecked("checked", radio.IsChecked);
    }

    private void WriteBackChecked(string attribute, bool isChecked)
    {
        if (HasAttribute(attribute) == isChecked) return;
        if (isChecked) SetAttribute(attribute, "");
        else RemoveAttribute(attribute);
    }

    /// <summary>Mirrors the select value back onto the selected attribute of the matching option.</summary>
    private void WriteBackSelected(Select select)
    {
        foreach (var child in Children)
        {
            if (child is not HtmlElement { TagName: "option" } option) continue;
            var matches = OptionString(option) == select.Value;
            if (option.HasAttribute("selected") == matches) continue;
            if (matches) option.SetAttribute("selected", "");
            else option.RemoveAttribute("selected");
        }
    }

    /// <summary>Concatenated text of the element's runs (option display text).</summary>
    private string Text
    {
        get
        {
            var builder = new System.Text.StringBuilder();
            AppendText(this, builder);
            return builder.ToString();
        }
    }

    private static void AppendText(Element element, System.Text.StringBuilder builder)
    {
        if (element is Square.Controls.Text run)
        {
            builder.Append(run.TextContent);
            return;
        }
        foreach (var child in element.Children)
            AppendText(child, builder);
    }

    private static int ParsePositiveInt(string? raw, int fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : fallback;
}
