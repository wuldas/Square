using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

if (args is not ["--pids", var pidsArg, "--seconds", var durationArg] ||
    !int.TryParse(durationArg, out var seconds) || seconds <= 0)
    throw new ArgumentException("Usage: ChromeSampler --pids 123,456 --seconds 30");

var ids = pidsArg.Split(',').Select(int.Parse).Distinct().ToArray();
if (ids.Length == 0) throw new ArgumentException("At least one Chrome PID is required.");

// Roots and descendants are tracked through retained Process handles: a terminated
// process keeps answering GetProcessTimes for its last handle holder, so CPU already
// spent by an exited child stays in the total, and identity is the handle itself —
// a later process that reuses the same PID is always a separate tracked entry.
var roots = new List<TrackedProcess>(ids.Length);
foreach (var id in ids)
{
    try { roots.Add(new TrackedProcess(Process.GetProcessById(id))); }
    catch (ArgumentException) { /* A short-lived renderer can exit after PID discovery. */ }
}
if (roots.Count == 0) throw new InvalidOperationException("No tracked Chrome processes remain.");
var entries = new List<TrackedProcess>(roots);

long privateTotal = 0, workingTotal = 0, privatePeak = 0, workingPeak = 0;
var samples = 0;
var incomplete = false;
var clock = Stopwatch.StartNew();
var measurementStartFileTime = DateTime.UtcNow.ToFileTimeUtc();

void Sample(bool isInitialSample)
{
    // Refresh descendant membership before reading metrics so a process spawned during
    // the interval is measured in the same tick it is first observed.
    var parents = TakeParentSnapshot();
    if (parents is null)
    {
        incomplete = true;
        Console.Error.WriteLine("Chrome: process snapshot failed; sample is incomplete.");
    }
    else
        DiscoverDescendants(parents);

    long privateBytes = 0, workingBytes = 0;
    foreach (var tracked in entries)
    {
        tracked.Exited = tracked.HasExited;

        // GetProcessTimes is read straight from the retained handle because it keeps
        // working after exit, unlike Process.TotalProcessorTime, which throws once the
        // runtime has observed the exit. Sum of kernel+user matches TotalProcessorTime.
        if (!tracked.Inaccessible)
        {
            if (!NativeMethods.GetProcessTimes(tracked.QueryHandle, out var creationTime, out _, out var kernelTime, out var userTime))
                MarkInaccessible(tracked, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            else
            {
                tracked.LatestCpuSeconds = (userTime + kernelTime) / 10_000_000.0;
                if (double.IsNaN(tracked.FirstCpuSeconds))
                    // Only children born inside this interval contribute lifetime CPU.
                    // A pre-existing member discovered late cannot charge startup CPU.
                    tracked.FirstCpuSeconds = !isInitialSample && creationTime >= measurementStartFileTime
                        ? 0 : tracked.LatestCpuSeconds;
            }
        }

        if (!tracked.Exited && !tracked.Inaccessible)
        {
            var memory = new NativeMethods.PROCESS_MEMORY_COUNTERS_EX
            {
                cb = (uint)Marshal.SizeOf<NativeMethods.PROCESS_MEMORY_COUNTERS_EX>()
            };
            if (NativeMethods.GetProcessMemoryInfo(tracked.QueryHandle, ref memory, memory.cb))
            {
                privateBytes += (long)memory.PrivateUsage;
                workingBytes += (long)memory.WorkingSetSize;
            }
            else
                MarkInaccessible(tracked, new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
    }
    privateTotal += privateBytes;
    workingTotal += workingBytes;
    privatePeak = Math.Max(privatePeak, privateBytes);
    workingPeak = Math.Max(workingPeak, workingBytes);
    samples++;
}

void DiscoverDescendants(Dictionary<int, (int ParentPid, string ExeName)> parents)
{
    // Dead roots stop adopting so a reused root PID can never pull in an unrelated tree.
    var liveRootPids = new HashSet<int>();
    foreach (var root in roots)
    {
        if (!root.HasExited) liveRootPids.Add(root.Pid);
    }
    if (liveRootPids.Count == 0) return;

    var untracked = 0;
    foreach (var (pid, info) in parents)
    {
        if (!liveRootPids.Contains(pid) && !ReachesRoot(pid, parents, liveRootPids)) continue;
        var alreadyTracked = false;
        foreach (var tracked in entries)
        {
            if (tracked.Pid != pid) continue;
            if (!tracked.HasExited) { alreadyTracked = true; break; }
        }
        if (alreadyTracked) continue;
        Process process;
        try { process = Process.GetProcessById(pid); }
        catch (ArgumentException)
        {
            // Exited between the snapshot and the handle open. Its CPU is unrecoverable:
            // short-lived children below one sample interval are a known blind spot.
            untracked++;
            incomplete = true;
            continue;
        }
        entries.Add(new TrackedProcess(process, info.ExeName));
    }
    if (untracked > 0)
        Console.Error.WriteLine($"Chrome: {untracked} descendant process(es) exited before they could be tracked.");
}

void MarkInaccessible(TrackedProcess tracked, Exception exception)
{
    if (tracked.Inaccessible) return;
    tracked.Inaccessible = true;
    incomplete = true;
    Console.Error.WriteLine($"Chrome {tracked.Name} pid {tracked.Pid}: {exception.GetType().Name}: {exception.Message}");
}

static bool ReachesRoot(int pid, Dictionary<int, (int ParentPid, string ExeName)> parents, HashSet<int> liveRootPids)
{
    HashSet<int> visited = new();
    var current = pid;
    while (current != 0 && visited.Add(current))
    {
        if (liveRootPids.Contains(current)) return true;
        if (!parents.TryGetValue(current, out var parent)) return false;
        current = parent.ParentPid;
    }
    return false;
}

static Dictionary<int, (int ParentPid, string ExeName)>? TakeParentSnapshot()
{
    for (var attempt = 0; attempt < 3; attempt++)
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            if (Marshal.GetLastWin32Error() == NativeMethods.ERROR_BAD_LENGTH) continue;
            return null;
        }
        var entry = new NativeMethods.PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<NativeMethods.PROCESSENTRY32W>() };
        if (!NativeMethods.Process32FirstW(snapshot, ref entry))
        {
            if (Marshal.GetLastWin32Error() == NativeMethods.ERROR_BAD_LENGTH) continue;
            return null;
        }
        var parents = new Dictionary<int, (int ParentPid, string ExeName)>();
        do
        {
            parents[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, entry.szExeFile);
        }
        while (NativeMethods.Process32NextW(snapshot, ref entry));
        if (Marshal.GetLastWin32Error() != 18) return null; // ERROR_NO_MORE_FILES
        return parents;
    }
    return null;
}

Sample(isInitialSample: true);
Console.WriteLine("READY");
Console.Out.Flush();
for (var second = 1; second <= seconds; second++)
{
    var remaining = TimeSpan.FromSeconds(second) - clock.Elapsed;
    if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
    Sample(isInitialSample: false);
}
var elapsed = clock.Elapsed.TotalSeconds;
var cpuSeconds = 0.0;
foreach (var tracked in entries)
    if (!tracked.Inaccessible)
        cpuSeconds += Math.Max(0, tracked.LatestCpuSeconds - tracked.FirstCpuSeconds);
var result = new
{
    seconds = elapsed,
    cpuSeconds,
    cpuOneCorePercent = cpuSeconds / elapsed * 100,
    privateAvgMiB = privateTotal / (double)samples / 1048576,
    privatePeakMiB = privatePeak / 1048576d,
    workingAvgMiB = workingTotal / (double)samples / 1048576,
    workingPeakMiB = workingPeak / 1048576d,
    samples,
    processCount = entries.Count,
    incomplete,
};
Console.WriteLine(JsonSerializer.Serialize(result));
foreach (var tracked in entries)
{
    tracked.QueryHandle.Dispose();
    tracked.Handle.Dispose();
}

internal sealed class TrackedProcess
{
    public TrackedProcess(Process handle, string? exeName = null)
    {
        Handle = handle;
        Pid = handle.Id;
        QueryHandle = NativeMethods.OpenProcess(0x1000, false, Pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (QueryHandle.IsInvalid)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if (string.IsNullOrEmpty(exeName))
        {
            try { exeName = handle.ProcessName; }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { exeName = null; }
        }
        if (string.IsNullOrEmpty(exeName)) exeName = "unknown";
        Name = exeName;
    }

    public Process Handle { get; }
    public SafeProcessHandle QueryHandle { get; }
    public bool HasExited
    {
        get
        {
            if (Exited) return true;
            if (!NativeMethods.GetProcessTimes(QueryHandle, out _, out var exitTime, out _, out _))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return Exited = exitTime != 0;
        }
    }
    public int Pid { get; }
    public string Name { get; }
    public double FirstCpuSeconds = double.NaN; // Baseline until the first successful sample.
    public double LatestCpuSeconds;
    public bool Exited; // Sticky; exited members keep their retained handle and final CPU.
    public bool Inaccessible;
}

internal static class NativeMethods
{
    internal const uint TH32CS_SNAPPROCESS = 0x00000002;
    internal const int ERROR_BAD_LENGTH = 24;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern bool Process32FirstW(SafeFileHandle snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern bool Process32NextW(SafeFileHandle snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creationTime, out long exitTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [DllImport("psapi.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern bool GetProcessMemoryInfo(SafeProcessHandle process, ref PROCESS_MEMORY_COUNTERS_EX counters, uint size);
}
