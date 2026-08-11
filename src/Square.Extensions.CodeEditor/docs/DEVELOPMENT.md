# Square.Extensions.CodeEditor — 开发文档

本文档是 **CodeEditor** 包的设计与实现指南，随源码维护。面向贡献者与后续迭代。

---

## 1. 定位

| 项 | 说明 |
|---|---|
| 程序集 | `Square.Extensions.CodeEditor`（独立项目，**不**并入 `Square.Extensions`） |
| 依赖 | `Square`、TextMateSharp grammar 数据库 |
| 控件标签 | `<CodeEditor />`（需 `CodeEditorRegistration.RegisterDefaults()`） |
| 对标 | 编辑内核自研；语言层使用 **TextMate** 与 VS Code language-configuration 子集 |
| 非目标 | Roslyn、LSP、补全、诊断、嵌 WebView Monaco / Avalonia |

与 `Square.Extensions.Markdown`、`Square.Extensions`（RichText / Routing）并列，按需引用。

---

## 2. 架构

```
CodeEditor (UIElement + ITextEditor)
  ├── Model/          PieceTable · TextModel · EditStack
  ├── View/           Viewport · Painter · HitTest · Gutter
  ├── Language/       Registry · Configuration · TextMate · Themes
  └── Commands/       内置键位 → 编辑命令
```

数据流：

```
Edit → Model.ContentChanged
     → Tokenizer.Invalidate (Phase 3)
     → Viewport 可见行
     → Theme 上色
     → Painter
```

### 2.1 与 Square 其它模块

| 模块 | 关系 |
|---|---|
| `Square` | UIElement、ITextEditor、Graphics、宿主焦点/IME |
| `TextMateSharp` | TextMate grammar 解析、逐行 tokenization 与跨行 rule stack |
| `Square.Extensions` | **无**引用；**不**通过 `ExtensionRegistration` 注册 |
| `TextEditorBase` / `TextArea` | 不作为内核基类（全串 + 全量绘制不适合大文件） |
| `RichText` | 不同文档模型；不共享实现 |

---

## 3. 语言方案（对齐 Monaco / VS Code）

| 层 | 对齐 | 实现计划 |
|---|---|---|
| languageId / extensions / aliases | Monaco + VS Code | `LanguageRegistry` |
| 编辑配置 | VS Code `language-configuration.json` | `LanguageConfiguration` 解析与消费 |
| 语法分词 | VS Code **TextMate grammar** | TextMateSharp + `ITokenizer` 适配层 |
| 主题 | VS Code Color Theme 子集 | `CodeEditorTheme` + token 规则 |
| 语义高亮 / LSP | — | **不做** |

### 3.1 注册概念映射

| Monaco / VS Code | Square.Extensions.CodeEditor |
|---|---|
| `languages.register` | `LanguageRegistry.Register` |
| `setLanguageConfiguration` | `LanguageContribution.Configuration` |
| TextMate grammar | `TextMateLanguageProvider` → `ITokenizer` |
| Color Theme | `CodeEditorThemeRegistry` |
| `editor.language` | `CodeEditor.Language` |

### 3.2 Language Configuration（Phase 2）

优先实现 VS Code schema 字段：

- `comments`（行/块注释切换）
- `brackets`（括号匹配）
- `autoClosingPairs` / `autoCloseBefore`
- `surroundingPairs`
- `wordPattern`
- `indentationRules` / `onEnterRules`（基础）

### 3.3 TextMate（Phase 3）

- 使用 TextMateSharp.Grammars 内置 grammar 数据库
- 仅注册语言元数据；grammar/tokenizer 在首次使用对应 `languageId` 时懒加载
- 支持加载 VS Code 扩展中的 `package.json` 与 `tmLanguage.json`
- 按行增量 tokenize，保留 grammar rule stack
- 输出 `TokenSpan { start, length, type }`
- 未知语言回退到 `PlainTextTokenizer`

### 3.4 解析器边界

- TextMate 负责编辑过程中的实时高亮和跨行词法状态。
- 折叠引擎使用括号、XML 标签和 Python 缩进等轻量规则，保证不完整代码仍可折叠。
- ANTLR4 不进入本项目核心依赖。需要语法诊断、符号、大纲、格式化或精确折叠时，应由独立可选语言服务在后台解析并覆盖轻量结果。
- 语言服务不得阻塞输入、绘制或 TextMate tokenization。

---

## 4. 分阶段路线图

### Phase 0 — 工程骨架（当前）

- [x] 独立 csproj + solution 条目
- [x] `CodeEditorRegistration` / `CodeEditor` 壳 / 模型占位
- [x] `LanguageRegistry` / `CodeEditorThemeRegistry` 内置 plaintext + 默认主题
- [x] 本开发文档
- [ ] 测试项目与基础单测

### Phase 1 — 大文件编辑内核

**必达**

| 模块 | 内容 |
|---|---|
| PieceTable | 替换 `CodeEditorTextModel` 内部实现，公开 API 不变 |
| 行表 | `GetLineContent` / offset↔(line,col) O(log n) 或摊销高效 |
| 增量 Undo/Redo | 操作逆元，禁止全文 snapshot |
| 视口虚拟化 | 只布局/绘制可见行 ± overscan |
| 固定行高 + monospace | `tab-size` 列网格展开 `\t` |
| 编辑 | 输入、Enter、BS/Del、方向/Home/End、词跳转、Tab、单选区、指针 |
| 宿主 | 完整 `ITextEditor`、IME caret、纯文本剪贴板 |

**验收：** 万行级打开/滚动/键入可测；禁止每帧 `DrawText(全文)`。

### Phase 2 — Language Configuration

- 解析 VS Code configuration JSON
- 自动闭合、注释切换、wordPattern 选词、onEnter、括号匹配
- 内置若干语言 configuration 资源

### Phase 3 — TextMate + 主题

- TextMate grammar + 脏行增量 tokenize
- 视口按 token 分段绘制
- VS Code 主题子集；批量语言包（3a 常用 → 3b 扩展）

### Phase 4 — Chrome

- 行号 gutter、当前行高亮、查找替换、soft wrap（可选）
- Glyph margin、行 decoration、只读模式
- 折叠占位：花括号 `{...}`、数组 `[...]`、XML ` ...>`、Python 缩进块 `...`

### Phase 5 — 性能 · Chrome · 查找

- `Element.InvalidatePaint(Rect)` 局部脏区；CodeEditor caret 闪烁走局部失效
- Overview ruler（装饰色 / 查找匹配 / 视口指示）
- Find 面板状态：`FindPanelVisible`、`FindMatchCount`、`FindMatchIndex`、`GetFindMatchLines`
- 主题 `OverviewRulerBackground` / `OverviewRulerBorder`；装饰 `OverviewRulerColor`
- 折叠块整块编辑：`SelectCollapsedFoldAt` / `TryGetFoldDocumentRange` / `SelectRange`
  - Shift+点击折叠槽、双击 `⋯` 选中折叠
  - 选区与折叠相交时，`SelectedText` 与删除/输入会扩展到隐藏行
  - 折叠头上 Delete/Backspace 删除整块折叠
- 多光标：`AddCursor` / `ClearExtraCursors` / `SetCursors` / `CursorCount`
  - Alt+点击添加/切换光标；普通点击或 Esc 清除附加光标
  - 输入、Delete、Backspace、方向键对各光标同步生效
  - Shift+方向键 / Shift+Home/End 扩展各光标选区；Ctrl+Shift+方向键按词扩展

### 计划外

- Roslyn / LSP / 补全 / 诊断 / Code Fix
- 完整 minimap、diff 编辑器

---

## 5. 目录约定

```
src/Square.Extensions.CodeEditor/
  CodeEditor.cs
  CodeEditorRegistration.cs
  Model/                 # 文档缓冲与编辑事务
  View/                  # 视口与绘制（Phase 1+）
  Language/              # 注册、配置、分词、主题
  Languages/             # 嵌入 JSON 语言包（Phase 2+）
  Commands/              # 可选
  docs/
    DEVELOPMENT.md       # 本文
    ROADMAP.md           # 阶段检查清单（可与本文同步）
  README.md              # 包说明与快速开始
```

测试：

```
tests/Square.Extensions.CodeEditor.Tests/
```

---

## 6. 公共 API 约定

### 注册

```csharp
using Square.Extensions.CodeEditor;

CodeEditorRegistration.RegisterDefaults();
```

**不要**依赖 `Square.Extensions.ExtensionRegistration` 注册 CodeEditor。

### 控件

```csharp
var pad = new CodeEditor
{
    Language = "csharp",
    TabSize = 4,
    InsertSpaces = true,
    ShowLineNumbers = true,
};
pad.Model.SetValue(source); // 大文件优先
// 或 pad.Value = source;
```

### SQX / SQV

```xml
<CodeEditor Language="json" ShowLineNumbers="true" />
```

应用须先 `CodeEditorRegistration.RegisterDefaults()`，并引用本项目（及生成器若需要）。

### 大文件

- 宿主应优先 `Model.SetValue` / 后续 `ApplyEdits`，避免每键整串 `Value` get/set；`SetValue` 视为加载文档并清空 undo/redo 历史。
- Phase 0 模型为整串占位；Phase 1 换 PieceTable 后 API 不变。

---

## 7. 实现原则

1. **内核语言无关**：高亮/配置可插拔，plaintext 永远可用。
2. **视口优先**：布局与绘制复杂度相对可见行，不相对全文。
3. **增量变更**：Model 发 `ContentChanged`；tokenizer/layout 只失效脏区。
4. **AOT 友好**：无反射发现语言包；工厂与注册显式调用。
5. **公开 API 稳定**：`ICodeEditorTextModel` / Registry 行为可扩展，勿随意改签名。
6. **无 Roslyn**：任何 C# 智能留待未来独立包，不进本程序集。

---

## 8. 测试策略

| 层级 | 内容 |
|---|---|
| 模型 | SetValue、行边界、ApplyEdits、undo（P1） |
| 视口 | 可见行范围、滚动、overscan（P1） |
| 注册 | RegisterDefaults 幂等、ElementRegistry 可创建 |
| Configuration | 解析 + 自动闭合/注释（P2） |
| TextMate | scope 映射、跨行 rule stack、扩展 grammar 导入（P3） |
| 主题 | token → 颜色（P3） |

集成测试可不绑 `Square.UI.Tests` 全量依赖；本包自有测试项目即可。

---

## 9. 构建与引用

```bash
dotnet build src/Square.Extensions.CodeEditor
dotnet test tests/Square.Extensions.CodeEditor.Tests
```

应用 csproj：

```xml
<ProjectReference Include="..\..\src\Square.Extensions.CodeEditor\Square.Extensions.CodeEditor.csproj" />
```

---

## 10. 文档同步

| 文档 | 职责 |
|---|---|
| 本文件 `docs/DEVELOPMENT.md` | 架构、阶段、实现约定（源码内权威） |
| 包 `README.md` | 快速开始、依赖、非目标 |
| 仓库 `docs/API-Reference.md` | 对外 API 摘要（实现稳定后更新） |
| 仓库 `docs/Architecture.md` | 程序集表补一行（可选） |

变更阶段状态时，更新本文 §4 检查清单。

---

## 11. 当前代码状态

| 类型 | 状态 |
|---|---|
| `CodeEditorRegistration` | 已实现，幂等 |
| `CodeEditor` | 完整编辑 + 视口绘制 + 高亮 + 行号 |
| `CodeEditorTextModel` / `PieceTable` | 已实现 + 增量 undo |
| `LanguageRegistry` | TextMate 多语言数据库 + 纯文本回退 |
| `CodeEditorThemeRegistry` | default-light / default-dark |
| Soft wrap / Roslyn | 未做 |

实现细节以源码与 `ROADMAP.md` 为准。
