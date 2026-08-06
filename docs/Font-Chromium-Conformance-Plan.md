# 字体与 Chromium 渲染一致性计划

> Document Revision: 0.2
> 基准平台：Windows
> 浏览器基准：Microsoft.Playwright 随包 Chromium
> Square 后端：Software / Skia / Vulkan

## 1. 目标

建立一套可重复运行的字体渲染一致性系统。相同的字体文件、文本和 CSS 参数分别由 Chromium 与 Square 渲染，自动采集布局度量和截图，并生成机器可读结果、差分图片与离线 HTML 报告。

本项目不把跨栅格器的逐像素完全相等作为目标。验收分为两层：

1. 布局、换行、基线、字符 advance 和墨迹边界按严格数值阈值比较。
2. 抗锯齿 coverage 按墨迹 mask、alpha 差异和差异像素比例比较。

当前支持范围内的差异需要修复到阈值。尚未完整实现的 kerning、ligature、复杂脚本 shaping、BiDi、emoji 和变量字体作为非阻塞探针，只报告差距，不阻塞基础一致性验收。

## 2. 成功标准

- Chromium、Software、Skia 和 Vulkan 使用同一份固定字体文件，不依赖同名字体恰好已安装在系统中。
- 每次报告记录 Playwright/Chromium 版本、Windows 版本、DPI、字体文件 SHA-256、Square 后端及 Vulkan 设备信息。
- 同一份用例清单同时驱动 Chromium 和 Square，不维护两套手写页面参数。
- 阻塞用例的字体匹配、行数和换行 UTF-16 偏移完全一致。
- 阻塞用例的几何度量和像素差异达到第 8 节阈值。
- Software 与 Vulkan 共享字形 coverage 的内部一致性比 Chromium 对比更严格。
- 失败报告可以区分字体回退、布局、换行、基线、字形位置和栅格化差异。
- CI 失败时保留完整 HTML、JSON、截图和差分图。

## 3. 当前基线与已知问题

### 3.1 文本布局

- `TextLayout.DefaultLineHeight` 当前为 `1.2`。
- 文本按 Unicode Rune 独立测量和绘制，尚无完整 shaping、kerning、ligature 和 BiDi。
- `DisplayTree.CollectTextFragments()` 已提供字符级布局边界，可作为 Square 度量输出基础。
- `TextMetrics.RegisterProvider()` 是进程级全局状态。不同后端必须用独立子进程采集，避免度量提供器互相覆盖。

### 3.2 Software 与 Vulkan

- Windows 系统字体通过 GDI `GetGlyphOutline` 生成灰度 coverage。
- 自定义 `FontFace` 通过 stb_truetype 光栅化。
- stb 当前使用 `font.Size * 1.12` 的光学放大，会造成 CSS 字号系统性偏差。
- stb advance 当前被取整为整数，长文本会产生累计宽度误差。
- Software/Vulkan 的字体垂直度量当前主要是字号比例估算，不是字体文件的真实 ascent/descent/line gap。

### 3.3 Skia

- Skia 使用 `SKFont.Metrics` 与 glyph bounds 作为度量基准。
- Skia 当前按字体族名从系统创建 `SKTypeface`，不会读取通过 `FontFace` 注册的固定字体字节。
- 固定字体比较前必须让 Skia 从已注册字体数据创建并缓存 `SKTypeface`。

### 3.4 CSS 链路

- 已有 `font-family`、`font-size`、`font-weight`、`font-style` 和部分 `line-height` 支持。
- 普通 `Text`、编辑器、DisplayTree 和各后端必须验证使用同一套 line-height、baseline 与 advance。
- `text-align` 的 CSS 到 `TextLayout.Alignment` 链路需要补齐和验证。
- 固定尺寸容器内的水平/垂直居中需要比较文本盒相对容器的 `left/top`，不能只比较文本自身的宽高。
- 字体相关继承必须覆盖 weight、style、line-height 和 text-align，而不只 family/size/color。

## 4. 交付结构

新增独立工具项目：

```text
tools/Square.FontComparison/
  Assets/
    Fonts/
    browser.css
  Cases/
    FontComparisonCases.json
  BrowserCapture.cs
  SquareCapture.cs
  ComparisonEngine.cs
  ReportWriter.cs
  Program.cs
```

输出目录：

```text
artifacts/font-comparison/
  index.html
  report.json
  environment.json
  chrome/
    metrics.json
    cases/*.png
  software/
    metrics.json
    cases/*.png
  skia/
    metrics.json
    cases/*.png
  vulkan/
    metrics.json
    cases/*.png
  diff/
    software/
    skia/
    vulkan/
```

## 5. 固定字体

基线使用两套开源字体：

| CSS family | 用途 | 字体面 |
|---|---|---|
| `Square Inter` | Latin、数字、标点 | Regular / Bold / Italic / BoldItalic |
| `Square Noto Sans SC` | 简体中文和中英混排 | Regular / Bold |

字体目录必须包含：

- 原始字体文件。
- 上游许可证文件。
- 来源和版本说明。
- 每个文件的 SHA-256 清单。

字体不能静默回退。Chromium 采集前必须等待 `document.fonts.ready` 并验证 `document.fonts.check()`；Square 必须输出最终匹配的字体面与文件哈希。

## 6. 用例模型

用例 JSON 是 Chromium 和 Square 的唯一输入来源：

```json
{
  "id": "latin-16-400-normal-lineheight-1_2",
  "category": "supported",
  "fontFamily": "Square Inter",
  "fontSize": 16,
  "fontWeight": 400,
  "fontStyle": "normal",
  "lineHeight": "1.2",
  "textAlign": "left",
  "width": 240,
  "text": "Hamburgefontsiv AVATAR 0123456789"
}
```

### 6.1 阻塞用例

避免全笛卡尔积，使用约 60 到 80 个有针对性的组合：

| 维度 | 范围 |
|---|---|
| 字体 | Square Inter / Square Noto Sans SC |
| 字号 | 12 / 14 / 16 / 20 / 24 / 32px |
| 字重 | 400 / 700 |
| 字体样式 | normal / italic；CJK 首轮只要求 normal |
| 行高 | 1 / 1.2 / 1.5 / 固定 px |
| 对齐 | left / center / right |
| 宽度 | 无约束 / 96 / 160 / 240px |
| 文本 | 大小写、数字、标点、升部、降部、窄字、宽字、中文和中英混排 |
| 换行 | 空格断词、CJK 断行、显式换行 |
| 继承 | 父元素字体、子元素单项覆盖 |
| 回退 | 缺失首选 family 后命中明确备用 family |

阻塞基线在 Chromium 中显式关闭 Square 尚未实现的行为：

```css
font-synthesis: none;
font-kerning: none;
font-variant-ligatures: none;
```

### 6.2 非阻塞探针

- 默认 kerning：`AV`、`To`、`Wa`。
- ligature：`fi`、`ffi`。
- 组合字符：`e` + U+0301。
- 阿拉伯文、天城文和 RTL/BiDi。
- emoji 与彩色字体。
- 变量字体轴。
- `line-height: normal`。
- `font-weight: 500/600` 字体匹配。
- synthetic bold/italic。
- `letter-spacing`、`word-spacing`、`text-align: justify`。
- `white-space`、`text-indent`、`text-transform` 和 `text-decoration`。
- 固定尺寸容器中的单行和换行文本水平/垂直居中。
- 多字体 run 级 fallback。

## 7. 采集数据

### 7.1 Chromium

- Playwright 与 Chromium 版本。
- Windows 版本、视口、DPR 和颜色设置。
- `getComputedStyle()` 的最终字体属性。
- `document.fonts.check()` 与 `document.fonts.ready` 状态。
- 元素 `getBoundingClientRect()`。
- 基于 DOM `Range` 的字符或 grapheme rect。
- Canvas `measureText()` 的 width、actualBoundingBoxAscent 和 actualBoundingBoxDescent。
- 行数与每行 UTF-16 起止偏移。
- 每个用例的元素截图。

### 7.2 Square

- 后端、DPI、最终 family/weight/style 与字体文件哈希。
- `FontMetrics`。
- 字符或 grapheme advance 和 ink bounds。
- `TextLayout.Measure()`。
- `DisplayTree.CollectTextFragments()` 的字符边界。
- 行数与每行 UTF-16 起止偏移。
- 实际后端截图。
- Vulkan 设备、驱动与 readback 状态。

所有数值坐标统一为 CSS 逻辑像素；PNG 差分使用物理像素。

## 8. 验收阈值

### 8.1 几何

| 项目 | 阈值 |
|---|---:|
| 字体加载 | 必须确认指定字体面，不允许静默 fallback |
| 行数 | 完全一致 |
| 换行 UTF-16 偏移 | 完全一致 |
| 元素 left/top | `<= 0.25 CSS px` |
| 元素 width/height | `<= 0.5 CSS px` |
| 整行 advance | `<= max(0.35px, 0.15%)` |
| 单字符或 grapheme 起点 | `<= 0.5 CSS px` |
| baseline | `<= 0.5 CSS px` |
| ink bounds 每条边 | `<= 1 physical px` |

### 8.2 Chromium 与 Skia 像素

| 项目 | 阈值 |
|---|---:|
| 1px 邻域墨迹形状匹配 | `>= 0.995` |
| 1px 邻域平均 coverage 差 | `<= 35/255` |
| coverage 差大于 64 的样本比例 | `<= 18%` |
| 缺字或错误字体 | 不允许 |

### 8.3 Chromium 与 Software/Vulkan 像素

| 项目 | 阈值 |
|---|---:|
| 1px 邻域墨迹形状匹配 | `>= 0.78` |
| 1px 邻域平均 coverage 差 | `<= 90/255` |
| coverage 差大于 64 的样本比例 | `<= 42%` |
| 缺字或错误字体 | 不允许 |

### 8.4 Software 与 Vulkan 内部一致性

| 项目 | 阈值 |
|---|---:|
| 布局和 glyph 位置 | 完全一致 |
| 墨迹 mask | 完全一致，最多允许边缘 1px 差异 |
| 平均 alpha 差 | `<= 2/255` |
| 最大通道差 | `<= 8` |

首轮校准只能调整跨栅格器像素阈值，不能放宽字体匹配、换行、baseline 和几何阈值。任何阈值调整必须在文档中记录样本与原因。

首轮 Windows 固定字体校准结果：Chromium 与 Skia 的字符位置差小于 `0.02px`，1px 邻域形状匹配为 `0.9992–1.0`；Chromium 与 stb Software 的字符位置同样小于 `0.02px`，但 20/24px 多行样本因 hinting 和 coverage 分配不同，1px 邻域形状匹配最低约 `0.797`。截图确认字形与行位置一致，因此采用上表阈值，并继续把几何、换行和 baseline 保持为严格阻塞项。

## 9. 实施阶段

### 阶段 1：计划、资产与用例

工作：

- 提交本计划文档。
- 添加字体文件、许可证、来源说明和 SHA-256 清单。
- 建立统一用例 JSON 和 schema/模型验证。

验证：

- 字体可被 stb、Skia 和 Chromium 解析。
- 用例 ID 唯一，所有阻塞用例字段完整。
- 字体哈希与清单一致。

### 阶段 2：统一字体面注册

工作：

- 为 `FontFace` 增加 weight/style 描述。
- 允许同一 family 注册多个字体面。
- `FontManager` 按 family/weight/style 选择最接近字体面。
- Skia 从已注册字体字节创建 `SKTypeface`。

验证：

- Regular、Bold、Italic 和 BoldItalic 分别命中正确文件。
- Software、Skia 和 Vulkan 输出相同字体文件哈希。
- 不安装测试字体到 Windows 字体目录时仍可运行。

### 阶段 3：修正字体度量

工作：

- 移除 stb 的固定 `1.12` 光学放大。
- 从字体 em units 映射 CSS px。
- 保留浮点 logical advance，避免长文本逐字符整数累计。
- 从字体读取真实 ascent、descent 和 line gap。

验证：

- 12 到 32px 的短文本和长文本 advance 达到阈值。
- baseline、升部、降部和行盒高度达到阈值。
- Software 与 Vulkan 继续共享一致的字形位置和 coverage。

### 阶段 4：CSS 文本链路

工作：

- 普通 `Text` 使用 CSS `line-height`。
- 连接 left/center/right `text-align` 到布局和绘制。
- 补齐字体 weight/style/line-height/text-align 继承。
- 确保布局、DisplayTree、命中测试、选择区和绘制共享同一结果。

验证：

- 对齐、继承、固定行高和自动换行测试通过。
- 三后端的 Square 几何输出一致。

### 阶段 5：Chromium 采集

工作：

- 引入 Microsoft.Playwright。
- 使用随包 Chromium，不使用自动更新的系统 Chrome 作为阻塞基准。
- 固定 viewport、DPR、背景、margin 和字体特性。
- 等待字体加载后采集 DOM/Canvas 度量和截图。

验证：

- 连续运行两次的结构化度量完全一致。
- Chromium 版本和字体哈希写入环境报告。
- 字体加载失败会明确失败，不生成伪基线。

### 阶段 6：Square 多后端采集

工作：

- 父进程按后端启动独立子进程。
- Software 和 Skia 使用离屏上下文。
- Vulkan 使用真实 Win32 surface 和 GPU readback。
- 输出统一 schema 的 JSON 与 PNG。

验证：

- 后端执行顺序不影响结果。
- Vulkan 未启用真实 readback 时明确失败，不回退为 Software 截图。
- 每个用例均有度量和截图。

### 阶段 7：比较与报告

工作：

- 比较字体匹配、行断点、几何、baseline、ink bounds 和像素。
- 生成 mask、overlay、heatmap 和分类失败原因。
- 生成 `report.json` 与离线 `index.html`。

验证：

- 人为引入错误字体、1px 偏移和错误换行时，报告给出正确分类。
- HTML 可离线打开，全部资源使用相对路径。
- 非阻塞探针不会导致进程失败。

### 阶段 8：差异修复与阈值收敛

修复顺序：

1. 字体文件或字体面匹配。
2. CSS 字号到字体 em 的映射。
3. glyph advance 与行宽。
4. ascent/descent/baseline。
5. line-height 与 vertical placement。
6. text-align 与 wrapping。
7. glyph coverage 与后端混合。

验证：

- 所有阻塞用例达到第 8 节阈值。
- 每个保留的探针差距都能追溯到未实现能力。

### 阶段 9：CI

工作：

- Windows 普通 CI 运行 Chromium、Software 和 Skia。
- 真实 Vulkan 比较进入已有 self-hosted GPU workflow。
- 失败时上传整个 `artifacts/font-comparison`。

验证：

- 普通 CI 无 GPU 时不伪造 Vulkan 成功。
- GPU workflow 强制 `SQUARE_VULKAN_READBACK=1`。
- 报告 artifact 可直接下载并离线查看。

## 10. 预期命令

安装绑定版本 Chromium：

```powershell
dotnet build tools/Square.FontComparison/Square.FontComparison.csproj -c Release
pwsh tools/Square.FontComparison/bin/Release/net10.0/playwright.ps1 install chromium
```

运行 Chromium、Software 和 Skia：

```powershell
dotnet run --project tools/Square.FontComparison/Square.FontComparison.csproj -c Release -- `
  compare `
  --backends Software,Skia `
  --output artifacts/font-comparison
```

## 10.1 2026-08-06 实际 Chromium 基线

已在 Windows 上运行当前工具链：

```text
Chromium: 149.0.7827.55
Fonts: 6 fixed local font faces
Cases: 21 total, 14 supported, 7 probes
Software: 14 passed, 0 failed, 7 probes
Skia: 14 passed, 0 failed, 7 probes
```

输出目录为 `artifacts/chrome-conformance-text/`，包含 `report.json`、离线 `index.html`、Chromium/Square 截图和差分图。该目录被 `.gitignore` 忽略，不作为源码基线提交。

新增 CSS Text probe 已覆盖：

- `white-space: pre-wrap` 与 `text-indent`；
- `white-space: pre-line`、`letter-spacing`、`word-spacing` 与 `text-transform`；
- `text-decoration: underline line-through`。

容器居中 supported cases：

- `container-center-single-line`：320x120 容器内单行文本；
- `container-center-wrapped`：320x180 容器内固定宽度换行文本。

两组 case 的 Chromium/Square 文本盒位置差均小于 `0.004px`，字符 X 位置差小于 `0.016px`。比较器仅对 manifest 明确声明 `containerWidth`/`containerHeight` 的 case 比较容器相对 `X/Y`，不会改变普通字体 case 的基线语义。

新增 probe 的几何指标与 Chromium 对齐；像素差异仍按 probe 报告，不作为阻塞通过条件。现有已支持字体用例的布局阈值全部通过。`probe-bidi` 和 `probe-combining-mark` 仍显示预期差异，分别对应完整 Unicode bidi/shaping 与 grapheme shaping 尚未实现。

运行真实 Vulkan：

```powershell
$env:SQUARE_VULKAN_READBACK = "1"
dotnet run --project tools/Square.FontComparison/Square.FontComparison.csproj -c Release -- `
  compare `
  --backends Vulkan `
  --output artifacts/font-comparison
```

## 11. 非目标

- 本阶段不实现完整 HarfBuzz 级 shaping。
- 本阶段不要求跨 Windows/Linux/macOS 像素一致。
- 本阶段不把系统 Segoe UI 作为 CI 阻塞基线。
- 本阶段不比较浏览器原生 Button/Input 的 UA 样式。
- 本阶段不把图片位置微调或放宽几何阈值作为字体差异修复方式。

## 12. 风险与约束

- 固定字体是可复现性的前提；系统字体只能作为附加观察项。
- Vulkan 结果依赖真实 GPU、驱动和 readback，必须记录设备环境。
- Skia、Chromium 和 stb 的 hinting/抗锯齿天然不同，像素阈值必须基于墨迹 mask 而不是 RGBA 完全相等。
- 复杂脚本在 Square 完整 shaping 前只能作为探针。
- 当前工作区可能包含其他未提交改动；实现过程只修改本计划直接涉及的文件，不回退无关更改。
