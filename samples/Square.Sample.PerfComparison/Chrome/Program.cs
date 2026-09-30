using System.Diagnostics;
using System.Text.Json;

if (args is not ["--pids", var pidsArg, "--seconds", var durationArg] ||
    !int.TryParse(durationArg, out var seconds) || seconds <= 0)
    throw new ArgumentException("Usage: ChromeSampler --pids 123,456 --seconds 30");

var ids = pidsArg.Split(',').Select(int.Parse).Distinct().ToArray();
if (ids.Length == 0) throw new ArgumentException("At least one Chrome PID is required.");
var processes = new List<Process>(ids.Length);
foreach (var id in ids)
{
    try { processes.Add(Process.GetProcessById(id)); }
    catch (ArgumentException) { /* A short-lived renderer can exit after PID discovery. */ }
}
if (processes.Count == 0) throw new InvalidOperationException("No tracked Chrome processes remain.");
var firstCpu = new Dictionary<int, double>();
var latestCpu = new Dictionary<int, double>();
long privateTotal = 0, workingTotal = 0, privatePeak = 0, workingPeak = 0;
var samples = 0;

void Sample()
{
    long privateBytes = 0, workingBytes = 0;
    foreach (var process in processes)
    {
        try
        {
            process.Refresh();
            if (process.HasExited) continue;
            var cpu = process.TotalProcessorTime.TotalSeconds;
            firstCpu.TryAdd(process.Id, cpu);
            latestCpu[process.Id] = cpu;
            privateBytes += process.PrivateMemorySize64;
            workingBytes += process.WorkingSet64;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Chrome pid {process.Id}: {exception.GetType().Name}: {exception.Message}");
        }
    }
    privateTotal += privateBytes;
    workingTotal += workingBytes;
    privatePeak = Math.Max(privatePeak, privateBytes);
    workingPeak = Math.Max(workingPeak, workingBytes);
    samples++;
}

var clock = Stopwatch.StartNew();
Sample();
Console.WriteLine("READY");
Console.Out.Flush();
for (var second = 1; second <= seconds; second++)
{
    var remaining = TimeSpan.FromSeconds(second) - clock.Elapsed;
    if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
    Sample();
}
var elapsed = clock.Elapsed.TotalSeconds;
var cpuSeconds = latestCpu.Sum(pair => Math.Max(0, pair.Value - firstCpu[pair.Key]));
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
    processCount = processes.Count
};
Console.WriteLine(JsonSerializer.Serialize(result));
foreach (var process in processes) process.Dispose();
