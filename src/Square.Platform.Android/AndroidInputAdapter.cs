using Android.Content;
using Android.Views;
using Square.Graphics;
using Square.Platform;

namespace Square.Platform.Android;

/// <summary>Android 触摸、鼠标滚轮和 fling 输入适配器。</summary>
public sealed class AndroidInputAdapter
{
    private const float TouchSlop = 8f;
    private const float FlingVelocityThreshold = 80f;
    private const float FlingMaxVelocity = 6000f;
    private readonly AndroidPlatformHost _host;
    private bool _active;
    private bool _moved;
    private bool _flinging;
    private int _pointerId = -1;
    private Point _lastPoint;
    private long _lastEventTime;
    private float _velocityX;
    private float _velocityY;
    private PointerDeviceKind _deviceKind;
    private int _flingPointerId;

    internal AndroidInputAdapter(AndroidPlatformHost host) => _host = host;

    /// <summary>当前是否有待处理的 fling。</summary>
    public bool HasFling => _flinging && (MathF.Abs(_velocityX) >= FlingVelocityThreshold ||
                                         MathF.Abs(_velocityY) >= FlingVelocityThreshold);

    /// <summary>处理 View 的触摸/鼠标指针事件。</summary>
    public bool HandleTouchEvent(MotionEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var action = e.ActionMasked;
        var index = Math.Clamp(e.ActionIndex, 0, Math.Max(0, e.PointerCount - 1));
        var device = ResolveDeviceKind(e, index);
        if (action == MotionEventActions.Down)
        {
            CancelFling();
            _pointerId = e.GetPointerId(index);
            _deviceKind = device;
            _lastPoint = ToLogicalPoint(e, index);
            _downPoint = _lastPoint;
            _lastEventTime = e.EventTime;
            _velocityX = 0;
            _velocityY = 0;
            _active = true;
            _moved = false;
            _host.RaisePointer(new PointerInput(
                _lastPoint, PointerAction.Down, _pointerId, device, ResolveButton(e), true));
            if (device == PointerDeviceKind.Touch)
                _host.RequestTextInputSurface();
            return true;
        }

        if (action == MotionEventActions.Cancel)
        {
            Cancel();
            return true;
        }

        if (!_active || _pointerId < 0) return action is MotionEventActions.PointerDown or MotionEventActions.Move;

        if (action == MotionEventActions.Move)
        {
            var currentIndex = e.FindPointerIndex(_pointerId);
            if (currentIndex < 0) return true;
            var point = ToLogicalPoint(e, currentIndex);
            var elapsed = Math.Max(1, e.EventTime - _lastEventTime);
            var deltaX = point.X - _lastPoint.X;
            var deltaY = point.Y - _lastPoint.Y;
            if (!_moved)
            {
                var fromDownX = point.X - _downPoint.X;
                var fromDownY = point.Y - _downPoint.Y;
                _moved = fromDownX * fromDownX + fromDownY * fromDownY > TouchSlop * TouchSlop;
            }
            _velocityX = Math.Clamp(deltaX * 1000f / elapsed, -FlingMaxVelocity, FlingMaxVelocity);
            _velocityY = Math.Clamp(deltaY * 1000f / elapsed, -FlingMaxVelocity, FlingMaxVelocity);
            _lastPoint = point;
            _lastEventTime = e.EventTime;
            _host.RaisePointer(new PointerInput(point, PointerAction.Move, _pointerId, _deviceKind,
                ResolveButton(e), true));
            if (_moved && (deltaX != 0 || deltaY != 0))
                _host.RaiseWheel(new WheelInput(point, -deltaX, -deltaY, true, false, _deviceKind, _pointerId));
            return true;
        }

        if (action is MotionEventActions.Up or MotionEventActions.PointerUp)
        {
            if (e.GetPointerId(index) != _pointerId) return true;
            var point = ToLogicalPoint(e, index);
            _host.RaisePointer(new PointerInput(point, PointerAction.Up, _pointerId, _deviceKind,
                ResolveButton(e), true));
            if (_moved && _deviceKind == PointerDeviceKind.Touch)
            {
                _flingPointerId = _pointerId;
                BeginFling();
            }
            _active = false;
            _moved = false;
            _pointerId = -1;
            return true;
        }

        return true;
    }

    /// <summary>处理外接鼠标的悬停和滚轮事件。</summary>
    public bool HandleGenericMotionEvent(MotionEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if ((e.Source & InputSourceType.ClassPointer) == 0) return false;
        var point = ToLogicalPoint(e, 0);
        if (e.ActionMasked == MotionEventActions.HoverMove)
        {
            _host.RaisePointer(new PointerInput(point, PointerAction.Move, 0,
                PointerDeviceKind.Mouse, MouseButton.None, true));
            return true;
        }

        if (e.ActionMasked != MotionEventActions.Scroll) return false;
        var deltaX = e.GetAxisValue(Axis.Hscroll);
        var deltaY = -e.GetAxisValue(Axis.Vscroll);
        if (deltaX == 0 && deltaY == 0) return true;
        _host.RaiseWheel(new WheelInput(point, deltaX, deltaY, true));
        return true;
    }

    /// <summary>推进一次 fling；返回是否仍需继续调度。</summary>
    public bool StepFling(float deltaSeconds = 1f / 60f)
    {
        if (!HasFling) return false;
        var point = _lastPoint;
        var deltaX = -_velocityX * deltaSeconds;
        var deltaY = -_velocityY * deltaSeconds;
        if (deltaX != 0 || deltaY != 0)
            _host.RaiseWheel(new WheelInput(point, deltaX, deltaY, true, true,
                PointerDeviceKind.Touch, _flingPointerId));
        var damping = MathF.Exp(-10f * deltaSeconds);
        _velocityX *= damping;
        _velocityY *= damping;
        return HasFling;
    }

    /// <summary>取消 active pointer 和 fling。</summary>
    public void Cancel()
    {
        if (_active && _pointerId >= 0)
            _host.RaisePointer(new PointerInput(_lastPoint, PointerAction.Cancel, _pointerId, _deviceKind,
                MouseButton.None, true));
        CancelPointer();
        CancelFling();
    }

    private Point _downPoint;

    private void BeginFling()
    {
        if (MathF.Abs(_velocityX) < FlingVelocityThreshold) _velocityX = 0;
        if (MathF.Abs(_velocityY) < FlingVelocityThreshold) _velocityY = 0;
        _flinging = true;
    }

    private void CancelPointer()
    {
        _active = false;
        _moved = false;
        _pointerId = -1;
        _velocityX = 0;
        _velocityY = 0;
    }

    private void CancelFling()
    {
        _flinging = false;
        _velocityX = 0;
        _velocityY = 0;
        _flingPointerId = 0;
    }

    private Point ToLogicalPoint(MotionEvent e, int index) =>
        _host.ToLogicalPoint(e.GetX(index), e.GetY(index));

    private static PointerDeviceKind ResolveDeviceKind(MotionEvent e, int index) =>
        e.GetToolType(index) switch
        {
            MotionEventToolType.Mouse => PointerDeviceKind.Mouse,
            MotionEventToolType.Stylus => PointerDeviceKind.Pen,
            _ => PointerDeviceKind.Touch
        };

    private static MouseButton ResolveButton(MotionEvent e)
    {
        var state = e.ButtonState;
        if ((state & MotionEventButtonState.Primary) != 0) return MouseButton.Left;
        if ((state & MotionEventButtonState.Tertiary) != 0) return MouseButton.Middle;
        if ((state & MotionEventButtonState.Secondary) != 0) return MouseButton.Right;
        return MouseButton.Left;
    }
}
