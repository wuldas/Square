# Square Framework 开发计划

> 配套设计文档：`design.md`（总体架构、模块划分、Phase 1 详细设计、模板/绑定/流程控制规范、关键技术决策）
> 需求来源：`docs/Requirements.md`（v0.3 Draft）
> 范围：全模块分阶段路线图 + 里程碑排期 + 风险与缓解 + 交付说明
> 状态：M0–M2 已完成，架构重建（rebuild）已完成并合并至 main。后续扩展：`.sqv` Vue 模板前端初步实现（规范化路径，详见 `docs/vue-plan.md`）；新增独立 `Square.Extensions.Markdown`（Markdig + TextMate）与 `Square.Extensions.CodeEditor`（PieceTable + TextMate）扩展、平台截图 `PlatformScreenshot`（Win32/X11）、PNG 编码 `BitmapPngEncoder` 与 BMP 解码 `BmpPngConverter`、DOM `Range` 文本选择模型与 `TextFragment` 字符级命中测试；Software Renderer 完成性能优化（位图像素/裁剪区域缓存、批量 BGRA 填充）。

---

## 0. 范围与边界说明

Square 是 **纯 C#、编译优先（Compile First）、NativeAOT 优先、渲染后端可插拔** 的跨平台 UI 框架。UI 以无文件级根标签的单文件 `.sqx` 描述：唯一 `<template>`，以及各自最多一个的 `<script lang="csharp">` / `<style>`；由 Source Generator 在编译期解析并生成 C#，运行时零解析。

六大核心约束（Compile First / Pure C# Core / NativeAOT First / Backend Independent / Retained Rendering / Low Coupling）见 `design.md` §1；完整架构与 Phase 1 详细设计见 `design.md` §2–§4。

---

## 1. 分阶段路线图

| 里程碑 | 目标 | 涉及模块 | 退出标准（Exit Criteria） |
|---|---|---|---|
| **M0 脚手架** | 解决方案与全部 `Square.*` 空项目、目录规范、`.editorconfig`、AOT/Trim 发布配置 | 全部 | 空项目可编译；`dotnet build` 通过 |
| **M1 Phase 1 MVP** | 编译优先可运行 Demo：`.sqx`→C#、基础 CSS、flex 布局、纯C#软件渲染、基础控件、事件、Win32 宿主、构建层平台/后端裁剪（C# `#if` + MSBuild 常量）与组件生命周期机制、NativeAOT 验证 | Markup, SourceGenerator, Runtime, UI, Controls, CSS(子集), Layout(子集), Graphics, Rendering, Platform(Win32), Backends(Software), Text(基础), DevTools(基础诊断) | 一个 `.sqx` 示例经 Source Generator 编译为 AOT 可执行，窗口渲染出 Text/Button/Input 并响应点击；`<Show>`/`<For>` 流程控制与组件生命周期钩子可用 |
| **M2 CSS 完整化 + 组件组合 + 动画 + 主题** | 默认/具名 Slot、fallback、嵌套组件；`Signal<T>` + `SignalHub` 跨组件通信与 Dispatcher 跨线程投递；完整 Selector/Cascade/Specificity/Var/Inheritance/Pseudo/Animation；Theme 系统 | SourceGenerator, Runtime, UI, CSS, Animation | 插槽保持调用方作用域且无隐式布局容器；后台信号安全送达 UI；CSS 与组件组合测试通过 |
| **M3 扩展控件 + 路由** | 内存路由、参数/通配符、嵌套布局、Link；List/Tree/Menu/Dialog/ScrollViewer/Grid/Popup/Swiper/Navigator | Router, Controls, Layout, Platform | 前进/后退与路由生命周期正确；各控件可交互、可组合 |
| **M4 图形后端扩展** | Skia / Vulkan 后端完善（保持 `IRenderContext` 不变） | Backends, Graphics | 同一 Demo 切换后端渲染一致 |
| **M5 跨平台桌面** | Linux(X11)、macOS 平台宿主；高 DPI/高刷新率打磨 | Platform, Rendering, Text | 三桌面平台 AOT 可执行均运行 |
| **M6 移动端与 WebAssembly** | Android / iOS / WASM 平台层（最小实现） | Platform, Backends, Runtime | 目标平台可启动并渲染基础 UI |
| **M7 文本与 Canvas 完整** | BiDi、Font Fallback、Caret/Selection/HitTest、标准 RichTextBox/WYSIWYG 富文本模型与渲染、Canvas `CanvasRenderingContext2D` 兼容层→DrawCommand | Text, Controls(Canvas), Graphics, Extensions | 复杂文本/富文本编辑与 Canvas 绘图可运行 |
| **M8 工具链** | 完整 Source Generator 诊断、IDE 智能提示/补全、编译期检查 | DevTools, SourceGenerator | IDE 内 `.sqx` 报错可定位、可补全 |

> Phase 1（M0+M1）的详细设计（项目结构、各模块接口要点、模板/绑定/流程控制规范、示例应用、任务清单、关键技术决策）已独立成文于 `design.md` §4，本节仅保留路线图视图。

---

## 2. 里程碑时间建议（相对，供排期参考）

- M0：约 1 周（脚手架与规范）
- M1：约 6–8 周（核心 MVP，可并行：Generator/Markup 线、Graphics/Backend 线、Controls/Layout 线）
- M2–M8：每个约 2–4 周，按路线图递进；M1 验收后细化

---

## 3. 风险与缓解

| 风险 | 缓解 |
|---|---|
| Source Generator 增量缓存导致 IDE 诊断滞后 | 严格设计 `IIncrementalGenerator` 缓存键；单测覆盖 |
| 纯 C# 软件渲染性能不足 | 预乘 Alpha + SIMD + 脏区；明确为 Phase 1 后端，Skia 等后续补齐 |
| 完整 CSS/布局引擎工作量巨大 | M1 仅子集，M2 再扩展；优先 flex + 基础属性 |
| NativeAOT 裁剪误删后端/平台代码 | 后端/平台用 `Register()` 显式注册 + `[DynamicDependency]`/trim 注解；平台/后端裁剪下沉构建层（C# `#if` + MSBuild `DefineConstants`/条件 `ProjectReference`，见 `design.md` §4.5.2） |
| 文本引擎（BiDi/Fallback）复杂 | M1 仅基础，M7 引入成熟的纯 C# 文本整形/排版思路（自研或集成纯 C# 文本库） |

---

## 4. 交付说明

- 本文档记录项目最初的 M0/M1 交付计划，已经完成，不再作为当前状态来源；实时进度与下一步以 `docs/Roadmap.md` 为准。
- 本计划已评审通过，正式计划文档位于仓库根目录 `plan.md`，配套详细设计文档为 `design.md`。
- 详细设计（`design.md`）覆盖：项目定位与核心约束、总体架构（保留模式管线）、模块划分与职责、Phase 1 详细设计（M0+M1，含模板/绑定/流程控制规范）、关键技术决策。
- 后续 M2–M8 的详细设计将在 Phase 1 验收后由独立计划文档细化。
- M0/M1 已完成，后续工作按 `docs/Roadmap.md` 推进。
