# CSS 支持范围

> Document Revision: 1.1
> 更新日期：2026-08-06
> MDN 基线：[CSS：层叠样式表](https://developer.mozilla.org/zh-CN/docs/Web/CSS)，页面更新时间 2025-11-10
> 配套文档：`Architecture.md`、`Sqx-Spec.md`

本文按 MDN CSS 指南模块盘点 Square 的实际实现。状态以当前源码中的解析、级联、布局、绘制、交互和测试路径为准，不以“声明能够被解析或保存在 `Style` 中”作为支持依据。

Square 的兼容目标是以 CSS2.1 为基础、叠加 CSS2.2 修订/勘误对齐的可移植子集，而不是完整 CSS2.2。CSS2.2 相关条目只描述已实现且可验证的局部语义；完整规范模型仍按下文边界处理。

## CSS2.1 基线与 CSS2.2 对齐状态

当前已接入 CSS2.1 阶段 1-9 的最低可用实现路径，包括属性元数据与简写展开、`@media` 的 `screen` / `all` 求值、Block/Inline normal flow、直接 Float/Clear、Relative/Absolute/Fixed 定位、通用盒绘制、生成内容/Counter/List Marker，以及独立 Table formatting path。

这不等于完整 CSS2.1 一致性。当前实现仍保留以下边界：

- 未显式使用 CSS2.1 normal-flow 特性的旧 Block 树仍可走 Yoga column-flex 兼容路径。
- Margin collapse 目前覆盖相邻 Block 兄弟，不含全部父子与空 Block 折叠规则。
- Inline formatting 支持 Text fragment 与原子 inline 元素，但不是完整匿名 inline box、Bidi 和所有 baseline 语义。
- Float 主要覆盖直接子元素及后续 inline 内容绕排，不是完整 BFC float 模型。
- Absolute/Fixed 已支持显式 inset；复杂 static-position 和完整 stacking context 尚未实现。
- Table 支持基本 auto/fixed sizing、row/column span、caption、border spacing 与有限 collapsed border；未实现完整 CSS2.1 border conflict 和匿名 table box 规则。
- 通用盒绘制支持纯色背景、solid/none 边框、outline、shadow 与 subtree opacity；背景图片、全部边框样式和跨后端完整合成仍有限。

因此，本文后续矩阵中的“已支持”仍按具体子功能判定，不应将本节理解为 CSS2.1/CSS2.2 浏览器级合规声明。

## CSS2.1 本地一致性阶段 10-11

阶段 10-11 建立的是仓库内的 CSS2.1 **本地 fixture harness**，不是 W3C CSS2.1 测试套件的移植，也不是浏览器级一致性声明。每个 fixture 定义 CSS 文本、元素工厂、期望的 computed style、几何结果和 Square 绘制失效标志；适用时还比较 `direction` / `unicode-bidi`、文本视觉顺序与逻辑 UTF-16 命中偏移、`@font-face` descriptor 元数据，以及运行时 media type 切换。测试复用 `CssEngine` 与 `LayoutEngine`。

| 阶段 | Feature ID | 状态 | 验证范围 |
|---|---|---|---|
| 10 | `CSS21-SYNTAX`、`CSS21-SELECTORS`、`CSS21-CASCADE`、`CSS21-VALUES` | 🟢 **fixture 已覆盖** | 语法恢复、选择器匹配、继承/specificity/源码顺序/`!important`，以及所选值的 computed-style 字符串。 |
| 10 | `CSS21-BOX`、`CSS21-VISUAL-FORMATTING` | 🟢 **fixture 已覆盖** | 盒尺寸、padding/margin、Block normal flow 和显式 relative 定位的逻辑像素几何。 |
| 10 | `CSS21-GENERATED-CONTENT`、`CSS21-TABLES` | 🟢 **fixture 已覆盖** | `::before`/`::after` 字符串子树、基础 table sizing 和 cell geometry。 |
| 10 | `CSS21-FONTS`、`CSS21-MEDIA` | 🟡 **有限覆盖** | 字体声明/inheritance、`screen`/`print` media type 选择和切换；不覆盖平台 shaping 或完整 media features。 |
| 11 | `CSS21-PAINT-FLAGS` | 🟢 **fixture 已覆盖** | `NeedsPaint`、full/partial dirty 和已布局几何标志；不比较像素。 |
| 11 | `CSS21-AT-FONT-FACE` | 🟡 **已支持子集** | `@font-face` descriptor parsing、descriptor 数量/source/local 状态与可移植的本地文件 `LoadFontsAsync`；仅 local file，跳过 remote/data 源，不宣称完整 CSS font source selection。 |
| 11 | `CSS21-BIDI` | 🟡 **已支持子集** | computed `direction` / `unicode-bidi`、basic run-level bidi mapping、文本视觉顺序和 hit-test 逻辑 UTF-16 偏移与 ASCII 括号镜像；不含 Arabic shaping、Unicode 全量镜像、isolates 或完整 UAX #9。 |
| 11 | `CSS21-PAGED-MEDIA`、`CSS21-ANONYMOUS-BOXES`、`CSS21-FULL-COLOR-PAINT` | ⚪ **明确 deferred** | 这些 ID 保留在 manifest 以便报告覆盖边界；分页、完整匿名盒构造和完整颜色/背景绘制均未声明支持。 |

<!-- CSS21-MANIFEST-SUPPORTED: CSS21-SYNTAX CSS21-SELECTORS CSS21-CASCADE CSS21-VALUES CSS21-BOX CSS21-VISUAL-FORMATTING CSS21-GENERATED-CONTENT CSS21-TABLES CSS21-FONTS CSS21-MEDIA CSS21-PAINT-FLAGS CSS21-AT-FONT-FACE CSS21-BIDI -->
<!-- CSS21-MANIFEST-DEFERRED: CSS21-PAGED-MEDIA CSS21-ANONYMOUS-BOXES CSS21-FULL-COLOR-PAINT -->

W3C CSS2.1 full suite remains **Deferred** and is not claimed by this manifest. The IDs above describe only the repository's local fixtures and implemented subsets; they are not W3C pass/fail results.

## CSS2.2 revision/errata subset

The local manifest records the CSS2.2 revision/errata subset layered over the CSS2.1 baseline that can be checked through Square's portable parser, style, layout, display-tree, and table APIs. These are focused regression fixtures, not a CSS2.2 specification implementation or a W3C conformance result.

| Feature ID | Status | Portable verification boundary |
|---|---|---|
| `CSS22-PARSER-NUMBERS-ESCAPES` | 🟢 **fixture covered** | Leading-dot numbers, exponent notation, escaped identifiers, and hexadecimal escapes are checked through `CssTokenizer`. |
| `CSS22-FONT-FAMILY-KEYWORDS` | 🟢 **fixture covered** | Generic family keywords and serialized family lists are checked through `FontManager` and `Style.Get(...)`. |
| `CSS22-MARGIN-COLLAPSE` | 🟢 **fixture covered** | Adjacent positive block margins are checked through the portable normal-flow layout path. Parent-edge, empty-block, clearance, and all negative-margin combinations are not claimed. |
| `CSS22-FORMATTING-CONTEXTS-CONTAINING-BLOCKS` | 🟢 **fixture covered** | Implemented block layout and direct absolute containing-block geometry are checked. Full BFC behavior is deferred. |
| `CSS22-OVERFLOW-TABLE-BEHAVIOR` | 🟢 **fixture covered** | Table roots preserve the overflow declaration and expose the public scroll-content-size contract. Complete browser table overflow clipping interoperability is not claimed. |
| `CSS22-TABLE-WRAPPER` | 🟡 **supported subset** | Explicit `table` / `inline-table` roots and table-cell sizing/geometry are checked. Square does not claim generated or anonymous table-wrapper construction. |
| `CSS22-FIXED-STACKING` | 🟡 **supported subset** | Only viewport fixed-overlay rendering and simple `z-index` ordering are checked. This is not a full fixed stacking context or browser stacking-context isolation. |
| `CSS22-CANVAS-BACKGROUND-VISIBILITY` | 🟡 **subtree visibility subset (legacy ID)** | This legacy ID covers `display: none` subtree removal and `visibility: hidden` paint suppression while an explicitly visible descendant remains paintable. It does not claim canvas background propagation. |
| `CSS22-HEIGHT-PERCENTAGE-COMPUTED` | 🟢 **fixture covered** | Percentage `height` is preserved as the computed `Style.Get(...)` value; generalized used-value resolution is not claimed. |
| `CSS22-MALFORMED-DECLARATION-RECOVERY` | 🟢 **fixture covered** | Malformed declarations are skipped while later valid declarations in the same block survive. |
| `CSS22-W3C-CONFORMANCE` | ⚪ **deferred** | Full CSS2.2 W3C conformance is not imported or claimed. |
| `CSS22-ANONYMOUS-BOXES` | ⚪ **deferred** | Complete anonymous block, inline, and table box construction is deferred. |
| `CSS22-FULL-BFC` | ⚪ **deferred** | Complete block formatting context establishment, float interaction, clearance, and containment are deferred. |
| `CSS22-FULL-TABLE-MODEL` | ⚪ **deferred** | Complete table wrapper, anonymous table boxes, border conflict, and table layout semantics are deferred. |
| `CSS22-FULL-UNICODE-BIDI` | ⚪ **deferred** | Full Unicode bidi layout, shaping, mirroring, and UAX #9 behavior are deferred. |
| `CSS22-PAGED-MEDIA` | ⚪ **deferred** | Page boxes, pagination, fragmentation, and paged-media layout are deferred. |

<!-- CSS22-MANIFEST-SUPPORTED: CSS22-PARSER-NUMBERS-ESCAPES CSS22-FONT-FAMILY-KEYWORDS CSS22-MARGIN-COLLAPSE CSS22-FORMATTING-CONTEXTS-CONTAINING-BLOCKS CSS22-OVERFLOW-TABLE-BEHAVIOR CSS22-TABLE-WRAPPER CSS22-FIXED-STACKING CSS22-CANVAS-BACKGROUND-VISIBILITY CSS22-HEIGHT-PERCENTAGE-COMPUTED CSS22-MALFORMED-DECLARATION-RECOVERY -->
<!-- CSS22-MANIFEST-DEFERRED: CSS22-W3C-CONFORMANCE CSS22-ANONYMOUS-BOXES CSS22-FULL-BFC CSS22-FULL-TABLE-MODEL CSS22-FULL-UNICODE-BIDI CSS22-PAGED-MEDIA -->

The CSS2.2 IDs intentionally distinguish portable local behavior from deferred browser-level claims. Full CSS2.2 W3C conformance, full BFC behavior, anonymous boxes, the full table model, full Unicode bidi, and paged media remain deferred. The local table, fixed-layer, and subtree-visibility tests do not constitute full model conformance.

本地 harness 的明确限制：

- fixture 数量很小，覆盖的是已实现路径的回归样例，不代表 CSS2.1 每个 property、value、selector、错误恢复或交互组合。
- computed style 比较的是 Square `Style.Get(...)` 的字符串结果，不是浏览器 `getComputedStyle()` 的完整规范化序列化；方向相关断言也只覆盖 fixture 中明确声明的 `direction` / `unicode-bidi` 值。
- geometry 比较的是 Square 逻辑像素 `Element.Geometry`；它不验证浏览器 viewport、DPI、字体度量、Bidi baseline 或 margin-collapse 的全部算法。
- paint 断言只检查 invalidation flags 和显示状态，不检查 Skia/Software/Vulkan 的像素、抗锯齿、颜色空间或合成结果。
- 表格 fixture 不覆盖完整匿名 table box、border conflict、分页、复杂 rowspan/colspan 和所有 CSS2.1 table edge cases。
- 字体 fixture 验证 descriptor 数量、family/source/local 状态，并在存在可用本地字体文件时验证 `LoadFontsAsync`；不固定系统字体，不加载 remote/data 源，且不宣称跨平台 glyph shaping 一致。
- media fixture 选择并切换 `screen` 和 `print` media type，并检查切换后的 computed style；不实现 viewport、resolution、色彩、无障碍偏好或 `matchMedia()` 一致性。
- bidi fixture 只验证 CSS direction values、basic run-level ordering、Text fragment 的视觉字符顺序和逻辑 UTF-16 hit-test 偏移，以及有限 ASCII 括号镜像；不验证 Arabic shaping、Unicode 全量镜像、isolates、baseline 或完整 UAX #9。
- 生成内容 fixture 验证 Square 内部 Text 子元素顺序，不宣称完整 CSS generated box、marker、quote、counter 或 replaced-content 语义。

Feature ID 的唯一来源是 `tests/Square.CSS.Tests/Css21ConformanceFixtures.cs` 中独立的 known-ID 集合与 manifest；报告测试会拒绝重复、未知和遗漏 ID。

## 状态图例

| 标记 | 含义 |
|---|---|
| 🟢 **已支持** | 所列子功能已接入实际消费路径，并能影响匹配、布局、绘制、交互或公开 API。 |
| 🟡 **部分支持** | 已实现可用子集，但语法、值域、适用对象或浏览器语义不完整。 |
| ⚪ **未支持** | 没有语义实现；即使属性值可被保存，也不会产生对应 CSS 效果。 |

## 判定原则

- `Style.Get("x")` 能返回字符串，不代表属性 `x` 已实现。
- 控件自定义 `Paint()` 只会读取明确接入的属性，因此部分绘制属性具有控件依赖性。
- Square 的 Element Tree、Yoga 布局和保留模式渲染不是浏览器 DOM、格式化上下文与绘制模型的完整复制。
- 本文中的“支持”默认指 Square 桌面 UI 元素；SVG 的独立子集会单独说明。
- 浏览器私有前缀、怪异模式和依赖浏览器页面环境的模块不属于兼容目标。

---

## 1. MDN CSS 模块支持矩阵

以下模块名称与顺序来自 MDN CSS 页面中的“指南 / 模块”目录。

### 1.1 布局、盒模型与定位

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 1 | [Anchor positioning](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Anchor_positioning) | 锚点定位、锚点函数、回退位置、溢出处理、锚定容器查询 | ⚪ **未支持**：无 `anchor-name`、`position-anchor`、`anchor()`、`@position-try`。 |
| 6 | [CSS 盒子对齐](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Box_alignment) | Flex、Grid、块、绝对定位、表格、多列中的主轴和交叉轴对齐 | 🟡 **部分支持**：🟢 Flex 的 `justify-content`、`align-items`、`align-content`、`align-self`；Grid 和块布局对齐模型未完整实现。 |
| 7 | [CSS 基础框盒模型](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Box_model) | content、padding、border、margin、尺寸和外边距折叠 | 🟡 **部分支持**：🟢 物理方向 margin/padding、边框宽度参与布局，以及相邻 Block 兄弟的 margin collapse 子集；⚪ 无完整外边距折叠和浏览器 BFC。 |
| 8 | [Box sizing](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Box_sizing) | 内在/外在尺寸、`box-sizing`、宽高约束、宽高比 | 🟡 **部分支持**：🟢 `width`、`height`、min/max、`box-sizing`、`aspect-ratio` 子集；内在尺寸算法不完整。 |
| 20 | [显示](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Display) | block/inline、常规流、包含块、格式化上下文、脱离文档流、多关键字 `display` | 🟡 **部分支持**：`block`、`inline`、`inline-block`、`flex`、`grid`、`table`、`inline-table`、`none` 提供已接入子集；`block` 实际按 Yoga column flex 或 CSS normal-flow 路径处理，`inline-block` 仅按有限 block-like/atomic 子集处理。⚪ 无完整 BFC/IFC、`contents` 和完整匿名盒规则。 |
| 24 | [CSS 弹性盒子布局](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Flexible_box_layout) | 方向、换行、对齐、弹性比例、顺序和典型布局 | 🟡 **部分支持且可用**：🟢 方向、wrap、grow/shrink/basis、Flex 简写子集、对齐和 gap；⚪ 无 `order`、`flex-flow` 完整语法。 |
| 28 | [Gaps](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Gaps) | `gap`、`row-gap`、`column-gap` 在 Grid、Flex 和多列中的语义 | 🟡 **部分支持**：🟢 Flex/Grid gap；⚪ 无多列布局。 |
| 30 | [网格布局](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Grid_layout) | Track、线定位、区域、自动放置、对齐、subgrid、masonry | 🟡 **部分支持**：模板行列、`fr`、百分比、简化 `minmax()`、区域、基础 span 与自动放置；⚪ 无 repeat、命名线、dense、subgrid、masonry 和完整对齐。 |
| 32 | [Inline layout](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Inline_layout) | 行内格式化上下文、line box、baseline、inline flow | 🟡 **部分支持**：提供有限 inline formatting path、文本 fragment 和 atomic inline 元素；不等价于完整浏览器 inline layout，匿名 inline box、全部 line box/baseline 语义和完整 bidi 仍未实现。 |
| 34 | [CSS 逻辑属性与逻辑值](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Logical_properties_and_values) | 逻辑尺寸、逻辑 margin/padding/border、逻辑定位 | 🟡 **极少量支持**：`inset-block-start/end`、`inset-inline-start/end`，但固定映射到物理方向；无 writing-mode 感知。 |
| 38 | [CSS 多列布局](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Multicol_layout) | column count/width、列规则、跨列、平衡、分段 | ⚪ **未支持**。`column-gap` 仅用于 Flex/Grid。 |
| 41 | [Overflow](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Overflow) | 溢出裁剪、滚动容器、滚动内容和 carousel | 🟡 **部分支持且可用**：🟢 `overflow`、`overflow-x/y` 的 visible/hidden/clip/scroll/auto，裁剪、滚动偏移、wheel 和命中映射；无通用滚动条 UI。 |
| 42 | [Overscroll behavior](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Overscroll_behavior) | 滚动链、边界行为、overscroll containment | ⚪ **未支持**。 |
| 43 | [CSS 分页媒体](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Paged_media) | 页面盒、分页、纸张尺寸、打印分页 | ⚪ **未支持**。 |
| 44 | [CSS 定位布局](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Positioned_layout) | relative/absolute/fixed/sticky、包含块、堆叠上下文、`z-index` | 🟡 **部分支持**：🟢 relative、absolute、fixed viewport layer、物理 inset、`inset`；fixed 仅提供 viewport overlay ordering，`z-index` 为简单同级排序。⚪ 无 sticky 和浏览器堆叠上下文。 |
| 47 | [Round display](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Round_display) | 圆形视口、shape-aware 布局、内容适配 | ⚪ **未支持**。 |
| 48 | [Ruby layout](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Ruby_layout) | Ruby 注音、定位、对齐和行间注音 | ⚪ **未支持**。 |
| 58 | [CSS Table](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Table) | 表格格式化、列宽、边框合并、行列和单元格 | 🟡 **部分支持**：显式 `table` / `inline-table` 根、行/单元格、基本 auto/fixed sizing、span 和部分边框/间距语义已接入；⚪ 无完整 table model、生成/匿名 wrapper 和完整 border conflict。 |
| 65 | [Viewport](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Viewport) | layout/visual viewport、视口单位、viewport adaptation | 🟡 **部分支持**：🟢 `vw`、`vh`；⚪ 无 VisualViewport、动态视口单位和 viewport at-rule。 |
| 68 | [CSS 书写模式](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Writing_modes) | writing-mode、逻辑轴、竖排文本、竖向表单 | ⚪ **未支持**。`writing-mode` 未实现；`direction: ltr/rtl` 可影响 Yoga 和 basic bidi helper，但不等价于完整文本排版。 |

### 1.2 级联、语法、选择器与作用域

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 9 | [Cascading and inheritance](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Cascade) | 级联、来源、优先级、继承、CSS-wide 值、简写 | 🟡 **部分支持**：🟢 specificity、源码顺序、内联样式、`!important`、有限继承；`inherit/initial/unset` 为简化语义。⚪ 无 origin、layer、revert。 |
| 13 | [Conditional rules](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Conditional_rules) | `@supports`、条件规则、容器 scroll-state 查询 | ⚪ **未支持条件求值**。通用 At 规则只会被解析为 AST 元数据。 |
| 17 | [Custom functions and mixins](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Custom_functions_and_mixins) | 自定义 CSS 函数、mixins 和可复用样式逻辑 | ⚪ **未支持**。 |
| 19 | [Custom properties for cascading variables](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Cascading_variables) | `--*`、`var()`、fallback、继承和循环处理 | 🟢 **已支持核心功能**：自定义属性参与级联与继承，支持 fallback、嵌套 fallback 和循环检测。 |
| 39 | [Namespaces](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Namespaces) | `@namespace`、命名空间限定类型和属性选择器 | ⚪ **未支持**。 |
| 40 | [CSS 嵌套](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Nesting) | `&`、嵌套规则、嵌套 At 规则、嵌套 specificity | ⚪ **未支持**。 |
| 45 | [Properties and values API](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Properties_and_values_API) | `@property`、typed custom properties、Houdini 注册 | ⚪ **未支持**。 |
| 49 | [Scoping](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Scoping) | `@scope`、scope root/limit、scoping proximity | ⚪ **未支持标准 `@scope`**。组件样式作用域是 Square 框架机制，不等价于 CSS Scoping。 |
| 54 | [CSS 选择器](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Selectors) | 基础选择器、属性、组合器、结构/状态伪类、函数式伪类 | 🟡 **部分支持且较完整**：🟢 type/class/id/universal、属性操作符、四种组合器、多种状态和结构伪类；高级 Level 4 选择器未实现。 |
| 57 | [Syntax](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Syntax) | token、声明、规则、注释、At 规则、错误恢复 | 🟡 **部分支持**：普通规则、声明、注释、字符串、数字、单位、函数文本、`!important`；无完整 CSS Syntax error recovery 和嵌套 block grammar。 |
| 63 | [CSS 值和单位](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Values_and_units) | 数值/文本类型、单位、函数、数学表达式、typed arithmetic | 🟡 **部分支持**：长度单位和字符串子集；⚪ 无 `calc()`、`min()`、`max()`、`clamp()`、typed arithmetic。 |

### 1.3 颜色、背景、边框与视觉效果

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 3 | [CSS 背景和边框](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Backgrounds_and_borders) | 背景颜色/图片/多层、尺寸与位置、边框、圆角和阴影 | 🟡 **部分支持**：🟢 纯色背景、统一圆角、外阴影子集、边框宽度布局；⚪ 无背景图片/渐变/多层，通用 View 不绘制完整 CSS border。 |
| 5 | [Borders and box decorations](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Borders_and_box_decorations) | 边框形状、装饰边缘和高级 border | 🟡 **少量支持**：统一圆角和部分控件边框；⚪ 无 border-shape、border-image、复杂每边样式。 |
| 10 | [CSS 颜色调整](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Color_adjustment) | `color-scheme`、forced colors、打印颜色调整 | ⚪ **未支持**。 |
| 11 | [CSS 颜色](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Colors) | 颜色值、相对颜色、颜色空间、混色和无障碍 | 🟡 **部分支持**：普通控件仅支持 `#RGB`、`#RRGGBB`、Square 非标准 `#AARRGGBB`；SVG 和 box-shadow 有各自有限解析器。 |
| 12 | [Compositing and blending](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Compositing_and_blending) | alpha compositing、blend mode、isolation | ⚪ **未支持 CSS compositing/blending**。 |
| 23 | [Filter effects](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Filter_effects) | `filter`、`backdrop-filter`、滤镜链 | ⚪ **未支持**。 |
| 35 | [CSS Masking](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Masking) | mask、clip-path、多重蒙版和裁剪 | ⚪ **未支持 CSS masking/clip-path**。渲染器内部 clip 不是 CSS API。 |
| 56 | [CSS 形状](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Shapes) | `shape-outside`、box/image shape、文本环绕 | ⚪ **未支持**。 |

### 1.4 文本、字体、列表与生成内容

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 15 | [CSS 计数器样式](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Counter_styles) | counter reset/increment、`@counter-style`、自动编号 | ⚪ **未支持**。 |
| 25 | [Font loading](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Font_loading) | `@font-face`、FontFace、FontFaceSet、加载事件 | 🟡 **部分支持**：🟢 命令式 `FontFace`、`FontFaceSet`、`Document.Fonts` 与 CSS `@font-face` descriptor/local-file load 子集；⚪ remote/data CSS 源、加载事件和完整 web-font selection 未实现。 |
| 26 | [CSS 字体](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Fonts) | family、size、weight、style、OpenType、variable fonts、WOFF | 🟡 **部分支持**：🟢 family 列表、通用族、px/数字字号、weight/style 子集；⚪ 无 font shorthand、feature/variation 设置和完整 Web Font CSS。 |
| 29 | [CSS 生成内容](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Generated_content) | `content`、伪元素内容、attr、counter、quote、图片内容 | 🟡 **部分支持**：🟢 `::before`/`::after` 的字符串 content 和动态协调；⚪ 无 attr/counter/quote/image。 |
| 33 | [CSS 列表与计数器](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Lists) | list-style、marker、缩进和计数器 | ⚪ **未支持 CSS list formatting**。`ListItem.Marker` 是框架属性。 |
| 46 | [Pseudo-elements](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Pseudo-elements) | generated box、文字片段、高亮和元素子部件 | 🟡 **部分支持**：🟢 `::before`、`::after`；🟡 `::selection` 仅背景/颜色映射；其他伪元素未实现。 |
| 59 | [CSS 文本](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Text) | wrapping、breaking、white-space、text align/indent/spacing | 🟡 **部分支持**：🟢 左/中/右 `text-align`、自动换行、`white-space` 的 normal/pre/nowrap/pre-wrap/pre-line 子集、`letter-spacing`、`word-spacing`、`text-indent` 和 `text-transform`；⚪ 无 `word-break`、`overflow-wrap`、`text-overflow`。 |
| 60 | [CSS 文本装饰](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Text_decoration) | underline、overline、line-through、text-shadow 和装饰样式 | 🟡 **可绘制子集**：🟢 `underline`、`overline`、`line-through`、`none`，跟随共享文本布局、换行和三种后端绘制；⚪ 无 text-shadow、装饰样式/波形、语言特定位置。 |

### 1.5 动画、过渡与变换

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 2 | [CSS 动画](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Animations) | `@keyframes`、animation 属性、方向、迭代、延迟和事件 | 🟡 **部分支持**：🟢 数值关键帧、duration/delay/count/direction、帧循环；⚪ 无颜色/transform 插值、fill-mode、play-state、多动画和事件。 |
| 21 | [Easing functions](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Easing_functions) | linear、cubic-bezier、steps 和 easing function | 🟡 **部分支持**：`ease-in`、`ease-out`、`ease-in-out` 为内置三次曲线；其他值运行时退化为 linear。 |
| 37 | [Motion path](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Motion_path) | offset path/distance/rotate 和路径动画 | ⚪ **未支持**。 |
| 52 | [Scroll-driven animations](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Scroll-driven_animations) | scroll/view timeline、timeline inset/range | ⚪ **未支持**。 |
| 61 | [CSS Transforms](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Transforms) | 2D/3D transform、origin、perspective | ⚪ **未支持普通元素 CSS transform**。SVG `transform` 属性是独立子集。 |
| 62 | [CSS 过渡](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Transitions) | transition property/duration/delay/timing 和事件 | ⚪ **未支持**；样式变更立即生效。 |
| 64 | [CSS 视图过渡](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/View_transitions) | same/cross-document transition、生命周期和伪元素 | ⚪ **未支持**。 |
| 67 | [Will change](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Will_change) | 渲染变化提示、资源预分配和性能权衡 | ⚪ **未支持**。 |

### 1.6 查询、环境、滚动与平台能力

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 14 | [CSS 局限](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Containment) | `contain`、size/style container queries、containment | ⚪ **未支持**。 |
| 16 | [CSS 对象模型视图](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/CSSOM_view) | 坐标系统、viewport、geometry、scroll APIs | 🟡 **部分支持**：🟢 `GetBoundingClientRect()`、`ScrollLeft/Top`、滚动尺寸和 Element HitTest；无完整 CSSOM View API。 |
| 18 | [Custom highlight API](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Custom_highlight_API) | Highlight registry、自定义 Range、highlight 伪元素 | ⚪ **未支持**。`::selection` 子集不等价于 Custom Highlight API。 |
| 22 | [Environment variables](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Environment_variables) | `env()`、safe-area 和 UA 环境变量 | ⚪ **未支持**。 |
| 36 | [媒体查询](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Media_queries) | viewport/media feature、无障碍偏好、打印和 matchMedia | 🟡 **部分支持**：`@media screen`/`print`/`all` 可求值并可通过 `CssEngine.SetMediaType` 切换；无 viewport/features、无障碍偏好和 `matchMedia()`。 |
| 50 | [Scroll anchoring](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Scroll_anchoring) | 滚动位置稳定、anchor node、`overflow-anchor` | ⚪ **未支持**。 |
| 51 | [CSS 滚动吸附](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Scroll_snap) | snap container、snap position、snap event | ⚪ **未支持**。 |
| 53 | [CSS Scrollbars](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Scrollbars_styling) | scrollbar color/width/gutter 和平台样式 | ⚪ **未支持通用 CSS scrollbar styling**。 |
| 66 | [WebXR DOM overlays](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/WebXR_DOM_overlays) | XR DOM overlay、沉浸式呈现和 overlay 交互 | ⚪ **未支持**。 |

### 1.7 图片、分段、Shadow DOM 和其他模块

| # | MDN 模块 | MDN 核心功能 | Square 当前支持 |
|---:|---|---|---|
| 4 | [CSS Basic User Interface](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Basic_user_interface) | appearance、cursor、outline、resize、user-select、caret | 🟡 **部分支持**：🟢 `appearance: auto/none`（UA 对齐 Chrome `html.css` 浅色表单控件子集，Software/Skia/Vulkan 盒绘制消费计算样式）、cursor 子集、`user-select: text/none`、`caret-color`；⚪ 无 outline、resize。 |
| 27 | [CSS 片段](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Fragmentation) | page/column/region 分段和 break 控制 | ⚪ **未支持**。 |
| 31 | [Images](https://developer.mozilla.org/en-US/docs/Web/CSS/Guides/Images) | CSS 渐变、replaced element、object-fit、sprite | 🟡 **框架 Image 控件部分支持**：内在尺寸和等比缩放；⚪ 无 CSS gradient、object-fit/object-position 和 CSS image values。 |
| 55 | [CSS 影子部件](https://developer.mozilla.org/zh-CN/docs/Web/CSS/Guides/Shadow_parts) | Shadow DOM `::part()`、exportparts 和受控主题 | ⚪ **未支持 Shadow DOM/parts**。 |

---

## 2. 选择器详细矩阵

### 2.1 样式表选择器

| 功能 | 示例 | 状态 | 说明 |
|---|---|---|---|
| 类型选择器 | `Button` | 🟢 | TagName 大小写不敏感匹配。 |
| 类选择器 | `.primary` | 🟢 | ClassList 大小写敏感。 |
| ID 选择器 | `#main` | 🟢 | ID 大小写敏感。 |
| 通配选择器 | `*` | 🟢 | 不增加 specificity。 |
| 复合选择器 | `Button.primary#save` | 🟢 | 同一元素同时匹配多个简单选择器。 |
| 选择器列表 | `Button, Text` | 🟢 | 解析为独立规则并保留源码顺序。 |
| 后代组合器 | `View Text` | 🟢 | 向祖先链匹配。 |
| 子代组合器 | `View > Text` | 🟢 | 仅匹配直接父子。 |
| 相邻兄弟 | `.a + .b` | 🟢 | 支持动态样式协调。 |
| 通用兄弟 | `.a ~ .b` | 🟢 | 匹配任一前置兄弟。 |
| 属性存在 | `[IsDisabled]` | 🟢 | 匹配 `Element.Properties`，不是 DOM Attr。 |
| 属性精确值 | `[variant=primary]` | 🟢 | 默认 Ordinal 大小写敏感。 |
| 属性词列表 | `[tags~=primary]` | 🟢 | 按空白词列表匹配。 |
| 属性 dash match | `[lang|=en]` | 🟢 | `en` 或 `en-*`。 |
| 属性前/后/包含 | `^=` `$=` `*=` | 🟢 | 支持字符串比较。 |
| 属性大小写修饰 | `[label=x i]` `[label=x s]` | 🟢 | `i` 使用 .NET OrdinalIgnoreCase。 |
| 命名空间选择器 | `svg|rect` | ⚪ | 未实现。 |
| Column combinator | `A || B` | ⚪ | 未实现。 |

### 2.2 伪类

| 伪类 | 状态 | 说明 |
|---|---|---|
| `:hover` | 🟢 | 基于 `ElementState.Hover`。 |
| `:focus` | 🟢 | 基于 `ElementState.Focus`。 |
| `:active` | 🟢 | 基于 `ElementState.Active`。 |
| `:disabled` | 🟢 | 基于 `ElementState.Disabled`。 |
| `:checked` | 🟢 | 基于 `ElementState.Checked`。 |
| `:open` | 🟢 | 基于 `ElementState.Open`。 |
| `:empty` | 🟢 | 检查 ChildNodes；生成伪元素不计入。 |
| `:first-child`、`:last-child`、`:only-child` | 🟢 | 生成伪元素不参与索引。 |
| `:root` | 🟡 | 定义为 `Parent == null`，组件 scope root 也可能匹配。 |
| `:nth-child(An+B)` | 🟢 | 支持整数、odd、even、一般 `An+B`；无 `of selector`。 |
| `:not(...)` | 🟡 | 仅类型、class、ID、`*` 单一简单参数。 |
| `:is()`、`:where()`、`:has()` | ⚪ | 未实现。 |
| `:focus-visible`、`:focus-within` | ⚪ | 未实现。 |
| `:nth-of-type()`、`:nth-last-child()` | ⚪ | 未实现。 |
| `:target`、`:lang()`、`:dir()`、链接和表单校验伪类 | ⚪ | 未实现。 |

### 2.3 伪元素

| 伪元素 | 状态 | 说明 |
|---|---|---|
| `::before`、`:before` | 🟢 | 字符串 `content` 或显式 `content: ""` 装饰盒子；创建内部 Text 子元素并应用声明，背景/边框由通用绘制管线渲染。 |
| `::after`、`:after` | 🟢 | 字符串 `content` 或显式 `content: ""` 装饰盒子；创建内部 Text 子元素并应用声明，背景/边框由通用绘制管线渲染。 |
| `::selection` | 🟡 | 仅映射 `background`/`background-color`/`color`。 |
| `::first-line`、`::first-letter`、`::marker`、`::placeholder` | ⚪ | 未实现。 |
| `::part()`、highlight 系列、View Transition 系列 | ⚪ | 未实现。 |

> 说明：`::before`/`::after` 未设置 `content`（或为 `none`/`normal`）时伪元素被移除；显式 `content: ""` 保留为空装饰盒子，可用于纯样式伪元素（如分隔条握把、hover 高亮条）。类型选择器（如 `Splitter { … }`）按 `TagName` 匹配，可配合伪类/伪元素为内置控件定制样式。

### 2.4 DOM 查询 API 与样式表选择器差异

`QuerySelector` / `QuerySelectorAll` 使用独立的轻量选择器实现，支持范围比样式表匹配器更窄。

| Query API 功能 | 状态 |
|---|---|
| 类型、class、ID、复合选择器 | 🟢 |
| 后代、子代、逗号分组 | 🟢 |
| 属性、通配、伪类、伪元素 | ⚪ |
| 相邻兄弟、通用兄弟 | ⚪ |
| `matches()`、`closest()` | ⚪ |

---

## 3. 级联、继承、变量与 CSSOM

### 3.1 级联和 specificity

| 功能 | 状态 | 说明 |
|---|---|---|
| ID/class/type specificity | 🟢 | 使用 `(Ids, Classes, Types)`。 |
| 属性和伪类 specificity | 🟢 | 计入 class 位。 |
| 伪元素 specificity | 🟢 | 计入 type 位。 |
| 同 specificity 源码顺序 | 🟢 | 后定义覆盖先定义。 |
| 内联 style | 🟢 | 高于普通样式表声明。 |
| `!important` | 🟢 | 高于非 important 声明；内联 important 仍保留优先。 |
| 多组件样式引擎级联元数据 | 🟢 | 后应用的低 specificity 不会覆盖先前高 specificity。 |
| `:not()` specificity | 🟡 | 仅支持简单参数。 |
| UA/User/Author origins | ⚪ | 未建模。 |
| Cascade layers | ⚪ | 无 `@layer` 排序语义。 |
| `revert`、`revert-layer` | ⚪ | 未实现。 |

### 3.2 自动继承

自动继承以下属性：

| 属性 | 状态 |
|---|---|
| `color` | 🟢 |
| `font-family` | 🟢 |
| `font-size` | 🟢 |
| `font-weight` | 🟢 |
| `font-style` | 🟢 |
| `line-height` | 🟢 |
| `text-align` | 🟢 |
| `visibility` | 🟡 仅值继承，普通元素没有隐藏效果 |
| `--*` 自定义属性 | 🟢 |

`cursor` 和 `user-select` 在交互使用点向祖先查找，但不属于通用继承表。

### 3.3 CSS-wide 值

| 值 | 状态 | 说明 |
|---|---|---|
| `inherit` | 🟡 | 从父元素读取该属性。 |
| `initial` | 🟡 | 返回 `null`，没有标准属性 initial-value 数据库。 |
| `unset` | 🟡 | 依赖有限的硬编码继承属性表。 |
| `revert`、`revert-layer` | ⚪ | 未实现。 |

### 3.4 自定义属性和主题

```css
:root {
  --primary: #175cd3;
  --spacing: 12px;
}

Button {
  color: var(--primary);
  padding: var(--spacing, 8px);
}
```

| 功能 | 状态 |
|---|---|
| `--name: value` | 🟢 |
| `var(--name)` | 🟢 |
| `var(--name, fallback)` | 🟢 |
| 嵌套 fallback | 🟢 |
| 继承、级联、循环检测 | 🟢 |
| `CssEngine.RegisterTheme` / `SetTheme` | 🟢 Square 扩展 |
| `@property` 和 typed custom properties | ⚪ |

### 3.5 `element.Style` CSSOM 子集

| Web API 表面 | Square | 状态 |
|---|---|---|
| `style.setProperty(name, value, priority)` | `Style.SetProperty(...)` | 🟢 |
| `style.getPropertyValue(name)` | `Style.GetPropertyValue(...)` | 🟢 |
| `style.getPropertyPriority(name)` | `Style.GetPropertyPriority(...)` | 🟢 |
| `style.removeProperty(name)` | `Style.RemoveProperty(...)` | 🟢 |
| `style.cssText` | `Style.CssText` | 🟢 |
| `style.length` / `item(i)` | `Style.Length` / `Style.Item(i)` | 🟢 |
| camelCase/PascalCase 规范化 | `fontSize` -> `font-size` | 🟢 Square 扩展 |
| 最终值读取 | `Style.Get(name)` | 🟢 Square 扩展 |
| `getComputedStyle()` | 无 | ⚪ |
| Mutable CSSStyleSheet / CSSRule API | 无 | ⚪ |
| Typed OM / `CSS.supports()` | 无 | ⚪ |

---

## 4. 属性支持矩阵

### 4.1 Display、尺寸和盒模型

| 属性 | 状态 | 支持值或限制 |
|---|---|---|
| `display` | 🟡 | `block`、`inline`、`inline-block`、`flex`、`grid`、`table`、`inline-table`、`none` 的已接入子集；`inline-block` 仅按有限 block-like/atomic 子集处理，其他值按 block-like column flex。 |
| `width`、`height` | 🟢 | `auto`、百分比和已支持长度。 |
| `min-width`、`min-height` | 🟢 | 百分比和已支持长度。 |
| `max-width`、`max-height` | 🟢 | 百分比和已支持长度。 |
| `box-sizing` | 🟡 | `content-box` 与默认 `border-box`；Square 默认值不同于浏览器。 |
| `aspect-ratio` | 🟢 | 正数或 `a / b`；无 replaced-element `auto <ratio>` 语义。 |
| `margin` 和四个物理 longhand | 🟢 | 1-4 值、长度、百分比、`auto`。 |
| `padding` 和四个物理 longhand | 🟢 | 1-4 值、长度、百分比。 |
| `border-width` 和四个边宽 | 🟢 布局 | 参与 Yoga box model。 |
| `border`、`border-style` | ⚪ | 无通用 shorthand 和绘制实现。 |
| 逻辑尺寸、margin、padding、border | ⚪ | 未实现。 |
| `contain`、`content-visibility` | ⚪ | 未实现。 |

### 4.2 Flex

| 属性 | 状态 | 支持值或限制 |
|---|---|---|
| `flex-direction` | 🟢 | row、row-reverse、column、column-reverse。 |
| `flex-wrap` | 🟢 | nowrap、wrap、wrap-reverse。 |
| `justify-content` | 🟢 | flex-start/start、center、flex-end/end、space-between、space-around、space-evenly。 |
| `align-items` | 🟢 | stretch、start、center、end、baseline。 |
| `align-content` | 🟢 Flex 多行 | auto、stretch、start/end、center、space-*；单行容器通常无可见效果。 |
| `align-self` | 🟢 | auto、stretch、start、center、end、baseline。 |
| `flex-grow`、`flex-shrink` | 🟢 | 浮点数。 |
| `flex-basis` | 🟢 | auto、百分比、长度。 |
| `flex` | 🟡 | none、auto、单数字和有限 grow/shrink/basis 组合。 |
| `gap`、`row-gap`、`column-gap` | 🟢 | 已支持长度。 |
| `order` | ⚪ | 未实现。 |
| `flex-flow` | ⚪ | 未实现。 |

### 4.3 Grid

| 属性或功能 | 状态 | 支持值或限制 |
|---|---|---|
| `grid-template-columns/rows` | 🟡 | `px`、`%`、数字、`fr`、auto、min/max-content、裸 `fit-content`、简化 `minmax(px, fr)`。 |
| `grid-template-areas` | 🟢 子集 | 引号行和旧 pipe 行语法。 |
| `grid-column`、`grid-row` | 🟡 | 数字线、数字结束线、`span N`。 |
| `grid-area` | 🟢 子集 | 匹配命名区域。 |
| `grid-column-span`、`grid-row-span` | 🟢 Square 扩展 | 非标准便利属性。 |
| 基础 auto-placement | 🟡 | Row-major，跳过已占用格，支持简单 span。 |
| `gap`、`row-gap`、`column-gap` | 🟢 | 支持。 |
| `repeat()`、named lines、negative lines | ⚪ | 未实现。 |
| `grid-auto-*`、dense | ⚪ | 未实现。 |
| Grid item alignment / `place-*` | ⚪ | 未完整实现。 |
| subgrid、masonry | ⚪ | 未实现。 |

### 4.4 Positioning、方向和堆叠

| 属性 | 状态 | 支持值或限制 |
|---|---|---|
| `position` | 🟡 | relative、absolute、fixed；fixed 仅为 viewport layer 子集；默认 normal flow。 |
| `top/right/bottom/left` | 🟢 | auto、百分比、已支持长度。 |
| `inset` | 🟢 | 1-4 值。 |
| `inset-block-*`、`inset-inline-*` | 🟡 | 固定映射到物理方向。 |
| `direction` | 🟡 | ltr/rtl 传给 Yoga，并作为 basic bidi helper 的 base direction；不提供完整 UAX #9 文本布局。 |
| `unicode-bidi` | 🟡 | normal、embed、bidi-override 的 basic run-level subset；无 isolates、Arabic shaping 或 glyph mirroring。 |
| `z-index` | 🟡 | 整数同级排序；无 stacking context。 |
| `sticky` | ⚪ | 未实现。 |
| anchor positioning | ⚪ | 未实现。 |

### 4.5 Overflow 和滚动

| 属性/API | 状态 | 支持值或限制 |
|---|---|---|
| `overflow`、`overflow-x/y` | 🟢 子集 | visible、hidden、clip、scroll、auto。 |
| 子树绘制裁剪 | 🟢 | 支持轴向裁剪。 |
| 命中测试裁剪和滚动映射 | 🟢 | 支持。 |
| `ScrollLeft`、`ScrollTop` | 🟢 | Element API。 |
| wheel 滚动最近祖先 | 🟢 | 默认动作。 |
| 通用 scrollbar chrome | ⚪ | 未实现。 |
| scroll snap、overscroll、smooth scroll | ⚪ | 未实现。 |
| scrollbar styling | ⚪ | 未实现。 |

### 4.6 背景、边框和阴影

| 属性 | 状态 | 支持值或限制 |
|---|---|---|
| `background`、`background-color` | 🟡 | 仅纯色，且控件必须调用 styled background 绘制。`background` 不展开 shorthand。 |
| `border-color` | 🟡 | 部分输入类控件的统一边框颜色。 |
| `border-width` | 🟡 | 布局支持；绘制仅部分控件。 |
| `border-radius` | 🟡 | 单一统一半径；数字、px、百分比。 |
| `box-shadow` | 🟡 | 多个外阴影、offset/blur/spread、hex/rgb/rgba；无 inset。 |
| `background-image`、gradient、repeat/position/size | ⚪ | 未实现。 |
| `border-image`、每边 style/color | ⚪ | 未实现。 |
| `outline-*` | ⚪ | 未实现。 |

### 4.7 颜色值

普通控件颜色解析器支持：

| 值 | 状态 | 说明 |
|---|---|---|
| `#RGB` | 🟢 | 支持。 |
| `#RRGGBB` | 🟢 | 支持。 |
| `#AARRGGBB` | 🟡 Square 格式 | 与标准 CSS `#RRGGBBAA` 不兼容。 |
| Named colors | 🟡 子集 | `transparent`、`black`/`white`/`red`/`green`/`blue`。 |
| `rgb()` / `rgba()` | 🟡 盒绘制 | `CssBoxPainter` 解析；box-shadow 另有 legacy parser。 |
| hsl/hwb/lab/lch/oklab/color/color-mix | ⚪ | 未实现。 |
| `currentColor` | 🟡 盒绘制 | 边框/背景解析时回退到 `color`。 |
| system colors | 🟡 浅色子集 | `ButtonFace`/`ButtonText`/`ButtonBorder`/`Field`/`FieldText`/`Canvas`/`CanvasText`/`GrayText`/`Highlight`/`HighlightText`/`ThreeDFace`，按 Chrome Win11 浅色默认。 |

### 4.8 字体和文本

| 属性 | 状态 | 支持值或限制 |
|---|---|---|
| `font-family` | 🟡 | 逗号列表、引号、通用族和已注册字体。 |
| `font-size` | 🟡 | 实际字体构造仅正数字和 px；em/rem/% 不完整。 |
| `font-weight` | 🟡 | normal、bold、lighter、bolder、100-900 数值量化。 |
| `font-style` | 🟢 子集 | normal、italic、oblique；无 oblique angle。 |
| `line-height` | 🟡 | px 或 unitless multiplier。 |
| `text-align` | 🟡 | left、center、right；justify 被接受但按 left 绘制。 |
| `color` | 🟡 | 受有限颜色解析器和控件绘制路径约束。 |
| 自动文本换行 | 🟢 Square 文本布局 | 按宽度、空白、CJK 和有限断点换行。 |
| `white-space` | 🟡 子集 | normal、pre、nowrap、pre-wrap、pre-line；共享 TextLayout、normal-flow 和三种后端绘制路径。 |
| `word-break`、`overflow-wrap` | ⚪ | 未实现 CSS 控制。 |
| `text-overflow`、ellipsis | ⚪ | 未实现。 |
| `text-decoration`、`text-decoration-line` | 🟡 子集 | 支持 none、underline、overline、line-through；绘制矩形装饰线，参与 dirty bounds；不支持 blink、复杂样式和颜色 longhand。 |
| `text-shadow` | ⚪ | 未绘制。 |
| `letter-spacing`、`word-spacing` | 🟡 子集 | 支持逻辑像素和 `em`；参与测量、换行、绘制和命中测试。 |
| `text-indent` | 🟡 子集 | 首行逻辑像素缩进，参与测量、normal-flow 和绘制。 |
| `text-transform` | 🟡 子集 | none、capitalize、uppercase、lowercase；按 Unicode rune 的基础转换，不含语言特定 casing。 |

### 4.9 交互和表单 UI

| 属性 | 状态 | 支持值或限制 |
|---|---|---|
| `cursor` | 🟡 | pointer、hand、text、default、auto。 |
| `user-select` | 🟡 | text、none；向祖先查找。 |
| `caret-color` | 🟡 | 文本编辑器，有限颜色值。 |
| `selection-background(-color)`、`selection-color` | 🟡 | 文本编辑器内部属性。 |
| `appearance` | 🟡 子集 | `none`（initial）、`auto`。UA 对齐 Chrome `html.css` 浅色表单控件：`Button` 为 `ButtonFace` + `2px outset ButtonBorder`，`:active` 切 `inset`，`:disabled` 用半透明灰；`Input` 为 `Field` + `2px inset #767676`；`TextArea`/`Select` 为 `Field` + `1px solid #767676`。Software/Skia/Vulkan 盒绘制消费计算样式。`none` 不自动清掉 UA 边框/背景，作者需覆盖。不支持 `button`/`checkbox` 等控件关键字、AppearanceBase、暗色 `light-dark()`。 |
| `accent-color`、`resize` | ⚪ | 未实现。 |
| `pointer-events`、`touch-action` | ⚪ | 未实现。 |

### 4.10 图片和 SVG

| 功能 | 状态 | 说明 |
|---|---|---|
| `Image` 控件内在尺寸 | 🟢 | 使用图片原始尺寸并按可用空间等比缩小。 |
| CSS width/height/aspect-ratio 约束 Image | 🟡 | 使用通用布局子集。 |
| `object-fit`、`object-position` | ⚪ | 未实现。 |
| CSS gradients / image-set / cross-fade | ⚪ | 未实现。 |
| SVG `fill`、`stroke`、`stroke-width` | 🟡 | Live SVG 元素支持有限颜色和数值。 |
| SVG `opacity`、`fill-opacity`、`stroke-opacity` | 🟢 SVG 子集 | 普通 UI 元素 opacity 不受支持。 |
| SVG transform 属性 | 🟡 | translate/scale/rotate/matrix；不是 CSS transform。 |

---

## 5. 单位和函数

### 5.1 通用布局长度

| 单位/值 | 状态 | 说明 |
|---|---|---|
| `px` | 🟢 | Square 逻辑布局单位，输出阶段按 DPI 缩放。 |
| `%` | 🟢 | 按相关父尺寸解析。 |
| `em` | 🟢 布局长度 | 相对当前字体尺寸；font-size 自身支持不完整。 |
| `rem` | 🟢 布局长度 | 相对根字体尺寸。 |
| `vw`、`vh` | 🟢 | 相对当前根布局视口。 |
| Unitless number | 🟡 | 被多个长度路径接受，与标准 CSS 非零 unitless length 不一致。 |
| `rp` | 🟢 Square 扩展 | 按父宽百分比解析。 |
| `auto` | 🟡 | 仅适用于明确处理 auto 的属性。 |
| `min-content`、`max-content`、`fit-content` | 🟡 Grid 子集 | 通用尺寸没有完整 intrinsic sizing。 |
| `fr` | 🟡 Grid 子集 | 仅自定义 Grid track 算法。 |
| `vmin/vmax`、sv*/lv*/dv* | ⚪ | 未实现。 |
| `ch/ex/cap/lh/rlh` | ⚪ | 未实现。 |
| 物理单位 `cm/mm/in/pt/pc/Q` | ⚪ | 未实现。 |
| Container query units | ⚪ | 未实现。 |

### 5.2 函数

| 函数 | 状态 |
|---|---|
| `var()` | 🟢 |
| `url()` | 🟡 仅 `@import` 和命令式字体源的有限路径 |
| `minmax()` | 🟡 Grid 简化 `minmax(px, fr)` |
| `rgb()` / `rgba()` | 🟡 仅 box-shadow 独立 parser |
| `calc()`、`min()`、`max()`、`clamp()` | ⚪ |
| gradient 函数 | ⚪ |
| `env()` | ⚪ |
| `attr()`、`counter()`、`counters()` | ⚪ |
| transform/filter/shape 函数 | ⚪ CSS 路径 |

---

## 6. At 规则

| At 规则 | 状态 | 说明 |
|---|---|---|
| `@import` | 🟡 | 本地文件、递归导入、相对路径、循环检测、最大 64 层；不支持 HTTP 和条件。 |
| `@keyframes` | 🟡 | from/to/单百分比 stop；仅数值轨道。 |
| `@charset` | ⚪ 语义 | 仅影响 parser 的 import 前缀容忍，不处理编码。 |
| `@media` | 🟡 | `all`、`screen`、`print` 的本地求值和运行时 media type switching；无完整 media features。 |
| `@supports` | ⚪ | 无 feature query。 |
| `@container` | ⚪ | 无容器查询。 |
| `@layer` | ⚪ 语义 | 分号形式只用于 import 排序容忍，无 layer cascade。 |
| `@font-face` | 🟡 | Engine 解析有效 family/src/weight/style descriptor，并可通过 `LoadFontsAsync` 加载 local file；remote/data 源和完整 source selection 未实现。 |
| `@namespace`、`@scope`、`@property` | ⚪ | 未实现。 |
| `@page`、`@counter-style`、`@starting-style`、`@view-transition` | ⚪ | 未实现。 |

### 6.1 `@import` 约束

```css
@import "base.css";
@import url("./themes/light.css");
```

- 必须出现在普通规则之前。
- 允许位于 `@charset` 和分号形式 `@layer` 声明之后。
- 相对路径基于当前 CSS 文件目录。
- 支持递归本地文件导入、循环检测和最大 64 层深度。
- 不支持 HTTP/HTTPS、media、`supports()`、`layer()` 条件。
- 内存 CSS 没有文件基址，不能解析相对导入。

---

## 7. Animation 子集

```css
@keyframes grow {
  from { width: 40px; }
  50% { width: 80px; }
  to { width: 120px; }
}

.item {
  animation: grow 300ms ease-in-out 0ms 2 alternate;
}
```

| 功能 | 状态 | 说明 |
|---|---|---|
| `@keyframes` 名称和 stop | 🟢 子集 | from、to、单百分比 selector。 |
| 数值属性插值 | 🟢 子集 | unitless 和 px 输入；动画结果序列化为 unitless 数字。 |
| `animation-name` | 🟢 | 单名称。 |
| `animation-duration`、`animation-delay` | 🟢 | ms、s、裸秒数。 |
| `animation-iteration-count` | 🟢 | 非负浮点或 infinite。 |
| `animation-direction` | 🟢 | normal、reverse、alternate、alternate-reverse。 |
| `animation-timing-function` | 🟡 | ease-in/out/in-out；其他运行时按 linear。 |
| `animation` shorthand | 🟡 | 单动画、空白 token 子集。 |
| 颜色、transform、shadow 插值 | ⚪ | 未实现。 |
| fill-mode、play-state、composition | ⚪ | 未实现。 |
| 多动画列表、事件、timeline/range | ⚪ | 未实现。 |

普通 UI 元素的 `opacity` 没有接入显示树 opacity layer，因此 `opacity` 动画只会改变样式值，不会产生可见淡入淡出。SVG opacity 是独立实现。

---

## 8. Font Loading 子集

| Web API | Square | 状态 |
|---|---|---|
| `FontFace` | `Square.Text.Fonts.FontFace` | 🟡 本地路径或字节 |
| `FontFaceSet` | `Square.Text.Fonts.FontFaceSet` | 🟡 |
| `document.fonts` | `Document.Fonts` | 🟢 表面 |
| `fonts.add/delete/clear/check/load/ready` | 对应方法 | 🟡 简化匹配语义 |
| `FontFaceSetLoadEvent` | 无 | ⚪ |
| `@font-face` | `CssEngine` descriptor parsing + local-file `LoadFontsAsync` | 🟡 子集 |
| 网络 WOFF/WOFF2 CSS 加载 | 无 | ⚪ |

```csharp
var face = new FontFace("AppBrand", @"C:\Fonts\Brand.ttf");
document.Fonts.Add(face);
await face.LoadAsync();
```

---

## 9. 当前会被保存但没有普通 UI 视觉效果的属性

以下属性可能成功解析、参与级联、触发 invalidation 或被动画修改，但当前普通 UI 元素没有对应视觉消费路径。

| 属性 | 实际状态 |
|---|---|
| `opacity` | ⚪ 普通元素不创建 opacity layer；SVG 单独支持。 |
| `visibility` | ⚪ 值可继承，但不会同步到 `Element.IsVisible`。 |
| `text-decoration` 及相关 longhand | 🟡 子集 | 绘制 underline/overline/line-through；不支持复杂装饰样式、波形、blink 和颜色 longhand。 |
| `font` | ⚪ 不展开 shorthand。 |
| `border` | ⚪ 不解析完整 shorthand。 |
| `border-style` | ⚪ 不绘制 style。 |
| `grid`、`grid-template` | ⚪ 不展开 shorthand。 |
| `transform` | ⚪ 不生成 TransformCommand。 |
| `transition-*` | ⚪ 无 transition engine。 |
| `filter`、`clip-path`、`mask-*` | ⚪ 无 CSS 消费路径。 |

---

## 10. 已知标准差异

- Square 默认 `box-sizing` 为 `border-box`，浏览器 CSS 初始值通常为 `content-box`。
- Square 八位十六进制颜色使用 `#AARRGGBB`，标准 CSS 使用 `#RRGGBBAA`。
- 普通控件颜色不支持 named colors、`rgb()`、`rgba()`；测试中 `Style.Get("color") == "red"` 只证明级联存储，不证明绘制。
- Unitless 非零数字会被多个布局长度路径接受，这比标准 CSS 宽松。
- `block` 是 Yoga column flex 或 CSS normal-flow 的近似；仅提供相邻 Block 兄弟的 margin-collapse、有限 inline/atomic inline layout，不提供完整 BFC 或 inline formatting model。
- `z-index` 是简单数值同级排序；fixed 元素进入 viewport fixed layer，但没有浏览器 stacking context。
- `:root` 表示无 Parent 的元素，组件样式作用域根也可能匹配。
- 属性选择器读取 `Element.Properties` 强类型属性袋，不是独立 DOM Attr 集合。
- `::before` / `::after` 作为实际内部 Text 子元素参与 Square 布局，不是完整 CSS generated box 模型。
- Grid 是自定义有限实现，不应描述为“Grid 全量”。
- `text-align: justify` 会被解析，但当前渲染行为等同 left。

---

## 11. 主要实现位置

| 范围 | 文件 |
|---|---|
| Tokenizer | `src/Square/CSS/Tokenizer/CssTokenizer.cs` |
| AST | `src/Square/CSS/Ast/CssAst.cs` |
| Parser | `src/Square/CSS/Engine/CssParser.cs` |
| Selector/Cascade/Variables/Pseudo-elements | `src/Square/CSS/Engine/CssEngine.cs` |
| Dynamic reconciliation | `src/Square/CSS/Engine/CssStyleReconciler.cs` |
| Animation | `src/Square/CSS/Engine/CssAnimationTimeline.cs` |
| `@import` | `src/Square/CSS/Engine/DocumentStyleSheetLoader.cs` |
| CSSOM declaration surface | `src/Square/UI/ElementApi/StyleAccessor.cs` |
| Flex/Grid/Box/Positioning | `src/Square/Rendering/Layout/LayoutEngine.cs` |
| Overflow/scroll/hit testing | `src/Square/UI/Element/Element.cs` |
| Control painting/text properties | `src/Square/Controls/View.cs`、`Text.cs`、`Button.cs`、`ListItem.cs`、`Select.cs`、`Image.cs`、`Splitter.cs`、`Dialog.cs`、`ControlDrawing.cs`（原 `Controls.cs` 已按控件拆分） |
| Text editor selection/caret | `src/Square/Controls/TextEditors.cs` |
| Box shadow | `src/Square/Graphics/Primitives/BoxShadow.cs` |
| Font parsing/loading | `src/Square/Graphics/Primitives/Font.cs`、`src/Square/Text/Fonts` |
| Live SVG CSS subset | `src/Square/UI/Svg/SVGElements.cs` |
| Chromium comparison tool | `tools/Square.FontComparison/BrowserCapture.cs`、`tools/Square.FontComparison/SquareCapture.cs`、`tools/Square.FontComparison/ComparisonEngine.cs` |

主要测试：

- `tests/Square.CSS.Tests/CssTests.cs`
- `tests/Square.CSS.Tests/PseudoClassTests.cs`
- `tests/Square.CSS.Tests/ChromeConformanceTests.cs`
- `tests/Square.UI.Tests/GridLayoutTests.cs`
- `tests/Square.UI.Tests/FlexSizingTests.cs`
- `tests/Square.UI.Tests/StyleAndFontTests.cs`
- `tests/Square.Backends.Tests/SoftwareRendererTests.cs`
- `tools/Square.FontComparison/Cases/FontComparisonCases.json`
- `tools/Square.FontComparison/ComparisonEngine.cs`：Chromium/Square 几何、字符位置和像素差异比较；容器 fixture 额外比较相对 `X/Y`。

---

## 12. 兼容目标

Square 的 CSS 目标是为桌面 UI 提供熟悉、可预测且可逐步扩展的 CSS 子集，而不是立即复制完整浏览器引擎。新增支持应满足以下条件：

- Parser 能稳定接受并保留必要语法。
- Cascade、specificity、inheritance 和 dynamic reconciliation 行为明确。
- 属性必须接入布局、绘制、交互或公开 API，不能只保存在字符串字典中。
- 新功能需明确值域、标准差异和不支持范围。
- 新功能应有对应单元测试；视觉功能还应覆盖至少一个实际渲染后端或显示树测试。
