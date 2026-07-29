# SQV / Vue 模板语法

> 状态：默认推荐的模板入口仍在迭代中  
> 范围：`.sqv` 是 Square 的 Vue 3 模板语法前端，拥有独立解析栈，并与 `.sqx` 共享编译后组件模型。

## 目标

- 将 `.sqv` 作为优先面向用户的模板语法，以 Vue 3 模板语法兼容为目标。
- 保留 `.sqx` 不变，作为 Square 的原生模板语言。
- 使用专门的 Vue 解析器，而不是把现有 SQX 解析器改造成 Vue 解析器。
- 重构 `Square.Compiler`，使其可以通过共享的中间表示（IR）同时编译 `.sqx` 和 `.sqv`。
- 保留 Square 的“编译优先”模型：不引入 Vue 运行时、不引入 JavaScript 运行时、不做运行时模板解析。
- 将 NativeAOT 兼容性与强 C# 诊断体验作为一等约束保持不变。

## 非目标

- 不移除或弃用 `.sqx` 语法。
- 不在模板中执行 JavaScript 表达式。
- 不在 Square 中内嵌 Vue 运行时行为。
- 不静默忽略不支持的 Vue 特性。对它们进行解析，并在无法生成代码时输出明确的诊断信息。

## 兼容性定义

`.sqv` 旨在支持 Vue 3 的模板语法表面。Vue 语法内部的表达式被解释为 C# 表达式文本，并由 Source Generator 编译为 C# 代码。

示例：

```vue
<template>
  <View class="page">
    <Text>{{ Title.Value }}</Text>

    <Input
      :value="Name"
      @input="OnNameChanged" />

    <Button ref="SaveButton" @click.stop.prevent="OnSave">
      Save
    </Button>

    <Text v-if="Saved.Value">Saved</Text>

    <Text v-for="item in Items" :key="item.Id">
      {{ item.Name }}
    </Text>
  </View>
</template>

<script lang="csharp">
  public ObservableValue<string> Title = new("Hello Square");
  public ObservableValue<string> Name = new("");
  public ObservableValue<bool> Saved = new(false);
</script>

<style>
  .page { padding: 16px; }
</style>
```

## 文件格式

`.sqv` 与 `.sqx` 保持相同的顶层分区模型：

- 必须有一个 `<template>` 分区。
- 至多有一个 `<script lang="csharp">` 分区。
- 至多有一个 `<style>` 分区。
- 组件元数据仍然位于 `<script>` 标签上：`namespace`、`name`、`access`。

这样可以在 `.sqx` 和 `.sqv` 之间保持生成器、元数据、样式管线以及组件命名模型的一致性。

## 架构

SQV 拥有完全独立的解析栈，不依赖 SQX 的词法器/解析器：

- `SqvLexer`：Vue 模板词法器，识别 `{{ }}` 插值、标签、属性名（含 `:`/`@`/`#`/`v-` 前缀与 `.修饰符`）、字符串、注释。
- `SqvTemplateParser`：消费 `SqvLexer` token，构造共享模板 IR，并把 `v-for`/`v-if` 链降低为语言中性的 `TemplateForDirective`/`TemplateIfChainDirective`。
- `SqvDocumentParser`：拆分 `<template>`/`<script>`/`<style>` 分区（支持嵌套 `<template>` 配对），提取脚本元数据。
- `SqvAttributeConverter`：把原始 Vue 属性名/值转换为 emitter 可消费的 `SqxAttribute` 形式。
- `SqvParser`：薄入口，委托给 `SqvDocumentParser`。

依赖方向为单向：**SQV → SQX（仅复用 `SqxNode`/`SqxElement`/`SqxAttribute`/`SqxDocument` AST 基类与 `ComponentEmitter`），SQX 不感知 SQV**。两者仅在最终生成的 C# 组件类型层面互相引用。

实际管线：

```text
.sqx -> SqxParser          -> SqxDocument -> ComponentEmitter -> 生成的组件
.sqv -> SqvDocumentParser  -> SqxDocument -> ComponentEmitter -> 生成的组件
        (SqvLexer + SqvTemplateParser + SqvAttributeConverter)
```

## Vue 语法表面

`.sqv` 解析器应识别并保留下列 Vue 3 语法形式。

### 文本与插值

- 普通文本。
- 适用情况下的 HTML 实体。
- `{{ expression }}` 插值。
- 注释：`<!-- ... -->`。

### 属性与绑定

- 静态属性：`name="value"`、`disabled`。
- 动态绑定：`:prop="expr"`、`v-bind:prop="expr"`。
- 对象绑定：`v-bind="expr"`。
- 动态参数：`:[name]="expr"`、`v-bind:[name]="expr"`。
- 绑定修饰符：`.camel`、`.prop`、`.attr`。

### 事件

- 事件简写：`@click="handler"`。
- 完整事件形式：`v-on:click="handler"`。
- 对象事件绑定：`v-on="expr"`。
- 动态事件名：`@[event]="handler"`、`v-on:[event]="handler"`。
- 事件修饰符：`.stop`、`.prevent`、`.capture`、`.self`、`.once`、`.passive`、`.exact`，以及按键和鼠标按键修饰符。

### 控制流

- `v-if="condition"`。
- `v-else-if="condition"`。
- `v-else`。
- `v-for="item in items"`。
- `v-for="(item, index) in items"`。
- `v-for="(value, key, index) in object"`。
- `:key="expr"`。

### 插槽

- `<slot>` 出口。
- `<slot name="header">` 出口。
- `v-slot`。
- `v-slot:name`。
- `#name` 简写。
- 动态插槽名：`#[name]`。
- 作用域插槽属性，先解析并表示，即使初期能力有限。

### 特殊指令与内置组件

- `v-model` 及修饰符 `.trim`、`.number`、`.lazy`。
- `v-text`。
- `v-html`。
- `v-pre`。
- `v-once`。
- `v-memo`。
- `v-cloak`。
- 作为结构容器的 `<template>`。
- `<component :is="...">`。
- `<Teleport>`。
- `<Transition>`。
- `<TransitionGroup>`。
- `<KeepAlive>`。
- `<Suspense>`。

## 生成语义

已实现：

- `{{ expr }}` -> Square 文本插值 / 文本绑定。
- `:prop="expr"` 与 `v-bind:prop="expr"` -> `BindProperty`；当表达式为局部循环变量时直接设置属性。
- `@click="OnClick"` 与 `v-on:click="OnClick"` -> `AddEventListener("click", OnClick)`。
- `.stop` 与 `.prevent` 事件修饰符 -> 生成事件包装器，在调用处理函数前先调用 `StopPropagation()` 和 `PreventDefault()`。
- 静态 `class` 与 `style` -> 沿用现有的 class/style 生成。
- `v-text="expr"` -> 绑定或设置 `TextContent`。
- `v-show="expr"` -> 绑定 `IsVisible`。
- `v-if` / `v-else-if` / `v-else` -> `SqvIfChainDirective` -> 互斥 `ShowNode` 条件链。
- `v-for="item in Items"` / `v-for="(item, index) in Items"` -> `SqvForDirective` -> `ForNode.Create`（含索引重载）。
- `ref="Name"` -> 生成 ref 字段。
- `<slot>` / 基础具名插槽 -> 沿用现有的 Square 插槽模型。
- `v-model`（含 `.trim`/`.number`/`.lazy`）-> 按控件类型绑定属性与回写事件。

当前不生成并输出明确诊断：

- `v-html`，因为 Square 不是 HTML DOM。
- `<component :is="...">`，在出现 AOT 安全的动态组件工厂模型之前。
- `<Teleport>`，在 Square 拥有传送门/层目标模型之前。
- `<Transition>` 与 `<TransitionGroup>`，在存在动画集成之前。
- `<KeepAlive>`，在具备组件实例缓存语义之前。
- `<Suspense>`，在具备异步组件语义之前。

## 条件链

Vue 条件链通过 `SqvIfChainDirective` 保持互斥：

```vue
<Text v-if="State.Value == 0">A</Text>
<Text v-else-if="State.Value == 1">B</Text>
<Text v-else>C</Text>
```

实际 AST：

```text
SqvIfChainDirective
  Branch condition: State.Value == 0
    Text
  Branch condition: State.Value == 1
    Text
  Branch IsElse
    Text
```

生成（每个分支独立 `ShowNode`，条件累积互斥）：

```csharp
_vif0 = new ShowNode(State.Value == 0, () => { ... });
_vif0.AttachTo(parent);
_vif1 = new ShowNode(!((State.Value == 0)) && (State.Value == 1), () => { ... });
_vif1.AttachTo(parent);
_vif2 = new ShowNode(!((State.Value == 0) || (State.Value == 1)), () => { ... });
_vif2.AttachTo(parent);
```

## 循环

Vue 循环语法：

```vue
<Text v-for="item in Items">
  {{ item.Name }}
</Text>

<Text v-for="(item, index) in Items">
  {{ index }} {{ item.Name }}
</Text>
```

实际 AST（`SqvForDirective`）：

```text
SqvForDirective
  Source: Items
  ItemName: item
  IndexName: index?
  Children: 原始元素
```

生成：

```csharp
// 单变量
_vfor0 = ForNode.Create(Items, item => { ... });

// 含索引
_vfor0 = ForNode.Create(Items, (item, index) => { ... });
```

运行时 `ForNode` 已支持 `Func<T,Element?>` 与 `Func<T,int,Element?>` 两套重载。`:key` 当前由 `ForNode` 运行时接管，模板层暂不处理。

## v-model

`v-model` 已完成解析与生成。按控件类型选择目标属性与回写事件：

| Vue 语法 | Square 含义 |
|---|---|
| `<Input v-model="Name" />` | 绑定 `Value` 并在 input 时更新 |
| `<CheckBox v-model="Checked" />` | 绑定 `IsChecked` 并在 change 时更新 |
| `<Select v-model="Selected" />` | 绑定 `Value` 并在 change 时更新 |

修饰符：

- `.trim` 在回写前修剪字符串。
- `.number` 在回写前解析数值。
- `.lazy` 使用 commit/change 事件，而不是即时 input 事件。

## 插槽

基础 Vue 插槽应映射到 Square 插槽。

```vue
<Card>
  <template #header>
    <Text>Header</Text>
  </template>

  <Text>Default content</Text>
</Card>
```

归一化分组：

```text
Card
  Slot "header": Text
  Slot "": Text
```

作用域插槽属性应被解析进 IR，但在运行时支持插槽属性传递之前，初期可输出诊断。

## 诊断

建议的 `.sqv` 诊断：

| ID | 含义 |
|---|---|
| `SQV0001` | Vue 模板语法错误 |
| `SQV0002` | 不支持的 Vue 指令生成 |
| `SQV0003` | 无效的 `v-for` 表达式 |
| `SQV0004` | `v-else` / `v-else-if` 前面没有 `v-if` 链 |
| `SQV0005` | 同一属性/事件存在重复绑定 |
| `SQV0006` | 无效的动态参数 |
| `SQV0007` | 不支持的 Vue 内置组件生成 |
| `SQV0008` | 无效或不支持的作用域插槽属性形式 |
| `SQV0009` | 模板表达式必须是 C# 表达式 |

通用的分区与组件诊断在合适时可继续使用现有 SQX 诊断，或后续迁移到与语言无关的 ID。

## 实现状态

以下能力已落地，通过 `VueGeneratorTests`（SourceGenerator 34 项、Markup 19 项、UI 216 项全部通过）。SQV 使用独立解析栈（`SqvLexer`/`SqvTemplateParser`/`SqvDocumentParser`/`SqvAttributeConverter`），不再调用 `SqxParser`/`SqxCoreParser`。

已支持的 Vue 语法：

- `{{ expr }}` 文本插值。
- 静态属性、`:prop`、`v-bind:prop` 绑定。
- `@event`、`v-on:event` 事件绑定。
- 事件修饰符 `.stop`、`.prevent`：生成 `e.StopPropagation(); e.PreventDefault(); handler(e);` 包装。
- `v-if` -> `SqvIfChainDirective`（单分支）/ `ShowNode`。
- `v-else-if` / `v-else` -> `SqvIfChainDirective` 多分支条件链，互斥 `ShowNode`（`!(prev) && (cond)` / `!(prev)`）。条件链按分支计数字段，每个分支生成独立的 `_vifN` 字段与 `ShowNode`。
- `v-for="item in Items"` -> `SqvForDirective` -> `ForNode.Create(Items, item => ...)`。
- `v-for="(item, index) in Items"` -> `SqvForDirective` -> `ForNode.Create(Items, (item, index) => ...)`（运行时新增 `Func<T,int,Element?>` 重载）。
- `v-show="expr"` -> 绑定 `IsVisible`。
- 嵌套指令（`v-for` 内含 `v-if` / `v-for` 等）通过递归 `RewriteSiblings` 支持。
- `<template>` -> 透明展开（emitter 的 `IsTemplateFragment` 同时识别 `template` 与 `Fragment`）。
- `v-slot` / `#name` 具名插槽 -> `slot="..."` 静态属性。
- `ref="Name"` -> 组件 ref 字段。
- `v-text` -> 文本绑定。
- `v-model`（含 `.trim` / `.number` / `.lazy`）：按 `Input`/`TextArea`/`CheckBox`/`Radio`/`Select` 选择目标属性与事件。
- `v-for` 支持 `in` 与 `of` 分隔符。

当前不生成并输出明确诊断，留待后续里程碑：

- `v-html`、`v-pre`、`v-once`、`v-memo`、`v-cloak`。
- 自定义组件 `v-model`、未知 `v-*` 指令，以及尚未实现的事件和 `v-model` 修饰符。

## 示例

`samples/Square.Sample.Vue/Components/ControlsSamplesPage.sqv` 展示了 Vue 原生语法的实际用法（`v-if`、`v-for`、`:prop`、`@event`、`ref`、`v-model`）。

## 实现里程碑

### ✅ 里程碑 A：独立 Vue 解析栈

已完成。SQV 不再依赖 SQX 解析器：

- `SqvLexer`：Vue 模板词法器。
- `SqvTemplateParser`：Vue 模板解析器，直接构造 `SqxNode` 树与 Vue 专属指令节点。
- `SqvDocumentParser`：分区解析器，支持嵌套 `<template>` 配对。
- `SqvAttributeConverter`：Vue 属性转换。
- `SqvParser`：薄入口。
- SQX 侧（`SqxParser`/`SqxAst`/`ParserCore`/`Directives`）零 `Sqv` 引用，验证依赖单向。

### ✅ 里程碑 B：基础 Vue 语法生成

已完成：

- `{{ expr }}` 插值、静态属性、`:prop`/`v-bind:prop`、`@event`/`v-on:event`。
- 静态 `class`/`style`。
- `ref="Name"`。
- `<template>` 透明展开。
- `v-slot`/`#name` 具名插槽。

### ✅ 里程碑 C：控制流

已完成：

- `v-if`/`v-else-if`/`v-else` -> `SqvIfChainDirective` -> 互斥 `ShowNode`。
- `v-for="item in Items"` -> `SqvForDirective` -> `ForNode.Create`。
- `v-for="(item, index) in Items"` -> 索引重载（运行时新增 `Func<T,int,Element?>`）。
- `v-show` -> 绑定 `IsVisible`。
- 嵌套指令递归支持。

### ✅ 里程碑 D：事件修饰符与 v-model

已完成：

- `.stop`/`.prevent` 事件修饰符包装。
- `v-model`（`Input`/`TextArea`/`CheckBox`/`Radio`/`Select`）。
- `.trim`/`.number`/`.lazy` 修饰符。

### ✅ 里程碑 E：动态参数与对象绑定

- `:[name]`、`@[event]`、`#[name]` 动态参数已支持；属性/事件名可使用 `IReactiveValue<string>` 响应式切换。
- `v-bind="obj"` 已支持 `IReadOnlyDictionary<string, object?>`，并支持 `ObservableValue<TMap>` / `IReactiveValue<TMap>` 响应式更新。
- `v-on="obj"` 已支持 `IReadOnlyDictionary<string, Action<Event>>`，并支持响应式事件表替换和监听器清理。
- 对象协议不使用反射；匿名对象、`dynamic` 和任意 `Delegate` 字典不在支持范围内。
- 动态属性和事件使用 `SqvObjectBinding.BindProperty` / `BindEvent`，名称变化时清理旧属性或监听器；动态插槽名直接编译为 C# 字符串表达式。

### 里程碑 F：内置组件与高级特性（部分完成）

- `<component :is="...">`、`<Teleport>`、`<Transition>`、`<TransitionGroup>`、`<KeepAlive>`、`<Suspense>`。
- 作用域插槽属性支持 `#name="slotProps"` 整包形式，以及基于组件 `[SlotContract(name, propsType)]` 元数据的 `{ item, label: caption }` 类型化解构；生成代码使用 `SlotProps.Get<T>`，保持 AOT-safe。
- `:key` 循环键。

### 里程碑 G：诊断与清理（基本完成）

- 已实现 `SQV0001`-`SQV0009`，覆盖模板语法错误、不支持指令、无效 `v-for`、孤立条件分支、重复绑定、动态参数、Vue 内置组件、作用域插槽属性和非法 C# 表达式。
- 已校验结束标签配对、未闭合插值/字符串/注释和 `<script lang>`。
- 已在最终 AST 上检查重复属性/事件，包括 `v-model` 展开后与显式 `value` / `input` 绑定形成的冲突。
- 已使用 Roslyn 对插值、绑定、事件、`v-if` 和 `v-for` 表达式进行 C# 语法验证；生成后的临时 Compilation 会把映射回 `.sqv` 的成员、类型和事件错误转换为 `SQV0013`。跨模板联合绑定和更细分的语义诊断 ID 仍继续完善。
- 新增 `docs/Sqv-Spec.md`。
- 更新 `README.md`、`docs/Architecture.md`、`docs/Generator.md`。

### ✅ 里程碑 H：循环键化复用

- `:key` / `v-bind:key` 在 `v-for` 元素上提升为 `SqvForDirective.KeyExpression`。
- 生成 keyed `ForNode.Create(source, keySelector, build)` 调用，支持普通和带 index 的循环。
- keyed `ForNode` 支持 `ObservableCollection<T>`、`IEnumerable<T>` 和 reactive list。
- 列表重排按 key 保持节点身份，并通过 `Children.Move` 避免重复 detach / attach。
- 同 key 替换为不同 item 实例时重建节点，避免循环局部表达式保留旧 item 数据。
- null key 和运行时重复 key 会失败；模板中的重复 `:key` 报告 `SQV0005`。

### ✅ 里程碑 I：对象属性与事件绑定

- `v-bind="obj"` 使用 AOT-safe 的 `IReadOnlyDictionary<string, object?>` 协议。
- `v-on="obj"` 使用 `IReadOnlyDictionary<string, Action<Event>>` 协议。
- `SqvObjectBinding` 对响应式字典执行增量属性更新、缺失键删除和事件监听器替换。
- `null` 属性值移除对应属性；事件名统一为小写，映射后重复键会失败。
- 对象绑定句柄由生成组件记录，并在 `OnGeneratedDetachedCore` 中统一释放。
- 属性名通过 `SqvPropertyNames` 映射到 Square 控件和 SVG 属性名。

## 测试计划

已覆盖（`VueGeneratorTests`，23 项）：

- `.sqv` 文件生成一个组件。
- `{{ expr }}` 插值生成文本绑定。
- `:prop` 生成属性绑定。
- `@event` 生成事件绑定。
- `.stop.prevent` 生成事件包装器。
- `v-if` 生成 `ShowNode`。
- `v-else-if` / `v-else` 生成互斥条件链。
- `v-for="item in Items"` 生成 `ForNode.Create`。
- `v-for="(item, index) in Items"` 生成索引重载。
- `v-show` 绑定 `IsVisible`。
- 嵌套 `v-for` + `v-if` 生成独立节点，并通过生成后 C# 编译回归覆盖字段计数与嵌套 ref。
- `v-slot` / `#name` 具名插槽。
- `v-model`（`Input`/`CheckBox`/`Select`）及 `.trim`/`.number`/`.lazy` 修饰符。
- `ref="Name"` 生成 ref 字段。
- 内置控件（`View`/`Text`/`ScrollViewer`/`Popup`/`Dialog`/`MenuBar` 等）降级。
- 内联 SVG 降级。
- `.sqx` 与 `.sqv` 组件可以通过组件名互相引用。
- 现有 `.sqx` 测试保持不变通过。

运行时测试（`M1IntegrationTests`）：

- `ForNode.Create` 索引重载接收正确索引。
- `ShowNode` / `ForNode` 对 `ObservableValue` / `ObservableCollection` 响应。

待补充：

- 解析器单元测试（`SqvLexer`/`SqvTemplateParser` 独立于生成器）。
- 更完整的源码区间（行/列）保留测试。
- 不支持特性的诊断测试（里程碑 G）。

已新增生成器级错误模板与诊断回归测试，以及 `SqvLexer` / `SqvTemplateParser` 的 token、源码位置、指令重写和嵌套重复绑定测试；SQV 文档也会保留 `SourcePath` 供生成的 Element 调试信息使用。更完整的词法与源码区间矩阵仍待补充。

命令：

```powershell
dotnet test tests/Square.Markup.Tests/Square.Markup.Tests.csproj
dotnet test tests/Square.SourceGenerator.Tests/Square.SourceGenerator.Tests.csproj
dotnet test
```

## 待决设计问题

- `.sqv` 表达式是否要求对 `ObservableValue<T>` 显式使用 `.Value`，还是生成器应尽可能沿用当前 SQX 的简写行为？
- 当前条件链用独立 `ShowNode` 实现互斥，是否应改用 `SwitchNode`/`Match` 以获得更精确的运行时语义？
- `:key` 已由 `ForNode` 运行时支持键化复用；后续可增加同 key item scope 更新能力，以支持 immutable item 替换时继续复用节点。
- 不支持的 Vue 特性当前输出诊断；后续需要确定各特性的严重级别以及是否提供可配置降级策略。
- `v-model` 对非内置控件（自定义组件）如何确定目标属性与事件？
