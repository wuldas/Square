using System.Text.Json;
using Xunit;

namespace Square.DevTools.Tests;

public sealed class MemorySnapshotTests
{
    [Theory]
    [InlineData(long.MinValue, 0L)]
    [InlineData(-1L, 0L)]
    [InlineData(0L, 0L)]
    [InlineData(12345L, 12345L)]
    public void SerializedCurrentMemoryIsNonnegativeAndPreservesValidSamples(long sampledBytes, long expectedBytes)
    {
        var managed = new ManagedMemorySnapshot(
            CurrentBytes: sampledBytes,
            ApproximateTotalAllocatedBytes: 54321,
            HeapSizeAfterLastGcBytes: 4096,
            FragmentedAfterLastGcBytes: 0,
            TotalCommittedBytes: 8192,
            TotalAvailableMemoryBytes: 65536,
            MemoryLoadBytes: 16384,
            HighMemoryLoadThresholdBytes: 49152,
            PendingFinalizers: 0,
            PinnedObjects: 0,
            PauseTimePercentage: 0);
        var snapshot = new MemorySnapshot(1, 123, new ProcessMemorySnapshot(1, 2, 3), managed,
            new CollectionSnapshot(0, 0, 0));

        using var json = JsonDocument.Parse(MemorySnapshotJson.Serialize(snapshot));

        var values = json.RootElement.GetProperty("managed");
        Assert.Equal(expectedBytes, managed.CurrentBytes);
        Assert.Equal(expectedBytes, values.GetProperty("currentBytes").GetInt64());
        Assert.Equal(54321, values.GetProperty("approximateTotalAllocatedBytes").GetInt64());
        Assert.Equal(4096, values.GetProperty("heapSizeAfterLastGcBytes").GetInt64());
    }
}
