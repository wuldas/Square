using System;
using System.Diagnostics;
using Android.Graphics;
using Square.Graphics;
using SquareBitmap = Square.Graphics.Bitmap;
using SquareRect = Square.Graphics.Rect;
using AndroidView = global::Android.Views.View;
using AndroidBitmap = Android.Graphics.Bitmap;

namespace Square.Platform.Android;

/// <summary>将 Square BGRA 位图呈现到 Android ARGB_8888 位图。</summary>
public sealed class AndroidBitmapPresenter : IDisposable
{
    private readonly object _gate = new();
    private AndroidView? _view;
    private AndroidBitmap? _bitmap;
    private int[] _argbPixels = [];
    private global::Android.Graphics.Rect _destination = new();
    private bool _disposed;
    private long _presentCount;
    private long _totalUploadTicks;
    private long _lastUploadTicks;
    private long _uploadedBytes;

    /// <summary>绑定承载绘制的 Square View。</summary>
    public void AttachView(AndroidView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _view = view;
        }
    }

    /// <summary>当前呈现位图宽度（物理像素）。</summary>
    public int Width
    {
        get { lock (_gate) return _bitmap?.Width ?? 0; }
    }

    /// <summary>当前呈现位图高度（物理像素）。</summary>
    public int Height
    {
        get { lock (_gate) return _bitmap?.Height ?? 0; }
    }

    /// <summary>同步复制一帧 BGRA 数据并请求 View 重绘。</summary>
    public void Present(SquareBitmap frame, IReadOnlyList<SquareRect>? dirtyRects)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var started = Stopwatch.GetTimestamp();
        AndroidView? view;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (dirtyRects is { Count: 0 }) return;
            EnsureBitmap(frame.Width, frame.Height);
            if (dirtyRects == null)
                CopyRect(frame, 0, 0, frame.Width, frame.Height);
            else
                foreach (var dirty in dirtyRects)
                    CopyRect(frame, dirty);

            // Android's SetPixels accepts packed ARGB ints. Square stores BGRA bytes;
            // on every supported Android ABI the explicit conversion below preserves
            // channel order without relying on host endianness.
            _bitmap!.SetPixels(_argbPixels, 0, frame.Width, 0, 0, frame.Width, frame.Height);
            var elapsed = Stopwatch.GetTimestamp() - started;
            _presentCount++;
            _totalUploadTicks += elapsed;
            _lastUploadTicks = elapsed;
            _uploadedBytes += checked((long)_argbPixels.Length * sizeof(int));
            view = _view;
        }

        view?.PostInvalidateOnAnimation();
    }

    internal AndroidPresenterMetrics GetMetrics()
    {
        lock (_gate)
            return new AndroidPresenterMetrics(
                _presentCount, _totalUploadTicks, _lastUploadTicks, _uploadedBytes,
                _bitmap?.Width ?? 0, _bitmap?.Height ?? 0);
    }

    /// <summary>在 Android Canvas 上绘制当前帧。</summary>
    public void Draw(Canvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        lock (_gate)
        {
            if (_disposed || _bitmap == null) return;
            canvas.DrawBitmap(_bitmap, null, _destination, null);
        }
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap is { Width: var currentWidth, Height: var currentHeight } &&
            currentWidth == width && currentHeight == height)
            return;

        _bitmap?.Dispose();
        _bitmap = AndroidBitmap.CreateBitmap(width, height, AndroidBitmap.Config.Argb8888!);
        _bitmap.SetPremultiplied(true);
        _argbPixels = new int[checked(width * height)];
        _destination.Set(0, 0, width, height);
    }

    private void CopyRect(SquareBitmap frame, SquareRect rect)
    {
        var left = Math.Clamp((int)MathF.Floor(rect.Left), 0, frame.Width);
        var top = Math.Clamp((int)MathF.Floor(rect.Top), 0, frame.Height);
        var right = Math.Clamp((int)MathF.Ceiling(rect.Right), left, frame.Width);
        var bottom = Math.Clamp((int)MathF.Ceiling(rect.Bottom), top, frame.Height);
        CopyRect(frame, left, top, right, bottom);
    }

    private void CopyRect(SquareBitmap frame, int left, int top, int right, int bottom)
    {
        var source = frame.Pixels;
        for (var y = top; y < bottom; y++)
        {
            var sourceOffset = y * frame.Stride + left * 4;
            var destinationOffset = y * frame.Width + left;
            for (var x = left; x < right; x++, destinationOffset++, sourceOffset += 4)
            {
                var blue = source[sourceOffset];
                var green = source[sourceOffset + 1];
                var red = source[sourceOffset + 2];
                var alpha = source[sourceOffset + 3];
                _argbPixels[destinationOffset] = blue | green << 8 | red << 16 | alpha << 24;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _view = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _destination.Dispose();
            _argbPixels = [];
        }
    }
}

internal readonly record struct AndroidPresenterMetrics(
    long PresentCount,
    long TotalUploadTicks,
    long LastUploadTicks,
    long UploadedBytes,
    int BitmapWidth,
    int BitmapHeight);
