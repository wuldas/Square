using Square.Events;
using Square.UI;

namespace Square.Controls;

/// <summary>A single-page viewport that navigates through its direct children.</summary>
public class Swiper : View
{
    private int _selectedIndex;

    /// <summary>初始化 <see cref="Swiper"/> 的新实例。</summary>
    public Swiper()
    {
        Style.SetCascaded("overflow", "hidden", int.MinValue);
        AddEventListener<KeyboardEvent>(StandardEvents.KeyDown, e =>
        {
            if (!HandleKey(e.KeyCode)) return;
            e.PreventDefault();
        });
    }

    /// <summary>当前页索引，无子项时为 -1。</summary>
    public int SelectedIndex
    {
        get => Children.Count == 0 ? -1 : Math.Clamp(_selectedIndex, 0, Children.Count - 1);
        set => GoTo(value);
    }

    /// <summary>是否循环切换。</summary>
    public bool Loop
    {
        get => Properties.HasValue(nameof(Loop)) && GetProperty<bool>(nameof(Loop));
        set => SetProperty(nameof(Loop), value);
    }

    /// <summary>子页数量。</summary>
    public int Count => Children.Count;
    /// <summary>当前页元素。</summary>
    public Element? SelectedItem => SelectedIndex >= 0 ? Children[SelectedIndex] : null;
    /// <summary>是否可以前往上一页。</summary>
    public bool CanGoPrevious => Children.Count > 1 && (Loop || SelectedIndex > 0);
    /// <summary>是否可以前往下一页。</summary>
    public bool CanGoNext => Children.Count > 1 && (Loop || SelectedIndex < Children.Count - 1);

    /// <summary>跳转到指定索引的页，返回是否实际切换。</summary>
    public bool GoTo(int index)
    {
        if (Children.Count == 0)
        {
            _selectedIndex = 0;
            return false;
        }

        var next = Loop ? Mod(index, Children.Count) : Math.Clamp(index, 0, Children.Count - 1);
        var previous = SelectedIndex;
        _selectedIndex = next;
        UpdatePageVisibility();
        if (previous == next) return false;
        DispatchEvent(StandardEvents.CreateChange());
        return true;
    }

    /// <summary>跳到下一页。</summary>
    public bool Next() => CanGoNext && GoTo(SelectedIndex + 1);
    /// <summary>跳到上一页。</summary>
    public bool Previous() => CanGoPrevious && GoTo(SelectedIndex - 1);

    /// <summary>处理键盘导航，返回是否已处理。</summary>
    public bool HandleKey(int keyCode)
    {
        if (!IsEnabled || Children.Count == 0) return false;
        return keyCode switch
        {
            37 => Previous(),
            39 => Next(),
            36 => GoTo(0),
            35 => GoTo(Children.Count - 1),
            _ => false
        };
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(SelectedIndex)) GoTo(GetProperty<int>(nameof(SelectedIndex)));
    }

    internal override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        // Pages are viewport-sized by default; author styles keep precedence.
        child.Style.SetCascaded("width", "100%", int.MinValue);
        child.Style.SetCascaded("height", "100%", int.MinValue);
        child.IsVisible = Children.IndexOf(child) == SelectedIndex;
    }

    internal override void OnChildRemoved(Element child)
    {
        var removedIndex = Children.IndexOf(child);
        base.OnChildRemoved(child);
        if (removedIndex >= 0 && removedIndex < _selectedIndex) _selectedIndex--;
        if (_selectedIndex >= Children.Count) _selectedIndex = Math.Max(0, Children.Count - 1);
        UpdatePageVisibility();
    }

    private void UpdatePageVisibility()
    {
        var selected = SelectedIndex;
        for (var i = 0; i < Children.Count; i++) Children[i].IsVisible = i == selected;
        InvalidateLayout();
    }

    private static int Mod(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
