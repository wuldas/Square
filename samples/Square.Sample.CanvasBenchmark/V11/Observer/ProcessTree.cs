using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CanvasBenchmark.Observer;

internal sealed record Unavailable(double AtMs, int? Pid, string Operation, int Win32Error);
internal sealed record ProcessReading(int Pid, string Identity, int ParentPid, string Name,
    double CpuMs, ulong? PrivateBytes, ulong? WorkingSetBytes, bool Exited);
internal sealed record TreeSample(double AtMs, double SamplingMs, double? GapMs, double CpuLowerBoundMs,
    double? CpuPercentOfOneCore, ulong PrivateBytes, ulong WorkingSetBytes, bool MemoryComplete,
    double ObserverCpuMs, List<ProcessReading> Processes);

internal sealed class ProcessIdentity(int pid, int parentPid, string name, long creation, nint handle, double firstSeenMs)
{
    internal nint Handle { get; } = handle;
    public int Pid { get; } = pid;
    public int ParentPid { get; } = parentPid;
    public string Name { get; } = name;
    public long CreationFileTime { get; } = creation;
    public string Identity => $"{Pid}:{CreationFileTime}";
    public DateTime CreationUtc => DateTime.FromFileTimeUtc(CreationFileTime);
    public double FirstSeenMs { get; } = firstSeenMs;
    public double LastSeenMs { get; internal set; } = firstSeenMs;
    public double? ExitObservedMs { get; internal set; }
    public long? ExitFileTime { get; internal set; }
    public double CpuMs { get; internal set; }
    public bool CleanupTerminated { get; internal set; }
}

internal sealed class ProcessTreeSampler(int rootPid, Stopwatch clock, Process observer, double observerCpuBaseMs) : IDisposable
{
    internal List<ProcessIdentity> Identities { get; } = [];
    internal List<TreeSample> Samples { get; } = [];
    internal List<Unavailable> RecordUnavailable { get; } = [];
    private readonly HashSet<string> _identities = [];

    private Dictionary<int, Native.ProcessEntry> Snapshot(double atMs)
    {
        var result = new Dictionary<int, Native.ProcessEntry>();
        var snapshot = Native.CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new nint(-1))
        { RecordUnavailable.Add(new(atMs, null, "CreateToolhelp32Snapshot", Marshal.GetLastWin32Error())); return result; }
        try
        {
            var entry = new Native.ProcessEntry { Size = (uint)Marshal.SizeOf<Native.ProcessEntry>(), ExeFile = "" };
            if (!Native.Process32First(snapshot, ref entry))
            { RecordUnavailable.Add(new(atMs, null, "Process32First", Marshal.GetLastWin32Error())); return result; }
            do { result[(int)entry.Pid] = entry; } while (Native.Process32Next(snapshot, ref entry));
            var error = Marshal.GetLastWin32Error();
            if (error != 18) RecordUnavailable.Add(new(atMs, null, "Process32Next", error));
            return result;
        }
        finally { Native.CloseHandle(snapshot); }
    }

    private ProcessIdentity? Discover(Native.ProcessEntry entry, double atMs)
    {
        // Retained handles identify a process even after its PID has been reused.
        var handle = Native.OpenProcess(0x1000 | 0x0010, false, entry.Pid);
        if (handle == 0) handle = Native.OpenProcess(0x1000, false, entry.Pid);
        if (handle == 0)
        { RecordUnavailable.Add(new(atMs, (int)entry.Pid, "OpenProcess", Marshal.GetLastWin32Error())); return null; }
        if (!Native.GetProcessTimes(handle, out var creation, out _, out var kernel, out var user))
        {
            RecordUnavailable.Add(new(atMs, (int)entry.Pid, "GetProcessTimes(discover)", Marshal.GetLastWin32Error()));
            Native.CloseHandle(handle); return null;
        }
        var key = $"{entry.Pid}:{creation}";
        if (!_identities.Add(key)) { Native.CloseHandle(handle); return null; }
        var identity = new ProcessIdentity((int)entry.Pid, (int)entry.ParentPid, entry.ExeFile, creation, handle, atMs)
            { CpuMs = (kernel + user) / 10000d };
        Identities.Add(identity);
        return identity;
    }

    internal void Sample()
    {
        var begin = clock.Elapsed.TotalMilliseconds;
        var snapshot = Snapshot(begin);
        var current = new Dictionary<int, ProcessIdentity>();
        foreach (var known in Identities)
        {
            if (!Native.GetProcessTimes(known.Handle, out _, out var exit, out var kernel, out var user))
            { RecordUnavailable.Add(new(begin, known.Pid, "GetProcessTimes", Marshal.GetLastWin32Error())); continue; }
            known.CpuMs = (kernel + user) / 10000d;
            if (exit != 0)
            { known.ExitFileTime = exit; known.ExitObservedMs ??= begin; }
            if (!current.TryGetValue(known.Pid, out var prior) || known.CreationFileTime > prior.CreationFileTime)
                current[known.Pid] = known;
        }
        if (!Identities.Any(p => p.Pid == rootPid) && snapshot.TryGetValue(rootPid, out var root))
        {
            var identity = Discover(root, begin);
            if (identity is not null) current[rootPid] = identity;
        }
        // Root and retained owned identities seed ancestry, including an exited parent whose
        // surviving child was born within that parent's actual lifetime. PID reuse is rejected.
        bool added;
        do
        {
            added = false;
            foreach (var entry in snapshot.Values)
            {
                if (current.ContainsKey((int)entry.Pid) || entry.Pid == rootPid ||
                    !current.TryGetValue((int)entry.ParentPid, out var parent)) continue;
                var child = Discover(entry, begin);
                if (child is null) continue;
                if (child.CreationFileTime < parent.CreationFileTime ||
                    (parent.ExitFileTime is { } parentExit && child.CreationFileTime > parentExit))
                {
                    RecordUnavailable.Add(new(begin, child.Pid, "ancestryCreationMismatch", 0));
                    Identities.Remove(child); _identities.Remove(child.Identity); Native.CloseHandle(child.Handle);
                    continue;
                }
                current[child.Pid] = child; added = true;
            }
        } while (added);

        var readings = new List<ProcessReading>();
        ulong privateBytes = 0, workingSet = 0;
        var memoryComplete = true;
        foreach (var identity in Identities)
        {
            if (identity.ExitObservedMs is not null) continue;
            identity.LastSeenMs = begin;
            if (Native.GetProcessTimes(identity.Handle, out _, out var exit, out var kernel, out var user))
            {
                identity.CpuMs = (kernel + user) / 10000d;
                if (exit != 0) { identity.ExitFileTime = exit; identity.ExitObservedMs = begin; }
            }
            else RecordUnavailable.Add(new(begin, identity.Pid, "GetProcessTimes(sample)", Marshal.GetLastWin32Error()));
            ulong? processPrivate = null, processWorking = null;
            if (identity.ExitObservedMs is null)
            {
                var counters = new Native.MemoryCounters { Size = (uint)Marshal.SizeOf<Native.MemoryCounters>() };
                if (Native.K32GetProcessMemoryInfo(identity.Handle, ref counters, counters.Size))
                {
                    processPrivate = counters.PrivateUsage; processWorking = counters.WorkingSetSize;
                    privateBytes += processPrivate.Value; workingSet += processWorking.Value;
                }
                else
                { memoryComplete = false; RecordUnavailable.Add(new(begin, identity.Pid, "K32GetProcessMemoryInfo", Marshal.GetLastWin32Error())); }
            }
            readings.Add(new(identity.Pid, identity.Identity, identity.ParentPid, identity.Name, identity.CpuMs,
                processPrivate, processWorking, identity.ExitObservedMs is not null));
        }
        observer.Refresh();
        var observerCpuMs = observer.TotalProcessorTime.TotalMilliseconds - observerCpuBaseMs;
        var atMs = clock.Elapsed.TotalMilliseconds;
        var cpu = Identities.Sum(p => p.CpuMs);
        var gap = Samples.Count > 0 ? atMs - Samples[^1].AtMs : (double?)null;
        var percent = gap > 0 ? (cpu - Samples[^1].CpuLowerBoundMs) / gap * 100 : null;
        Samples.Add(new(atMs, atMs - begin, gap, cpu, percent, privateBytes, workingSet, memoryComplete, observerCpuMs, readings));
    }

    internal object Result(double? measurementStart, double? measurementEnd)
    {
        var start = measurementStart is { } s ? Samples.LastOrDefault(x => x.AtMs <= s) : null;
        var end = measurementEnd is { } e ? Samples.FirstOrDefault(x => x.AtMs >= e) : null;
        var duration = end is not null && start is not null ? end.AtMs - start.AtMs : (double?)null;
        return new
        {
            targetSampleIntervalMs = 100,
            samplingGapMs = Distribution.Of(Samples.Where(s => s.GapMs.HasValue).Select(s => s.GapMs!.Value)),
            samplingCostMs = Distribution.Of(Samples.Select(s => s.SamplingMs)),
            cpuLowerBoundMs = Identities.Sum(p => p.CpuMs),
            peakSampledPrivateBytes = Samples.Select(s => s.PrivateBytes).DefaultIfEmpty().Max(),
            peakSampledWorkingSetBytes = Samples.Select(s => s.WorkingSetBytes).DefaultIfEmpty().Max(),
            cpuIsLowerBound = true,
            memoryNote = "PrivateBytes are summed. Working sets may double-count shared pages. Peaks between samples are unobserved.",
            coverageNote = "Descendants born and exited between Toolhelp snapshots, unavailable processes, and descendants reparented before discovery may be absent; CPU is a lower bound. Cumulative totals retain final CPU of discovered exited processes.",
            measurementCpu = new
            {
                startSampleMs = start?.AtMs, endSampleMs = end?.AtMs, sampledDurationMs = duration,
                cpuLowerBoundMs = end is not null && start is not null ? end.CpuLowerBoundMs - start.CpuLowerBoundMs : (double?)null,
                percentOfOneLogicalCore = duration > 0 ? (end!.CpuLowerBoundMs - start!.CpuLowerBoundMs) / duration * 100 : null,
                boundaryNote = "Uses process samples bracketing marker boundaries, with up to one actual process sampling interval of extra CPU on each side. Not exact frame CPU."
            },
            identities = Identities, RecordUnavailable, samples = Samples
        };
    }

    internal object ObserverMeasurement(double? measurementStart, double? measurementEnd)
    {
        var start = measurementStart is { } s ? Samples.LastOrDefault(x => x.AtMs <= s) : null;
        var end = measurementEnd is { } e ? Samples.FirstOrDefault(x => x.AtMs >= e) : null;
        return new { startSampleMs = start?.AtMs, endSampleMs = end?.AtMs,
            cpuMs = end is not null && start is not null ? end.ObserverCpuMs - start.ObserverCpuMs : (double?)null };
    }

    internal int LiveCount => Identities.Count(p => p.ExitObservedMs is null);

    internal void TerminateOwnedDescendants()
    {
        foreach (var identity in Identities.Where(p => p.Pid != rootPid && p.ExitObservedMs is null))
        {
            var handle = Native.OpenProcess(0x1000 | 0x0001, false, (uint)identity.Pid);
            if (handle == 0)
            { RecordUnavailable.Add(new(clock.Elapsed.TotalMilliseconds, identity.Pid, "OpenProcess(cleanup)", Marshal.GetLastWin32Error())); continue; }
            try
            {
                if (!Native.GetProcessTimes(handle, out var creation, out var exit, out _, out _) ||
                    creation != identity.CreationFileTime || exit != 0) continue;
                if (Native.TerminateProcess(handle, 1)) identity.CleanupTerminated = true;
                else RecordUnavailable.Add(new(clock.Elapsed.TotalMilliseconds, identity.Pid, "TerminateProcess(cleanup)", Marshal.GetLastWin32Error()));
            }
            finally { Native.CloseHandle(handle); }
        }
    }

    public void Dispose() { foreach (var identity in Identities) Native.CloseHandle(identity.Handle); }
}
