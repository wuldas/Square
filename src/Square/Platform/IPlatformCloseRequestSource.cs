using System.ComponentModel;

namespace Square.Platform;

/// <summary>可选的原生关闭请求源。未实现时窗口关闭不可取消。</summary>
public interface IPlatformCloseRequestSource
{
    /// <summary>原生关闭请求；将 <see cref="CancelEventArgs.Cancel"/> 设为 true 可阻止关闭。</summary>
    event EventHandler<CancelEventArgs>? Closing;
}
