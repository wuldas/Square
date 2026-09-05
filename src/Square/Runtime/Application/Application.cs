namespace Square.Runtime;

/// <summary>应用基类，管理调度器与运行生命周期。</summary>
public abstract class Application
{
    [ThreadStatic]
    private static Application? _current;
    /// <summary>当前线程的应用实例；未启动时抛出异常。</summary>
    public static Application Current => _current ?? throw new InvalidOperationException("Application not started");

    /// <summary>应用是否已启动。</summary>
    public static bool IsStarted => _current != null;

    /// <summary>关联的调度器。</summary>
    public Dispatcher Dispatcher { get; } = new();
    /// <summary>应用是否正在运行。</summary>
    public bool IsRunning { get; private set; }
    /// <summary>应用退出时触发。</summary>
    public event Action? Exited;

    private bool _externalRunEntered;
    /// <summary>构造应用并将当前线程设为当前实例。</summary>
    protected Application()
    {
        _current = this;
    }

    /// <summary>启动应用，执行核心循环后触发退出。</summary>
    public void Run()
    {
        if (IsRunning) throw new InvalidOperationException("Application already running");
        IsRunning = true;
        try
        {
            OnStart();
            RunCore();
        }
        finally
        {
            try
            {
                OnExit();
            }
            finally
            {
                IsRunning = false;
                Exited?.Invoke();
            }
        }
    }

    /// <summary>为外部事件循环进入应用运行状态。</summary>
    internal void EnterExternalRun()
    {
        if (IsRunning) throw new InvalidOperationException("Application already running");
        IsRunning = true;
        _externalRunEntered = true;
        try
        {
            OnStart();
        }
        catch
        {
            ExitExternalRun();
            throw;
        }
    }

    /// <summary>退出由外部事件循环进入的应用运行状态。</summary>
    internal void ExitExternalRun()
    {
        if (!_externalRunEntered) return;
        _externalRunEntered = false;
        try
        {
            OnExit();
        }
        finally
        {
            IsRunning = false;
            Exited?.Invoke();
        }
    }

    /// <summary>请求关闭应用。</summary>
    public void Shutdown()
    {
        IsRunning = false;
    }

    /// <summary>核心运行循环（由派生类实现）。</summary>
    protected abstract void RunCore();

    /// <summary>启动时回调。</summary>
    protected virtual void OnStart() { }
    /// <summary>退出时回调。</summary>
    protected virtual void OnExit() { }
}