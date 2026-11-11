# Avalonia Canvas v11 adapter

该项目独立于旧 CanvasBenchmark/Avalonia，使用根目录集中管理的 Avalonia 11.3.12 和 .NET 10。读同一份 V11 trace、image.png、font.ttf，不自行生成输入。

```powershell
dotnet build samples/Square.Sample.CanvasBenchmark/V11/Avalonia/Avalonia.csproj -c Release -p:BuildProjectReferences=false

# 由 lead 串行启动；将 trace/output 替换为绝对路径。
.\samples\Square.Sample.CanvasBenchmark\V11\Avalonia\bin\Release\net10.0\CanvasBenchmark.V11.Avalonia.exe --trace C:\absolute\trace.json --output C:\absolute\avalonia.json --tag 31 --verify 0
```

CLI 参数严格成对；trace/output/tag 必填，verify 默认 0，值只允许 0/1。重复参数或未知参数会失败。Avalonia 使用 `UsePlatformDetect` 默认平台 Skia 管线，不接受 Square 专属的 backend 选择。客户区固定 1280×800；启动时 DPR 或客户区不匹配会报告 unsupported，退出码为 1。

每次实际 `Control.Render` 依次消费一个 trace 逻辑帧，不使用墙钟选相位或取模回放。`RequestAnimationFrame` 只负责请求重绘，帧推进发生于 Render。测量帧逐次累加实际 opcode 次数，结束时再次核对 trace 的期望总数。全部场景命令之后绘制 96×8、LSB-first、0xA5 + payload + CRC8 标记。最后一个测量帧之后的实际 Render 重绘最后画面并标记 phase3/seq=measureFrames，再保留 750ms、写 JSON、退出。保留期间的额外暴露重绘只重绘 END 状态。

逻辑 JSON、点坐标、矩阵、颜色和文字在计时前解析。静态路径按引用 ID 预建；poly/clip 为闭合路径，stroke 为开放路径，只有实际使用的变体才会创建。静态文字使用 `TextLayout` 预建并保留行布局；不同颜色的静态布局计入静态资源数量。poly-new、stroke-new、clip-new 和 text-new 每次 Render 都重新创建原生对象，text-new 在该帧绘制后释放，不保存有限帧池。画笔、pen、gradient、layer mask 也在每帧 prepare 中准备。

分组透明度使用 `PushClip(bounds)` 后 `PushOpacityMask(常量白色 alpha brush, bounds)`，按相反顺序弹出。Avalonia.Skia 11.3.12 的 PushOpacityMask 建立 SaveLayer，PopOpacityMask 通过 DstIn 应用 mask、还原并整体 source-over 合成；重叠 child 不会各自乘 group opacity。clip 使用真实 StreamGeometry，transform 使用原生矩阵栈；协议范围内 layer 顶层且不嵌套，内部可有 clip/transform。梯度使用 trace 的绝对起止坐标和 native Pad 插值。

字体直接通过 trace 字节流加载到独立 `fonts:CanvasV11` 单字体 collection，使用 `fonts:CanvasV11#<字节文件中的 family name>` 指定字体。先断言解析后的 FontFamily.Key.Source 等于注册 collection URI，再核对 FontManager 返回同一个 glyph-typeface 实例，并在帧计时之前逐一检查 trace 全部文本的 Unicode codepoint 覆盖。`FontFamily(baseUri, bareFamilyName)` 仅设置相对资源解析基址，bare name 的 Key 仍为 null，会走系统字体路径，不能用于此注册。font/image 实际 SHA256 写入结果；若 trace 声明 assets 的哈希也会先核对。加载失败、类型不能证明相同或缺字会报告 unsupported，禁止改用同名字体或系统 fallback。这个 adapter 仅支持该字节文件能够独立覆盖的文本，不支持需要其它字体补字的 profile。

流字体加载属于该版本的 unstable 实现 API，因此项目显式开启 `AvaloniaAccessUnstablePrivateApis`，保留预期的 AVA3001 构建警告，并依赖根目录固定的 11.3.12。升级 Avalonia 后必须重新查实际 API 和语义。没有反射绕过或 silent fallback。

主 image-grid/mixed profile 只接受整数坐标、1:1 图像，设置 `BitmapInterpolationMode.None`。trace `samplingProfile=native-scaled` 允许整数 2×，使用 `LowQuality`（该 Skia 版本映射为 linear）；该 profile 明确单列，不作为六方相同采样画质结论。输出记录实际 profile 与插值模式。native stroke 的默认 cap/join/miter，以及 native gradient 的插值质量，仍须独立的图像门槛检查。

`frameCpu` 只含 measurement，每项记录 logicalFrame/sequence、nativePrepareMs、recordSubmitMs、totalMs；`presentMs=null`，因为 Control.Render 不提供 compositor/GPU 完成信号。recordSubmitMs 包含 DrawingContext 命令、marker 和动态文字释放。producerFrameTimes 为实际 Render 入口时间与 phase/seq；producerFrameIntervalsMs 是相邻测量 Render 入口间隔。assetLoadMs 包含逻辑输入解析和资产加载/验证，staticPrepareMs 为静态原生路径/文字准备，firstDrawMs 为第一次 Render 的 CPU 耗时。assetsReadyMs 相对本程序计时器，不冒充包含运行时启动的外部 process-cold 时间。准备前后及结束时 private bytes 为计时外当前进程快照；统一进程树内存/CPU仍由外部 observer 记录。

`--verify 1` 仍执行 trace 的完整 warmup+measurement 序列，结果 `timingEligible=false`。END 保留之后，独立 SnapshotCanvas 重新绘制存在的 measurement seq 0/17/37，写 output 同目录的 `.frame0.png`、`.frame17.png`、`.frame37.png`；不会调用 BenchmarkCanvas 重新推进 producer。截图、PNG 编码、文件输出均不计入帧 CPU 数组或 opcode 次数。普通模式不截图。

本次仅做 CPU build；没有启动窗口、GUI/GPU benchmark。最终 native raster/桌面 marker、字体、clip、变换和 layer 语义由 lead 统一运行 fixtures 验证。硬件加速状态不推测，JSON 保留默认管线说明与平台初始化日志，主指标由公共外部 observer 提供。

实际引用与实现参考：

- [BitmapInterpolationMode.None，11.3.12](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Base/Media/Imaging/BitmapInterpolationMode.cs)
- [Skia DrawingContextImpl：PushOpacityMask/PopOpacityMask、native gradient](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Skia/Avalonia.Skia/DrawingContextImpl.cs)
- [RelativePoint.ToPixels：Absolute 不加目标矩形原点](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Base/RelativePoint.cs)
- [TextLayout：constructor 创建行、Draw 复用行](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Base/Media/TextFormatting/TextLayout.cs)
- [FontManager：AddFontCollection/字体 key 与初始化](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Base/Media/FontManager.cs)
- [IFontManagerImpl：stream overload](https://github.com/AvaloniaUI/Avalonia/blob/11.3.12/src/Avalonia.Base/Platform/IFontManagerImpl.cs)
