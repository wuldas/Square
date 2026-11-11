using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CanvasBenchmark.Observer;

internal sealed record DesktopFrameTelemetry(long LastPresentQpc, long LastMouseQpc, uint AccumulatedFrames,
    bool PointerOnly, bool ProtectedContentMaskedOut, bool RectsCoalesced,
    double AcquiredAtMs, double ReceivedAtMs, double ReadbackLatencyMs, double NativeProcessingMs,
    uint SourceWidth = 0, uint SourceHeight = 0, uint SourceFormat = 0, uint RowPitch = 0,
    uint RoiLeft = 0, uint RoiTop = 0, uint NativeFirstCellBgra = 0, uint NativeBufferHash = 0);
internal sealed record AcquisitionRecord(double AtMs, double CallMs, string Status, string HResult, DesktopFrameTelemetry? Frame);

[StructLayout(LayoutKind.Sequential)]
internal struct DuplicationInfo
{
    internal long QpcFrequency;
    internal int Left, Top, Right, Bottom;
    internal uint RefreshNumerator, RefreshDenominator, Rotation, OutputIndex;
    internal long AdapterLuid;
}
[StructLayout(LayoutKind.Sequential)]
internal struct DuplicationFrame
{
    internal long LastPresentQpc, LastMouseQpc, AcquiredQpc, CompletedQpc;
    internal uint AccumulatedFrames, PointerOnly, ProtectedContentMaskedOut, RectsCoalesced;
    internal uint SourceWidth, SourceHeight, SourceFormat, RowPitch, RoiLeft, RoiTop, NativeFirstCellBgra, NativeBufferHash;
}

internal sealed class DuplicationCapture : IDisposable
{
    private nint _handle;
    private readonly long _originQpc;
    private readonly Stopwatch _clock;
    private readonly byte[] _pixels = new byte[96 * 8 * 4];
    internal DuplicationInfo Info { get; }
    internal double InitializeMs { get; }
    internal List<AcquisitionRecord> Acquisitions { get; } = [];
    internal ReadOnlySpan<byte> Pixels => _pixels;

    internal DuplicationCapture(int x, int y, long originQpc, Stopwatch clock)
    {
        _originQpc = originQpc; _clock = clock;
        var begin = clock.Elapsed.TotalMilliseconds;
        var hr = Create(x, y, out _handle, out var info);
        InitializeMs = clock.Elapsed.TotalMilliseconds - begin;
        if (hr < 0) throw new COMException($"Desktop Duplication Create failed 0x{hr:X8}; no fallback.", hr);
        Info = info;
        if (info.QpcFrequency != Stopwatch.Frequency)
        { Dispose(); throw new InvalidOperationException("Native QPC and Stopwatch frequencies differ."); }
    }

    internal DesktopFrameTelemetry? Next(int x, int y)
    {
        var begin = _clock.Elapsed.TotalMilliseconds;
        var hr = NextNative(_handle, x, y, 8, _pixels, (uint)_pixels.Length, out var frame);
        var received = _clock.Elapsed.TotalMilliseconds;
        var telemetry = hr == 0 ? ConvertFrame(frame, _originQpc, Info.QpcFrequency, received) : null;
        Acquisitions.Add(new(received, received - begin, hr switch { 0 => telemetry!.PointerOnly ? "pointerOnly" : "desktop", 1 => "timeout", _ => "failed" },
            $"0x{hr:X8}", telemetry));
        if (hr < 0) throw new COMException($"Desktop Duplication Next failed 0x{hr:X8}; no fallback (ACCESS_LOST is fatal).", hr);
        if (hr != 0 && hr != 1) throw new InvalidDataException($"Unexpected Desktop Duplication status {hr}.");
        return telemetry;
    }

    internal static DesktopFrameTelemetry ConvertFrame(DuplicationFrame frame, long originQpc, long frequency, double receivedMs)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        double Relative(long qpc) => (qpc - originQpc) * 1000d / frequency;
        return new(frame.LastPresentQpc, frame.LastMouseQpc, frame.AccumulatedFrames, frame.PointerOnly != 0,
            frame.ProtectedContentMaskedOut != 0, frame.RectsCoalesced != 0,
            Relative(frame.AcquiredQpc), receivedMs,
            frame.LastPresentQpc != 0 ? receivedMs - Relative(frame.LastPresentQpc) : 0,
            (frame.CompletedQpc - frame.AcquiredQpc) * 1000d / frequency,
            frame.SourceWidth, frame.SourceHeight, frame.SourceFormat, frame.RowPitch,
            frame.RoiLeft, frame.RoiTop, frame.NativeFirstCellBgra, frame.NativeBufferHash);
    }

    internal double PresentTimeMs(DesktopFrameTelemetry frame) => (frame.LastPresentQpc - _originQpc) * 1000d / Info.QpcFrequency;

    internal object Report() => new
    {
        backend = "desktop-duplication", eventDriven = true, acquireTimeoutMs = 8,
        InitializeMs, originQpc = _originQpc, stopwatchFrequency = Stopwatch.Frequency, Info.QpcFrequency,
        output = new { Info.Left, Info.Top, Info.Right, Info.Bottom, Info.Rotation, Info.OutputIndex, Info.AdapterLuid,
            Info.RefreshNumerator, Info.RefreshDenominator },
        acquiredCalls = Acquisitions.Count,
        timeouts = Acquisitions.Count(a => a.Status == "timeout"),
        pointerOnlyEvents = Acquisitions.Count(a => a.Status == "pointerOnly"),
        acquisitionCallMs = Distribution.Of(Acquisitions.Select(a => a.CallMs)),
        acquisitions = Acquisitions
    };

    public void Dispose() { if (_handle != 0) { DisposeNative(_handle); _handle = 0; } }
    [DllImport("CanvasObserverCapture.dll", EntryPoint = "ObserverCaptureCreate", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Create(int x, int y, out nint handle, out DuplicationInfo info);
    [DllImport("CanvasObserverCapture.dll", EntryPoint = "ObserverCaptureNext", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NextNative(nint handle, int x, int y, uint timeoutMs, [Out] byte[] pixels, uint size, out DuplicationFrame frame);
    [DllImport("CanvasObserverCapture.dll", EntryPoint = "ObserverCaptureCpuCopyRows", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CpuCopyRows([In] byte[] source, uint sourceSize, uint rowPitch,
        [Out] byte[] destination, uint destinationSize, out uint nativeFrameSize);
    [DllImport("CanvasObserverCapture.dll", EntryPoint = "ObserverCaptureDispose", CallingConvention = CallingConvention.Cdecl)]
    private static extern void DisposeNative(nint handle);
}
