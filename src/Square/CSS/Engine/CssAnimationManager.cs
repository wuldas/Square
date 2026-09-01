using Square.UI;

namespace Square.CSS.Engine;

/// <summary>管理元素树中 CSS 动画的创建与推进。</summary>
public sealed class CssAnimationManager
{
    private readonly CssEngine _engine;
    private readonly List<CssAnimationTimeline> _timelines = [];

    /// <summary>初始化 CssAnimationManager 的新实例。</summary>
    /// <param name="engine">关联的 CSS 引擎。</param>
    public CssAnimationManager(CssEngine engine)
    {
        _engine = engine;
    }

    /// <summary>获取是否存在仍在运行的动画。</summary>
    public bool HasRunningAnimations => _timelines.Any(timeline => !timeline.IsComplete);

    /// <summary>附加到指定根元素，收集并启动其动画时间线。</summary>
    /// <param name="root">根元素。</param>
    public void Attach(Element root)
    {
        Clear();
        Collect(root);
        foreach (var timeline in _timelines)
            timeline.Start();
    }

    /// <summary>推进所有未完成动画的时间线。</summary>
    /// <param name="deltaSeconds">增量秒数。</param>
    public void Tick(float deltaSeconds)
    {
        foreach (var timeline in _timelines.Where(timeline => !timeline.IsComplete).ToArray())
            timeline.Tick(deltaSeconds);
    }

    internal void Clear()
    {
        foreach (var timeline in _timelines)
            timeline.Cancel();
        _timelines.Clear();
    }

    private void Collect(Element Element)
    {
        var timeline = _engine.CreateAnimationTimeline(Element);
        if (timeline != null) _timelines.Add(timeline);
        foreach (var child in Element.Children)
            Collect(child);
    }
}
