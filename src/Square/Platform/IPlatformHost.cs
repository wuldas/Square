using Square.Graphics;
using Square.Hosting;

namespace Square.Platform;

/// <summary>平台宿主接口，封装原生窗口和事件循环。</summary>
public interface IPlatformHost : IDisposable
{
    /// <summary>客户区逻辑尺寸。</summary>
    Size ClientSize { get; }
    /// <summary>DPI 缩放比例。</summary>
    float DpiScale { get; }
    /// <summary>是否正在运行。</summary>
    bool IsRunning { get; }
    /// <summary>窗口状态。</summary>
    AppWindowState State => AppWindowState.Normal;
    /// <summary>窗口标题。</summary>
    string Title { get; set; }
    /// <summary>鼠标光标样式。</summary>
    CursorKind Cursor { get; set; }
    /// <summary>当前修饰键状态。</summary>
    KeyModifiers Modifiers { get; }

    /// <summary>尺寸变化事件。</summary>
    event Action<Size>? SizeChanged;
    /// <summary>鼠标事件。</summary>
    event Action<Point, MouseAction, MouseButton>? MouseEvent;
    /// <summary>滚轮事件。</summary>
    event Action<WheelInput>? WheelEvent;
    /// <summary>键盘事件。</summary>
    event Action<int, KeyAction>? KeyEvent;
    /// <summary>文本输入事件。</summary>
    event Action<string>? TextInput;
    /// <summary>帧滴答事件。</summary>
    event Action? Tick;

    /// <summary>平台或原生后端请求重新提交完整画面。</summary>
    event Action? RenderRequested
    {
        add { }
        remove { }
    }

    /// <summary>窗口状态变化事件。</summary>
    event Action<AppWindowState>? StateChanged
    {
        add { }
        remove { }
    }

    /// <summary>窗口关闭事件。</summary>
    event Action? Closed
    {
        add { }
        remove { }
    }

    /// <summary>显示窗口。</summary>
    void Show();

    /// <summary>首帧渲染后显示窗口。</summary>
    void ShowAfterFirstFrame()
    {
    }

    /// <summary>关闭窗口。</summary>
    void Close();

    /// <summary>最小化。</summary>
    void Minimize()
    {
    }

    /// <summary>最大化。</summary>
    void Maximize()
    {
    }

    /// <summary>还原。</summary>
    void Restore()
    {
    }

    /// <summary>开始拖动窗口。</summary>
    void BeginMove()
    {
    }

    /// <summary>创建渲染上下文。</summary>
    IRenderContext CreateRenderContext();
    /// <summary>处理平台事件队列。</summary>
    void PumpEvents();
    /// <summary>设置输入法候选区矩形。</summary>
    void SetTextInputRect(Rect rect);
    /// <summary>获取剪贴板文本。</summary>
    string GetClipboardText();
    /// <summary>设置剪贴板文本。</summary>
    void SetClipboardText(string text);
}

internal interface IPlatformNativeWindow
{
    IntPtr Handle { get; }
}

/// <summary>鼠标动作。</summary>
public enum MouseAction
{
    /// <summary>按下。</summary>
    Down,
    /// <summary>释放。</summary>
    Up,
    /// <summary>移动。</summary>
    Move,
    /// <summary>滚轮。</summary>
    Wheel
}

/// <summary>鼠标按键。</summary>
public enum MouseButton
{
    /// <summary>无按键（移动事件）。</summary>
    None,
    /// <summary>主按键。</summary>
    Left,
    /// <summary>中键。</summary>
    Middle,
    /// <summary>次按键。</summary>
    Right
}

/// <summary>键盘动作。</summary>
public enum KeyAction
{
    /// <summary>按下。</summary>
    Down,
    /// <summary>释放。</summary>
    Up
}

/// <summary>鼠标光标样式。</summary>
public enum CursorKind
{
    /// <summary>箭头。</summary>
    Arrow,
    /// <summary>文本。</summary>
    Text,
    /// <summary>手型。</summary>
    Hand,
    /// <summary>水平调整尺寸。</summary>
    ResizeHorizontal,
    /// <summary>垂直调整尺寸。</summary>
    ResizeVertical
}

/// <summary>窗口状态。</summary>
public enum AppWindowState
{
    /// <summary>正常。</summary>
    Normal,
    /// <summary>最小化。</summary>
    Minimized,
    /// <summary>最大化。</summary>
    Maximized
}

/// <summary>修饰键标志。</summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>无修饰键。</summary>
    None = 0,
    /// <summary>Shift。</summary>
    Shift = 1,
    /// <summary>Ctrl。</summary>
    Control = 2,
    /// <summary>Alt。</summary>
    Alt = 4
}

/// <summary>平台工厂接口。</summary>
public interface IPlatformFactory
{
    /// <summary>平台名称。</summary>
    string Name { get; }
    /// <summary>创建平台宿主。</summary>
    IPlatformHost CreateHost(PlatformHostCreateInfo info);
}

/// <summary>平台截图提供器接口。</summary>
public interface IPlatformScreenshotProvider
{
    /// <summary>尝试按进程 ID 截取窗口位图。</summary>
    bool TryCaptureByProcessId(int processId, out Bitmap? bitmap);
}

/// <summary>软件渲染表面种类。</summary>
public enum SoftwareRenderSurfaceKind
{
    /// <summary>自动选择。</summary>
    Auto,
    /// <summary>使用位图表面。</summary>
    Bitmap
}

/// <summary>平台宿主创建参数。</summary>
public sealed class PlatformHostCreateInfo
{
    /// <summary>窗口标题。</summary>
    public required string Title { get; set; }
    /// <summary>初始宽度。</summary>
    public int Width { get; set; } = 800;
    /// <summary>初始高度。</summary>
    public int Height { get; set; } = 600;
    /// <summary>渲染后端名称。</summary>
    public string RenderBackend { get; set; } = "Software";
    /// <summary>软件渲染表面种类。</summary>
    public SoftwareRenderSurfaceKind SoftwareSurface { get; set; } = SoftwareRenderSurfaceKind.Auto;
    /// <summary>标题栏样式。</summary>
    public TitleStyle TitleStyle { get; set; } = TitleStyle.System;
    /// <summary>边框样式。</summary>
    public BorderStyle BorderStyle { get; set; } = BorderStyle.Resizable;
    /// <summary>所有者窗口句柄。</summary>
    public IntPtr OwnerHandle { get; set; }
    /// <summary>是否为模态窗口。</summary>
    public bool IsModal { get; set; }
}
