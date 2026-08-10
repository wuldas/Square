using Square.Graphics;
using Square.Platform;
using Square.Resources;
using Square.Text.Fonts;
using Square.UI;

namespace Square.Controls;

/// <summary>
/// Semantic custom title bar with <c>icon</c>, default title, and <c>control</c> slots.
/// The control slot falls back to standard window buttons.
/// </summary>
public class TitleBar : View
{
    private const string IconFontFamily = "Square Iconfont";
    private const string MaximizeIcon = "\ue669";
    private const string MinimizeIcon = "\ue66a";
    private const string CloseIcon = "\ue66b";
    private const string RestoreIcon = "\ue66c";
    private static readonly Lazy<bool> IconFontLoaded = new(LoadIconFont);
    private bool _visualTreeBuilt;

    /// <inheritdoc/>
    public override string TagName => "TitleBar";

    /// <summary>标题栏首选高度（像素）。</summary>
    public float PreferredHeight
    {
        get => GetProperty<float>(nameof(PreferredHeight)) is > 0 and var value ? value : 36f;
        set => SetProperty(nameof(PreferredHeight), Math.Max(1f, value));
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize) =>
        new(availableSize.Width, ResolveHeight());

    /// <inheritdoc/>
    public override void BuildElementTree()
    {
        if (_visualTreeBuilt) return;
        _visualTreeBuilt = true;

        Style.Set("display", "flex");
        Style.Set("flex-direction", "row");
        Style.Set("align-items", "center");
        Style.Set("justify-content", "space-between");

        var iconHost = CreateHost("title-bar-icon");
        var titleHost = CreateHost("title-bar-title");
        var controlHost = CreateHost("title-bar-control");
        iconHost.Style.Set("flex-shrink", "0");
        titleHost.Style.Set("flex-grow", "1");
        titleHost.Style.Set("flex-shrink", "1");
        titleHost.Style.Set("min-width", "0");
        controlHost.Style.Set("flex-direction", "row");
        controlHost.Style.Set("flex-shrink", "0");

        Children.Add(iconHost);
        Children.Add(titleHost);
        Children.Add(controlHost);

        Slots.Render("icon", iconHost);
        if (!Slots.Render("", titleHost))
            titleHost.Children.Add(new Text(AppWindow?.Title ?? ""));
        if (!Slots.Render("control", controlHost))
            BuildDefaultControls(controlHost);
    }

    private void BuildDefaultControls(View host)
    {
        _ = IconFontLoaded.Value;
        var minimize = CreateWindowButton("title-bar-minimize", MinimizeIcon, "最小化", out _);
        var maximize = CreateWindowButton("title-bar-maximize", MaximizeIcon, "最大化", out var maximizeIcon);
        var close = CreateWindowButton("title-bar-close", CloseIcon, "关闭", out _);

        minimize.AddEventListener("click", _ => AppWindow?.Minimize());
        maximize.AddEventListener("click", _ =>
        {
            if (AppWindow?.State == AppWindowState.Maximized)
                AppWindow.Restore();
            else
                AppWindow?.Maximize();
        });
        close.AddEventListener("click", _ => AppWindow?.Close());
        if (AppWindow is { } window)
        {
            void UpdateMaximizeIcon(AppWindowState state)
            {
                var maximized = state == AppWindowState.Maximized;
                maximizeIcon.TextContent = maximized ? RestoreIcon : MaximizeIcon;
                maximize.Tooltip = maximized ? "还原" : "最大化";
            }
            UpdateMaximizeIcon(window.State);
            window.StateChanged += UpdateMaximizeIcon;
        }

        host.Children.Add(minimize);
        host.Children.Add(maximize);
        host.Children.Add(close);
    }

    private static View CreateHost(string className)
    {
        var host = new View();
        host.ClassList.Add(className);
        host.Style.Set("display", "flex");
        host.Style.Set("align-items", "center");
        return host;
    }

    private static Button CreateWindowButton(string className, string glyph, string tooltip, out Text icon)
    {
        var button = new TitleBarButton(className == "title-bar-close");
        button.ClassList.Add("title-bar-button");
        button.ClassList.Add(className);
        button.Tooltip = tooltip;
        button.Style.Set("width", "46px");
        button.Style.Set("height", "36px");
        button.Style.Set("display", "flex");
        button.Style.Set("align-items", "center");
        button.Style.Set("justify-content", "center");
        icon = new Text(glyph);
        icon.ClassList.Add("title-bar-button-icon");
        icon.Style.Set("font-family", $"'{IconFontFamily}'");
        icon.Style.Set("font-size", "16px");
        icon.Style.Set("color", "#ffffff");
        icon.Style.Set("user-select", "none");
        button.Children.Add(icon);
        return button;
    }

    private static bool LoadIconFont()
    {
        var data = ApplicationResource.ReadAllBytes("iconfont/iconfont.ttf", typeof(TitleBar).Assembly);
        var face = new FontFace(IconFontFamily, data);
        FontFaceSet.Default.Add(face);
        face.LoadAsync().GetAwaiter().GetResult();
        return true;
    }

    private sealed class TitleBarButton(bool isClose) : Button
    {
        public override void Paint(IRenderContext context)
        {
            var fallback = HasState(ElementState.Active)
                ? isClose ? Color.FromRgb(181, 28, 28) : Color.FromRgba(255, 255, 255, 40)
                : HasState(ElementState.Hover)
                    ? isClose ? Color.FromRgb(232, 17, 35) : Color.FromRgba(255, 255, 255, 24)
                    : Color.Transparent;
            ControlDrawing.DrawStyledBackground(context, this,
                ControlDrawing.GetStyledColor(this, "background", fallback));
        }
    }

    private float ResolveHeight()
    {
        var value = Style.Get("height")?.Trim();
        if (!string.IsNullOrEmpty(value))
        {
            if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                value = value[..^2];
            if (float.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var height) && height > 0)
                return height;
        }
        return PreferredHeight;
    }
}
