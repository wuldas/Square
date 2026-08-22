namespace Square.Runtime;

/// <summary>调度器优先级。</summary>
public enum DispatcherPriority
{
    /// <summary>空闲优先级。</summary>
    Idle = 0,
    /// <summary>普通优先级。</summary>
    Normal = 1,
    /// <summary>高优先级。</summary>
    High = 2
}

/// <summary>线程关联的工作项调度器，按入队顺序在所属线程上执行委托。</summary>
public sealed class Dispatcher
{
    private readonly Queue<Action> _queue = new();
    private readonly object _lock = new();
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    /// <summary>判断当前线程是否为调度器所属线程。</summary>
    public bool CheckAccess() => Environment.CurrentManagedThreadId == _ownerThreadId;

    /// <summary>验证当前线程为所属线程，否则抛出异常。</summary>
    public void VerifyAccess()
    {
        if (!CheckAccess())
            throw new InvalidOperationException("The Dispatcher queue can only be drained by its owning thread.");
    }

    /// <summary>将委托入队，等待所属线程执行。</summary>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_lock) _queue.Enqueue(action);
    }

    /// <summary>异步执行委托；若已在所属线程则同步执行。</summary>
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Invoke(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    /// <summary>在所属线程上排空并执行队列中的全部工作项。</summary>
    public void Run()
    {
        VerifyAccess();
        RunPending();
    }

    /// <summary>排空队列；无窗口宿主须在自行串行化访问后调用。</summary>
    internal void RunPending()
    {
        while (true)
        {
            Action? action;
            lock (_lock)
            {
                if (_queue.Count == 0) break;
                action = _queue.Dequeue();
            }
            action?.Invoke();
        }
    }

    /// <summary>队列中是否存在待执行工作项。</summary>
    public bool HasWork
    {
        get { lock (_lock) return _queue.Count > 0; }
    }
}
