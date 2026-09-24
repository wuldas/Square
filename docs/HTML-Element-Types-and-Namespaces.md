# HTML 元素类型与可配置默认命名空间（拟议）

> 状态：设计文档，**尚未实现**。本文件规定目标契约；[HTML5-Elements.md](HTML5-Elements.md) 记录现有实现与其 113 标签行为矩阵。两者冲突时，不能把本文件描述为当前行为。未获实施指令前不修改运行时、编译器、样例或测试。

## 目标与边界

- 按 [WHATWG 2026-09-22 符合规范元素索引](https://html.spec.whatwg.org/multipage/indices.html#elements-3) 冻结 **113 个 HTML 标签**，每个标签有唯一可引用的具体 CLR 类型；不把 SVG、MathML、过时 param 或第三方元素计入 113。标签与类型映射见末尾全量表。
- HTML 类型不继承 Square 的 UIElement：Square.UI.Element 是布局、树、事件和绘制的共同根；拟议 Square.Html.HTMLElement : Square.UI.Element，Square.UI.UIElement 与 Square.UI.Svg.SVGElement 是并列家族。独立的是继承关系和属性/焦点语义，不是再造第二棵树或浏览器引擎。
- 保留现有“主动内容禁用”边界：模板脚本执行、媒体解码、嵌入文档/插件、Canvas 2D/WebGL 和桌面网络表单提交不因增加类型而获得实现；必须有明确诊断或文档化降级，不能仅凭类型名宣称浏览器兼容。现有安全 URL、HTML 编码和 Web framework-owned 桥接策略不放松。
- 这是一项破坏性类型与模板语义迁移；目标不是在现有单一 HtmlElement 上增加 113 个别名，也不是恢复 Square.Extensions.Html 的自研浏览内核。

## 类型模型

```text
Square.UI.Element
├── Square.Html.HTMLElement                 // XHTML 命名空间，HTML Attr API；不继承 UIElement
│   ├── Square.Html.HTMLButtonElement       // button 的具体类型
│   ├── Square.Html.HTMLInputElement        // input 的具体类型
│   ├── Square.Html.HTMLHeadingElement      // 共享的抽象 DOM 接口层
│   │   ├── Square.Html.HTMLH1Element       // h1 的具体类型
│   │   └── Square.Html.HTMLH2Element       // h2；h3…h6 同理
│   └── …                                   // 其余 concrete 类型见映射表
├── Square.UI.UIElement                     // Square 控件，原有 API 保留
└── Square.UI.Svg.SVGElement                // SVG 原有类型
```

- **具体类一标签一型**，TagName/LocalName 固定；例如 article 是 HTMLArticleElement、button 是 HTMLButtonElement。TagName 不再由传入字符串决定，编译器直接构造解析出的具体类型。不得保留“任意标签共用 HtmlElement(string)”作为公开替代入口。
- WHATWG 的 DOM Interface 列**并非一标签一接口**。同一接口服务多个标签时，Square 以它作为抽象共享基类，再加每标签具体类：HTMLHeadingElement→HTMLH1Element…HTMLH6Element、HTMLQuoteElement→HTMLBlockquoteElement/HTMLQElement、HTMLTableCellElement→HTMLTdElement/HTMLThElement 等。WHATWG 仅列 HTMLElement 的标签也有 Square 特有的具体类型；不得把 HTMLArticleElement 冒称为标准浏览器接口。
- 属性 API 由 HTMLElement 和共享语义基类实现；有标准专属属性的类暴露强类型属性（例如 HTMLInputElement.Value/Checked、HTMLAnchorElement.Href），属性与 Attr 存储只有一个事实来源。普通无差异标签的具体类负责**类型身份**，不复制 113 套布局/绘制代码。
- HTML 文本仍是 DOM Text 节点（Node），不是 UIElement 控件；当前 HtmlTextRun : Square.Controls.Text 和 HTML 元素内部的 Square 控件代理不能作为最终 HTML 语义子节点。共用测量/绘制算法可以复用，但焦点、尺寸、disabled、事件和属性由 HTML 自己的契约处理。
- 第三方扩展 HTML 类可以继承 HTMLElement 或允许扩展的 HTMLButtonElement 等具体类。公共类不能一律 sealed；构造与生命周期受注册定义约束。

## 命名空间、配置与解析

三个内置解析 URI：HTML = http://www.w3.org/1999/xhtml；Square UI = urn:square:ui；SVG = http://www.w3.org/2000/svg。第三方组件由包 URI（例如 urn:acme:widgets）导出。配置存 **URI**，而不是 Html/Ui/Extension 枚举；URI/局部名是模板解析键。

拟议项目属性（**不是当前已存在的 MSBuild 属性**）：

```xml
<PropertyGroup>
  <SquareDefaultElementNamespace>http://www.w3.org/1999/xhtml</SquareDefaultElementNamespace>
</PropertyGroup>
```

默认值按以下顺序选择：模板根 `<template xmlns="URI">` → 项目 `SquareDefaultElementNamespace` → HTML URI。每个模板选择一个**优先命中的默认 URI**；其他家族的名称在候选唯一时也可省前缀，碰撞时才需显式指定。本轮设计不引入子树中途重定义默认 `xmlns`，避免解析范围歧义。

| 项目属性值 / 模板 `xmlns` | 同名候选的默认选择 | 默认空间未命中时 |
| --- | --- | --- |
| `http://www.w3.org/1999/xhtml` | `<button>` → `HTMLButtonElement` | `<View>` 只有 UI 候选、`<element>` 只有一个包导出时均可省前缀 |
| `urn:square:ui` | `<button>` → Square `Button` | `<article>` 只有 HTML 候选时可省前缀 |
| `urn:acme:widgets` | `<element>` → 该包导出的具体类型 | `<button>` 同时命中 HTML/UI 且包内无此名时有歧义，必须写 `<html:button>` 或 `<ui:button>` |

```xml
<template xmlns="http://www.w3.org/1999/xhtml"
          xmlns:ui="urn:square:ui"
          xmlns:svg="http://www.w3.org/2000/svg"
          xmlns:my="urn:acme:widgets">
  <button>HTML；同名 Square 控件须显式写 ui:button</button>
  <ui:button>Square</ui:button>
  <View /> <!-- 仅 Square 有此名称，可省前缀 -->
  <my:element /> <!-- 若只有该包导出 element，也可写 element -->
  <svg><circle r="8" /></svg>
</template>
```

若默认 URI 改为 `urn:square:ui`，则无前缀 `<button>`（也包括 `<Button>`）是 Square Button；要选同名 HTML 按钮才写 `<html:button>`，并声明 `xmlns:html`。若默认 URI 为 `urn:acme:widgets`，则 `<element>` 优先查该包；若包内没有 `<button>` 而 HTML/UI 均有，就必须加前缀。HTML 名按 ASCII 大小写不敏感归一到小写；Square 维持现有控件大小写别名；第三方局部名按导出契约严格匹配。

解析规则：先读模板声明与项目默认 URI。有前缀时**只**查绑定 URI。无前缀时先查默认 URI：命中唯一描述符即选定（即使其他空间同名）；未命中则在可见的内置目录、当前/引用组件及包导出中查找，**唯一候选自动解析**，零候选报未知，多个候选报歧义并要求前缀。默认空间内重复导出直接报错，不因其他候选掩盖。配置消解同名冲突，不靠 PascalCase 猜 UI，也不靠程序集声明的 prefix 抢占模板前缀；新包引入第二个同级候选必须转成诊断，不能静默重定向。未知前缀、保留前缀重绑定、重复 URI+local 导出都报明确诊断；`html:`/`ui:`/`svg:` 是保留含义的可声明前缀，不得重绑定到第三方 URI。

顶层 `<template>` 是 SQX/SQV 文件的模板分区，`xmlns` 仅在编译期消费、不导出到 HTML。顶层 `<script>/<style>` 仍是 C# / 组件 CSS 分区；模板内部嵌套 `<template>/<script>/<style>` 遵循上述默认优先、唯一候选规则：HTML 默认时分别选 `HTMLTemplateElement`、受禁用 `HTMLScriptElement`、受禁用 `HTMLStyleElement`，不再把嵌套 `<template>` 当片段包装。Square 的 `Fragment`、`Show`、`For`、`Slot` 等结构原语归 UI 空间；独有名称 `<Show>` 可省前缀，冲突时写 `<ui:Show>`。HTML 默认下同名 `<slot>` 是 `HTMLSlotElement`，Square 插槽写 `<ui:Slot>`；`v-if`/`v-for` 等属性级指令按原方言规则。

`<svg>` 在只有 SVG 候选时无需前缀；选中 SVG 根后其后代进入 SVG 空间。HTML 默认空间中的 `<svg>` 同样按 HTML 外来内容集成点切换；若未来另一空间也导出同名元素，默认 URI 或显式 `<svg:svg>` 决定选择，不按子标签名猜。

## 扩展元素：借鉴 WHATWG Custom Elements，而非混同两种语法

WHATWG 区分 [自主自定义元素](https://html.spec.whatwg.org/multipage/custom-elements.html#autonomous-custom-element)（如 `<acme-badge>`，通过 `customElements.define` 注册）和 [定制内建元素](https://html.spec.whatwg.org/multipage/custom-elements.html#customized-built-in-element)（如 `<button is="acme-button">`，继承 `HTMLButtonElement`）。合法浏览器自定义元素名称需小写、包含连字符等；`<my:element>` **不是**浏览器 Custom Elements 的名称。

- `<my:element>` 是 Square **模板包解析**：`xmlns:my` 的包 URI+local name 找到已引用程序集的公开 `Element` 导出。当前 `ElementExport(namespaceUri, prefix, localName, type)` 可作为迁移来源；包自报的 prefix 不能覆盖模板作者的 `xmlns:my`。该包若是同名唯一候选，`<element>` 也能直接解析；只有碰撞或需要选择非默认同名类型时才要求声明/书写前缀。装配时保证类型可构造、导出唯一、AOT 可登记、卸载资源不泄漏。
- 包 URI 是模板里的解析键，**不自动等于运行时 DOM NamespaceURI**：一个包可导出 HTMLElement 派生类（真实 DOM NamespaceURI 仍为 XHTML）或 Square UI 类型。不能把 urn:acme:widgets 作为浏览器 XHTML custom element 的 NamespaceURI；若需原生 DOM createElementNS，该 API 只处理真实 DOM 命名空间。程序化包组件实例化另走按包 URI+local name 的组件工厂。
- Square 内核可参照 Custom Elements 定义受观察属性、connected/disconnected、属性旧值→新值、移动/重挂载等生命周期；与现有 Element 生命周期对接，规定初始化一次、通知顺序和重复连接语义。仅对注册的属性发送变化，不从所有 PropertyStore 写入臆造 HTML 属性；第三方处理异常须产生可定位的诊断。
- 自主 HTML 扩展类型继承 HTMLElement；定制内建类型继承具体 HTML 类且保留原本 tag/语义，以 is/定义名识别。这两种不同于任意 Square UI 控件导出。表单关联、ElementInternals/ARIA 默认状态、Shadow DOM 不是注册时自动具备的能力；若不实现，应明示限制。
- 无前缀 `<acme-badge>` 遵循相同候选规则：可命中已注册的自主 HTML 扩展，或唯一的 Square 包导出；后者**不因此变成**浏览器 Custom Element。只有前者可采用浏览器同名标签的语义与合规连字符命名；任意未知标签不能静默降级为普通 `HTMLElement`。同一类型的 `<my:element>` 包别名与 HTML 扩展名必须由定义显式关联，不能凭字符串相似推导。
- 定制内建元素仅通过 `<button is="acme-button">`（或非 HTML 默认空间下 `<html:button is="acme-button">`）选择；注册定义必须声明 `extends=button` 且类型继承 `HTMLButtonElement`。改变现有元素的 `is` 属性不触发重新升级，不能把 `<my:element>` 当作等价写法。
- Web 导出默认由包提供**安全、静态可展开的 HTML 表示**，并受核心 exporter 的编码与 URL/脚本策略约束；无表示时报诊断，不输出看似可用的未知占位。要输出浏览器真正可升级的 `<acme-badge>` 或 `<button is="acme-button">`，必须同时有合规连字符名称和受信的客户端定义/注册机制；这会改变当前禁用模板主动脚本的安全边界，需要单独授权，不能把第三方 JS 悄悄混入页面。
- 仓库现有 Square.Extensions.WebView 是操作系统 WebView 包装；文档提及的 Square.Extensions.Html 轻量内核仍是后续设想，**不是**这次核心类型/包注册方案的依赖。

## 运行时与输出边界

- `UIDocument.CreateElement(name)` 使用文档绑定的默认解析 URI，未命中时按同一“唯一候选或歧义”规则选择（不读可变进程全局变量）；显式 DOM `createElementNS` 只创建真实 HTML/SVG 等 DOM 命名空间元素。UI/第三方包也可通过其解析 URI 的组件工厂显式创建；两条路径必须与生成代码落到相同具体类型。
- Element/Node 保留统一子树、CSS、事件、LayoutEngine 与 DisplayTree 管线。HTML 盒模型、焦点管理和文本布局不能靠继承 UIElement 获得；HTML 元素及纯 DOM Text 必须能独立参与原有管线。旧控件和 SVG 的渲染/事件路径不被 HTML UA 规则覆盖。
- Web 输出先按运行时**具体类型及真实 DOM namespace**分派：HTML 为安全原生小写标签，SVG 为 SVG；Square UI 控件按既有映射；模板包前缀不是响应中的 HTML 标签前缀。主动内容继续受 HTML5-Elements.md 的安全矩阵约束。
- 113 种具体类型是类型身份与适用属性覆盖的范围，**不是** 113 套独立 painter 或完整浏览器 API。每类必须有确定的 native 像素/交互、非视觉标准职责或有诊断的禁用行为；共享布局、替代绘制与语义基类实现，禁止空类型掩盖未支持行为。

## 与当前实现的差异及迁移

| 当前已实现行为 | 目标行为 / 迁移动作 |
| --- | --- |
| HTMLElement : UIElement；一个 HtmlElement(string) 表示 113 标签，HTML 文字 HtmlTextRun : Controls.Text | HTMLElement : Element；113 个具体 HTML 类型，DOM Text 非 UIElement；移除通用 string 构造语义和内部 UI 控件语义子节点 |
| 无前缀精确小写 HTML 优先，其余大小写可回退 Square（`<Button>` 等） | 无前缀先查配置的默认 URI，再查其他空间的唯一候选；HTML 默认下同名 `<Button>` 是 HTML button，选 Square 才需 `<ui:Button>`；独有的 `<View>` 仍可省前缀 |
| 组件文件没有 `xmlns` 语法；导出包可在唯一候选时使用无前缀名 | 引入模板 `xmlns` / 项目默认 URI；**保留**唯一包候选的无前缀用法，冲突后要求前缀；原 `<acme:chart>` 等显式包前缀使用点迁为模板 `xmlns:acme` 声明，不再靠程序集 prefix 隐式注入 |
| 无前缀嵌套 `<template>` 是片段包装 | 顶层 `<template>` 仍是文件分区；嵌套标签遵循默认优先/唯一候选规则；Square 片段改为独有的 `<Fragment>` 或显式 `<ui:Fragment>` |
| 文档说明与补全/生成器、ElementRegistry 各有大小写与前缀特例 | 同一 URI+local 解析结果贯穿语法树、lowerer、分析器、补全/hover、生成器、运行时工厂和导出；过时规则与文档在实施时一起迁移，不留下双重约定 |

这份拟议文档**不覆盖**当前代码已实现的事实。特别是 docs/Sqx-Spec.md §2.3 仍写“无需 xmlns”且称 html: 为空，和当前代码已有的 113 标签实现都不完全一致；实施此设计时必须一并修正 Sqx-Spec.md、HTML5-Elements.md、API 文档、样例和既有测试，而不是先把旧文档改成未实现的承诺。

## 完成判据（实施阶段；本次仅编写文档）

1. 对下表 113 行逐一证明：唯一 concrete CLR 类型、TagName 与 XHTML NamespaceURI 固定、不是 UIElement；WHATWG 多标签共享接口的继承链与表一致。保留 HTML 非视觉/禁用行为的诚实诊断。
2. 对每种默认 URI（HTML、UI、包）分别编译 `.sqx`/`.sqv`：同名元素由默认空间确定，默认未命中时唯一候选可省前缀、多个候选需前缀；显式 `ui:`/`html:`/`my:` 恒定命中声明 URI。覆盖 `<Button>`/`<button>`、独有 `<View>`/`<article>`/包 `<element>`、嵌套 `template`、SVG 子树、未知/冲突前缀；项目配置被模板 `xmlns` 覆盖，不同模板/文档不会互相污染。
3. 已有 UI/SVG 模板中**受同名冲突、默认配置或旧片段语法影响**的使用点迁移并通过原有测试/实际桌面截图；独有名称的旧调用保持可用。HTML Native 布局/焦点/事件/属性绑定、Web 原生标签安全输出、第三方包实例化与生命周期用真实行为验证。浏览器 DOM 检查自定义元素输出安全；未授权浏览器注册脚本不得出现。

## 113 标签 → 拟议具体 CLR 类型 → WHATWG DOM 接口

第三列来自冻结的 WHATWG 索引；第二列是 Square 拟议类型，不是声称浏览器已有同名接口。对共享 WHATWG 接口，第二列具体类型继承第三列命名的抽象共享基类；第三列为 HTMLElement 时直接继承 Square.Html.HTMLElement。

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
