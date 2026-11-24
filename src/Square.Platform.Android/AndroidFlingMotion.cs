namespace Square.Platform.Android;

/// <summary>按真实帧间隔积分的触摸 fling 速度，位移只取决于经过的时间。</summary>
internal struct AndroidFlingMotion
{
    private const float StopVelocity = 80f;
    private const float Damping = 10f;

    private float _velocityX;
    private float _velocityY;

    /// <summary>是否仍有超过停止阈值的速度。</summary>
    public readonly bool IsActive => MathF.Abs(_velocityX) >= StopVelocity || MathF.Abs(_velocityY) >= StopVelocity;

    /// <summary>以 DIP/秒开始 fling；低于停止阈值的轴立即停止。</summary>
    public void Begin(float velocityX, float velocityY)
    {
        _velocityX = MathF.Abs(velocityX) < StopVelocity ? 0 : velocityX;
        _velocityY = MathF.Abs(velocityY) < StopVelocity ? 0 : velocityY;
    }

    /// <summary>停止 fling。</summary>
    public void Cancel()
    {
        _velocityX = 0;
        _velocityY = 0;
    }

    /// <summary>推进一帧并返回滚动增量（DOM 方向，与手指运动方向相反）。</summary>
    public (float X, float Y) Step(float deltaSeconds)
    {
        if (!IsActive) return (0, 0);
        var decay = MathF.Exp(-Damping * deltaSeconds);
        // 指数衰减的精确积分：本帧滚动量等于本帧衰减掉的速度除以阻尼系数，方向与手指相反。
        var delta = (-_velocityX * (1 - decay) / Damping, -_velocityY * (1 - decay) / Damping);
        _velocityX *= decay;
        _velocityY *= decay;
        return delta;
    }
}
