using System.Diagnostics;
using Square.Events;
using Square.Graphics;
using Square.UI;

namespace Square.Controls;

/// <summary>图像控件，支持位图与矢量图，可加载本地或远程源。</summary>
public class Image : UIElement, ITextSelectable, IFrameScheduledElement
{
    private IImageFrameSource? _frameSource;
    private Bitmap? _sourceSurface;
    private CancellationTokenSource? _loadCancellation;
    private int _loadVersion;
    private int _frameIndex;
    private int _completedPlays;
    private TimeSpan _remainingFrameDelay;
    private long _frameDeadline;
    private bool _frameScheduled;

    /// <summary>图像源地址。</summary>
    public string Source { get => GetProperty<string>(nameof(Source)) ?? ""; set => SetProperty(nameof(Source), value); }
    /// <summary>直接绑定的图像内容。</summary>
    public Square.Graphics.Image? ImageContent { get => GetProperty<Square.Graphics.Image>(nameof(ImageContent)); set => SetProperty(nameof(ImageContent), value); }
    /// <summary>加载过程中遇到的错误（如有）。</summary>
    public Exception? Error { get; private set; }

    /// <inheritdoc/>
    public string SelectableText => Source;
    /// <inheritdoc/>
    public Rect SelectableTextBounds => string.IsNullOrEmpty(Source)
        ? Rect.Empty
        : ControlDrawing.GetTextBounds(this, Source, 12f, new Point(Geometry.X + 8, Geometry.Y + 8));

    private Square.Graphics.Image? DisplayImage => _sourceSurface ?? ImageContent;

    /// <inheritdoc/>
    public override Size Measure(Size availableSize)
    {
        var image = DisplayImage;
        if (image == null) return new Size(160, 96);

        var scale = 1f;
        if (availableSize.Width > 0 && float.IsFinite(availableSize.Width))
            scale = Math.Min(scale, availableSize.Width / image.Width);
        if (availableSize.Height > 0 && float.IsFinite(availableSize.Height))
            scale = Math.Min(scale, availableSize.Height / image.Height);
        return new Size(image.Width * scale, image.Height * scale);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(string name)
    {
        base.OnPropertyChanged(name);
        if (name == nameof(Source)) BeginSourceLoad();
        else if (name == nameof(ImageContent))
        {
            if (ImageContent != null)
            {
                ++_loadVersion;
                CancelPendingLoad();
                DisposeLoadedSource();
            }
            else if (!string.IsNullOrWhiteSpace(Source)) BeginSourceLoad();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedCore()
    {
        base.OnAttachedCore();
        if (_frameSource == null && ImageContent == null && !string.IsNullOrWhiteSpace(Source)) BeginSourceLoad();
        else ResumeAnimation();
    }

    /// <inheritdoc/>
    protected override void OnDetachedCore()
    {
        CancelPendingLoad();
        DisposeLoadedSource();
        base.OnDetachedCore();
    }

    /// <inheritdoc/>
    protected override void OnEffectiveVisibilityChanged(bool isVisible)
    {
        base.OnEffectiveVisibilityChanged(isVisible);
        if (isVisible) ResumeAnimation();
        else PauseAnimation();
    }

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        var image = DisplayImage;
        if (image is VectorImage vectorImage)
        {
            vectorImage.Draw(ctx, Geometry);
            return;
        }

        if (image != null)
        {
            ctx.DrawImage(image, Geometry);
            return;
        }

        const int tileSize = 12;
        for (var y = 0; y < Geometry.Height; y += tileSize)
            for (var x = 0; x < Geometry.Width; x += tileSize)
                ctx.FillRect(new Rect(Geometry.X + x, Geometry.Y + y, tileSize, tileSize),
                    new SolidColorBrush(((x + y) / tileSize) % 2 == 0 ? Color.FromRgb(230, 233, 236) : Color.White));
        ctx.DrawRect(Geometry, Pen.FromColor(Color.FromRgb(150, 155, 160)));
        if (!string.IsNullOrEmpty(Source))
            ControlDrawing.DrawText(ctx, this, Source, new Point(Geometry.X + 8, Geometry.Y + 8), Color.FromRgb(80, 85, 90), 12f);
    }

    private void BeginSourceLoad()
    {
        var version = ++_loadVersion;
        CancelPendingLoad();
        DisposeLoadedSource();
        Error = null;
        InvalidateLayout();

        var source = Source;
        if (!IsAttached || ImageContent != null || string.IsNullOrWhiteSpace(source)) return;

        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _ = LoadSourceAsync(source, version, cancellation.Token);
    }

    private async Task LoadSourceAsync(string source, int version, CancellationToken cancellationToken)
    {
        IImageFrameSource? loaded = null;
        try
        {
            loaded = await ImageSourceLoaderRegistry.LoadAsync(source, cancellationToken).ConfigureAwait(false);
            var dispatcher = Dispatcher;
            if (dispatcher == null)
            {
                loaded.Dispose();
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (version != _loadVersion || cancellationToken.IsCancellationRequested || !IsAttached)
                {
                    loaded.Dispose();
                    return;
                }

                ApplyLoadedSource(loaded);
                loaded = null;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            loaded?.Dispose();
        }
        catch (Exception exception)
        {
            loaded?.Dispose();
            var dispatcher = Dispatcher;
            if (dispatcher == null) return;
            await dispatcher.InvokeAsync(() =>
            {
                if (version != _loadVersion || cancellationToken.IsCancellationRequested || !IsAttached) return;
                Error = exception;
                InvalidatePaint();
                DispatchEvent(new Event("loaderror", new EventInit { Bubbles = true }));
            }).ConfigureAwait(false);
        }
    }

    private void ApplyLoadedSource(IImageFrameSource source)
    {
        DisposeLoadedSource();
        _frameSource = source;
        _sourceSurface = new Bitmap(source.Width, source.Height);
        _frameIndex = 0;
        _completedPlays = 0;
        CopyCurrentFrame();
        Error = null;
        InvalidateLayout();
        DispatchEvent(new Event("load", new EventInit { Bubbles = true }));
        ResumeAnimation();
    }

    private void CopyCurrentFrame()
    {
        if (_frameSource == null || _sourceSurface == null) return;
        _sourceSurface.CopyPixelsFrom(_frameSource.GetFrame(_frameIndex));
    }

    private void ResumeAnimation()
    {
        if (!CanAnimate() || _frameScheduled) return;
        var delay = _remainingFrameDelay > TimeSpan.Zero
            ? _remainingFrameDelay
            : NormalizeFrameDelay(_frameSource!.GetFrameDuration(_frameIndex));
        _remainingFrameDelay = TimeSpan.Zero;
        _frameDeadline = Stopwatch.GetTimestamp() + ToStopwatchTicks(delay);
        _frameScheduled = true;
        DispatchEvent(StandardEvents.CreateRequestFrame(delay));
    }

    private void PauseAnimation()
    {
        if (!_frameScheduled) return;
        var ticks = Math.Max(0, _frameDeadline - Stopwatch.GetTimestamp());
        _remainingFrameDelay = TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
        _frameScheduled = false;
    }

    private void AdvanceAnimationIfDue()
    {
        var now = Stopwatch.GetTimestamp();
        if (!_frameScheduled || now < _frameDeadline) return;
        _frameScheduled = false;
        if (!CanAnimate()) return;

        var advanced = false;
        while (now >= _frameDeadline)
        {
            if (_frameIndex + 1 < _frameSource!.FrameCount)
            {
                _frameIndex++;
            }
            else
            {
                _completedPlays++;
                if (_frameSource.PlayCount > 0 && _completedPlays >= _frameSource.PlayCount)
                {
                    if (advanced) CopyCurrentFrame();
                    return;
                }
                _frameIndex = 0;
            }

            advanced = true;
            _frameDeadline += ToStopwatchTicks(NormalizeFrameDelay(_frameSource.GetFrameDuration(_frameIndex)));
        }

        if (advanced) CopyCurrentFrame();
        _frameScheduled = true;
        DispatchEvent(StandardEvents.CreateRequestFrame(
            TimeSpan.FromSeconds(Math.Max(0, _frameDeadline - now) / (double)Stopwatch.Frequency)));
    }

    void IFrameScheduledElement.OnFrameDue()
    {
        AdvanceAnimationIfDue();
        InvalidatePaint();
    }

    private bool CanAnimate() => IsAttached && IsEffectivelyVisible && _frameSource is { FrameCount: > 1 };

    private static TimeSpan NormalizeFrameDelay(TimeSpan delay) =>
        delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(10);

    private static long ToStopwatchTicks(TimeSpan delay) =>
        Math.Max(0, (long)Math.Ceiling(delay.TotalSeconds * Stopwatch.Frequency));

    private void CancelPendingLoad()
    {
        var cancellation = Interlocked.Exchange(ref _loadCancellation, null);
        if (cancellation == null) return;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void DisposeLoadedSource()
    {
        PauseAnimation();
        _frameSource?.Dispose();
        _frameSource = null;
        _sourceSurface?.Dispose();
        _sourceSurface = null;
        _frameIndex = 0;
        _completedPlays = 0;
        _remainingFrameDelay = TimeSpan.Zero;
    }
}

/// <summary>自由绘制画布控件，通过回调向其注入绘制逻辑。</summary>
public class Canvas : UIElement, ITextSelectable
{
    private Action<IRenderContext, Rect>? _animationFrameCallback;

    /// <summary>自定义绘制回调，每帧调用以绘制内容。</summary>
    public Action<IRenderContext, Rect>? DrawContent { get; set; }

    /// <inheritdoc/>
    public string SelectableText => GetProperty<string>(nameof(SelectableText)) ?? "Canvas";
    /// <inheritdoc/>
    public Rect SelectableTextBounds => Geometry;

    /// <summary>请求后续帧；默认 30fps，避免软件全窗口 Present 时 CPU 过高。</summary>
    public void RequestFrame(double fps = 30d)
    {
        InvalidatePaint();
        DispatchEvent(StandardEvents.CreateRequestFrame(fps));
    }

    /// <summary>请求一帧动画并绑定回调。</summary>
    public void RequestAnimationFrame(Action<IRenderContext, Rect> callback) =>
        RequestAnimationFrame(callback, 30d);

    /// <summary>请求一帧动画并绑定回调，指定帧率。</summary>
    public void RequestAnimationFrame(Action<IRenderContext, Rect> callback, double fps)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _animationFrameCallback = callback;
        RequestFrame(fps);
    }

    /// <summary>取消动画帧回调。</summary>
    public void CancelAnimationFrame() => _animationFrameCallback = null;

    /// <inheritdoc/>
    protected override void OnDetachedCore()
    {
        CancelAnimationFrame();
        base.OnDetachedCore();
    }

    /// <inheritdoc/>
    public override Size Measure(Size availableSize) => new(300, 140);

    /// <inheritdoc/>
    public override void Paint(IRenderContext ctx)
    {
        // 轻量背景：避免每帧绘制大量网格 Path（软件光栅很贵）
        ctx.FillRect(Geometry, new SolidColorBrush(Color.White));
        ctx.DrawRect(Geometry, Pen.FromColor(Color.FromRgb(170, 175, 180)));

        var frameCallback = _animationFrameCallback;
        _animationFrameCallback = null;
        if (frameCallback != null)
            frameCallback(ctx, Geometry);
        else if (DrawContent != null)
            DrawContent(ctx, Geometry);
        else
        {
            ctx.FillRect(new Rect(Geometry.X + 20, Geometry.Y + 20, 80, 44), new SolidColorBrush(Color.FromRgb(0, 120, 212)));
            ctx.FillGeometry(new EllipseGeometry(new Point(Geometry.X + 150, Geometry.Y + 50), 28, 28),
                new SolidColorBrush(Color.FromRgb(18, 155, 105)));
        }
    }
}
