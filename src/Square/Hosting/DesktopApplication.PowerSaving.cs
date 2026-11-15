using Square.Platform;
using Square.CSS.Engine;

namespace Square.Hosting;

public sealed partial class DesktopApplication
{
    private int _frameProcessingDepth;
    private bool _frameClockIdle = true;

    /// <summary>帧需求改变；外部宿主应在 UI 线程查询下一次截止时间并合并调度。</summary>
    public event Action? FrameRequested;

    internal TimeSpan? NextFrameDelay
    {
        get
        {
            Dispatcher.VerifyAccess();
            if (!_sessionPrepared || _sessionSuspended || _host == null) return null;
            if (_host.State == AppWindowState.Minimized)
            {
                _frameClockIdle = true;
                return Dispatcher.HasWork || _document.Context.Reconciler.HasWork ? TimeSpan.Zero : null;
            }
            if (_host.ClientSize.Width <= 0 || _host.ClientSize.Height <= 0) return null;
            if (!MainWindow.PowerSavingEnabled) return TimeSpan.Zero;
            if (Volatile.Read(ref _renderRequested) || Dispatcher.HasWork || _pendingRendererCaptures.Count > 0 ||
                _document.Context.Reconciler.HasWork || CssStyleReconciler.HasWorkForTree(_root) ||
                HasVisualInvalidation(_root) || CssStyleReconciler.HasRunningAnimationsForTree(_root))
                return TimeSpan.Zero;

            var now = _clock.Elapsed.TotalSeconds;
            TimeSpan? delay = null;
            foreach (var pair in _scheduledFrames)
                if (pair.Key.IsAttached && pair.Key.IsEffectivelyVisible && ReferenceEquals(pair.Key.OwnerDocument, _document))
                    Include(TimeSpan.FromSeconds(Math.Max(0, pair.Value - now)));
            if (_pressedScrollbar != null)
                Include(TimeSpan.FromSeconds(Math.Max(0, _nextScrollbarRepeatSeconds - now)));
            if (FocusedTextEditor?.CaretBlinkDelay is { } caretDelay)
                Include(caretDelay);
            // No CSS timeline is running here. A caret/control deadline can still
            // leave a long sleep before a newly started animation's first Tick.
            _frameClockIdle = true;
            return delay;

            void Include(TimeSpan candidate)
            {
                if (candidate < TimeSpan.Zero) candidate = TimeSpan.Zero;
                if (delay == null || candidate < delay.Value) delay = candidate;
            }
        }
    }

    private void NotifyFrameRequested()
    {
        // In-frame invalidation is consumed by this frame or its final deadline query.
        // Other threads still wake the native loop so work queued at the end cannot be lost.
        if (Dispatcher.CheckAccess() && _frameProcessingDepth > 0) return;
        _host?.Wake();
        FrameRequested?.Invoke();
    }

    private void HandleHostStateChanged(AppWindowState state)
    {
        _frameClockIdle = true;
        _lastAnimationTickSeconds = _clock.Elapsed.TotalSeconds;
        if (state != AppWindowState.Minimized) RequestRender();
        else NotifyFrameRequested();
    }
}
