# Source Generator

> Document Revision: 0.3
> 配套：`Architecture.md`、`Sqx-Spec.md`

---

## 1. 定位

`Square.Compiler` 是框架核心。

将 `.sqx` / `.sqv` 在编译期转换为 C# 代码。

---

## 2. 流程

```
.sqx (AdditionalText)
  ↓
Square.Markup 解析 → AST
  ↓
语义分析（Props / ref / 绑定 / 事件 / 结构原语）
  ↓
生成 C# (partial 组件类)
  ↓
Roslyn 编译
```

Debug `dotnet watch` 下，模板仍经过相同生成管线。生成程序集包含 `MetadataUpdateHandler`，模板或组件样式的方法体更新应用完成后，由桌面宿主在 UI Dispatcher 上重建顶层生成组件的后代树。

---

## 3. IIncrementalGenerator

```csharp
[Generator]
public class SqxGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var sqxFiles = context.AdditionalTextsProvider
            .Where(f => f.Path.EndsWith(".sqx"));

        var parsed = sqxFiles.Select((file, ct) =>
        {
            var ast = SqxParser.Parse(file.GetText(ct)!, file.Path);
            return (file.Path, ast);
        });

        context.RegisterSourceOutput(parsed, (spc, item) =>
        {
            var code = EmitComponent(item.ast, item.Path);
            spc.AddSource(..., code);
        });
    }
}
```

---

## 4. 生成产物

### 4.1 组件类

```csharp
public partial class MyComponent : UIElement
{
    // Props
    [Prop] public ObservableValue<string> Title { get; set; } = new("");

    // ref 字段
    internal Button MyBtn;

    // 绑定源
    public ObservableCollection<Item> Items = new();

    // 构建元素树
    public override void BuildElementTree() { ... }

    // 生命周期钩子（虚方法）
    protected override void OnPropChanged(string name) { }
    protected override void OnAttachedCore() { }
}
```

### 4.2 BuildElementTree

生成器将 `<template>` 编译为命令式构建代码：

```csharp
public override void BuildElementTree()
{
    var view = new View();
    // <Show when={LoggedIn}>
    var _show0 = new ShowNode(LoggedIn, () => {
        var text = new Text();
        text.BindText(() => UserName.Value);
        return text;
    });
    // <Button ref={MyBtn} onClick={OnClick}>
    MyBtn = new Button();
    MyBtn.AddEventListener("click", OnClick);
    // <For each={Items}>
    var _for0 = ForNode.Create(Items, (it) => {
        var text = new Text();
        text.BindText(() => it.Name);
        return text;
    });
}
```

结构指令节点使用局部变量，并通过 `RegisterGeneratedResource(...)` 绑定到生成子树生命周期。这样添加或删除 `Show`、`For`、`Switch`、`v-if` 或 `v-for` 时不会仅因生成字段布局变化而触发 Hot Reload rude edit。

### 4.3 Debug Hot Reload

每个 Debug 生成组件包含稳定的热更新成员形状：

- 当前 template + component style 指纹方法。
- 上次成功构建版本。
- `ISquareHotReloadComponent` 重建入口。
- 稳定存在的组件样式 AST 工厂；无 `<style>` 时返回 `null`。

普通 `<script>` 方法体变化不会改变模板/样式指纹，因此 CLR 直接更新代码并请求重绘。模板或组件样式变化会释放旧生成资源、清空旧后代、重置 Slot 与 ref、执行新版 `BuildElementTree()`，然后重新应用 CSS scope。文件/类型重命名、成员删除和部分 ref 类型变化仍由 CLR 判定为需重启的 rude edit。

---

## 5. Props 处理

### 5.1 解析

- 扫描 `<script lang="csharp">` 中 `[Prop]` 特性标记的属性
- 提取：名称、类型、Required、默认值

### 5.2 生成

- 产出 prop 存储字段
- 产出 prop 传入赋值代码
- 产出 `OnPropChanged` 调度

### 5.3 校验

- 调用方模板中检查必填 Prop 是否传入
- 缺失 → 诊断（带 `.sqx` 行列）

---

## 6. ref 处理

- 扫描 `ref={Name}` 属性
- 产出强类型字段
- 在元素挂载时赋值
- 在元素卸载时置 null

---

## 7. 绑定编译

### 7.1 文本插值

```xml
<Text>{Name}</Text>
```

→

```csharp
text.BindText(() => Name.Value);
```

### 7.2 属性绑定

```xml
<Text text={Title} />
```

→

```csharp
text.BindProperty("text", () => Title.Value);
```

### 7.3 事件

```xml
<Button onClick={OnClick}>
```

→

```csharp
btn.AddEventListener("click", OnClick);
```

### 7.4 双向

```xml
<Input value={UserName} onInput={OnUserNameChanged} />
```

→

```csharp
input.BindProperty("value", () => UserName.Value);
input.AddEventListener("input", OnUserNameChanged);
```

---

## 8. 结构原语编译

### 8.1 `<Show>`

```xml
<Show when={LoggedIn}><Text>欢迎</Text></Show>
```

→

```csharp
_show0 = new ShowNode(
    condition: () => LoggedIn.Value,
    build: () => new Text("欢迎")
);
```

- `ObservableValue<bool>` 变化 → 子树挂卸
- 记忆化复用

### 8.2 `<For>`

```xml
<For each={Items}>{(it)=><Text>{it.Name}</Text>}</For>
```

→

```csharp
_for0 = new ForNode<Item>(
    source: Items,
    build: (it) => { var t = new Text(); t.BindText(() => it.Name); return t; }
);
```

- `ObservableCollection<T>` 变化 → keyed 增量增删

### 8.3 `<Switch>` / `<Match>`（M2）

编译为互斥条件子树。

### 8.4 自定义组件与 `<Slot>`

未知 PascalCase 标签按自定义组件处理。生成顺序固定为：

1. 构造组件实例。
2. 写入常量 Props 与绑定 Props。
3. 将调用处 children 按 `slot` 属性编译为调用方作用域内的 `RenderFragment`。
4. 设置默认/具名 Slot。
5. 调用子组件 `BuildElementTree()`。
6. 将子组件加入父视觉树。

组件模板中的 `<Slot>` 不生成可布局 Element，而是生成 `SlotOutlet.AttachTo(parent, slotName, fallback)`。多个根节点作为连续区域插入，不创建隐式 `View`。

### 8.5 路由声明

旧 `<Router>`/`<Route>` 结构语法已删除。路由通过 `AppWindow.UseRouter` 使用静态页面工厂注册；模板中的 `<RouterView>` / `<RouterLink>` 是 `Square.Extensions.Routing` 普通组件，不需要生成器特殊发射逻辑。

---

## 8.1 结构指令校验（Directive SDK）

| Id | 含义 |
|----|------|
| SQXD001 | 重复指令标签（Catalog 扫描） |
| SQXD002 | 指令缺少必需属性（如 Show 缺 when、For 缺 each） |
| SQXD003 | 父标签不匹配（Match 须在 Switch；Route 须在 Router/Route） |
| SQXD004 | 未知 Emit Pattern |
| SQXD005 | SkipStandalone 指令出现在模板根 |

Source Generator AST：`SqxNodeKind.Directive` + `DirectiveId`（别名归一）。

## 9. 诊断

### 9.1 错误映射

- 解析错误 → `Diagnostic`，带 `.sqx` 文件路径与行列
- 编译错误映射回 `.sqx` 位置

### 9.2 诊断类型

| 诊断 | 说明 |
|---|---|
| `SQX0001` | 语法错误 |
| `SQX0002` | 未定义的控件 |
| `SQX0003` | 必填 Prop 缺失 |
| `SQX0004` | 绑定表达式成员未找到 |
| `SQX0005` | 事件方法签名不匹配 |
| `SQX0006` | ref 名称冲突 |
| `SQX0007` | Prop 类型不匹配 |

---

## 10. 增量缓存

- 缓存键：`.sqx` 文件内容 hash + 引用的 C# 类型签名 hash
- IDE 诊断不滞后
- 单测覆盖缓存键设计

---

## 11. 平台/后端裁剪

不在生成器内处理。

由 C# `#if` + MSBuild 常量在构建层完成。

---

## 12. IDE 支持（M8）

- `.sqx` 类型检查
- 智能补全（控件名、Props、事件）
- 编译错误定位
- Source Generator Diagnostics
