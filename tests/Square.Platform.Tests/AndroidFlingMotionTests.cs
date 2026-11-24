using Square.Platform.Android;
using Xunit;

namespace Square.Platform.Tests;

public sealed class AndroidFlingMotionTests
{
    [Theory]
    [InlineData(60f)]
    [InlineData(90f)]
    [InlineData(120f)]
    [InlineData(144f)]
    public void StepReachesTheSameDistanceAndDurationAtEveryRefreshRate(float framesPerSecond)
    {
        var motion = new AndroidFlingMotion();
        motion.Begin(0f, 6000f);
        var distance = 0f;
        var frames = 0;
        while (motion.IsActive)
        {
            distance -= motion.Step(1f / framesPerSecond).Y;
            frames++;
        }

        // 6000 DIP/s 衰减到 80 DIP/s 的停止阈值：总位移 (6000 - 80) / 10，时长 ln(75) / 10。
        Assert.InRange(distance, 592f, 593f);
        Assert.InRange(frames / framesPerSecond, 0.43f, 0.44f);
    }

    [Fact]
    public void StepDisplacementDependsOnElapsedTimeNotFrameCount()
    {
        var sixty = Travel(15, 1f / 60f);
        var hundredTwenty = Travel(30, 1f / 120f);
        var hundredFortyFour = Travel(36, 1f / 144f);

        Assert.Equal(sixty, hundredTwenty, 2);
        Assert.Equal(sixty, hundredFortyFour, 2);
    }

    [Fact]
    public void BeginBelowStopVelocityStopsImmediately()
    {
        var motion = new AndroidFlingMotion();
        motion.Begin(79f, -79f);

        Assert.False(motion.IsActive);
        Assert.Equal((0f, 0f), motion.Step(1f / 60f));
    }

    [Fact]
    public void BeginClearsOnlyAxesBelowStopVelocity()
    {
        var motion = new AndroidFlingMotion();
        motion.Begin(10f, -3000f);
        var delta = motion.Step(1f / 60f);

        Assert.Equal(0f, delta.X);
        Assert.True(delta.Y > 0f);
        Assert.True(motion.IsActive);
    }

    [Fact]
    public void CancelStopsAnActiveFling()
    {
        var motion = new AndroidFlingMotion();
        motion.Begin(0f, 6000f);
        motion.Cancel();

        Assert.False(motion.IsActive);
        Assert.Equal((0f, 0f), motion.Step(1f / 60f));
    }

    private static float Travel(int frames, float deltaSeconds)
    {
        var motion = new AndroidFlingMotion();
        motion.Begin(0f, 6000f);
        var distance = 0f;
        for (var frame = 0; frame < frames; frame++)
            distance -= motion.Step(deltaSeconds).Y;
        return distance;
    }
}
