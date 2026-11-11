using System.Runtime.InteropServices;
using Square.Graphics;

namespace Square.Platform.Win32;

internal sealed unsafe class Win32DibSoftwareRenderSurface : ISoftwareRenderSurface
{
    private readonly Func<IntPtr> _windowHandle;
    private readonly Action? _presentCompleted;
    private IntPtr _memoryDc;
    private IntPtr _bitmap;
    private IntPtr _previousBitmap;
    private IntPtr _bits;
    private int _width;
    private int _height;
    private int _stride;
    private bool _disposed;

    public Win32DibSoftwareRenderSurface(Func<IntPtr> windowHandle, int width, int height)
        : this(windowHandle, width, height, null)
    {
    }

    public Win32DibSoftwareRenderSurface(Func<IntPtr> windowHandle, int width, int height, Action? presentCompleted)
    {
        _windowHandle = windowHandle;
        _presentCompleted = presentCompleted;
        Resize(width, height);
    }

    public int Width => _width;
    public int Height => _height;
    public int Stride => _stride;

    public Span<byte> GetRowSpan(int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)y >= (uint)_height) throw new ArgumentOutOfRangeException(nameof(y));
        return new Span<byte>((void*)IntPtr.Add(_bits, y * _stride), _stride);
    }

    public void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_width == width && _height == height) return;

        var bitmapInfo = new Win32Api.BITMAPINFO
        {
            bmiHeader = new Win32Api.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Win32Api.BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32Api.BI_RGB,
                biSizeImage = checked((uint)(width * height * 4))
            }
        };
        var memoryDc = Win32Api.CreateCompatibleDC(IntPtr.Zero);
        if (memoryDc == IntPtr.Zero)
            throw new InvalidOperationException("CreateCompatibleDC failed.");
        var bitmap = Win32Api.CreateDIBSection(
            memoryDc, ref bitmapInfo, Win32Api.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            Win32Api.DeleteDC(memoryDc);
            throw new InvalidOperationException("CreateDIBSection failed.");
        }
        var previousBitmap = Win32Api.SelectObject(memoryDc, bitmap);
        if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
        {
            Win32Api.DeleteObject(bitmap);
            Win32Api.DeleteDC(memoryDc);
            throw new InvalidOperationException("SelectObject failed for DIB section.");
        }

        if (_memoryDc != IntPtr.Zero && _width > 0 && _height > 0)
        {
            Win32Api.StretchBlt(
                memoryDc, 0, 0, width, height,
                _memoryDc, 0, 0, _width, _height,
                Win32Api.SRCCOPY);
            Win32Api.GdiFlush(); // Finish the GDI copy before exposing the new shared bits to the CPU.
        }

        ReleaseResources();
        _memoryDc = memoryDc;
        _bitmap = bitmap;
        _previousBitmap = previousBitmap;
        _bits = bits;
        _width = width;
        _height = height;
        _stride = width * 4;
    }

    public void Present(IReadOnlyList<Rect>? dirtyRects)
    {
        if (dirtyRects is { Count: 0 }) return;
        var hwnd = _windowHandle();
        if (hwnd == IntPtr.Zero) return;
        var destinationDc = Win32Api.GetDC(hwnd);
        var submitted = false;
        try
        {
            if (destinationDc != IntPtr.Zero) submitted = Blit(destinationDc, dirtyRects);
        }
        finally
        {
            if (destinationDc != IntPtr.Zero)
            {
                // Finish queued source reads before the CPU can modify this DIB again.
                submitted &= Win32Api.GdiFlush();
                Win32Api.ReleaseDC(hwnd, destinationDc);
            }
        }
        if (submitted) _presentCompleted?.Invoke();
    }

    internal void Repaint(IntPtr destinationDc, Win32Api.RECT rect)
    {
        if (destinationDc == IntPtr.Zero || _memoryDc == IntPtr.Zero) return;
        var left = Math.Clamp(rect.Left, 0, _width);
        var top = Math.Clamp(rect.Top, 0, _height);
        var right = Math.Clamp(rect.Right, left, _width);
        var bottom = Math.Clamp(rect.Bottom, top, _height);
        if (right <= left || bottom <= top) return;
        Win32Api.BitBlt(
            destinationDc, left, top, right - left, bottom - top,
            _memoryDc, left, top, Win32Api.SRCCOPY);
        Win32Api.GdiFlush(); // WM_PAINT releases GDI ownership but never waits for composition.
    }

    private bool Blit(IntPtr destinationDc, IReadOnlyList<Rect>? dirtyRects)
    {
        var left = 0;
        var top = 0;
        var right = _width;
        var bottom = _height;
        if (dirtyRects != null)
        {
            left = _width;
            top = _height;
            right = bottom = 0;
            foreach (var rect in dirtyRects)
            {
                var x0 = Math.Clamp((int)MathF.Floor(rect.Left), 0, _width);
                var y0 = Math.Clamp((int)MathF.Floor(rect.Top), 0, _height);
                var x1 = Math.Clamp((int)MathF.Ceiling(rect.Right), x0, _width);
                var y1 = Math.Clamp((int)MathF.Ceiling(rect.Bottom), y0, _height);
                if (x1 <= x0 || y1 <= y0) continue;
                left = Math.Min(left, x0);
                top = Math.Min(top, y0);
                right = Math.Max(right, x1);
                bottom = Math.Max(bottom, y1);
            }
        }
        if (right <= left || bottom <= top) return false;
        // One retained-buffer update avoids exposing separate application blits for one frame.
        return Win32Api.BitBlt(destinationDc, left, top, right - left, bottom - top,
            _memoryDc, left, top, Win32Api.SRCCOPY);
    }

    private void ReleaseResources()
    {
        if (_memoryDc != IntPtr.Zero)
        {
            if (_previousBitmap != IntPtr.Zero)
                Win32Api.SelectObject(_memoryDc, _previousBitmap);
            if (_bitmap != IntPtr.Zero)
                Win32Api.DeleteObject(_bitmap);
            Win32Api.DeleteDC(_memoryDc);
        }
        _memoryDc = IntPtr.Zero;
        _bitmap = IntPtr.Zero;
        _previousBitmap = IntPtr.Zero;
        _bits = IntPtr.Zero;
        _width = 0;
        _height = 0;
        _stride = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseResources();
    }
}
