using System.Globalization;
using Square.CSS.Ast;
using Square.UI;
using Square.UI.ElementApi;

namespace Square.CSS.Engine;

/// <summary>表示一条 CSS 动画时间线，负责根据关键帧驱动属性插值。</summary>
public sealed class CssAnimationTimeline
{
    private readonly Element _visual;
    private readonly List<AnimationTrack> _tracks;
    private readonly float _duration;
    private readonly float _delay;
    private readonly float _iterationCount;
    private readonly AnimationDirection _direction;
    private readonly Func<float, float> _easing;
    private float _elapsed;
    private bool _running;
    private bool _cleared;

    /// <summary>初始化 CssAnimationTimeline 的新实例。</summary>
    /// <param name="Element">目标元素。</param>
    /// <param name="keyFrames">关键帧规则。</param>
    /// <param name="duration">持续时间（秒）。</param>
    /// <param name="easing">缓动函数。</param>
    /// <param name="delay">延迟时间（秒）。</param>
    /// <param name="iterationCount">迭代次数。</param>
    /// <param name="direction">动画方向。</param>
    internal CssAnimationTimeline(Element Element, KeyFramesRule keyFrames, float duration, Func<float, float> easing, float delay = 0, float iterationCount = 1, string direction = "normal")
    {
        _visual = Element;
        _duration = Math.Max(0.0001f, duration);
        _delay = delay;
        _iterationCount = Math.Max(0, iterationCount);
        _direction = ParseDirection(direction);
        _easing = easing;
        _tracks = BuildTracks(keyFrames);
    }

    /// <summary>获取动画是否已完成。</summary>
    public bool IsComplete => _iterationCount != int.MaxValue && _elapsed >= Math.Max(0, _delay) + _duration * _iterationCount;

    /// <summary>动画目标元素；样式作用域重新应用时用它恢复进度。</summary>
    internal Element? Target => _visual;

    /// <summary>启动动画时间线，应用起始帧。</summary>
    public void Start() => Start(0f);

    /// <summary>以给定已流逝时间启动或恢复；点击等状态变化不应把动画倒回起始帧。</summary>
    public void Start(float elapsedSeconds)
    {
        _elapsed = Math.Max(0, elapsedSeconds);
        _running = true;
        _cleared = false;
        if (_iterationCount == 0)
        {
            _running = false;
            return;
        }
        if (_delay < 0)
        {
            _elapsed = Math.Min(_duration * _iterationCount, Math.Max(_elapsed, -_delay));
            Apply(_easing(GetDirectedProgress(_elapsed)));
            return;
        }
        // Resuming after a style replay must repaint the current frame, not wait for the next tick.
        if (_elapsed > 0 && _elapsed >= _delay)
            Apply(_easing(GetDirectedProgress(_elapsed - _delay)));
    }

    /// <summary>已流逝时间，供样式作用域重新应用时恢复进度。</summary>
    internal float Elapsed => _elapsed;

    /// <summary>推进动画时间线，应用当前帧。</summary>
    /// <param name="deltaSeconds">增量秒数。</param>
    public void Tick(float deltaSeconds)
    {
        if (!_running) return;
        var activeStart = Math.Max(0, _delay);
        var total = activeStart + _duration * _iterationCount;
        _elapsed = _iterationCount == int.MaxValue
            ? _elapsed + Math.Max(0, deltaSeconds)
            : Math.Min(total, _elapsed + Math.Max(0, deltaSeconds));
        if (_elapsed >= activeStart)
            Apply(_easing(GetDirectedProgress(_elapsed - activeStart + Math.Max(0, -_delay))));
        if (IsComplete)
        {
            _running = false;
            ClearAnimatedValues();
        }
    }

    internal void Cancel()
    {
        _running = false;
        ClearAnimatedValues();
    }

    private float GetDirectedProgress(float activeElapsed)
    {
        if (_iterationCount != int.MaxValue && activeElapsed >= _duration * _iterationCount)
            activeElapsed = _duration * _iterationCount;
        var maxIteration = Math.Max(0, (int)MathF.Ceiling(_iterationCount) - 1);
        var iteration = Math.Min(maxIteration, (int)MathF.Floor(activeElapsed / _duration));
        var local = Math.Clamp((activeElapsed - iteration * _duration) / _duration, 0f, 1f);
        if (_iterationCount != int.MaxValue && activeElapsed >= _duration * _iterationCount)
        {
            var fractional = _iterationCount - MathF.Floor(_iterationCount);
            local = fractional > 0 ? fractional : 1f;
        }
        var reverse = _direction switch
        {
            AnimationDirection.Reverse => true,
            AnimationDirection.Alternate => iteration % 2 == 1,
            AnimationDirection.AlternateReverse => iteration % 2 == 0,
            _ => false
        };
        return reverse ? 1f - local : local;
    }

    private void Apply(float progress)
    {
        foreach (var track in _tracks)
        {
            var value = track.ValueAt(progress);
            // 纯数字/px/单函数轨道写入数值 overlay；比较/输出沿用 FormatNumber("0.###") 量化语义。
            _visual.Style.SetAnimatedNumeric(track.Property,
                new AnimatedNumericValue(value, track.Format.Prefix, track.Format.Suffix));
        }
    }

    private void ClearAnimatedValues()
    {
        if (_cleared) return;
        _cleared = true;
        foreach (var track in _tracks)
            _visual.Style.RemoveAnimated(track.Property);
    }

    private static List<AnimationTrack> BuildTracks(KeyFramesRule keyFrames)
    {
        var values = new Dictionary<string, List<AnimationStop>>(StringComparer.OrdinalIgnoreCase);
        var formats = new Dictionary<string, AnimationValueFormat>(StringComparer.OrdinalIgnoreCase);
        foreach (var stop in keyFrames.Stops)
        {
            if (!TryParseProgress(stop.Selector, out var progress)) continue;
            foreach (var declaration in stop.Declarations)
            {
                if (!TryParseAnimationValue(declaration.Value, out var value, out var format)) continue;
                if (formats.TryGetValue(declaration.Property, out var known))
                {
                    if (known != format) continue;
                }
                else formats.Add(declaration.Property, format);
                if (!values.TryGetValue(declaration.Property, out var stops))
                    values.Add(declaration.Property, stops = []);
                stops.Add(new AnimationStop(progress, value));
            }
        }
        return values
            .Where(pair => pair.Value.Count >= 2)
            .Select(pair => new AnimationTrack(pair.Key, formats[pair.Key], pair.Value.OrderBy(stop => stop.Progress).ToArray()))
            .ToList();
    }

    private static bool TryParseProgress(string selector, out float progress)
    {
        if (string.Equals(selector, "from", StringComparison.OrdinalIgnoreCase))
        {
            progress = 0;
            return true;
        }
        if (string.Equals(selector, "to", StringComparison.OrdinalIgnoreCase))
        {
            progress = 1;
            return true;
        }
        if (selector.EndsWith("%", StringComparison.Ordinal) &&
            float.TryParse(selector[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            progress = Math.Clamp(percent / 100f, 0f, 1f);
            return true;
        }
        progress = 0;
        return false;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        var text = value.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseAnimationValue(string value, out float number, out AnimationValueFormat format)
    {
        number = 0;
        format = AnimationValueFormat.Number;
        value = value.Trim();
        if (TryParseFloat(value, out number)) return true;
        var open = value.IndexOf('(');
        if (open <= 0 || value[^1] != ')') return false;
        var name = value[..open].Trim();
        if (name.Length == 0 || !name.All(char.IsLetter)) return false;
        var argument = value[(open + 1)..^1].Trim();
        var unitStart = 0;
        while (unitStart < argument.Length &&
            (char.IsDigit(argument[unitStart]) || argument[unitStart] is '+' or '-' or '.' or 'e' or 'E')) unitStart++;
        var unit = argument[unitStart..].Trim().ToLowerInvariant();
        if (!unit.All(char.IsLetter)) return false;
        if (!float.TryParse(argument[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return false;
        format = new AnimationValueFormat(name.ToLowerInvariant() + "(", unit + ")");
        return true;
    }

    private sealed record AnimationTrack(string Property, AnimationValueFormat Format, AnimationStop[] Stops)
    {
        public float ValueAt(float progress)
        {
            if (progress <= Stops[0].Progress) return Stops[0].Value;
            for (var index = 1; index < Stops.Length; index++)
            {
                var end = Stops[index];
                if (progress > end.Progress) continue;
                var start = Stops[index - 1];
                var range = end.Progress - start.Progress;
                return range <= 0 ? end.Value : start.Value + (end.Value - start.Value) * ((progress - start.Progress) / range);
            }
            return Stops[^1].Value;
        }
    }

    private readonly record struct AnimationStop(float Progress, float Value);

    private readonly record struct AnimationValueFormat(string Prefix, string Suffix)
    {
        public static readonly AnimationValueFormat Number = new("", "");
    }
    private enum AnimationDirection { Normal, Reverse, Alternate, AlternateReverse }

    private static AnimationDirection ParseDirection(string value) => value.Trim().ToLowerInvariant() switch
    {
        "reverse" => AnimationDirection.Reverse,
        "alternate" => AnimationDirection.Alternate,
        "alternate-reverse" => AnimationDirection.AlternateReverse,
        _ => AnimationDirection.Normal
    };
}
