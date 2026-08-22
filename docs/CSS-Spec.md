# CSS 支持范围

> Document Revision: 0.3
> 配套：`Architecture.md`、`Sqx-Spec.md`

---

## 1. 目标

尽可能兼容现代 CSS 语义与 **CSSOM Web API** 表面，不兼容浏览器私有扩展。

CSS 是框架的重要组成部分，与 `.sqx` 的 `<style>` 段和 `style`/`class` 属性联动。

### 1.1 CSSOM 对齐（`element.Style`）

| Web API | Square |
|---------|--------|
| `style.setProperty(name, value)` | `Style.SetProperty` |
| `style.getPropertyValue(name)` | `Style.GetPropertyValue`（未设置返回 `""`） |
| `style.removeProperty(name)` | `Style.RemoveProperty` |
| `style.cssText` | `Style.CssText` |
| `style.length` / `item(i)` | `Style.Length` / `Style.Item(i)` |
| camelCase 属性名 | 自动规范为 kebab-case（`fontSize` → `font-size`） |

级联写入仍通过引擎调用 `SetCascaded`；内联 `Set`/`SetProperty` 使用最高 specificity。

---

## 2. 分阶段支持

| 阶段 | 范围 |
|---|---|
| **M1** | Selector 子集、Cascade、Specificity、Variables、Inheritance、基础属性、Flex |
| **M2** | Pseudo Class、Animation、Grid 全量、Theme 系统 |
| **M3+** | Container Query、Subgrid |

---

## 3. Selector（当前支持范围）

| 选择器 | 示例 | 当前状态 |
|---|---|---|
| 类型 | `Button` | ✅ 已实现 |
| 类 | `.active` | ✅ 已实现 |
| ID | `#main` | ✅ 已实现 |
| 后代 | `View Text` | ✅ 已实现 |
| 子代 | `View > Text` | ✅ 已实现 |
| 相邻兄弟 | `Text + Text` | ✅ 已实现 |
| 通用兄弟 | `Text ~ Text` | ✅ 已实现 |
| 通用 | `*` | ✅ 已实现 |
| 属性 | `[IsDisabled]` `[variant=primary]` `[tags~=primary]` `[lang|=en]` `[code^=pre]` `[code$=suffix]` `[code*=middle]` | ✅ 已实现 |
| 伪类 | `:hover` `:focus` `:active` | ✅ 基础已实现 |
| 函数式伪类 | `:nth-child(2)` `:not(.active)` | ⚠️ 部分实现 |
| 伪元素 | `::before` `::after`（兼容 `:before` `:after`） | ✅ 已实现：字符串 `content` 与 `content: ""` 装饰盒子（含背景/边框样式）；`::selection` 仅映射背景/颜色 |

> 说明：组合选择器、`!important`、`:nth-child(n)` 与属性选择器已有单元测试覆盖。属性选择器匹配 `Element.Properties` 强类型属性袋，不是独立 DOM Attr 集合；属性名保持 PropertyStore 的大小写语义，值匹配当前统一使用大小写不敏感比较。暂不支持命名空间属性及 `i` / `s` 大小写修饰符。

---

## 4. Cascade 与 Specificity

组件样式表分别在各组件构建时应用，但级联元数据必须保留在 `StyleAccessor` 中：

- 后应用的低 specificity 规则不得覆盖先应用的高 specificity 规则。
- specificity 相同时，后应用规则覆盖先应用规则。
- `element.Style.Set(...)` 视为 inline style，优先级高于所有样式表规则。
- 插槽内容保留调用方样式规则；进入子组件视觉树不会重置已计算 specificity。

- 级联顺序：`!important` > 内联 `style` > ID > 类/属性/伪类 > 类型
- Specificity 计算：`(id_count, class_count, type_count)`
- 同 specificity 时，后定义胜出
- Variables（`--x`）参与级联

当前实现中 `!important` 已解析为 declaration 标记，并按高于普通 specificity 的优先级应用。内联样式仍通过 `Style.Set(...)` 保持高优先级。

---

## 5. Variables

```css
:root {
  --primary: #0078d4;
  --spacing: 16px;
}

Button {
  color: var(--primary);
  padding: var(--spacing);
}
```

- 定义：`--name: value`
- 使用：`var(--name)` / `var(--name, fallback)`
- 继承：变量沿 Element Tree 继承

---

## 6. Inheritance

可继承属性：

- `color`
- `font-size` / `font-family` / `font-weight`
- `line-height`
- `text-align`
- `visibility`

不可继承（默认）：

- `margin` / `padding` / `border` / `background` / `width` / `height`

---

## 7. 属性（M1 基础集）

| 类别 | 属性 |
|---|---|
| 文本 | `color` `font-size` `font-family` `font-weight` `font-style` `line-height` `text-align` |

### 7.1 字体解析（对齐 CSS Fonts 简化）

- 控件绘制通过元素上的 CSS 读取 `font-family` / `font-size` / `font-weight` / `font-style`，不再写死单一族名。
- `font-family` 支持逗号列表与引号：`"Segoe UI", Tahoma, sans-serif`。
- 通用族映射：`sans-serif` / `system-ui` → Segoe UI，`serif` → Times New Roman，`monospace` → Consolas。
- `Font.FromCss` / `FontManager.FromCss` 提供与 CSSOM 一致的解析入口；绘图原语仍为 `Square.Graphics.Font`。

### 7.2 CSS Font Loading 子集

| Web API | Square |
|---------|--------|
| `FontFace` | `Square.Text.Fonts.FontFace`（本地路径 / 字节，`LoadAsync`） |
| `FontFaceSet` / `document.fonts` | `FontFaceSet`；`Document.Fonts` |
| `fonts.add` / `load` / `check` / `ready` | `Add` / `LoadAsync` / `Check` / `Ready` |
| `FontFaceSetLoadEvent` | 未实现（用 `Task` 代替） |
| `FontData` / `queryLocalFonts` | 不实现 |

示例：

```csharp
var face = new FontFace("AppBrand", @"C:\Fonts\Brand.ttf");
document.Fonts.Add(face);
await face.LoadAsync();
// 之后 CSS font-family: AppBrand 可被 FontManager 匹配
```

`@font-face` 样式表解析尚未接入；当前通过命令式 `FontFace` API 注册。
| 背景 | `background` `background-color` `box-shadow` |
| 边框 | `border` `border-width` `border-color` `border-radius` |
| 间距 | `padding` `margin` |
| 尺寸 | `width` `height` `min-width` `max-width` `min-height` `max-height` |
| 布局 | `display` `flex-direction` `justify-content` `align-items` `flex-grow` `flex-shrink` `flex-basis` `gap` |
| 定位 | `position` `top` `right` `bottom` `left` |
| 其他 | `opacity` `visibility` `overflow` `overflow-x` `overflow-y` |

当前 `overflow: hidden` / `overflow: clip` 会裁剪子树渲染与命中测试；`visible` 保持子元素可溢出命中。`overflow: scroll` / `auto` 会跟踪内容尺寸、裁剪并平移子树、映射滚动后的命中测试，并通过 wheel 默认动作滚动最近的可滚动祖先。`ScrollViewer` 控件在该通用机制上提供默认纵向滚动和强类型 offset / extent / viewport API。

`box-shadow` 支持逗号分隔的多个外阴影：`offset-x offset-y [blur-radius] [spread-radius] color`。支持 `px`、十六进制颜色、`rgb()` 和 `rgba()`；列表首项绘制在后续阴影之上。暂不支持 `inset` 和 `text-shadow`。全部阴影均不参与布局，但会共同扩展 DisplayTree 的视觉边界和脏矩形。Popup、Menu、ContextMenu 与 Dialog 默认使用 `0 4px 8px 2px rgba(0,0,0,0.48)` elevation 阴影，可通过 `box-shadow: none` 覆盖。

---

## 8. 单位

| 单位 | 说明 | M1 |
|---|---|---|
| `px` | 物理像素（经 DPI 缩放） | ✅ |
| `%` | 相对父容器 | ✅ |
| `auto` | 自动 | ✅ |
| `rp` | 响应式单位（基准尺寸比例） | ✅ |
| `vw` / `vh` | 视口宽/高百分比 | ✅ |
| `min-content` / `max-content` / `fit-content` | 内在尺寸 | M2 |
| `rem` / `em` | 相对字号 | M2 |

---

## 9. Flex（M1）

```css
View {
  display: flex;
  flex-direction: row | column | row-reverse | column-reverse;
  justify-content: flex-start | center | flex-end | space-between | space-around;
  align-items: stretch | flex-start | center | flex-end;
  flex-wrap: nowrap | wrap;
  gap: 8px;
}
```

---

## 10. Grid（M2）✅ 已实现

```css
View {
  display: grid;
  grid-template-columns: 1fr 2fr;
  grid-template-rows: auto;
  gap: 8px;
}
```

- 支持 `grid-template-columns`、`grid-template-rows`、`fr` 单位
- 支持 `grid-column`、`grid-row`、`grid-column-span`、`grid-row-span`
- 支持 `minmax()`、基础 auto-placement
- 支持 `grid-template-areas` / `grid-area` 命名区域

---

## 11. Pseudo Class（当前支持范围）

| 伪类 | 说明 | 当前状态 |
|---|---|---|
| `:hover` | 鼠标悬停 | ✅ 基于 `ElementState.Hover` |
| `:focus` | 获得焦点 | ✅ 基于 `ElementState.Focus` |
| `:active` | 激活（按下） | ✅ 基于 `ElementState.Active` |
| `:disabled` | 禁用 | ✅ 基于 `ElementState.Disabled` |
| `:checked` | 选中 | ✅ 基于 `ElementState.Checked` |
| `:empty` | 无子节点 | ✅ |
| `:first-child` / `:last-child` / `:only-child` | 位置 | ✅ |
| `:nth-child(n)` | 位置 | ✅ 支持整数、`odd`、`even` |
| `:not(...)` | 否定 | ⚠️ 仅支持简单参数（类型、类、ID、`*`） |

`Button`、`Input`、`TextArea`、`Select`、`CheckBox` 和 `Radio` 由 UA 样式默认 `appearance: auto`，对齐 Chrome `html.css` 浅色表单控件子集（`ButtonFace`/`Field`、`2px outset`/`inset`、`:active` 切 inset、`:disabled` 半透明灰）。Software / Skia / Vulkan 盒绘制消费计算样式。Chrome UA 没有 `button:hover` 颜色规则。`appearance: none` 不自动清掉 UA 边框/背景，作者需覆盖。

```css
Button:hover {
  background: #175cd3;
}

Button:active {
  background: #0b4a9e;
}
```

---

## 12. `@import`

由 `AppWindow.LoadGlobalCss(...)` 加载的文件样式表支持：

```css
@import "base.css";
@import url("./themes/light.css");
```

- `@import` 必须位于普通规则之前；允许出现在 `@charset` 和分号形式的 `@layer` 声明之后。
- 相对地址基于当前 CSS 文件目录，而不是应用程序根目录。
- 支持递归本地文件导入、循环检测和最多 64 层的深度限制。
- 导入规则先于当前文件规则进入级联，行为等同于在 `@import` 位置展开。
- 当前不支持 HTTP/HTTPS、media 条件、`supports()`、`layer()` 条件和内存 CSS 的相对导入。
- 顶层样式表通过 `Document.StyleSheets` 枚举，导入关系通过 `DocumentStyleSheet.Imports` 访问。

---

## 13. Animation（M2）✅ 已实现

```css
@keyframes fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}

Text {
  animation: fade-in 0.3s ease;
}
```

- `@keyframes` 定义
- `animation` 简写：`name duration timing-function delay iteration-count direction`
- CSS 动画 timeline 在样式作用域应用或重新级联后从 Element Tree 自动收集，并由桌面帧循环 tick
- 当前支持数值属性的分段关键帧插值（`from` / `to` / 百分比 stop）、delay、有限 iteration-count、normal/reverse/alternate/alternate-reverse 基础方向
- 颜色、transform 与完整浏览器级动画模型后续扩展

---

## 14. 内联样式与类

### 14.1 内联 `style`

```xml
<Button style="color: red; padding: 8px;">Click</Button>
```

- 优先级最高（仅次于 `!important`）
- 命令式：`el.Style.Set("color", "red")`

### 14.2 `class`

```xml
<Button class="primary large">Click</Button>
```

- 空格分隔多个类
- 命令式：`el.ClassList.Add("primary")` / `.Remove("primary")` / `.Toggle("primary")`

### 14.3 绑定

```xml
<Button class={ActiveClass}>Click</Button>
<Button style={DynamicStyle}>Click</Button>
```

- `class` 绑定 `ObservableValue<string>`
- `style` 绑定 `ObservableValue<string>` 或对象

---

## 14. 不支持范围

- 浏览器私有扩展（`-webkit-` 等）
- `@media` 全量（M3+ 考虑 Container Query 替代）
- `@supports`
- CSS Houdini
- 怪异模式
