# 表单控件 Chromium 一致性验证

`tools/Square.FontComparison` 同时承载字体与表单控件两套独立的一致性验证。控件验证固定使用 Microsoft.Playwright 包所携带的 Chromium，在 Windows、`deviceScaleFactor=1`、浅色模式和固定 `320 × 160` 容器中采集基线；它不改变字体一致性的用例、阈值或报告。

## 用例矩阵

| 控件 | `appearance:auto` 与 `appearance:none` 状态 |
| --- | --- |
| Button | normal、hover、active、focus、disabled |
| Input | normal、hover、focus、disabled、value、placeholder |
| TextArea | normal、hover、focus、disabled、value、placeholder |
| Select | normal、hover、focus、disabled |
| CheckBox | unchecked、checked、hover、active、focus、disabled |
| Radio | unchecked、checked、hover、active、focus、disabled |

完整 manifest 共 66 个用例，位于 `tools/Square.FontComparison/Cases/ControlComparisonCases.json`。`appearance:auto` 使用 Chromium 的真实语义控件；`appearance:none` 在 Chromium 与 Square 注入 manifest 中同一段显式作者 CSS，避免依赖隐含 UA 默认值。

Select 的原生下拉 popup/open 状态无法由 headless Chromium 稳定捕获，因此明确标记为不支持，不计为通过，也不伪造截图。当前阻塞后端为 Software 与 Skia。Vulkan 只在具备真实 Win32 readback 证据时报告，不以共享绘制命令路径代替像素通过结论。

## 门禁顺序和阈值

单命令严格先运行几何门，再运行视觉门。任一几何用例失败时立即返回非零，不执行视觉判定；视觉失败同样返回非零。

几何门比较相对固定容器的 border-box `x/y/width/height`，并在 `geometry.json` 中保留四边 used border、padding 与 content-box 诊断。每个坐标或尺寸的阻塞容差为 `0.5 CSS px`。

视觉门按控件区域比较 border、corner/radius、background、text/placeholder/caret、select arrow、checkbox check 与 radio dot，不要求不同光栅器逐像素 RGBA 相等。颜色差使用白底合成后的 RGB 三通道平均绝对差，不能把等亮度但不同色相误判为一致。下表依次列出最小 mask IoU、最大平均颜色差、最大高差异像素比例、最大圆角平均差和最大圆角高差异比例：

| 控件 | Mask IoU ≥ | Mean delta ≤ | High-delta ratio ≤ | Corner mean ≤ | Corner high ratio ≤ |
| --- | ---: | ---: | ---: | ---: | ---: |
| Button | 0.72 | 18 | 0.13 | 40 | 0.45 |
| Input | 0.65 | 18 | 0.13 | 60 | 0.80 |
| TextArea | 0.60 | 18 | 0.13 | 60 | 0.80 |
| Select | 0.60 | 26 | 0.15 | 100 | 0.80 |
| CheckBox | 0.65 | 27 | 0.19 | 100 | 0.80 |
| Radio | 0.65 | 27 | 0.19 | 100 | 0.80 |

阈值的机器可读原值写入 `visual.json`；每个区域的实际指标和失败原因也随用例保存。

## 本地复现

先构建工具并安装与包版本固定的 Chromium：

```powershell
dotnet build tools/Square.FontComparison/Square.FontComparison.csproj -c Release -p:SquareTargetPlatform=Win32
pwsh tools/Square.FontComparison/bin/Release/net10.0/playwright.ps1 install chromium
```

随后用一个命令采集 Chromium、Software、Skia，依次执行几何门与视觉门：

```powershell
dotnet run --project tools/Square.FontComparison/Square.FontComparison.csproj -c Release --no-build -p:SquareTargetPlatform=Win32 -- compare-controls --backends Software,Skia --output artifacts/control-comparison
```

需要定位阶段问题时可显式追加 `--phase geometry` 或 `--phase visual`；单独运行 visual 要求相同输出目录已存在通过且 manifest、当前工具/核心/后端二进制 build fingerprint 匹配的完整几何结果。Chromium 与各 Square 后端报告还必须属于同一个 capture session，捕获时间处于同一有效窗口。

输出目录包含 Chromium、Software、Skia 的 `geometry.json` 与逐用例 PNG、`geometry-matrix.md`、区域 diff PNG、`visual.json` 和离线 `report.html`。每张截图的 SHA-256 写入对应报告，视觉阶段会重新计算并拒绝缺失、替换或跨 run 混用的产物。`artifacts/` 已被 Git 忽略。对同一提交和环境重复执行同一命令应得到相同的门禁结论；绝对时间不参与像素指标，但会用于验证同一 capture session 的时序一致性。

## CI

Windows CI 复用字体工具的 restore/build 和 pinned Chromium 安装步骤，但分别执行并上传：

- `artifacts/font-comparison` → `font-conformance-windows`
- `artifacts/control-comparison` → `control-conformance-windows`

两个报告互不覆盖。上传步骤使用 `always()`，所以门禁失败时仍会保留已生成的几何、截图、diff、JSON 和 HTML，便于离线诊断。