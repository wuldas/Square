# 编码规范

> Document Revision: 0.3
> 适用于所有 `Square.*` 项目

---

## 1. 基本约定

### 1.1 语言

- C# 13+ / net10.0
- 编译器警告视为错误：`TreatWarningsAsErrors = true`
- 启用 `<Nullable>enable</Nullable>`
- 启用 `<ImplicitUsings>enable</ImplicitUsings>`

### 1.2 命名

| 类别 | 风格 | 示例 |
|---|---|---|
| 类型（类/接口/结构/枚举） | PascalCase | `Button`, `IRenderContext` |
| 公共成员（方法/属性/字段/事件） | PascalCase | `OnClick`, `Title` |
| 私有字段 | _camelCase | `_items`, `_show0` |
| 局部变量 | camelCase | `myBtn`, `itemCount` |
| 参数 | camelCase | `sender`, `args` |
| 接口 | I 前缀 | `IRenderContext`, `IPlatformHost` |
| 泛型参数 | T 前缀 | `T`, `TItem` |
| 命名空间 | PascalCase | `Square.UI`, `Square.Graphics` |

### 1.3 文件

- 一个公共类型一个文件
- 文件名 = 类型名
- 目录结构映射命名空间

---

## 2. NativeAOT 合规

### 2.1 禁止

- `Reflection.Emit`
- 运行时代码生成（`Expression.Compile`）
- `dynamic` 类型
- 运行时 `Assembly.Load`
- 反射式属性系统（`DependencyProperty` 式）

### 2.2 要求

- P/Invoke 用 `LibraryImport`（源生成）
- 绑定用 `ObservableValue<T>` 委托订阅
- 避免 `MakeGenericMethod` 运行时构造
- 标注 trim 注解（`DynamicallyAccessedMembers` 等，必要时）

### 2.3 构建层裁剪

- 平台/后端用 `#if PLATFORM_*` / `#if BACKEND_*`
- MSBuild `DefineConstants` 控制
- 条件 `ProjectReference` 装配

---

## 3. 模块边界

### 3.1 依赖方向

```
SourceGenerator → Markup
Controls/UI/Rendering/CSS/Text → Runtime + Graphics(抽象，按实际需要引用)
Backends/Platform → Graphics(抽象) + Runtime 接口
Hosting → 聚合上述全部模块（应用入口层）
```

### 3.2 禁止

- 核心层反向依赖 Backend/Platform
- `Square.Graphics` 依赖 CSS/Controls/Component/Runtime
- 模块间直接依赖具体实现（须通过接口）

---

## 4. 属性与绑定

### 4.1 ObservableValue

- 强类型 `ObservableValue<T>`
- 委托订阅，零反射
- `{expr}` 编译期解析成员引用

### 4.2 Props

- `[Prop]` 特性声明
- `ObservableValue<T>` 包装
- 单向数据流，子不可改写
- 编译期校验必填

### 4.3 ref

- 模板内 `ref={Name}`
- 生成器产出强类型字段
- 命令式操作不覆盖已绑定属性

---

## 5. 控件

### 5.1 命名

- PascalCase 控件类型
- `.sqx` 内标签同名

### 5.2 结构

- 控件 = 视觉 + 行为 + 默认样式
- 结构原语（Show/For/Switch/Match/Slot/Router）由生成器经指令 Catalog 编译，非普通 UI 控件

### 5.3 按标签即用

- 控件：Source Generator 内置标签表 + `UIDocument.CreateElement` 注册
- 结构指令：`[SqxDirective]` + 编译期扫描

---

## 6. CSS

### 6.1 `<style>` 段

- 标准 CSS 语法
- 不兼容浏览器私有扩展

### 6.2 内联

- `style="..."` 内联样式
- `class="..."` 类名（空格分隔）

---

## 7. 诊断

### 7.1 错误映射

- 解析错误带 `.sqx` 文件路径与行列
- `Diagnostic` 回抛供 IDE 定位

### 7.2 诊断代码

- `SQX0xxx`：Source Generator 诊断
- `SQX1xxx`：Markup 解析诊断

---

## 8. 测试

### 8.1 单元测试

- `Square.*.Tests` 项目（xUnit）
- 每个模块独立测试

### 8.2 覆盖

- Parser：AST 正确性
- Generator：生成代码正确性、诊断输出
- CSS：Selector/Cascade/Specificity
- Layout：盒模型/Flex
- 绑定：ObservableValue/ObservableCollection

---

## 9. 性能

- 优先 NativeAOT
- 启动速度、小体积、少依赖、低内存
- 高 DPI 物理像素对齐
- 脏区增量重绘
- 避免 GC 压力（复用、池化、struct）

---

## 10. 注释

- 不写无意义注释
- 公共 API 用 XML 文档注释
- 复杂逻辑用简短说明注释

---

## 11. 格式化

- 4 空格缩进
- `{` 不换行（Allman 风格仅用于类型/方法，控制块用 K&R）
- 实际以 `.editorconfig` 为准
