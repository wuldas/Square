namespace Square.Backends.Direct2D;

/// <summary>Direct2D 后端错误。</summary>
public sealed class Direct2DException : Exception
{
    /// <summary>使用错误消息构造。</summary>
    public Direct2DException(string message) : base(message)
    {
    }

    /// <summary>使用错误消息和内部异常构造。</summary>
    public Direct2DException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
