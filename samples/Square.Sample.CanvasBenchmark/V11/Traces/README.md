# Canvas v11 fixed logical inputs

The complete input inventory is [manifest.json](C:/.AA/dotnet-projects/Square/samples/Square.Sample.CanvasBenchmark/V11/Traces/manifest.json). It separates 29 performance traces, two semantic fixtures, and one observer calibration. [schema.json](C:/.AA/dotnet-projects/Square/samples/Square.Sample.CanvasBenchmark/V11/Traces/schema.json) defines the opcode arrays. [validation.json](C:/.AA/dotnet-projects/Square/samples/Square.Sample.CanvasBenchmark/V11/Traces/validation.json) records the independent CPU input checks; it does not claim a GUI/GPU run.

Every trace directory contains its own canonical JSON and identical copies of the Segoe UI Regular font and generated 32×32 RGBA checker. The font comes from the local Windows installation. All input files, logical paths/texts/frames, per-frame operations, text-frame content, and asset bytes have SHA256 evidence. JSON uses UTF-8 without BOM, sorted object keys, compact separators, and no trailing newline. Seed is `0x1234ABCD`; only the generator evaluates mix32. Adapters read the same exact numeric inputs.

Read all `frames` in order exactly once; `frames.length = warmupFrames + measureFrames`. Static profiles intentionally repeat equal logical values, while dynamic geometry, paint, transform and text are unique for every object across all 180 frames. Recreate uses equal logical points through `poly-new` every frame. Only cache-pressure rotates its static path pool; it still stores every logical frame explicitly and does not wrap the frame array. Native objects for `poly-new`, `stroke-new`, and `text-new` must be created in the current frame's native preparation time.

All regular profiles use 60 warmup and 120 measurement frames. Cache-pressure draws 128 paths per frame with pools 256/4096/65536 and warmup 60/64/1024 frames, respectively. This means 30/2/2 complete pool passes; every pool path is visited uniformly at least twice. Static/dynamic text uses 21 ASCII characters per item and the same glyph alphabet, with only the four-digit frame number changing in the dynamic profile.

Images in image-grid and mixed-dashboard map 32×32 source pixels to integer 32×32 destination pixels (`samplingProfile="identity-1to1"`). Image-grid low/high uses 64/256 independent items. The separate image-scaled profile uses 64×64 destinations, 32/128 items, `samplingProfile="native-scaled"`, and `qualityComparable=false`. It retains backend native/default nearest or linear differences. Native stroke cap/join/AA and native gradient interpolation are separate quality profiles.

Matrices use `x'=m11*x+m21*y+dx`, `y'=m12*x+m22*y+dy`, and `CTM=parent*local` for nested transforms. Paths are bare arrays of point pairs. Fill and clip close the polygon; stroke leaves the polyline open. Every state opcode, including each `end-*`, is part of measured opcode totals; the external marker is excluded. All geometric coordinates are multiples of 1/16 pixel and matrix coefficients are multiples of 1/4096. Opacity and color channels follow their own protocol units.

Verify mode executes the complete selected trace. Screenshots use **measurement sequence** 0/17/37 when present, so their absolute logical frame indices are `warmupFrames + sequence`. Verify runs are timing-ineligible.

- [semantic-fixture-core](C:/.AA/dotnet-projects/Square/samples/Square.Sample.CanvasBenchmark/V11/Traces/semantic-fixture-core/trace.json): warmup 0, measurement 1, all 16 opcodes. Seventeen predefined 5×5 probes check opaque interiors/background, noncommuting transforms, true clip geometry, source-over alpha intersections, group opacity, and 1:1 image content. Two text regions check nonempty strokes separately; exact content/origin and font bytes must also match producer declarations.
- [semantic-fixture-sampling](C:/.AA/dotnet-projects/Square/samples/Square.Sample.CanvasBenchmark/V11/Traces/semantic-fixture-sampling/trace.json): warmup 0, measurement 1, side-by-side 1:1 and 2:1 images, four predefined interior probes. Native sampler transition pixels are a separate quality region.
- [calibration-marker-only](C:/.AA/dotnet-projects/Square/samples/Square.Sample.CanvasBenchmark/V11/Traces/calibration-marker-only/trace.json): `kind="calibration"`, warmup 60, measurement 120, no scene paths/texts/ops and zero opcode totals. Adapters still clear the background and draw each marker. This input is excluded from `benchmarkTraces` and is listed in `calibrationTraces`.

The core fixture lists native stroke, gradient and AA edge regions separately from exact probes. Probe expectations are fixed before any real screenshot. The analytic CPU reference verifies only the declared interior samples; it does not construct an AA/font reference renderer or establish physical display correctness.

Generate and validate from the workspace root:

```powershell
$pythonExe = 'C:\Users\Wuldas\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
& $pythonExe samples\Square.Sample.CanvasBenchmark\V11\tools\generate-traces.py
& $pythonExe samples\Square.Sample.CanvasBenchmark\V11\tools\validate-traces.py
```

Use `--representative` on the generator for the four small integration inputs; it writes an incomplete manifest (`complete=false`). Run the generator without that flag to restore the complete inventory. Generation and validation are CPU-only.

The validator can also inspect an **existing** producer result and canvas PNG without launching any renderer:

```powershell
& $pythonExe samples\Square.Sample.CanvasBenchmark\V11\tools\validate-traces.py --trace <absolute-trace> --result <absolute-result> --png <absolute-PNG> --output <new-validation-JSON>
```

This checks trace SHA256, measured opcode totals, client dimensions/DPR and configured frame counts, then checks each declared image ROI and nonempty text. Font registration, text identity/origin and separately listed quality differences remain explicit producer/visual qualification evidence.
