using Square.UI;

namespace Square.CSS.Engine;

/// <summary>管理元素树中 CSS 动画的创建与推进。</summary>
public sealed class CssAnimationManager
{
    private readonly CssEngine _engine;
    private readonly List<CssAnimationTimeline> _timelines = [];
    private int _timelineRevision;

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
        // Preserve progress: a click only changes state pseudo-classes, and re-applying the
        // scope must not rewind an infinite animation to its first frame (extra full frames
        // and a visible jump on every interaction).
        var elapsed = new Dictionary<Element, float>();
        foreach (var timeline in _timelines)
        {
            if (!timeline.IsComplete && timeline.Target is { } target)
                elapsed[target] = Math.Max(elapsed.TryGetValue(target, out var previous) ? previous : 0f, timeline.Elapsed);
        }
        Clear();
        Collect(root);
        foreach (var timeline in _timelines)
            timeline.Start(timeline.Target != null && elapsed.TryGetValue(timeline.Target, out var resume) ? resume : 0f);
    }

    /// <summary>推进所有未完成动画的时间线；重复应用样式不会重置进度。</summary>
    /// <param name="deltaSeconds">增量秒数。</param>
    public void Tick(float deltaSeconds)
    {
        var revision = _timelineRevision;
        for (var i = 0; i < _timelines.Count; i++)
        {
            var timeline = _timelines[i];
            if (timeline.IsComplete)
                timeline.Cancel();
            else
                timeline.Tick(deltaSeconds);
            // 样式回调可同步 Attach/Clear；新列表应从下一 tick 一起推进。
            if (revision != _timelineRevision) return;
        }
        _timelines.RemoveAll(static timeline => timeline.IsComplete);
    }

    internal void Clear()
    {
        _timelineRevision++;
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
