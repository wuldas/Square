---
name: square-project
description: "帮助 AI Agent 快速理解和修改 Square 仓库。首次进入项目、解释架构、定位源码、选择构建与测试命令，或处理 SQX/SQV、C# 控件、CSS、布局、渲染后端、平台宿主及 IDE 工具任务时使用。Use for Square project onboarding, architecture, code navigation, and development."
---

# Square 项目导览

目标：用最少的阅读确定任务所属模块、实际调用链和验证方式，然后开展工作。此 Skill 不依赖特定 Agent API；不支持技能自动发现的工具也可直接读取。

## 1. 进入项目时

1. 确认仓库根目录并检查已有改动：`git rev-parse --show-toplevel`、`git status --short`。保留用户与其他 Agent 的修改。
2. 读取根目录 [AGENTS.MD](AGENTS.MD)；修改前再检查目标目录内是否有更具体的代理说明。阅读 [README](README.md) 的项目定位和运行示例、[贡献约定](CONTRIBUTING.md)，不必加载全部性能历史。
3. 根据下方任务表，只读取相关源码、项目文件和现有测试。先找到一个相似实现，再确定改动位置；不要一次扫描所有样例、第三方代码和构建产物。
4. 开始修改前，简短说明“目标行为 → 涉及模块 → 验证命令”。只有无法从仓库确认且会改变结果的歧义才需要向用户澄清。

本文位于仓库根目录；所有命令与相对链接均以仓库根目录为基准。版本、路径和能力以当前源码、项目配置、测试为准；文档或计划与实现不一致时，指出差异，不把计划当作已实现功能。

## 2. 必须先建立的项目模型

- Square 是实验性的纯 C#、编译优先、NativeAOT 友好的跨平台 UI 框架。`.sqv` 借鉴 Vue 模板语法，`.sqx` 是原生语法；逻辑写在 `<script lang="csharp">` 中，没有 Vue 运行时或 JavaScript 引擎。不要用浏览器 Vue 应用的 npm/Vite 流程开发核心 UI。
- 模板在编译期生成 C# 组件；运行时维护 Element Tree 和 Display Tree，通过响应式订阅及失效标记更新。修改布局、样式或绑定时，要追踪失效与生命周期，不能只看最终绘制函数。
- `Square.UI`、`Square.Runtime`、`Square.Controls`、`Square.CSS`、`Square.Rendering`、`Square.Text`、`Square.Graphics`、`Square.Hosting` 等是 [Square 核心工程](src/Square/Square.csproj)中的逻辑模块/命名空间，并非各自独立的工程。Software 渲染实现在核心的 [Backends](src/Square/Backends) 中。
- [Square.Compiler](src/Square.Compiler/Square.Compiler.csproj) 是独立的 Roslyn 生成器工程，目标为 `netstandard2.0;net10.0`；Analyzer 使用前者，测试和语言服务使用后者。`Square.Markup` 的兼容实现位于编译器的 [Markup](src/Square.Compiler/Markup)，不要凭命名空间猜测物理目录。
- 具体 Platform/Backend 通过核心抽象接入；平台窗口、输入、IME 与图形绘制分层。Skia、Vulkan、Direct2D 等是独立工程，核心不能反向引用它们。桌面宿主支持 Win32/X11/macOS；Android 仍是 Experimental，另有独立的 Godot 集成。
- NuGet 包使用 `Wuldas.Square` / `Wuldas.Square.*` 名称，程序集和命名空间仍为 `Square` / `Square.*`。框架仓库的项目引用与外部应用的 NuGet 引用要分别核对。

```text
.sqx / .sqv（AdditionalFiles）
  → SqxGenerator → TemplateSemanticAnalyzer（解析、语义分析、生成）
  → C# partial 组件 → Element Tree
  → CSS / Layout → Display Tree / DrawCommand
  → IRenderContext → Software 或独立 Backend
  → Platform 窗口 / Surface 呈现
```

编译入口是 [SqxGenerator](src/Square.Compiler/SqxGenerator.cs) 和 [TemplateSemanticAnalyzer](src/Square.Compiler/LanguageServices/TemplateSemanticAnalyzer.cs)；桌面入口从 [Vue 示例 Program](samples/Square.Sample.Vue/Program.cs) 跟到 [AppWindow](src/Square/Hosting/AppWindow.cs)、[DesktopApplication](src/Square/Hosting/DesktopApplication.cs) 和 [ApplicationSession](src/Square/Hosting/ApplicationSession.cs)。

两处容易误判的边界：布局由 [LayoutEngine](src/Square/Rendering/Layout/LayoutEngine.cs) 分流到 Grid、Table、CSS normal flow 或 Yoga，不能把所有 Block 布局归为 Yoga；模板在编译期处理，但外部 CSS 文件/字符串仍由 [DocumentStyleSheetLoader](src/Square/CSS/Engine/DocumentStyleSheetLoader.cs) 在运行时解析。

## 3. 按任务选择阅读范围

表中的测试是起点；搜索现有用例后，按改动影响补充跨模块验证。

| 任务 | 优先查看源码 | 测试 / 进一步说明 |
|---|---|---|
| SQX/SQV 解析、指令、绑定生成、诊断 | 编译器 [Syntax](src/Square.Compiler/Syntax)、[Parser](src/Square.Compiler/Parser)、[Template](src/Square.Compiler/Template)、[Emit](src/Square.Compiler/Emit)、[Diagnostics](src/Square.Compiler/Diagnostics) | [Markup 测试](tests/Square.Markup.Tests)、[SourceGenerator 测试](tests/Square.SourceGenerator.Tests)；[SQX 规范](docs/Sqx-Spec.md)、[SQV 语法](docs/vue-plan.md)、[Generator](docs/Generator.md) |
| 元素树、控件、事件、组件生命周期 | 核心 [UI](src/Square/UI)、[Controls](src/Square/Controls)、[Runtime](src/Square/Runtime) | [UI 测试](tests/Square.UI.Tests)、[Runtime 测试](tests/Square.Runtime.Tests)；[API](docs/API-Reference.md) |
| 信号、调度、跨线程 UI 更新 | 核心 [Runtime](src/Square/Runtime)、[Hosting](src/Square/Hosting) | [Runtime 测试](tests/Square.Runtime.Tests)；[组合与信号](docs/Composition-and-Signals.md) |
| CSS 解析、选择器、级联、继承、动画 | 核心 [CSS](src/Square/CSS)；组件 style 编译还涉及编译器 [Style 语法](src/Square.Compiler/Syntax/Style) 和 [StyleAstRuntimeEmitter](src/Square.Compiler/Emit/StyleAstRuntimeEmitter.cs) | [CSS 测试](tests/Square.CSS.Tests)、[UI 测试](tests/Square.UI.Tests)；[CSS 支持范围](docs/CSS-Support.md) |
| 盒模型、Flex/Grid、滚动、布局失效 | [LayoutEngine](src/Square/Rendering/Layout/LayoutEngine.cs)、[Layout](src/Square/Rendering/Layout)、[Yoga C# 移植](src/Square/ThirdParty/Yoga) | [UI 测试](tests/Square.UI.Tests)，其中包含 Yoga、Flex、Grid、滚动用例；[Layout](docs/Layout.md) |
| 文字测量、字体、换行、光标/选择区 | 核心 [Text](src/Square/Text)、[TextLayout](src/Square/Graphics/Primitives/TextLayout.cs) 及目标后端 | [Graphics 测试](tests/Square.Graphics.Tests)、[UI 测试](tests/Square.UI.Tests)；[Text](docs/Text.md) |
| Display Tree、脏区、Canvas、Software 绘制 | 核心 [Rendering](src/Square/Rendering)、[Graphics](src/Square/Graphics)、[Backends](src/Square/Backends) | [Backends 测试](tests/Square.Backends.Tests)、[Graphics 测试](tests/Square.Graphics.Tests)；[Rendering](docs/Rendering.md) |
| 特定图形后端 | [Vulkan](src/Square.Backends.Vulkan)、[Skia](src/Square.Backends.Skia)、[Direct2D](src/Square.Backends.Direct2D) | [Vulkan 测试](tests/Square.Backends.Vulkan.Tests)、[Skia 测试](tests/Square.Backends.Skia.Tests)、[Direct2D 测试](tests/Square.Backends.Direct2D.Tests)、[一致性测试](tests/Square.Backends.Conformance.Tests) |
| 窗口、输入、IME、剪贴板、宿主循环 | 核心 [Platform 抽象](src/Square/Platform)、[Hosting](src/Square/Hosting)；[Win32](src/Square.Platform.Win32)、[X11](src/Square.Platform.X11)、[MacOS](src/Square.Platform.MacOS) | [Platform 测试](tests/Square.Platform.Tests)、[UI 测试](tests/Square.UI.Tests)；[Win32 输入与边框](docs/Win32-Input-and-Custom-Frame.md) |
| 图片解码、帧、元数据 | [Square.Images](src/Square.Images) | [Images 测试](tests/Square.Images.Tests)、[图片 fixture 说明](tests/Square.Images.Tests/Fixtures/README.md) |
| 路由、RichText、Markdown、代码编辑器、终端、WebView | [Extensions](src/Square.Extensions)、[Markdown](src/Square.Extensions.Markdown)、[CodeEditor](src/Square.Extensions.CodeEditor)、[Terminal](src/Square.Extensions.Terminal)、[WebView](src/Square.Extensions.WebView) | 路由/RichText/Markdown 在 [UI 测试](tests/Square.UI.Tests)；另有 [CodeEditor](tests/Square.Extensions.CodeEditor.Tests)、[Terminal](tests/Square.Extensions.Terminal.Tests)、[WebView](tests/Square.Extensions.WebView.Tests) 测试 |
| 静态 HTML、Web 宿主 | [Native.Html](src/Square.Native.Html)、[Hosting.Web](src/Square.Hosting.Web) | [Native.Html 测试](tests/Square.Native.Html.Tests)、[Hosting.Web 测试](tests/Square.Hosting.Web.Tests)；[Web Hosting](docs/Web-Hosting.md) |
| DevTools、检查器 | [Square.DevTools](src/Square.DevTools) | [DevTools 测试](tests/Square.DevTools.Tests)、[UI 测试](tests/Square.UI.Tests)；[DevTools](docs/DevTools.md) |
| 补全、跳转、语义诊断、IDE 插件 | 编译器 [LanguageServices](src/Square.Compiler/LanguageServices)、[LanguageServer](tools/Square.LanguageServer)、[tooling](tooling) | [LanguageServer 测试](tests/Square.LanguageServer.Tests)需单独运行；[tooling 说明](tooling/README.md) |
| 应用模板、包消费 | [templates](templates)、编译器 [buildTransitive](src/Square.Compiler/buildTransitive)、[PackageConsumer](tests/Square.PackageConsumer) | [Verify-Templates](templates/Verify-Templates.ps1)、[入门指南](docs/Getting-Started.md)中的本地打包验证 |

## 4. 构建、运行与验证

先读取 [global.json](global.json)、[Directory.Build.props](Directory.Build.props)、[Directory.Build.targets](Directory.Build.targets) 和 [Directory.Packages.props](Directory.Packages.props)。当前公共配置是 .NET 10 / C# 14、Nullable 和警告视为错误，个别工程有覆盖。SDK 允许 `latestFeature` roll-forward；依赖版本集中管理，不要为绕过本机问题随意改版本或关闭告警。

桌面主入口是 [Square.slnx](Square.slnx)。[Android 解决方案](Square.Android.slnx)和 [Godot 解决方案](Square.Godot.slnx)按任务选用，普通桌面开发不需要安装其额外工具链。

```sh
# 检查 SDK；还原并构建桌面解决方案
dotnet --version
dotnet restore Square.slnx
dotnet build Square.slnx -c Release --no-restore

# 运行 SQV 示例；热更新使用 Debug
dotnet run --project samples/Square.Sample.Vue/Square.Sample.Vue.csproj
dotnet watch --project samples/Square.Sample.Vue/Square.Sample.Vue.csproj

# 优先运行与改动直接相关的工程，例如编译器生成逻辑
dotnet test tests/Square.SourceGenerator.Tests/Square.SourceGenerator.Tests.csproj -c Release

# Windows 功能测试筛选，与 CI 的普通测试步骤一致
dotnet test Square.slnx -c Release --no-build --no-restore --filter "Category!=ChromiumIntegration&Category!=Performance"
```

Linux/macOS 的普通测试还应排除 `Category=WindowsRenderingMetrics`。只有相同配置已构建、还原后才使用 `--no-build` / `--no-restore`。上面的功能测试不代表全部门禁；完整环境要求和平台矩阵见 [CI](.github/workflows/ci.yml)。Chromium 集成、真实后端呈现及 GPU 测试按相应门禁准备环境，不能把未执行或跳过写成通过。

CodeEditor 性能测试单独串行执行，避免并发构建或其他测试干扰；不提高阈值来掩盖回退：

```sh
dotnet test tests/Square.Extensions.CodeEditor.Tests/Square.Extensions.CodeEditor.Tests.csproj -c Release --filter "Category!=Performance"
dotnet test tests/Square.Extensions.CodeEditor.Tests/Square.Extensions.CodeEditor.Tests.csproj -c Release --no-build --filter "Category=Performance" --logger "console;verbosity=detailed"
```

语言服务相关修改还需单独运行：

```sh
dotnet test tests/Square.LanguageServer.Tests/Square.LanguageServer.Tests.csproj -c Release
```

需要检查模板生成代码时：

```sh
dotnet build samples/Square.Sample.Vue/Square.Sample.Vue.csproj -p:SquareEmitCompilerGeneratedFiles=true
```

随后在该工程的 `obj` 下查找生成文件，只用于诊断；修改模板或生成器。仓库样例显式配置 `AdditionalFiles`，根配置关闭默认模板发现；NuGet 编译器包默认启用发现。排查“新增模板没有编译”时检查对应工程和 [buildTransitive targets](src/Square.Compiler/buildTransitive/Square.Compiler.targets)。

平台由 `SquareTargetPlatform` 及条件引用决定，可用值为 `Win32`、`X11`、`MacOS`、`Android`，必须与 RID 匹配。主样例选择可选后端时同时检查构建开关与运行参数；其 NativeAOT 开关为 `SquareSamplePublishAot=true`，发布时默认关闭部分可选功能，详见 [主样例项目](samples/Square.Sample/Square.Sample.csproj)。包消费或 AOT 行为变更还需做相应的包安装/发布 smoke test，不能仅凭 Debug 运行判断。

## 5. 修改时保持的边界

- 遵循 [编码规范](docs/CodingStyle.md)与 [.editorconfig](.editorconfig)。先复用现有控件和相似实现，保持改动聚焦。
- 运行时核心保持 NativeAOT 兼容：不要引入反射式属性绑定、`dynamic`、`Reflection.Emit`、`Expression.Compile` 或动态程序集加载。编译期 Roslyn 语义分析与运行时限制分开看。
- 平台 API 留在具体平台工程；后端能力通过抽象暴露。核心不能为了某个平台或后端的特例反向依赖实现工程。
- UI 树修改遵守 Dispatcher 线程约束；订阅、绑定和生成资源要与挂载/卸载配对释放。Debug 热更新可能重建后代并重复触发生命周期；不要假设卸载等于实例永久销毁。
- 模板语法变动需考虑 SQX、SQV、共享语义模型、生成器诊断与 IDE 语言服务。CSS 变动区分编译期 style AST 和运行时级联；布局变动检查测量、失效、滚动与绘制是否一致。
- 为行为修改和回归补充能验证结果的测试。纯文档改动核对事实、路径和命令即可，不必运行整个框架测试。
- 公共 API 变化更新 [API Reference](docs/API-Reference.md)，能力变化更新 [Roadmap](docs/Roadmap.md)。不要手改构建产物；已提交的生成文件通过其文档指定的工具更新。

完成任务时报告：改了什么、关键文件、实际执行的验证及结果、尚未验证的边界。遇到环境缺失时写清原因，不虚构构建、测试或跨平台验证结果。
