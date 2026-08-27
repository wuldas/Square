# Square.Compiler 分区 AST 重构实施 TODO

> **For Hermes:** 按本计划逐阶段实施；每个行为变更严格遵循 RED → GREEN → REFACTOR，并在阶段边界独立提交。

**Goal:** 将 `.sqx` / `.sqv` 从“模板 AST + Script/Style 字符串”的扁平模型重构为独立的 Template、Script、Style Syntax AST，再通过方言 lowerer 汇入共享组件 IR，统一 Generator、LSP 与 Markup 的解析、诊断和源码范围。

**Architecture:** `Square.Compiler` 拥有共享文档分区模型和三个 section syntax AST。SQX 与 SQV 分别保留源码保真的模板 AST，随后 lowering 到语言无关 Template/Component IR；Script 使用 Roslyn synthetic wrapper 构造可映射回 section 的 C# AST；Style 使用 compiler-side CSS syntax AST，并由生成代码 adapter 对接现有 runtime `Square.CSS.Ast`，不让 `Square` runtime 依赖 analyzer DLL，也不新增程序集。

**Tech Stack:** .NET 10、netstandard2.0、Roslyn `Microsoft.CodeAnalysis.CSharp`、现有 SQX/SQV lexer/parser、现有 runtime CSS tokenizer/parser、xUnit、Square Language Server。

---

## 1. 已确认的架构决策

- [x] 分区 Syntax AST 放在 `Square.Compiler`，不新增 `Square.Syntax` 程序集。
- [x] `Square.Compiler` 不直接引用 `Square` / `Square.CSS`，避免 analyzer/runtime 循环依赖。
- [x] SQX 与 SQV 各自保留源码 AST，不在 parse 阶段把 SQV 节点伪装成 `SqxNode`。
- [x] SQX/SQV 通过独立 lowerer 汇入同一套语言无关 IR。
- [x] Generator、Emitter、DirectiveValidator、TemplateSemanticAnalyzer 最终只消费共享 IR。
- [x] Script/Style 不再以 `string ScriptCode` / `string StyleCode` 作为唯一事实来源。
- [x] 所有 section 和 syntax 节点使用 absolute zero-based `offset + length`；行列只在 adapter/UI 边界计算。
- [x] Style AST 到 runtime CSS AST 通过生成代码 adapter 对接，不让 runtime 引用 Compiler。
- [x] 迁移期保留旧模型 adapter，禁止大爆炸式一次替换。
- [x] 不引入 ANTLR4。

---

## 2. 当前基线与问题

### 2.1 当前模型

```text
SqxDocument
├─ SqxTemplate Template        // 真正 AST
├─ string ScriptCode           // 原始字符串
├─ string ScriptLang
├─ string StyleCode            // 原始字符串
├─ string Namespace
└─ string Access
```

主要文件：

- `src/Square.Compiler/Parser/SqxAst.cs`
- `src/Square.Compiler/ParserCore/SqxCoreAst.cs`
- `src/Square.Compiler/Parser/SqxParser.cs`
- `src/Square.Compiler/Parser/SqvDocumentParser.cs`
- `src/Square.Compiler/Template/TemplateDocument.cs`
- `src/Square.Compiler/LanguageServices/SqxCoreParserFacade.cs`

### 2.2 当前问题

- SQX 与 SQV 各自实现一套顶层 section scanner，section cardinality、注释、嵌套与容错规则容易漂移。
- `CoreDocument` 已有 `CoreTemplate/CoreScript/CoreStyle`，但只服务 SQX，随后又被扁平化。
- SQV 在 parse 阶段直接降低为 `SqxDocument` / `SqxNode`，丢失 `@`、`:`、`#`、`v-*` 与 modifier 的源码形状。
- Script metadata、C# syntax 与 source range 没有统一 AST。
- Style 只有字符串；LSP class completion、folding、color 仍需正则或独立扫描。
- `TemplateDocument` 再复制一遍扁平字段，无法成为明确 IR 边界。
- Markup facade 只能为 Script/Style 伪造 `line=1,column=1`。
- Generator 与 LSP 无法针对三个 section 独立缓存、诊断、补全和失效。

---

## 3. 目标模型

```text
ComponentDocumentSyntax
├─ Dialect: Sqx | Sqv
├─ SourcePath
├─ ComponentName
├─ TemplateSectionSyntax
│  ├─ SqxTemplateSyntax
│  └─ SqvTemplateSyntax
├─ ScriptSectionSyntax?
│  ├─ ScriptMetadataSyntax
│  ├─ RawText
│  ├─ UsingDirectives
│  ├─ MemberSyntax
│  └─ RoslynSourceMap
└─ StyleSectionSyntax?
   ├─ RawText
   ├─ CssRuleSyntax
   ├─ CssSelectorSyntax
   ├─ CssDeclarationSyntax
   └─ CssAtRuleSyntax

SqxTemplateSyntax ─┐
                   ├─> TemplateLowerer ─> ComponentDocumentIr
SqvTemplateSyntax ─┘                         ├─ TemplateIr
ScriptSectionSyntax ────────────────────────>├─ ScriptIr
StyleSectionSyntax ─────────────────────────>└─ StyleIr
```

### 3.1 共享 section contract

每个 section 必须保留：

- `FullRange`：包括 opening/content/closing tag；
- `OpeningTagRange`；
- `ContentRange`；
- `ClosingTagRange`；
- section 名称与 opening attributes；
- 原始 source slice；
- 是否完整闭合；
- section 级 diagnostics。

顶层约束：

- `template`：必须且只能一个；
- `script`：可选，最多一个；
- `style`：可选，最多一个；
- section 顺序不作为语义约束，但推荐 template → script → style；
- section 外只允许 whitespace/comment；
- duplicate 诊断定位第二个 section；
- tolerant 模式只恢复 section 边界，不伪造成功文档。

### 3.2 Template Syntax 与 IR

- `SqxTemplateSyntax` 保存 SQX 原生 tag、attribute、expression、directive 源形状。
- `SqvTemplateSyntax` 保存 Vue 属性简写、完整指令名、动态参数和 modifier，不在 lexer/parser 中转换成 SQX 属性。
- `SqxTemplateLowerer` 与 `SqvTemplateLowerer` 分别转换到共享 `TemplateIrNode`。
- `TemplateIr` 继续表达 Element、Text、Expression、For、IfChain、Slot 等生成语义。
- Directive catalog normalization 发生在 lowering/semantic 阶段，不污染 source syntax。

### 3.3 Script Syntax

`<script>` 内容是组件 class body fragment，不是完整 C# compilation unit。解析器必须：

1. 分离允许位于 section 顶部的 `using`；
2. 将剩余成员放入 synthetic namespace/partial class wrapper；
3. 使用 Roslyn 解析 synthetic text；
4. 维护 wrapper offset → section absolute offset 映射；
5. 把 Roslyn diagnostics 映射回 `.sqx/.sqv`；
6. 保留 RawText，Emitter 迁移前后输出不变。

禁止直接把裸成员文本传给 `CSharpSyntaxTree.ParseText` 后把 wrapper diagnostics 暴露给用户。

### 3.4 Style Syntax 与 runtime adapter

Compiler 内建立 source-preserving CSS syntax AST，至少覆盖当前 runtime parser 已支持的：

- type/class/id/universal/attribute selector；
- pseudo class/pseudo element；
- descendant/child/adjacent/general-sibling combinator；
- declaration、value、`!important`；
- `@import`、`@media`、`@keyframes` 与普通 at-rule；
- comment/string/range。

对接方式：

- Compiler 不引用 `Square.CSS.Ast`；
- `StyleAstRuntimeEmitter` 将 compiler Style AST 生成为使用 fully-qualified `Square.CSS.Ast.*` 的 C# 构造代码；
- adapter 完成前继续保留 raw CSS runtime parse fallback；
- parity tests 对同一 fixture 比较 compiler AST snapshot 与 runtime CSS AST snapshot；
- 只有 parity matrix 通过后，才将生成组件从 runtime raw parse 切到 generated AST adapter。

---

## 4. 分阶段实施

## Phase A1：冻结共享 Document/Section Syntax

### Task A1.1：建立 section source-range contract

**Objective:** 用单一模型表达三个 section 的绝对范围和完整性。

**Files:**

- Create: `src/Square.Compiler/Syntax/ComponentDialect.cs`
- Create: `src/Square.Compiler/Syntax/ComponentSectionKind.cs`
- Create: `src/Square.Compiler/Syntax/ComponentSectionSyntax.cs`
- Create: `src/Square.Compiler/Syntax/ComponentDocumentSyntax.cs`
- Test: `tests/Square.SourceGenerator.Tests/ComponentSectionScannerTests.cs`

**TDD:**

- [x] RED：LF/CRLF 下 Full/Opening/Content/Closing range 精确。
- [x] RED：空 script/style content range 精确。
- [x] RED：带 metadata、quoted `>`、comment 的 opening tag range 精确。
- [x] GREEN：添加 immutable-looking netstandard2.0-compatible syntax classes。
- [x] REFACTOR：所有范围统一使用 `SquareSourceRange`。

**Focused command:**

```bash
dotnet test tests/Square.SourceGenerator.Tests/Square.SourceGenerator.Tests.csproj --filter FullyQualifiedName~ComponentSectionScannerTests
```

### Task A1.2：实现共享 ComponentSectionScanner

**Objective:** 删除 SQX/SQV 顶层 section 扫描规则的行为漂移。

**Files:**

- Create: `src/Square.Compiler/Syntax/ComponentSectionScanner.cs`
- Test: `tests/Square.SourceGenerator.Tests/ComponentSectionScannerTests.cs`

**TDD:**

- [x] RED：missing/duplicate/unclosed/unknown/outside-content fixture matrix。
- [x] RED：duplicate 诊断定位第二个 section。
- [x] RED：section content 中的 `<template>` 字符串、comment、nested SQV slot template 不终止外层 section。
- [x] RED：strict/tolerant 模式边界一致，只有恢复结果不同。
- [x] GREEN：实现一次 scanner，输出 section syntax + diagnostics。
- [x] REFACTOR：删除 scanner 内行列计算，只保存 absolute ranges。

### Task A1.3：让 SQX/SQV document parser 共用 scanner

**Objective:** 替换两套 `SplitSections`，保持模板解析和生成输出不变。

**Files:**

- Modify: `src/Square.Compiler/ParserCore/SqxCoreParser.cs`
- Modify: `src/Square.Compiler/Parser/SqvDocumentParser.cs`
- Modify: `src/Square.Compiler/LanguageServices/SquareDocumentService.cs`
- Test: `tests/Square.SourceGenerator.Tests/LanguageServiceParityTests.cs`
- Test: `tests/Square.SourceGenerator.Tests/GeneratorDiagnosticsTests.cs`

**TDD:**

- [x] RED：同一 section 错误在 SQX/SQV 使用对应 ID，但 range 相同。
- [x] GREEN：两端委托共享 scanner。
- [x] 保留现有模板 parser，不在本 task 改节点模型。
- [x] 删除旧 `SplitSections` 前确认所有 fixture 通过。

**Phase A1 exit criteria:**

- [x] 仓库只有一个顶层 section scanner。
- [x] SQX/SQV section range parity 覆盖 LF/CRLF/Unicode。
- [x] Generator 输出 snapshot 不变化。
- [x] `Square.Compiler` netstandard2.0/net10.0 均构建通过。

**Commit:** `重构: 统一组件文档分区语法`

---

## Phase A2：ScriptSectionSyntax 与 Roslyn AST

### Task A2.1：定义 script metadata syntax

**Files:**

- Create: `src/Square.Compiler/Syntax/Script/ScriptSectionSyntax.cs`
- Create: `src/Square.Compiler/Syntax/Script/ScriptMetadataSyntax.cs`
- Create: `src/Square.Compiler/Syntax/Script/ScriptAttributeSyntax.cs`
- Test: `tests/Square.SourceGenerator.Tests/ScriptSectionSyntaxTests.cs`

**TDD:**

- [ ] lang/namespace/name/access 保存 value 与 attribute/value range。
- [ ] duplicate/unknown/invalid access 定位精确。
- [ ] SQX/SQV 共用相同 metadata parser 与 defaults。

### Task A2.2：实现 C# member-fragment parser

**Files:**

- Create: `src/Square.Compiler/Syntax/Script/CSharpScriptSyntaxParser.cs`
- Create: `src/Square.Compiler/Syntax/Script/RoslynSourceMap.cs`
- Test: `tests/Square.SourceGenerator.Tests/ScriptSectionSyntaxTests.cs`

**TDD:**

- [ ] using + field/property/method 解析成功。
- [ ] 空 script 产生空 AST，不产生错误。
- [ ] C# diagnostic 映射回 section absolute offset。
- [ ] LF/CRLF、Unicode、多个 using 的 mapping 正确。
- [ ] synthetic wrapper token 不出现在用户诊断中。

### Task A2.3：迁移语义分析器和 Emitter

**Files:**

- Modify: `src/Square.Compiler/LanguageServices/TemplateSemanticAnalyzer.cs`
- Modify: `src/Square.Compiler/Emit/ComponentEmitter.cs`
- Modify: `src/Square.Compiler/SqxGenerator.cs`
- Test: `tests/Square.SourceGenerator.Tests/TemplateSemanticAnalyzerTests.cs`
- Test: `tests/Square.SourceGenerator.Tests/GeneratorTests.cs`

**Steps:**

- [ ] `[Prop]` 提取从 regex 迁移到 Roslyn Script AST。
- [ ] using/member 输出从 Script AST 读取。
- [ ] 保留兼容 `ScriptCode` adapter，消费者全部迁移后再删。

**Commit:** `重构: 引入脚本分区语法树`

---

## Phase A3：StyleSectionSyntax 与 CSS AST

### Task A3.1：定义 compiler-side CSS syntax nodes

**Files:**

- Create: `src/Square.Compiler/Syntax/Style/StyleSectionSyntax.cs`
- Create: `src/Square.Compiler/Syntax/Style/CssSyntaxNodes.cs`
- Create: `src/Square.Compiler/Syntax/Style/CssSyntaxToken.cs`
- Test: `tests/Square.SourceGenerator.Tests/StyleSectionSyntaxTests.cs`

**Requirements:**

- [ ] 每个 selector/declaration/value/at-rule 保留 source range。
- [ ] 节点保留 raw slice，不只保存 normalized value。
- [ ] 错误编辑态可产生 bounded partial AST。
- [ ] 不引用 `Square.CSS.Ast`。

### Task A3.2：实现 CSS syntax parser 与 runtime parity fixtures

**Files:**

- Create: `src/Square.Compiler/Syntax/Style/CssSyntaxParser.cs`
- Create: `tests/Square.SourceGenerator.Tests/Fixtures/Styles/*.css`
- Create: `tests/Square.SourceGenerator.Tests/StyleAstParityTests.cs`
- Modify: `tests/Square.SourceGenerator.Tests/Square.SourceGenerator.Tests.csproj`（仅在测试侧引用 runtime CSS）

**TDD matrix:**

- [ ] selectors/combinators/attribute selectors/pseudo states。
- [ ] declarations、unit、function、CSS variable、important。
- [ ] import/media/keyframes/unknown at-rule。
- [ ] malformed declaration/block/comment/string。
- [ ] compiler AST 与 runtime AST normalized snapshot parity。

### Task A3.3：迁移 LSP style 功能

**Files:**

- Modify: `src/Square.Compiler/LanguageServices/TemplateCompletionService.cs`
- Modify: `src/Square.Compiler/LanguageServices/TemplateColorService.cs`
- Modify: `src/Square.Compiler/LanguageServices/TemplateFoldingService.cs`
- Test: `tests/Square.LanguageServer.Tests/LanguageServerCssClassCompletionTests.cs`
- Test: `tests/Square.LanguageServer.Tests/LanguageServerFoldingAndColorTests.cs`

**Steps:**

- [ ] class completion 从 regex 改为 Style AST selector index。
- [ ] color 仅扫描 style/inline-style 范围。
- [ ] folding 使用 section/style AST range。
- [ ] comment/string 中的伪 class/color 不产生候选。

### Task A3.4：生成 runtime CSS AST adapter

**Files:**

- Create: `src/Square.Compiler/Emit/StyleAstRuntimeEmitter.cs`
- Modify: `src/Square.Compiler/Emit/ComponentEmitter.cs`
- Test: `tests/Square.SourceGenerator.Tests/StyleAstRuntimeEmitterTests.cs`
- Test: `tests/Square.CSS.Tests/CssTests.cs`

**Gate:**

- [ ] parity matrix 未通过前不移除 runtime raw parse fallback。
- [ ] generated C# 使用 fully-qualified `Square.CSS.Ast.*`，Compiler 无 runtime reference。
- [ ] generated AST 与当前 runtime parser 的渲染行为一致。

**Commit:** `重构: 引入样式分区语法树`

---

## Phase A4：SQX/SQV Template Syntax 分离与统一 lowering

### Task A4.1：定义语言无关 Template IR

**Files:**

- Create: `src/Square.Compiler/Template/Ir/TemplateIrNode.cs`
- Create: `src/Square.Compiler/Template/Ir/TemplateIrDocument.cs`
- Move/Replace: `src/Square.Compiler/Template/TemplateDocument.cs`
- Test: `tests/Square.SourceGenerator.Tests/TemplateIrTests.cs`

**Requirements:**

- [ ] IR 不包含 `v-*`、`@`、`:`、`#` 等 source syntax。
- [ ] IR 不使用 `Sqx*` 命名。
- [ ] Element/Text/Expression/For/IfChain/Slot 均有明确节点。
- [ ] IR 节点保留 origin syntax range/link，供诊断和 definition 使用。

### Task A4.2：建立 SQX source syntax AST

**Files:**

- Create: `src/Square.Compiler/Syntax/Template/Sqx/*.cs`
- Modify: `src/Square.Compiler/ParserCore/SqxCoreLexer.cs`
- Modify: `src/Square.Compiler/ParserCore/SqxCoreParser.cs`
- Test: `tests/Square.SourceGenerator.Tests/SqxTemplateSyntaxTests.cs`

**Steps:**

- [ ] Core AST 迁移/重命名为 SQX source syntax。
- [ ] attribute/expression/tag range 完整。
- [ ] strict/tolerant parse 共享 token model。
- [ ] 当前 `SqxNode` 作为 compatibility adapter，不再是 parser 输出事实源。

### Task A4.3：建立 SQV source syntax AST

**Files:**

- Create: `src/Square.Compiler/Syntax/Template/Sqv/*.cs`
- Modify: `src/Square.Compiler/Parser/SqvLexer.cs`
- Modify: `src/Square.Compiler/Parser/SqvTemplateParser.cs`
- Replace: `src/Square.Compiler/Parser/SqvAttributeConverter.cs`（若当前仍内嵌则拆出）
- Test: `tests/Square.SourceGenerator.Tests/SqvParserTests.cs`

**Requirements:**

- [ ] 保留原始 directive name、argument、dynamic argument、modifier ranges。
- [ ] `@click.stop` 不在 parser 阶段改写为 `onClick`。
- [ ] `v-if/v-for/v-slot/v-model` source AST 与 lowering 分离。
- [ ] unsupported Vue syntax 可解析时先形成 syntax，再由 semantic/lowering 报明确诊断。

### Task A4.4：实现两个 lowerer

**Files:**

- Create: `src/Square.Compiler/Template/Lowering/SqxTemplateLowerer.cs`
- Create: `src/Square.Compiler/Template/Lowering/SqvTemplateLowerer.cs`
- Test: `tests/Square.SourceGenerator.Tests/TemplateLoweringParityTests.cs`

**Parity scenarios:**

- [ ] SQX `onClick={OnSave}` 与 SQV `@click="OnSave"` 降为同一 Event IR。
- [ ] SQX `Show/For` 与 SQV `v-if/v-for` 降为同一控制流 IR。
- [ ] SQX slot 与 SQV `v-slot/#` 降为同一 Slot IR。
- [ ] 静态/动态 props、class、style 与 SVG 节点等价。

**Commit:** `重构: 分离 SQX SQV 语法树并统一 IR`

---

## Phase A5：消费者迁移与旧 AST 删除

### Task A5.1：迁移 Generator/Emitter/Validator

- [ ] `SqxGenerator` 输入改为 `ComponentDocumentSyntax + ComponentDocumentIr`。
- [ ] `ComponentEmitter` 只消费 IR 与 section semantic model。
- [ ] `DirectiveValidator` 只消费 IR + origin range。
- [ ] `TemplateSemanticAnalyzer` 不再正则提取 script。
- [ ] 生成输出 snapshot 与当前基线一致。

### Task A5.2：迁移 Language Server

- [ ] diagnostics 按 section 独立计算与合并。
- [ ] completion 根据 cursor 所在 section 分派。
- [ ] semantic tokens 使用 source syntax，不使用 lowered name length。
- [ ] folding/symbols/definition 使用 AST range，删除手工 `<`/`>` 扫描。
- [ ] document cache 可独立失效 Template/Script/Style。

### Task A5.3：迁移 Markup facade

- [ ] `Square.Markup.Parser.SqxParser` 从新 syntax/IR adapter 转换。
- [ ] Script/Style 返回真实 source line/column/range。
- [ ] 保持现有 public API，除非单独批准 breaking change。

### Task A5.4：删除旧模型与重复解析器

**Delete/Replace candidates:**

- `src/Square.Compiler/Parser/SqxAst.cs` 中扁平 `SqxDocument`；
- `src/Square.Compiler/ParserCore/SqxCoreAst.cs` 中重复 document section 类型；
- `src/Square.Compiler/Template/TemplateDocument.cs` 扁平字段；
- SQX/SQV 私有 `SplitSections`；
- `ScriptCode` / `ScriptLang` / `StyleCode` compatibility properties；
- LSP style/script 正则扫描路径。

**Gate:** 只有全仓搜索确认无消费者后删除。

**Commit:** `重构: 完成组件分区 AST 迁移`

---

## 5. 验证矩阵

每个 Phase 至少运行：

```bash
dotnet build src/Square.Compiler/Square.Compiler.csproj -c Release -f netstandard2.0
dotnet build src/Square.Compiler/Square.Compiler.csproj -c Release -f net10.0
dotnet test tests/Square.SourceGenerator.Tests/Square.SourceGenerator.Tests.csproj -c Release
dotnet test tests/Square.LanguageServer.Tests/Square.LanguageServer.Tests.csproj -c Release
dotnet test tests/Square.Markup.Tests/Square.Markup.Tests.csproj -c Release
git diff --check
```

涉及 Style runtime adapter 时追加：

```bash
dotnet test tests/Square.CSS.Tests/Square.CSS.Tests.csproj -c Release
```

涉及三端 tooling 时追加：

```bash
cd tooling/square-language && npm test
cd ../vscode-square && npm run package
cd ../rider-square && ./gradlew buildPlugin -PlocalRiderPath="C:/Users/Wuldas/AppData/Local/Programs/Rider" --offline
cd ../visualstudio-square && dotnet build Square.VisualStudio.csproj -c Release
node verify-vsix.mjs bin/Release/Square.LanguageSupport.VisualStudio.vsix
```

最终收口：

- [ ] 使用系统 TEMP 下 `hermes-verify-` 脚本执行 focused/ad-hoc verification，并删除脚本。
- [ ] 构建真实 Sample，确认 Source Generator 使用新 AST/IR。
- [ ] 比较关键 generated `.g.cs` snapshot，排除无意生成变化。
- [ ] 对 SQX/SQV 等价 fixture 做 lowering parity。
- [ ] 对 LF/CRLF/Unicode 做 range parity。
- [ ] 三端安装包包含同一版本 Server。
- [ ] 不把 focused verification 表述为全仓 canonical suite green。

---

## 6. 风险与控制

### 风险：一次性替换导致 Generator 与 LSP 同时失效

控制：按 section scanner → Script → Style → Template → consumer 顺序迁移；每阶段保留 adapter 并独立提交。

### 风险：SQV source fidelity 在 lowering 前丢失

控制：SQV parser 只构造 Sqv source syntax；所有 alias/modifier 转换移入 `SqvTemplateLowerer`。

### 风险：Compiler CSS parser 与 runtime CSS parser 漂移

控制：共享 fixture + normalized AST parity tests；runtime adapter 切换受 parity gate 约束。

### 风险：Roslyn synthetic wrapper 导致错误 range 偏移

控制：专门 `RoslynSourceMap`，覆盖 LF/CRLF/Unicode/using/member diagnostics；禁止散落手工 offset 修正。

### 风险：netstandard2.0 analyzer 兼容

控制：Syntax/IR 模型避免 `record`、`required`、`System.HashCode` 等 analyzer target 不兼容 API；每阶段双目标构建。

### 风险：新旧 AST 长期并存

控制：compatibility adapter 带明确 TODO 和删除 gate；Phase A5 全仓搜索并删除旧事实源。

---

## 7. 非目标

本重构阶段不包含：

- 完整 CSS 规范实现；
- JavaScript/Vue runtime；
- Previewer/Designer/Hot Reload；
- 新程序集拆分；
- Runtime template parser；
- LSP incremental sync/cancellation/workspace project loading；
- Visual Studio/Rider/VS Code 客户端架构重写；
- 无关的 Generator 输出格式重排。

---

## 8. 提交边界

计划文档先独立提交，后续建议提交顺序：

1. `文档: 规划组件分区 AST 重构`
2. `重构: 统一组件文档分区语法`
3. `重构: 引入脚本分区语法树`
4. `重构: 引入样式分区语法树`
5. `重构: 分离 SQX SQV 语法树并统一 IR`
6. `重构: 完成组件分区 AST 迁移`

每次提交必须：

- 只暂存该阶段文件；
- 检查 `git diff --cached --name-status`；
- 检查 `git diff --cached --check`；
- 保留任何无关工作区修改；
- 不自动 push。
