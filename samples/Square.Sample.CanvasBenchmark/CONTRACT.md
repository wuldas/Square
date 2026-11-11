# Canvas benchmark contract

This independently adapts the five default workloads in https://www.qt.io/zh-cn/blog/accelerated-2d-canvas-benchmarks. It does not reproduce Qt's no-vsync throughput measurement.

## Common workload

- Targets: `square-software`, `square-direct2d`, `square-vulkan`, `electron`, `flutter`, `avalonia`.
- Logical client 800x600, background #101827, antialiasing enabled, Segoe UI regular. Caption is outside the client; formal summary requires devicePixelRatio=1. Do not append the removed `标题验证` test label.
- Shared scene JSON contains 60 deterministic frames, 284 logical operations each. Select floor(elapsedSeconds*60)%60 by wall clock. Prepare paths, paints, text and decoded image before warmup. Clear once and issue every command `load` times; no full-frame bitmap/picture/display-list cache. Coordinates are absolute logical pixels.
- Operations (color #RRGGBB or #RRGGBBAA):
  - `["line",x1,y1,x2,y2,color,width]`, butt caps.
  - `["rect",x,y,w,h,color]`, filled.
  - `["circle",cx,cy,r,color,width]`, stroked.
  - `["arc",cx,cy,r,startRadians,sweepRadians,color,width]`: butt arc followed by two filled endpoint circles, radius width/2.
  - `["curve",x0,y0,c1x,c1y,c2x,c2y,x1,y1,baseY,topColor,bottomColor,width]`: sample the cubic at i/32, i=0..32. Close the area down to baseY and x0/baseY; push that area as clip, fill (x0,200,x1-x0,190) using vertical gradient (0,210)..(0,390), pop clip and stroke only the open polyline with topColor and butt caps.
  - `["text",string,x,y,fontSize,color]`, upper-left origin.
  - `["image",x,y,w,h]`, shared icon.png. Disclose actual font/AA/image-sampling differences; common feature support is not pixel identity.
- The scene/input assets are unchanged. v4 gradient/true-geometry-clip/group-layer additions changed image correctness versus v3, not a same-image pure optimization. v5 preserves v4 shader/geometry output and introduces two independently owned in-flight frame-resource bundles with one shared atlas; v4 baseline artifacts remain separate.

## Collection and timing

- CLI: `--scene <absolute-json> --output <absolute-json> --load <integer> --seconds <number> --warmup <number>`, optional `--label <text>` as two argv. Square selects `--backend Software|Direct2D|Vulkan`. Default load=1. Framework/API/backend/load appears in the system title bar, no in-canvas overlay. Window verifier does not pass the removed title-test label.
- Keep default GPU/vsync/AA, no backend fallback. Vulkan requires DiscreteGpu or IntegratedGpu. Its performance samples must record readbackEnabled=false and validationEnabled=false; `--readback 1`/`--validation 1` are separate diagnostics.
- Default v5 round: 1/2/4/8/16/32 load, three repetitions, warmup2s/measure5s, rotating target order, sequential six-target fresh collection:108 raw samples,756 configured seconds plus startup/preparation/draining. Do not reuse old FPS in the new round.
- After warmup, Vulkan explicitly drains outside measurement and records a zero-pending baseline. Measurement start is taken after that drain; deadline=start+seconds. Animation elapsed continues independently. Root frames/fps count API returns inside the fixed window. Vulkan returns queue rendering and are not GPU-complete events; old v4 returned after regular queue-idle.
- `measurementVersion=5` and `vulkanFrameDiagnostics` preserve before/atDeadline/afterDrain snapshots (SubmittedFrames,CompletedFrames,PendingFrames,FramesInFlight), configuredSeconds, actual observationSeconds, apiReturnsObserved, submittedFrames/completedFrames deltas, completedFps, pendingAtDeadline, framesInFlight=2, warmupDrainMilliseconds/endDrainMilliseconds. Deadline snapshot is a nonblocking fence poll before end drain. completedFps uses the completed delta and real start-to-poll duration, which can exceed configured seconds after a long draw. End-drain completions never enter this rate; drain precedes safe window closure. Fixed-window API arrays and observation counters can differ by the final overrun API call. Preserve CPU observation duration separately.
- Counts must be monotonic/conserved; baseline pending=0, deadline pending=submitted-completed<=2, no new submissions during drain, after-drain completed=submitted/pending=0. Removing queue-idle or counting queued returns alone is not evidence of GPU throughput improvement.
- Collector matches visible HWND by child PID, waits20s at50ms intervals and raises the test window once with TOPMOST,NOMOVE,NOSIZE,NOACTIVATE. Record observedTitle/raised/raiseError; failure invalidates the new sample. It never calls SetForegroundWindow. Test window disappears on exit.
- Raw output preserves framework/load/frames/seconds/fps/frameIntervalsMs/drawTimesMs/clientWidth/clientHeight/devicePixelRatio/versions/metric. Square API returns, Avalonia Render, Electron rAF and Flutter raster FrameTiming are different event boundaries; none is physical scanout. Vulkan v5 drawMetric includes any reused-slot fence/acquire wait and CPU recording/submission; it has no regular present queue-idle, and is not GPU elapsed time. Report fence-observed rate separately. Legacy v4 drawMetric includes queue-idle. Flutter UI submission and raster timings remain separate.
- `versions.vulkan={device:{Name,DeviceType,ApiVersion,DriverVersion},colorSampleCount,readbackEnabled,validationEnabled}`. ApiVersion is physical-device support, not an application negotiated feature version; current Instance requests Vulkan1.1. DriverVersion is vendor-specific raw data.
- Optional screenshots/readbacks are outside measurement. Vulkan's initial `.gpu.bmp` is immutable evidence and must never be overwritten by a later window capture; when CaptureAvailable is true, preserve the readback `.bmp` as well. Save actual window-composition evidence separately as `.window.bmp`. Existing Electron no-sandbox/local TEMP compatibility is retained without changing GPU/vsync.
- Archived v4 passed native7/resources6/unit47/Canvas6. v5 is independently verified at native7/resources6/unit49/pipeline2/source21 plus six Canvas ROI cases (max channel error1, no mismatches). Six complete v5 GPU RGBA images also match v4 pixel-for-pixel,2880000 pixels/zero differences. These correctness checks do not turn queued-return FPS into GPU-completed FPS.

## Equivalence declarations and real GPU evidence

- Read the actual root `renderingEquivalent` and `renderingLimitations:string[]`; scripts never modify raw results or manufacture a producer declaration.
- For Vulkan, only the literal JSON boolean true is a positive declaration. False, missing, null or non-boolean values cannot become true because of report version, hardware type, a new shader file or a screenshot. All raw Vulkan samples in the cohort must explicitly be true before eligibility is considered.
- Even explicit true is insufficient without the matching real-device verification: feature-verification-vN.json must have status passed; nativeTests/resourcesTests must have actual passed>0 and failed/skipped/errors=0 with existing logs; canvasVisual.verified=true with comparisons and existing evidence; tested GPU source hashes must match the collected implementation hashes. Until then the report retains a limited/unverified label and explains why.
- Lead changes the producer flag only after the complete real-GPU feature matrix passes. Do not turn it on from report code. Original v3 false/missing declarations always remain limited when old data is re-summarized, even if current Vulkan source now supports the features.
- Known v3 gaps were bounds-only geometry clipping and center-color gradients. Preserve their actual root limitation lists as historical evidence. Do not hardcode those defects into new explicitly verified v4 rows, and do not erase them from v3 rows.
- Matching v5 real-GPU verification is artifacts/canvas-optimization/preflight-v5/feature-verification-v5.json with native/resource/unit/pipeline logs and sourceHashes; the new IVulkanFrameDiagnostics makes21 dynamic top-level C#/GLSL files. Summary accepts explicit verification/log paths. Old v4 evidence remains separate and cannot validate changed v5 source.
- Keep original GPU images, visual-v5/curve-comparison-v5.png and curve-verification-v5.json. sameImageAgainstV4 records six full-frame zero-difference comparisons. Diagnostic results never enter FPS groups, and count-boundary changes alone cannot establish a throughput gain.
- Actual window screenshots/title checks for all six remain required. Default verifier is Version5/ui-v5, accepts a fresh --output, saves window-titles-v5.json and images, and never restores the removed title-test label. Window checks alone do not prove scene equivalence.
- Shared workload eligibility does not eliminate cross-framework font/AA/sampling or event-count differences; do not call the whole table strict GPU throughput or pixel-identical ranking.

## Versions and provenance

- Environment/manifest record reportVersion; collector -Version (alias -ReportVersion) overrides automatic selection. Default round is at least v5, and later reports increment from the highest report-vN.md. Preserve v1/v2/v3/v4 raw data, reports and the v4baseline-v5 binaries/hashes.
- Use fresh output directories. Write report-v5.md/html,summary-v5.csv/json,fps-v5.svg (or chosen N), never overwrite prior outputs or unversioned aliases.
- Summary supports explicit report --version and preserved environment.reportVersion; legacy five-target/no-version data remains readable without invented Vulkan samples. Collection version and output report version are distinct when re-publishing old data; use that collection's real UI/feature evidence.
- Record all current GPU implementation hashes, including Context/Batch/Material/Pipeline/Atlas/Clipper/Offscreen/EmbeddedShaders/GLSL. Verify the tested snapshot against the captured run, not later mutable live files. A later correctness edit requires fresh verification.
- Original five drawing implementations retain their existing baseline (Software joint repair, Direct2D v1 caches); Square v5 measurement timing/labels now distinguish returns and completions. Do not claim all framework source is untouched. All six v5 measurements are freshly collected; benchmark producer and runtime changes are coordinated before freezing source.
