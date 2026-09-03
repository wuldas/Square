# Square.Extensions.CodeEditor

Square 可选代码编辑控件：

- **PieceTable** 文档模型 + 增量 undo/redo
- **视口虚拟化**绘制（只绘可见行）
- **TextMateSharp 2.0.4** + **TextMateSharp.Grammars 2.0.4** 语法高亮（内置 50+ 语言 grammar）
- 内置 grammar 按实际使用的 `languageId` 懒加载，避免启动时一次性占用全部语言包内存
- **VS Code 风格** language configuration（注释、自动闭合等）
- 行号、当前行、查找下一处、Tab 缩进
- 括号 / HTML·XML 标签层级折叠（gutter 可开关）
- Soft wrap（`WordWrap`，按可视宽度换行，不改文档）
- 查找/替换（`FindNext` / `FindPrevious` / `ReplaceNext` / `ReplaceAll`）
- 关闭 wrap 时长行横向滚动
- 滚动条（`ShowScrollBars` / `ScrollbarVisibility`，支持 auto/always/hover/scroll/hidden、CSS width/color/gutter、拖拽、轨道分页与按住重复）；`EditorScrollOffset` 和 scroll 事件可观察滚动状态
- 括号匹配高亮、查找匹配高亮
- Glyph margin + 行 decoration（断点图标、git 色条、行背景、`GutterClick`）
- 只读模式（`ReadOnly` / `ToggleReadOnly`）
- Overview ruler（装饰 / 查找标记）
- Find 面板状态（`FindPanelVisible`、匹配计数/序号）
- 局部绘制失效（`InvalidatePaint(Rect)`，caret 闪烁用）
- 折叠块整块选中与编辑：`SelectCollapsedFoldAt`、Shift+点折叠槽、双击 `⋯`；Delete/Backspace/输入会覆盖隐藏行
- 多光标：Alt+点击添加、Esc 清除；同步输入/删除；Shift+方向键选区、Ctrl+Shift+方向键按词选区

**不包含** Roslyn、LSP、补全/诊断。独立于 `Square.Extensions`。

## 快速开始

```xml
<ProjectReference Include="path\to\Square.Extensions.CodeEditor\Square.Extensions.CodeEditor.csproj" />
```

```csharp
using Square.Extensions.CodeEditor;

CodeEditorRegistration.RegisterDefaults();

var pad = new CodeEditor
{
    Language = "csharp",
    ThemeId = "default-dark",
    ShowLineNumbers = true,
};
pad.Model.SetValue("public class App { }"); // 加载文档，同时清空 undo/redo 历史
pad.SetDecoration(new CodeEditorLineDecoration
{
    Id = "bp-0",
    Line = 0,
    Glyph = "●",
    GlyphColor = Color.FromRgb(229, 57, 53),
});
pad.GutterClick += (_, e) => { /* e.Line / e.Lane */ };
```

SQX/SQV：

```xml
<CodeEditor Language="json" ShowLineNumbers="true" />
```

应用启动时调用 `CodeEditorRegistration.RegisterDefaults()`（**不是** `ExtensionRegistration`）。

## 语言支持

常用 languageId 包括 `plaintext`, `csharp`, `javascript`, `typescript`, `json`, `python`, `html`, `css`, `xml`, `sql`, `markdown`, `shellscript`, `yaml`, `rust`, `go`, `java` 等。

也可直接加载包含 `package.json` 与 `syntaxes/*.tmLanguage.json` 的 VS Code 扩展目录：

```csharp
TextMateLanguageProvider.RegisterExtension(@"C:\path\to\vscode-extension");
```

## 折叠显示

- 花括号块：`{...}`
- 多行数组/列表：`[...]`
- XML/HTML：保留开始标签首行内容，多行属性和正文折叠为 ` ...>`
- Python：多行列表使用 `[...]`，`def` / `class` / `if` / `for` 等缩进块使用 `...`

TextMate 负责即时语法着色；基础折叠由轻量结构规则完成。ANTLR4 不作为核心依赖，后续如需诊断、符号或精确语法树，可通过独立语言服务扩展接入。

## 文档

| 文档 | 说明 |
|---|---|
| [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) | 架构与实现约定 |
| [docs/ROADMAP.md](docs/ROADMAP.md) | 阶段清单 |

## Sample

```bash
dotnet run --project samples/Square.Sample.CodeEditor
```

演示语言切换、主题、行号、撤销/重做、注释与样例代码加载。

## 测试

```bash
dotnet test tests/Square.Extensions.CodeEditor.Tests
```
