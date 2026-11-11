# Vulkan 2D material shader ABI

The shared `Vertex2D` layout remains 20 bytes: physical root XY, atlas UV, packed straight RGBA.

- Push bytes 0–63: vertex projection matrix.
- Push bytes 64–79: fragment `RowX`.
- Push bytes 80–95: fragment `RowY`.
- Push bytes 96–111: `uvec4(mode, spread, stopOffset, stopCount)`.
- Descriptor binding 0: sampled atlas or resolved offscreen layer.
- Descriptor binding 1: std430 stop array, each stop `{float offset; uint RGBA}` with 8-byte stride. A valid dummy storage buffer is bound even without gradients.

Modes: 0 samples texture × vertex tint; 1 computes a linear gradient coordinate; 2 computes normalized radial XY and distance; 3 samples a premultiplied offscreen group. Spread IDs match `GradientSpreadMethod`: Pad=0, Reflect=1, Repeat=2. Stops retain their original order: the first upper and last lower duplicate define the discontinuity, with first-minimum/last-maximum endpoint behavior. Colors interpolate in the fragment shader; empty stops yield transparent output.

Rows map root physical pixels back through the draw-time inverse transform. Offscreen passes add the target's root origin into row bias because `gl_FragCoord` is local to the attachment. The vertex projection subtracts that same origin. No CPU gradient image or whole-frame fallback is used.

Mode 3 unpremultiplies sampled RGB before the existing straight-alpha pipeline blend. Its output alpha is sampled alpha × `RowX.w` × tint alpha. Group opacity remains an exact float in `RowX.w`; do not also bake it into child vertices or apply it a second time to RGB. Offscreen transparent/semitransparent clear values are premultiplied. Parent masks are applied once to the composite by the Context callback.

The Context records `RenderOffscreenLayers` before beginning the swapchain pass. Children render before parents; `Render` draws target 0 only and does not upload a second time. Previous-frame GPU completion is required before `BeginFrame` reuses mapped data or before `ReleaseLayerResources`/dispose. The idle native layer pool is limited to 16 entries and 64 MiB of actual allocation bytes; active frame resources are released or pooled after GPU completion.

Regenerate and validate the embedded SPIR-V with Vulkan SDK tools:

```powershell
pwsh -File src/Square.Backends.Vulkan/Shaders/generate-shaders.ps1 `
  -Compiler C:\VulkanSDK\1.4.350.0\Bin\glslangValidator.exe `
  -Validator C:\VulkanSDK\1.4.350.0\Bin\spirv-val.exe
```

The script invokes `glslangValidator -V --target-env vulkan1.1 -S vert|frag` and `spirv-val --target-env vulkan1.1`, retains ignored intermediates in `.build`, then generates `EmbeddedShaders.cs`. The app requests Vulkan 1.1; this does not imply use of the physical device's maximum supported API version. Do not edit embedded byte arrays manually.
