namespace Square.Backends.Vulkan;

/// <summary>Observes queued rendering and fence completion without conflating either with display scanout.</summary>
public interface IVulkanFrameDiagnostics
{
    /// <summary>Polls rendering fences without waiting and returns cumulative frame counts.</summary>
    VulkanFrameStatistics GetFrameStatistics();

    /// <summary>Explicitly drains submitted work; callers should keep this outside performance intervals.</summary>
    void WaitForSubmittedFrames();
}

/// <summary>Cumulative submission and observed GPU rendering completion counts.</summary>
/// <param name="SubmittedFrames">Frames successfully submitted to the graphics queue.</param>
/// <param name="CompletedFrames">Submitted frames whose rendering fences have been observed signalled.</param>
/// <param name="PendingFrames">Submitted frames not yet observed complete.</param>
/// <param name="FramesInFlight">Maximum number of independently owned frame-resource slots.</param>
public sealed record VulkanFrameStatistics(ulong SubmittedFrames, ulong CompletedFrames, int PendingFrames, int FramesInFlight);
