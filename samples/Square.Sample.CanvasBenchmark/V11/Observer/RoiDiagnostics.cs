using System.Security.Cryptography;

namespace CanvasBenchmark.Observer;

internal sealed record CellProbe(int Cell, int X, int Y, byte B, byte G, byte R, byte A, string Classification);
internal sealed class RoiSnapshot(string kind, double atMs, int screenX, int screenY, string decodeStatus,
    byte[] bytes, DesktopFrameTelemetry? nativeFrame)
{
    public string Kind { get; } = kind;
    public double AtMs { get; } = atMs;
    public int ScreenX { get; } = screenX;
    public int ScreenY { get; } = screenY;
    public int Width => 96;
    public int Height => 8;
    public int Stride => 384;
    public string PixelFormat => "BGRA8 top-down, no conversion";
    public string DecodeStatus { get; } = decodeStatus;
    public string? RawBgraPath { get; internal set; }
    public string Sha256 { get; } = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public uint ManagedBufferHash { get; } = RoiDiagnostics.Hash(bytes);
    public DesktopFrameTelemetry? NativeFrame { get; } = nativeFrame;
    public bool? NativeManagedHashMatches => NativeFrame is null ? null : ManagedBufferHash == NativeFrame.NativeBufferHash;
    public CellProbe[] CellCenters { get; } = RoiDiagnostics.Probe(bytes);
    internal byte[] Bytes { get; } = bytes;
}

internal sealed class RoiDiagnostics
{
    internal List<RoiSnapshot> Snapshots { get; } = [];
    private readonly byte[] _lastInvalidBytes = new byte[3072];
    private (double AtMs, int X, int Y, string Status, DesktopFrameTelemetry? Frame)? _lastInvalid;
    internal void Record(ReadOnlySpan<byte> pixels, double atMs, int screenX, int screenY, string status, DesktopFrameTelemetry? frame = null)
    {
        if (Snapshots.Count == 0) Snapshots.Add(new("first", atMs, screenX, screenY, status, pixels.ToArray(), frame));
        var kind = status == "valid" ? "first-valid" : "first-invalid";
        if (!Snapshots.Any(s => s.Kind == kind)) Snapshots.Add(new(kind, atMs, screenX, screenY, status, pixels.ToArray(), frame));
        if (status != "valid")
        {
            pixels.CopyTo(_lastInvalidBytes);
            _lastInvalid = (atMs, screenX, screenY, status, frame);
            if (atMs - Snapshots[0].AtMs >= 500 && !Snapshots.Any(s => s.Kind == "stable-invalid"))
                Snapshots.Add(new("stable-invalid", atMs, screenX, screenY, status, pixels.ToArray(), frame));
        }
    }

    internal void Save(string outputPath)
    {
        // Files are written only after observation/CPU accounting, never inside capture timing.
        if (_lastInvalid is { } last && !Snapshots.Any(s => s.Kind == "last-invalid"))
            Snapshots.Add(new("last-invalid", last.AtMs, last.X, last.Y, last.Status, _lastInvalidBytes.ToArray(), last.Frame));
        foreach (var snapshot in Snapshots)
        {
            snapshot.RawBgraPath = outputPath + $".{snapshot.Kind}.96x8.bgra";
            File.WriteAllBytes(snapshot.RawBgraPath, snapshot.Bytes);
        }
    }

    internal static uint Hash(ReadOnlySpan<byte> bytes)
    {
        uint hash = 2166136261;
        foreach (var value in bytes) hash = unchecked((hash ^ value) * 16777619);
        return hash;
    }

    internal static CellProbe[] Probe(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != 96 * 8 * 4) throw new ArgumentException("Expected exact 96x8 BGRA ROI.");
        var result = new CellProbe[48];
        for (var cell = 0; cell < 48; cell++)
        {
            var x = 2 * cell + 1; const int y = 4;
            var offset = (y * 96 + x) * 4;
            var b = pixels[offset]; var g = pixels[offset + 1]; var r = pixels[offset + 2]; var a = pixels[offset + 3];
            var classification = r >= 200 && g >= 200 && b >= 200 ? "white" : r <= 40 && g <= 40 && b <= 40 ? "black" : "uncertain";
            result[cell] = new(cell, x, y, b, g, r, a, classification);
        }
        return result;
    }
}
