using Android.Views;
using System.Diagnostics;
using Android.OS;

namespace Square.Platform.Android;

/// <summary>由 Android Choreographer 驱动的单 pending 帧调度器。</summary>
public sealed class AndroidFrameScheduler : Java.Lang.Object, Choreographer.IFrameCallback, IDisposable
{
    private readonly Choreographer _choreographer;
    private readonly Func<bool> _onFrame;
    private bool _callbackPending;
    private bool _paused;
    private bool _disposed;
    private long _frameCount;
    private long _totalFrameTicks;
    private long _lastFrameTicks;

    /// <summary>创建帧调度器；回调返回 true 时继续请求下一帧。</summary>
    public AndroidFrameScheduler(Func<bool> onFrame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        _choreographer = Choreographer.Instance ?? throw new InvalidOperationException("Android Choreographer is unavailable.");
        _onFrame = onFrame;
    }

    /// <summary>是否已有待处理的帧回调。</summary>
    public bool IsFramePending => _callbackPending;
    internal AndroidFrameMetrics GetMetrics() => new(_frameCount, _totalFrameTicks, _lastFrameTicks);

    /// <summary>请求一帧；重复请求合并。</summary>
    public void RequestFrame()
    {
        if (_disposed || _paused || _callbackPending) return;
        _callbackPending = true;
        _choreographer.PostFrameCallback(this);
    }

    /// <summary>暂停并撤销待处理回调。</summary>
    public void Pause()
    {
        if (_disposed) return;
        _paused = true;
        RemovePendingCallback();
    }

    /// <summary>恢复调度；不会在无需求时自动产生帧。</summary>
    public void Resume()
    {
        if (!_disposed) _paused = false;
    }

    /// <inheritdoc />
    public void DoFrame(long frameTimeNanos)
    {
        _callbackPending = false;
        if (_disposed || _paused) return;
        var started = Stopwatch.GetTimestamp();
        var requestNext = false;
        try
        {
            requestNext = _onFrame();
        }
        finally
        {
            var elapsed = Stopwatch.GetTimestamp() - started;
            _frameCount++;
            _totalFrameTicks += elapsed;
            _lastFrameTicks = elapsed;
            if (requestNext && !_disposed && !_paused)
                RequestFrame();
        }
    }

    private void RemovePendingCallback()
    {
        if (!_callbackPending) return;
        _choreographer.RemoveFrameCallback(this);
        _callbackPending = false;
    }

    /// <inheritdoc />
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemovePendingCallback();
        _paused = true;
        GC.SuppressFinalize(this);
    }
}

internal readonly record struct AndroidFrameMetrics(long FrameCount, long TotalFrameTicks, long LastFrameTicks);
