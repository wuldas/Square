# V11 desktop observer

Windows .NET 10 console plus a small Windows SDK C++ DLL; no third-party package dependencies. Exactly one owned benchmark process is launched. stdout/stderr are inherited; the outer runner redirects them. Only `--self-test` is CPU-only. Normal invocations launch the supplied application.

```text
Observer.exe --launch C:\absolute\launch.json --output C:\absolute\observer.json --tag 17 --timeout-seconds 180
Observer.exe --self-test
```

All normal CLI options are key/value pairs. Tag must match the application. Timeout defaults to 180 seconds.

## Launch JSON

```json
{
  "fileName": "C:\\absolute\\CanvasBench.exe",
  "args": ["--trace", "C:\\absolute\\trace.json", "--output", "C:\\absolute\\app.json", "--tag", "17"],
  "workingDirectory": "C:\\absolute",
  "environment": {},
  "expectedClientWidth": 1280,
  "expectedClientHeight": 800,
  "observeFrames": true,
  "captureBackend": "desktop-duplication"
}
```

Paths must be absolute. The trace is read before launch for exact measurement count and SHA256. The visible root-PID top-level window title must include `Canvas v11` (case insensitive). Physical client 1280×800, window DPI96/DPR1, and desktop containment are validated. The observer uses per-monitor V2 DPI awareness, TOPMOST without moving/resizing or activation, `UseShellExecute=false`, `CreateNoWindow=true`, and no redirected pipes.

`captureBackend` defaults to `desktop-duplication`. Explicit `gdi` retains the previous GDI polling implementation for calibration reference; there is no automatic fallback. Duplication selects the adapter/output containing the whole physical client, uses D3D11/IDXGIOutput1::DuplicateOutput, acquires with an 8 ms timeout, copies only the 96×8 ROI to a cached BGRA staging texture, maps/copies/unmaps, and releases every acquired frame. Pointer-only updates are recorded without readback or marker decoding. Rotated outputs, output changes and ACCESS_LOST fail explicitly.

`observeFrames` defaults to `true`. Set `false` for a same-application/same-trace paired diagnostic control. Root PID/window validation/TOPMOST, 4 ms idle polling and 100 ms process-tree sampling remain. It initializes neither GPU duplication nor GDI/DC/DIB, performs no readback/BitBlt/decode, and requires no marker boundaries. Success is `status:"control-complete"`, `sampling:{"enabled":false}`, `marker:null`; real failures remain `failed`. Marker-based CPU measurement boundaries are null, while launch-to-exit CPU/memory totals remain. Formal collection requires `status:"ok"`, `sampling.enabled==true`, and `launch.observeFrames!=false`. Controls measure capture/decoding perturbation; they are never formal desktop frame results.

## Result JSON (camelCase; existing fields retained)

- Identity/result: `protocolVersion`, `tag`, `status`, `failures`, `startedUtc`, `elapsedMs`, `rootPid`, `childExitCode`, `launch`, `traceSha256`, `expectedMeasuredFrames`.
- `window` and `windowGeometrySamples`: root HWND/title/owner, physical dimensions/origin, DPI/DPR, first-found time and topmost-without-activation confirmation.
- `marker`: `firstMarkerMs`, `firstMeasurementMs`, `firstEndMs`, `measurementDurationMs`, `desktopObservedChangesPerSecond`, `uniqueMeasuredCount`, `uniqueMeasuredFrames`, `missingMeasuredSequences`, `transitionRecords`, `visibleIntervalMs`, `measurementStartBracketMs`, `measurementEndBracketMs`, `timestampSource`.
- `sampling`: `enabled`, `backend`, `eventDriven`, `intervalResolution`, `targetPollMs` (4 for GDI, null for duplication), `acquireTimeoutMs` (8 for duplication), `qpcTickMs`, `desktopFrameIntervalMs`, `desktopFrameIntervalSource`, `nativeProcessingBudgetMs`, `readbackLatencyBudgetMs`, `readbackLatencyMs`, `nativeProcessingMs`, `accumulatedExtraDesktopFrames`, `captureFailures`; existing `sampleCount`, valid/invalid counts/rates, rejection counts, capture/gap distributions, `measurementGapMs`, `measurementCaptureMs`, `observerLimited`, `badCoverage`, reasons, and raw `samples` remain.
- Each `sampling.samples[]` retains `atMs`, `gapMs`, `captureMs`, `decodeStatus`; optional `desktopFrame` contains `lastPresentQpc`, `lastMouseQpc`, `accumulatedFrames`, `pointerOnly`, `protectedContentMaskedOut`, `rectsCoalesced`, `acquiredAtMs`, `receivedAtMs`, `readbackLatencyMs`, `nativeProcessingMs`.
- `capture` (duplication only): backend/eventDriven, initialization cost, QPC frequency/origin, output bounds/LUID/rotation/index/refresh, acquisition/timeout/pointer counts, call-duration distribution and raw `acquisitions` (completion timestamp, full Acquire/copy call duration, status/HRESULT/native metadata). Acquire timeouts are absence of desktop events, not failed marker samples. Pointer-only events never enter marker samples.
- `clock`: `originQpc`, `stopwatchFrequency`, `alignmentUncertaintyMs`. Native QPC frequency must equal Stopwatch frequency.
- `processTree`: timestamped root/descendant CPU/private bytes/working-set samples, sampled memory peaks, PID+creation-time identities, creation/first-seen/exit observations, `recordUnavailable`, aggregate CPU lower bound, and marker-window CPU with actual sampled bracketing boundaries. Working sets can count shared pages repeatedly. Short-lived undiscovered descendants and unavailable processes make CPU a lower bound; retained handles preserve final CPU after exit.
- `observer`: separate `preparationCpuMs`, launch-to-finish `cpuMs`, percentage of one logical core, and sampled marker-window CPU. Final JSON serialization is excluded.
- `exitGrace`: root-exit timestamp, maximum 2000 ms, elapsed grace, initially live/remaining owned PIDs, timeout flag and boundary note. Helpers receive bounded normal-exit time after root exits. This grace changes no marker measurement duration. Helpers still live afterward fail the run and only those owned identities are cleaned up.

All marker timestamps are milliseconds relative to the same Stopwatch used immediately before Process.Start. Duplication uses `DXGI_OUTDUPL_FRAME_INFO.LastPresentTime` QPC desktop-update time, while GDI uses capture-completion time. Duplication boundary arrays contain `[eventTime,eventTime]`; their QPC precision is not a physical scanout precision claim. `captureMs` includes Acquire waiting; `nativeProcessingMs` is acquisition-return through ROI readback/release, and `readbackLatencyMs` is desktop event through managed receipt. Duplicate marker samples are counted; only valid matching-tag changes enter transitions. Protocol regressions/range errors fail.

## Coverage and interpretation

Duplication desktop-event gaps normally follow display/desktop changes (often 16.7 ms); polling thresholds do not apply. Inside the marker measurement window, accumulated updates (`AccumulatedFrames>1`), missing native metadata, protected/masked content, nonmonotonic timestamps, native processing exceeding one desktop interval, readback latency exceeding two intervals, capture failure, or invalid/stale markers flag `observerLimited`/`badCoverage`. The interval is the reported DXGI refresh period, otherwise the measured event-gap P50 (60 Hz fallback only if unavailable); the exact value and source are emitted. The measured native acquisition/processing overhead and paired control remain part of qualification.

The GDI criteria remain unchanged: measurement P95 gap >12 ms, max gap >24 ms, unbracketed first measurement, invalid/stale measurement samples, or capture failures limit coverage. Both backends require visible phase2 and phase3 boundaries.

The primary metric is unique marker sequence IDs divided by first-visible phase2 to first-visible phase3 duration in the composited desktop. Missing IDs mean unobserved/merged sequences, not proven GPU drops. DXGI accumulated desktop updates are not counts of lost application frames. Neither backend measures physical scanout or GPU kernel time. The outer runner independently validates application JSON/opcode totals.

## Marker

48 cells, width2/height8, center `(2*cell+1,4)`: sync0xA5 + payload32 + CRC8, LSB first. Payload `(tag&255)<<18 | (phase&3)<<16 | (seq&65535)`; phase1 warmup, phase2 measurement, phase3 end. CRC uses four little-endian payload bytes, init0/poly0x07. White requires every RGB≥200; black every RGB≤40; intermediate pixels, sync/CRC/reserved bits/old tags are rejected. Neither backend invokes PrintWindow or asks the producer to render.

## Build and CPU validation

```powershell
cmake -S samples/Square.Sample.CanvasBenchmark/V11/Observer/CaptureNative -B samples/Square.Sample.CanvasBenchmark/V11/Observer/obj-native -G "Visual Studio 18 2026" -A x64
cmake --build samples/Square.Sample.CanvasBenchmark/V11/Observer/obj-native --config Release --parallel 1
dotnet build samples/Square.Sample.CanvasBenchmark/V11/Observer/Observer.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet samples/Square.Sample.CanvasBenchmark/V11/Observer/bin/Release/net10.0/Observer.dll --self-test
```

CMake writes `CanvasObserverCapture.dll` beside the managed executable under `bin/Release/net10.0`. Both files are required. CPU self-test loads no native capture DLL and launches no child, GUI or GPU work. It covers protocol/CRC, mode defaults/control schema, ABI sizes, QPC conversion and synthetic native coverage. Lead performs real desktop/GPU integration serially.

Reference coding follows Microsoft's [DuplicationManager](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/DXGIDesktopDuplication/cpp/DuplicationManager.cpp) and [OutputManager staging ROI](https://github.com/microsoft/Windows-classic-samples/blob/main/Samples/DXGIDesktopDuplication/cpp/OutputManager.cpp). The MIT notice is retained in `CaptureNative/LICENSE.microsoft.txt`. Native telemetry follows the official [frame-info contract](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/ns-dxgi1_2-dxgi_outdupl_frame_info) and [AcquireNextFrame contract](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_2/nf-dxgi1_2-idxgioutputduplication-acquirenextframe).
