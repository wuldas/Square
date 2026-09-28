# HTML 元素类型与可配置默认命名空间

> 状态：**已实现**。本文描述 `Square.Html` 类型家族与 URI 优先模板解析的当前行为；[HTML5-Elements.md](HTML5-Elements.md) 记录 113 标签的已支持与禁用行为矩阵。两者冲突时以行为矩阵为准。

## 目标与边界

- 按 [WHATWG 2026-09-22 符合规范元素索引](https://html.spec.whatwg.org/multipage/indices.html#elements-3) 冻结 **113 个 HTML 标签**，每个标签恰有一个可引用的具体 CLR 类型；不计 SVG、MathML、过时 `param` 或第三方元素。标签与类型映射见末尾全量表。
- HTML 类型不继承 Square 的 `UIElement`：`Square.UI.Element` 是布局、树、事件和绘制的共同根；`Square.Html.HTMLElement : Square.UI.Element`，`Square.UI.UIElement` 与 `Square.UI.Svg.SVGElement` 是并列家族。独立的是继承关系和属性/焦点语义，不是第二棵树或浏览器引擎。
- “主动内容禁用”边界不变：模板脚本执行、媒体解码、嵌入文档/插件、Canvas 2D/WebGL 和桌面网络表单提交没有实现，只保留明确诊断或文档化降级。类型名不构成浏览器兼容承诺；安全 URL、HTML 编码与 framework-owned Web 桥接策略不放松。
- 旧 API 已删除，不保留别名：`Square.UI.Html.HtmlElement(string)` 通用字符串构造、`HtmlTextRun : Square.Controls.Text`、旧 `Square.UI.HTMLElement`、`ElementNamespaceOrderAttribute`、`ElementNamespaceAliasAttribute`、四参数 `ElementExport` 与 `local:` 前缀捷径。

## 类型模型

```text
Square.UI.Element
├── Square.Html.HTMLElement                 // XHTML 命名空间，HTML Attr API；不继承 UIElement
│   ├── Square.Html.HTMLButtonElement       // button 的具体类型
│   ├── Square.Html.HTMLInputElement        // input 的具体类型
│   ├── Square.Html.HTMLHeadingElement      // 共享抽象基类
│   │   ├── Square.Html.HTMLH1Element       // h1 的具体类型
│   │   └── Square.Html.HTMLH2Element       // h2；h3…h6 同理
│   └── …                                   // 其余 concrete 类型见映射表
├── Square.UI.UIElement                     // Square 控件，原有 API 保留
└── Square.UI.Svg.SVGElement                // SVG 原有类型
```

- **具体类一标签一型**，`TagName`/`LocalName` 固定为小写、`NamespaceURI` 固定为 XHTML。编译产物直接 `new global::Square.Html.HTMLButtonElement()`，不经字符串构造；不存在“任意标签共用 HtmlElement(string)”的公开入口。
- WHATWG 的 DOM Interface 列**并非一标签一接口**。同一接口服务多个标签时，Square 以它作为抽象共享基类，再加每标签具体类：`HTMLHeadingElement`→`HTMLH1Element`…`HTMLH6Element`、`HTMLQuoteElement`→`HTMLBlockquoteElement`/`HTMLQElement`、`HTMLTableColElement`→`HTMLColElement`/`HTMLColgroupElement`、`HTMLModElement`→`HTMLDelElement`/`HTMLInsElement`、`HTMLTableSectionElement`→`HTMLTheadElement`/`HTMLTbodyElement`/`HTMLTfootElement`、`HTMLTableCellElement`→`HTMLTdElement`/`HTMLThElement`。WHATWG 仅列 HTMLElement 的标签也有 Square 特有的具体类型；`HTMLArticleElement` 不是标准浏览器接口。
- 属性 API 由 `HTMLElement` 和共享语义基类实现；有标准专属属性的类暴露强类型薄属性（如 `HTMLInputElement.Value/Checked/Type`、`HTMLAnchorElement.Href`、`HTMLSelectElement.Value`、`HTMLOptionElement.Selected`、`HTMLDetailsElement.Open`、`HTMLImageElement.Src/Alt`、`HTMLTableCellElement.ColSpan/RowSpan`），属性与 Attr 存储只有一个事实来源。布尔用存在性表示，数值按 invariant 解析/格式，缺席与无效值保留可空的降级语义。普通无差异标签的具体类负责**类型身份**，不复制 113 套布局/绘制代码。
- HTML 混合文本是真正的 DOM Text：`Square.UI.Text : CharacterData : Node` 子节点挂在 HTML 父元素的 `ChildNodes`，`Children` 不含文本控件。生成器对已解析 HTML 父节点发射 `parent.ChildNodes.Add(new global::Square.UI.Text(...))`；响应式文本经 `Square.Html.HtmlTextBinding.Bind`。布局按原位消费 Text 片段（记录源节点与 UTF-16 偏移），桌面选区直接构造指向源 Text 节点的 Range；空文本留在 DOM 但无可见片段。
- 表单/图片宿主的内部 Square 控件是 HTMLElement 私有**视觉 sidecar**：负责测量、绘制与输入，但对 `ChildNodes`、`Children`、DOM 查询和 Web 导出不可见；输入/选择按监听回写 HTML host。焦点、disabled、title/tooltip、`checked`/`open` 状态与事件由 HTMLElement 自身持有，不借 UIElement 继承。
- 第三方扩展 HTML 类可以继承 `HTMLElement` 或允许扩展的具体类（如 `HTMLButtonElement`）。具体类公开无参构造且不 sealed；任意标签字符串构造不公开，构造与生命周期受注册定义约束（见下节）。

## 命名空间、配置与解析

三个内置解析 URI：HTML = `http://www.w3.org/1999/xhtml`；Square UI = `urn:square:ui`；SVG = `http://www.w3.org/2000/svg`。第三方组件由包 URI（例如 `urn:acme:widgets`）导出。配置存 **URI**，不是 Html/Ui/Extension 枚举；URI/局部名是模板解析键。

项目属性（已实现，加入 `CompilerVisibleProperty`，与 `RootNamespace` 独立）：

```xml
<PropertyGroup>
  <SquareDefaultElementNamespace>http://www.w3.org/1999/xhtml</SquareDefaultElementNamespace>
</PropertyGroup>
```

默认值按以下顺序选择：模板根 `<template xmlns="URI">` → 项目 `SquareDefaultElementNamespace` → HTML URI。每个模板冻结一个**优先命中的默认 URI**；其他家族的名称在候选唯一时也可省前缀，碰撞时才需显式指定。不支持子树中途重定义默认 `xmlns`（报 `SQXE001`）。根 `<template>` 上的 `xmlns`/`xmlns:prefix` 只在编译期消费，不导出为 HTML 属性。

| 项目属性值 / 模板 `xmlns` | 同名候选的默认选择 | 默认空间未命中时 |
| --- | --- | --- |
| `http://www.w3.org/1999/xhtml` | `<button>`（含 `<Button>`）→ `HTMLButtonElement` | `<View>` 只有 UI 候选、`<element>` 只有一个包导出时均可省前缀 |
| `urn:square:ui` | `<button>`/`<Button>` → Square `Button` | `<article>` 只有 HTML 候选时可省前缀 |
| `urn:acme:widgets` | `<element>` → 该包导出的具体类型 | `<button>` 同时命中 HTML/UI 且包内无此名时有歧义，必须写 `<html:button>` 或 `<ui:button>` |

```xml
<template xmlns="http://www.w3.org/1999/xhtml"
          xmlns:ui="urn:square:ui"
          xmlns:svg="http://www.w3.org/2000/svg"
          xmlns:my="urn:acme:widgets">
  <button>HTML；同名 Square 控件须显式写 ui:button</button>
  <ui:Button>Square</ui:Button>
  <View /> <!-- 仅 Square 有此名称，可省前缀 -->
  <my:element /> <!-- 若只有该包导出 element，也可写 element -->
  <svg><circle r="8" /></svg>
</template>
```

若默认 URI 为 `urn:square:ui`，无前缀 `<button>`（含 `<Button>`）是 Square Button；选同名 HTML 按钮才写 `<html:button>`，并声明 `xmlns:html`。若默认 URI 为 `urn:acme:widgets`，`<element>` 优先查该包；包内没有 `<button>` 而 HTML/UI 均有时必须加前缀。HTML 名按 ASCII 大小写不敏感归一到小写；Square 维持现有控件大小写别名；第三方局部名按导出契约严格 ordinal 匹配。

解析规则：先读模板声明与项目默认 URI。有前缀时**只**查绑定 URI。无前缀时先查默认 URI：命中唯一描述符即选定（即使其他空间同名）；未命中则在可见的内置目录、当前/引用组件及包导出中查找，**唯一候选自动解析**，零候选报 `SQXE003`，多个候选报 `SQXE004` 并要求前缀。默认空间内重复导出报 `SQXE005`，不因其他候选掩盖。前缀只在模板根声明后可用；`html:`/`ui:`/`svg:` 是保留含义的可声明前缀，不得重绑定到其他 URI；空值/非绝对 URI/重复绑定/未知前缀报 `SQXE001`/`SQXE002`。不存在程序集 prefix 隐式注入，也没有 order/alias 覆盖歧义。

顶层 `<template>` 是 SQX/SQV 文件的模板分区；模板内部嵌套 `<template>/<script>/<style>` 遵循上述默认优先、唯一候选规则：HTML 默认时分别选 `HTMLTemplateElement`、受禁用的 `HTMLScriptElement`、受禁用的 `HTMLStyleElement`。Square 片段包装改用独有的 `<Fragment>`（或 `<ui:Fragment>`）。`Show`/`For`/`Switch`/`Match`/`Index`/`Slot`/`Outlet`/`Fragment` 等结构原语归 UI 空间；HTML 默认下 `<slot>` 是 `HTMLSlotElement`，Square 插槽写 `<ui:Slot>`；`v-if`/`v-for`/`template #slot` 等方言指令按原规则。

`<svg>` 在只有 SVG 候选时无需前缀；选中 SVG 根后其后代进入 SVG 空间。若另一空间也导出同名元素，由默认 URI 或显式 `<svg:svg>` 决定选择，不按子标签名猜。

## 扩展元素：借鉴 WHATWG Custom Elements，而非混同两种语法

WHATWG 区分[自主自定义元素](https://html.spec.whatwg.org/multipage/custom-elements.html#autonomous-custom-element)（如 `<acme-badge>`）和[定制内建元素](https://html.spec.whatwg.org/multipage/custom-elements.html#customized-built-in-element)（如 `<button is="acme-button">`）。合法浏览器自定义元素名称需小写、包含连字符等；`<my:element>` **不是**浏览器 Custom Elements 的名称。

- `<my:element>` 是 Square **模板包解析**：`xmlns:my` 的包 URI + local name 找到引用程序集的公开 `Element` 导出，声明为三参数 `[assembly: ElementExport("urn:acme:widgets", "element", typeof(AcmeBadgeElement))]`。包自报前缀的旧机制已删除；该包若是同名唯一候选，`<element>` 也能直接解析，只有碰撞时才要求前缀。
- 包 URI 是模板解析键，**不等于运行时 DOM NamespaceURI**：包可导出 HTMLElement 派生类（DOM `NamespaceURI` 仍为 XHTML）或 Square UI 类型。不能把 `urn:acme:widgets` 写成浏览器 XHTML 元素的 NamespaceURI。
- 装配级自定义元素定义：`[assembly: HtmlCustomElementExport("acme-badge", typeof(AcmeBadge), ObservedAttributes = new[] { "status" })]`；定制内建加 `ExtendsTag = "button"`，要求类型继承对应具体类。模板以此识别无前缀自主元素与 `<button is="acme-button">`；`is` 在创建后改变不重新升级。程序化路径 `UIDocument.CustomElements.DefineAutonomous<T>(name, factory, observedAttributes)` / `DefineCustomizedBuiltIn<T>(name, extendsTag, factory, observedAttributes)` 登记同一套定义与校验（合法名称、重复定义、`extendsTag` 为 113 内建标签、实际继承对应具体类）。
- `HTMLElement` 提供受保护 `ConnectedCallback()`、`DisconnectedCallback()`、`AttributeChangedCallback(string name, string? oldValue, string? newValue)`：定义初始化仅一次；只对注册 observed 属性的实际 old→new 变化通知（含 `id`/`class`/内联 style，不把一般 PropertyStore 项伪装成属性）；顺序为属性初始化→connected，移除时子先于父 disconnected，移除后重挂重新触发断开/连接。回调异常记录到 `UIDocument.CustomElements.Diagnostics`（含源位置）。ElementInternals、Shadow DOM、表单关联不提供。
- 无前缀 `<acme-badge>` 遵循相同候选规则：可命中已注册的自主 HTML 扩展或唯一的包导出；后者不因此变成浏览器 Custom Element。未知标签不能静默降级为普通 `HTMLElement`。
- Web 导出**只输出安全静态表示**：包导出与已定义自定义元素必须实现 `Square.Html.IHtmlStaticRepresentation`，返回新的分离 HTML 子树，再经 exporter 既有的编码与 URL/脚本策略归一化输出；无表示、表示抛异常或递归时报 `HtmlExportDiagnostic`，不输出伪可用占位。本轮不向浏览器输出 `customElements.define` 或可升级的 `<acme-badge>`/`is` 注册脚本——那需要单独授权的受信客户端注册机制，当前安全边界不允许。
- 仓库现有 Square.Extensions.WebView 是操作系统 WebView 包装，与本机制无关。

## 运行时与输出边界

- `new UIDocument(defaultElementNamespaceUri = "http://www.w3.org/1999/xhtml")` 保存不可变文档默认 URI；构造时确保 `ControlRegistration.RegisterDefaults()` 执行一次。内置 UI/SVG 与 113 个 HTML 标签经 `ElementRegistry.Register(namespaceUri, localName, factory)` 显式 AOT 工厂登记；同 URI+local 冲突拒绝，重注册同一工厂幂等；无可变进程级默认。
- `CreateElement(string localName, string? isName = null)` 按模板相同的默认优先/唯一候选规则创建；`CreateElementNS(namespaceUri, localName)` 只处理 XHTML/SVG 真 DOM 命名空间；`CreateComponentElement(namespaceUri, localName)` 处理 UI/包解析 URI。程序化 QName（`prefix:local`）一律拒绝；未知名称报文档默认 URI，歧义名称列出候选 URI。生成代码与两条运行时路径落到相同具体类型。
- Element/Node 保留统一子树、CSS、事件、LayoutEngine 与 DisplayTree 管线；HTML 元素与 DOM Text 独立参与原有管线，旧控件和 SVG 的渲染/事件路径不被 HTML UA 规则覆盖。
- Web 输出按运行时**具体类型及真实 DOM namespace**分派：HTML 为安全原生小写标签，文本按 `ChildNodes` 原序编码为裸文本；SVG 为 SVG；Square UI 控件按既有映射；模板包前缀不出现在响应标签中。导出拒绝 `on*`、`srcdoc`、畸形属性名、不安全 URL/`srcset`，并额外拒绝提供者写入的保留 `data-square-*` 属性与所有 `is` 属性（本轮不提供可信客户端注册）。
- 113 种具体类型是类型身份与属性覆盖的范围，**不是** 113 套独立 painter 或完整浏览器 API；共享布局、替代绘制与语义基类实现，禁用行为保留诊断。

## 与旧实现的差异（已完成迁移）

| 旧实现 | 现实现 |
| --- | --- |
| `HTMLElement : UIElement`；`HtmlElement(string)` 表示 113 标签；HTML 文字 `HtmlTextRun : Controls.Text` | `HTMLElement : Element`；113 个具体 HTML 类型；`Square.UI.Text` DOM 子节点 + 视觉 sidecar |
| 无前缀精确小写 HTML 优先，其余大小写回退 Square | 无前缀先查默认 URI，再接受唯一候选；HTML ASCII 大小写不敏感；XHTML 默认下 `<Button>` 是 HTML button，Square 控件走 `ui:` 或项目默认 `urn:square:ui` |
| 组件文件无 `xmlns` 语法；包 prefix 由程序集隐式注入；四参数 `ElementExport` | 根 `xmlns`/`xmlns:prefix` 声明 + 项目 `SquareDefaultElementNamespace`；三参数 `ElementExport(namespaceUri, localName, type)` |
| 无前缀嵌套 `<template>` 是片段包装 | 嵌套 `<template>` 在 HTML 默认下是 `HTMLTemplateElement`；片段用 `<Fragment>`/`<ui:Fragment>` |
| `ElementNamespaceOrder`/`ElementNamespaceAlias` 与各处大小写/前缀特例 | 同一 URI+local 解析贯穿语法树、分析器、LSP、生成器与运行时工厂；冲突一律转诊断 |

## 验证

- 113 行逐项核对：每个标签 `UIDocument.CreateElement` 与 `.sqx`/`.sqv` 两方言编译结果均为表中唯一具体类型，`LocalName` 固定、XHTML URI、不是 `UIElement`；六种共享接口继承链与表一致。
- 各默认 URI（HTML、UI、包）下的 `.sqx`/`.sqv` 解析 fixture：同名元素由默认空间确定，唯一候选省前缀，歧义报 `SQXE004`，显式前缀恒定命中声明 URI；未知/重绑/重复导出有准确诊断。
- DOM Text 场景：`<p>Hello <strong>世界</strong> !<br>next</p>` 的 `ChildNodes` 顺序为 Text/strong/Text/br/Text，桌面选区 Range 指向源 Text 节点，Web 编码输出一致。
- `samples/Square.Sample.WebServer` 的 `/html-elements` 用真实 Chromium 检查 DOM 顺序、namespace、void 元素与自定义元素静态表示；同一项目 `--desktop --html-elements` 加载相同组件并可用 `--screenshot` 截图对照。`Square.Sample --html-regression` 是独立的较完整布局回归页，不作文本同一性比较。

## 113 标签 → Square.Html 具体类型 → WHATWG DOM 接口

第三列来自冻结的 WHATWG 索引；第二列是已实现的 Square 类型。对共享 WHATWG 接口，具体类型继承第三列命名的抽象共享基类；第三列为 HTMLElement 时直接继承 `Square.Html.HTMLElement`。

| 标签 | Square 具体类（Square.Html） | WHATWG DOM 接口 | 当前矩阵分组 |
| --- | --- | --- | --- |
| `a` | `HTMLAnchorElement` | `HTMLAnchorElement` | Phrasing |
| `abbr` | `HTMLAbbrElement` | `HTMLElement` | Phrasing |
| `address` | `HTMLAddressElement` | `HTMLElement` | Sections |
| `area` | `HTMLAreaElement` | `HTMLAreaElement` | Media |
| `article` | `HTMLArticleElement` | `HTMLElement` | Sections |
| `aside` | `HTMLAsideElement` | `HTMLElement` | Sections |
| `audio` | `HTMLAudioElement` | `HTMLAudioElement` | Media |
| `b` | `HTMLBElement` | `HTMLElement` | Phrasing |
| `base` | `HTMLBaseElement` | `HTMLBaseElement` | Document |
| `bdi` | `HTMLBdiElement` | `HTMLElement` | Phrasing |
| `bdo` | `HTMLBdoElement` | `HTMLElement` | Phrasing |
| `blockquote` | `HTMLBlockquoteElement` | `HTMLQuoteElement` | Grouping |
| `body` | `HTMLBodyElement` | `HTMLBodyElement` | Document |
| `br` | `HTMLBRElement` | `HTMLBRElement` | Phrasing |
| `button` | `HTMLButtonElement` | `HTMLButtonElement` | Forms |
| `canvas` | `HTMLCanvasElement` | `HTMLCanvasElement` | Media |
| `caption` | `HTMLTableCaptionElement` | `HTMLTableCaptionElement` | Table |
| `cite` | `HTMLCiteElement` | `HTMLElement` | Phrasing |
| `code` | `HTMLCodeElement` | `HTMLElement` | Phrasing |
| `col` | `HTMLColElement` | `HTMLTableColElement` | Table |
| `colgroup` | `HTMLColgroupElement` | `HTMLTableColElement` | Table |
| `data` | `HTMLDataElement` | `HTMLDataElement` | Phrasing |
| `datalist` | `HTMLDataListElement` | `HTMLDataListElement` | Forms |
| `dd` | `HTMLDdElement` | `HTMLElement` | Grouping |
| `del` | `HTMLDelElement` | `HTMLModElement` | Phrasing |
| `details` | `HTMLDetailsElement` | `HTMLDetailsElement` | Forms |
| `dfn` | `HTMLDfnElement` | `HTMLElement` | Phrasing |
| `dialog` | `HTMLDialogElement` | `HTMLDialogElement` | Forms |
| `div` | `HTMLDivElement` | `HTMLDivElement` | Grouping |
| `dl` | `HTMLDListElement` | `HTMLDListElement` | Grouping |
| `dt` | `HTMLDtElement` | `HTMLElement` | Grouping |
| `em` | `HTMLEmElement` | `HTMLElement` | Phrasing |
| `embed` | `HTMLEmbedElement` | `HTMLEmbedElement` | Media |
| `fieldset` | `HTMLFieldSetElement` | `HTMLFieldSetElement` | Forms |
| `figcaption` | `HTMLFigcaptionElement` | `HTMLElement` | Grouping |
| `figure` | `HTMLFigureElement` | `HTMLElement` | Grouping |
| `footer` | `HTMLFooterElement` | `HTMLElement` | Sections |
| `form` | `HTMLFormElement` | `HTMLFormElement` | Forms |
| `h1` | `HTMLH1Element` | `HTMLHeadingElement` | Sections |
| `h2` | `HTMLH2Element` | `HTMLHeadingElement` | Sections |
| `h3` | `HTMLH3Element` | `HTMLHeadingElement` | Sections |
| `h4` | `HTMLH4Element` | `HTMLHeadingElement` | Sections |
| `h5` | `HTMLH5Element` | `HTMLHeadingElement` | Sections |
| `h6` | `HTMLH6Element` | `HTMLHeadingElement` | Sections |
| `head` | `HTMLHeadElement` | `HTMLHeadElement` | Document |
| `header` | `HTMLHeaderElement` | `HTMLElement` | Sections |
| `hgroup` | `HTMLHgroupElement` | `HTMLElement` | Sections |
| `hr` | `HTMLHRElement` | `HTMLHRElement` | Grouping |
| `html` | `HTMLHtmlElement` | `HTMLHtmlElement` | Document |
| `i` | `HTMLIElement` | `HTMLElement` | Phrasing |
| `iframe` | `HTMLIFrameElement` | `HTMLIFrameElement` | Media |
| `img` | `HTMLImageElement` | `HTMLImageElement` | Media |
| `input` | `HTMLInputElement` | `HTMLInputElement` | Forms |
| `ins` | `HTMLInsElement` | `HTMLModElement` | Phrasing |
| `kbd` | `HTMLKbdElement` | `HTMLElement` | Phrasing |
| `label` | `HTMLLabelElement` | `HTMLLabelElement` | Forms |
| `legend` | `HTMLLegendElement` | `HTMLLegendElement` | Forms |
| `li` | `HTMLLIElement` | `HTMLLIElement` | Grouping |
| `link` | `HTMLLinkElement` | `HTMLLinkElement` | Document |
| `main` | `HTMLMainElement` | `HTMLElement` | Sections |
| `map` | `HTMLMapElement` | `HTMLMapElement` | Media |
| `mark` | `HTMLMarkElement` | `HTMLElement` | Phrasing |
| `menu` | `HTMLMenuElement` | `HTMLMenuElement` | Grouping |
| `meta` | `HTMLMetaElement` | `HTMLMetaElement` | Document |
| `meter` | `HTMLMeterElement` | `HTMLMeterElement` | Forms |
| `nav` | `HTMLNavElement` | `HTMLElement` | Sections |
| `noscript` | `HTMLNoscriptElement` | `HTMLElement` | Document |
| `object` | `HTMLObjectElement` | `HTMLObjectElement` | Media |
| `ol` | `HTMLOListElement` | `HTMLOListElement` | Grouping |
| `optgroup` | `HTMLOptGroupElement` | `HTMLOptGroupElement` | Forms |
| `option` | `HTMLOptionElement` | `HTMLOptionElement` | Forms |
| `output` | `HTMLOutputElement` | `HTMLOutputElement` | Forms |
| `p` | `HTMLParagraphElement` | `HTMLParagraphElement` | Grouping |
| `picture` | `HTMLPictureElement` | `HTMLPictureElement` | Media |
| `pre` | `HTMLPreElement` | `HTMLPreElement` | Grouping |
| `progress` | `HTMLProgressElement` | `HTMLProgressElement` | Forms |
| `q` | `HTMLQElement` | `HTMLQuoteElement` | Phrasing |
| `rp` | `HTMLRpElement` | `HTMLElement` | Phrasing |
| `rt` | `HTMLRtElement` | `HTMLElement` | Phrasing |
| `ruby` | `HTMLRubyElement` | `HTMLElement` | Phrasing |
| `s` | `HTMLSElement` | `HTMLElement` | Phrasing |
| `samp` | `HTMLSampElement` | `HTMLElement` | Phrasing |
| `script` | `HTMLScriptElement` | `HTMLScriptElement` | Document |
| `search` | `HTMLSearchElement` | `HTMLElement` | Sections |
| `section` | `HTMLSectionElement` | `HTMLElement` | Sections |
| `select` | `HTMLSelectElement` | `HTMLSelectElement` | Forms |
| `selectedcontent` | `HTMLSelectedContentElement` | `HTMLSelectedContentElement` | Forms |
| `slot` | `HTMLSlotElement` | `HTMLSlotElement` | Forms |
| `small` | `HTMLSmallElement` | `HTMLElement` | Phrasing |
| `source` | `HTMLSourceElement` | `HTMLSourceElement` | Media |
| `span` | `HTMLSpanElement` | `HTMLSpanElement` | Phrasing |
| `strong` | `HTMLStrongElement` | `HTMLElement` | Phrasing |
| `style` | `HTMLStyleElement` | `HTMLStyleElement` | Document |
| `sub` | `HTMLSubElement` | `HTMLElement` | Phrasing |
| `summary` | `HTMLSummaryElement` | `HTMLElement` | Forms |
| `sup` | `HTMLSupElement` | `HTMLElement` | Phrasing |
| `table` | `HTMLTableElement` | `HTMLTableElement` | Table |
| `tbody` | `HTMLTbodyElement` | `HTMLTableSectionElement` | Table |
| `td` | `HTMLTdElement` | `HTMLTableCellElement` | Table |
| `template` | `HTMLTemplateElement` | `HTMLTemplateElement` | Document |
| `textarea` | `HTMLTextAreaElement` | `HTMLTextAreaElement` | Forms |
| `tfoot` | `HTMLTfootElement` | `HTMLTableSectionElement` | Table |
| `th` | `HTMLThElement` | `HTMLTableCellElement` | Table |
| `thead` | `HTMLTheadElement` | `HTMLTableSectionElement` | Table |
| `time` | `HTMLTimeElement` | `HTMLTimeElement` | Phrasing |
| `title` | `HTMLTitleElement` | `HTMLTitleElement` | Document |
| `tr` | `HTMLTableRowElement` | `HTMLTableRowElement` | Table |
| `track` | `HTMLTrackElement` | `HTMLTrackElement` | Media |
| `u` | `HTMLUElement` | `HTMLElement` | Phrasing |
| `ul` | `HTMLUListElement` | `HTMLUListElement` | Grouping |
| `var` | `HTMLVarElement` | `HTMLElement` | Phrasing |
| `video` | `HTMLVideoElement` | `HTMLVideoElement` | Media |
| `wbr` | `HTMLWbrElement` | `HTMLElement` | Phrasing |
