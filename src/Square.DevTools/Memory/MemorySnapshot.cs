using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Square.DevTools;

internal sealed record MemorySnapshot(
    int ProcessId,
    long SampledAtUnixMilliseconds,
    ProcessMemorySnapshot Process,
    ManagedMemorySnapshot Managed,
    CollectionSnapshot Collections);

internal sealed record ProcessMemorySnapshot(
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long VirtualMemoryBytes);

internal sealed record ManagedMemorySnapshot(
    long CurrentBytes,
    long ApproximateTotalAllocatedBytes,
    long HeapSizeAfterLastGcBytes,
    long FragmentedAfterLastGcBytes,
    long TotalCommittedBytes,
    long TotalAvailableMemoryBytes,
    long MemoryLoadBytes,
    long HighMemoryLoadThresholdBytes,
    long PendingFinalizers,
    long PinnedObjects,
    double PauseTimePercentage);

internal sealed record CollectionSnapshot(int Gen0, int Gen1, int Gen2);

internal static class MemorySnapshotCollector
{
    public static MemorySnapshot Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var gc = GC.GetGCMemoryInfo();
        return new MemorySnapshot(
            Environment.ProcessId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            new ProcessMemorySnapshot(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                process.VirtualMemorySize64),
            new ManagedMemorySnapshot(
                GC.GetTotalMemory(forceFullCollection: false),
                GC.GetTotalAllocatedBytes(precise: false),
                gc.HeapSizeBytes,
                gc.FragmentedBytes,
                gc.TotalCommittedBytes,
                gc.TotalAvailableMemoryBytes,
                gc.MemoryLoadBytes,
                gc.HighMemoryLoadThresholdBytes,
                gc.FinalizationPendingCount,
                gc.PinnedObjectsCount,
                gc.PauseTimePercentage),
            new CollectionSnapshot(
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2)));
    }
}

internal static class MemorySnapshotJson
{
    public static string Serialize(MemorySnapshot snapshot)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');
        AppendLong(builder, "processId", snapshot.ProcessId); builder.Append(',');
        AppendLong(builder, "sampledAtUnixMilliseconds", snapshot.SampledAtUnixMilliseconds); builder.Append(',');
        builder.Append("\"process\":{");
        AppendLong(builder, "workingSetBytes", snapshot.Process.WorkingSetBytes); builder.Append(',');
        AppendLong(builder, "privateMemoryBytes", snapshot.Process.PrivateMemoryBytes); builder.Append(',');
        AppendLong(builder, "virtualMemoryBytes", snapshot.Process.VirtualMemoryBytes);
        builder.Append("},\"managed\":{");
        AppendLong(builder, "currentBytes", snapshot.Managed.CurrentBytes); builder.Append(',');
        AppendLong(builder, "approximateTotalAllocatedBytes", snapshot.Managed.ApproximateTotalAllocatedBytes); builder.Append(',');
        AppendLong(builder, "heapSizeAfterLastGcBytes", snapshot.Managed.HeapSizeAfterLastGcBytes); builder.Append(',');
        AppendLong(builder, "fragmentedAfterLastGcBytes", snapshot.Managed.FragmentedAfterLastGcBytes); builder.Append(',');
        AppendLong(builder, "totalCommittedBytes", snapshot.Managed.TotalCommittedBytes); builder.Append(',');
        AppendLong(builder, "totalAvailableMemoryBytes", snapshot.Managed.TotalAvailableMemoryBytes); builder.Append(',');
        AppendLong(builder, "memoryLoadBytes", snapshot.Managed.MemoryLoadBytes); builder.Append(',');
        AppendLong(builder, "highMemoryLoadThresholdBytes", snapshot.Managed.HighMemoryLoadThresholdBytes); builder.Append(',');
        AppendLong(builder, "pendingFinalizers", snapshot.Managed.PendingFinalizers); builder.Append(',');
        AppendLong(builder, "pinnedObjects", snapshot.Managed.PinnedObjects); builder.Append(',');
        AppendDouble(builder, "pauseTimePercentage", snapshot.Managed.PauseTimePercentage);
        builder.Append("},\"collections\":{");
        AppendLong(builder, "gen0", snapshot.Collections.Gen0); builder.Append(',');
        AppendLong(builder, "gen1", snapshot.Collections.Gen1); builder.Append(',');
        AppendLong(builder, "gen2", snapshot.Collections.Gen2);
        builder.Append("}}");
        return builder.ToString();
    }

    private static void AppendLong(StringBuilder builder, string name, long value)
    {
        builder.Append('"').Append(name).Append("\":")
            .Append(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendDouble(StringBuilder builder, string name, double value)
    {
        builder.Append('"').Append(name).Append("\":")
            .Append(value.ToString("R", CultureInfo.InvariantCulture));
    }
}
