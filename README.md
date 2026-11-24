# Square

Square 是一个用 C# 编写的跨平台桌面 UI 框架，主要在 Windows 和使用 X11 的 Linux 上验证，也可在 macOS 上运行。项目处于实验阶段，API 和模板语法可能调整。

Square 提供两种组件模板。`.sqv` 借鉴 Vue 的写法，`.sqx` 使用 Square 自己的语法。模板用标签描述界面结构，用 C# 编写逻辑，用 CSS 定义样式。构建时模板会编译为 C# 组件，运行时由 Square 绘制界面。

## 创建独立应用

安装 .NET 10 SDK 后，在准备存放新项目的目录运行下面的命令。Linux 桌面需要 X11 会话和 `libX11`。[下载 .NET 10 SDK](<https://dotnet.microsoft.com/download/dotnet/10.0>)

```bash
dotnet new install Wuldas.Square.Templates@0.3.2
dotnet new square -n MyApp
dotnet run --project MyApp
```

应用会打开欢迎页，点击 **Increment** 按钮会增加计数。生成的项目默认使用 `.sqv` 模板和 CPU 软件渲染（Software），构建时会从 NuGet.org 自动下载依赖。[核心包](<https://www.nuget.org/packages/Wuldas.Square>) · [模板包](<https://www.nuget.org/packages/Wuldas.Square.Templates>)

创建项目时可用 `--markup sqx` 选择 `.sqx` 模板，用 `--platforms desktop,android` 创建可分别构建为桌面和 Android 应用的项目。平台要求、项目结构和组件模板用法见[入门指南](<docs/Getting-Started.md>)。

## 编写第一个组件

将刚创建的 `MyApp/Pages/MainPage.sqv` 替换为下面的内容。这个页面会随着输入更新问候语，点击「确认」后会显示提示文字。

```vue
<template>
  <View class="page">
    <Text class="title">你好，{{ Name.Value }}！</Text>
    <Input :value="Name" @input="OnNameChanged" />
    <Button @click="ConfirmName">确认</Button>
    <Text v-if="Confirmed">已确认</Text>
  </View>
</template>

<script lang="csharp" namespace="MyApp.Pages">
  public ObservableValue<string> Name = new("Square");
  public ObservableValue<bool> Confirmed = new(false);

  private void OnNameChanged(Event e)
  {
      Name.Value = ((Input)e.Target!).Value;
  }

  private void ConfirmName()
  {
      Confirmed.Value = true;
  }
</script>

<style>
  .page {
      display: flex;
      flex-direction: column;
      gap: 12px;
      padding: 16px;
  }

  .title {
      font-size: 20px;
  }
</style>
```

`ObservableValue<T>` 保存界面可以订阅的值。`@input` 和 `@click` 将输入和点击绑定到 C# 方法，`v-if` 根据 `Confirmed` 的值决定是否显示提示。在 `MyApp` 的上一级目录再次运行 `dotnet run --project MyApp` 即可查看效果。

## 截图

![Square 代码编辑器示例，展示语法高亮、行号和代码折叠](<samples/E46602.png>)

![SQV 表单示例，展示登录和注册控件](<samples/QQ20260724-162114.png>)

![SQV Markdown 示例，展示标题、列表、表格和图片](<samples/QQ20260724-162126.png>)

## 运行示例

克隆仓库后，在仓库根目录运行 SQV 示例，查看控件、表单、Markdown 和页面路由等功能。

```bash
dotnet run --project samples/Square.Sample.Vue/Square.Sample.Vue.csproj
```

开发时可用 `dotnet watch` 在 Debug 模式下热更新 C#、模板和组件样式。在此示例中，修改模板结构会保留根组件的状态。内部控件会重建，焦点或滚动位置可能重置。[热更新用法](<docs/Getting-Started.md#33-构建和运行>)

```bash
dotnet watch --project samples/Square.Sample.Vue/Square.Sample.Vue.csproj
```

另一个桌面示例可以切换绘制方式。下面的两条命令分别选择 Skia 和 Windows 上的 Direct2D。[渲染文档](<docs/Rendering.md>)

```bash
dotnet run --project samples/Square.Sample/Square.Sample.csproj -- --backend Skia
dotnet run --project samples/Square.Sample/Square.Sample.csproj -p:SquareTargetPlatform=Win32 -p:SquareSampleUseDirect2D=true -- --backend Direct2D
```

其他示例包括 [富文本控件](<samples/Square.Sample.RichText>) 和 [在本地 HTML 页面中嵌入 Square 组件](<docs/Web-Hosting.md#8-本地静态-html-外壳与编译组件挂载>)。构建整个框架及运行测试的命令见[贡献指南](<CONTRIBUTING.md#development-setup>)。

## Android（实验性）

Android 的基本交互已在模拟器上验证，arm64 真机仍待完整验证。运行示例需要 Android SDK、JDK 17 和 .NET Android 工作负载。先启动模拟器，再从仓库根目录安装并启动示例。[Android 平台说明](<docs/Android-Platform-TODO.md>)

```bash
dotnet workload restore Square.Android.slnx
dotnet build samples/Square.Sample.Android/Square.Sample.Android.csproj -t:Install -f net10.0-android -p:SquareTargetPlatform=Android
adb shell am start -n com.wuldas.square.sample/.MainActivity
```

示例默认使用软件渲染。要切换渲染方式，关闭示例后用 `backend` 启动参数选择 `AndroidCanvas`、`AndroidSkia` 或 `Vulkan`。

```bash
adb shell am start -n com.wuldas.square.sample/.MainActivity --es backend AndroidCanvas
```

开发时可以开启 DevTools，从电脑上查看应用截图、检查界面元素和模拟输入。关闭示例后，用以下命令重新启动并转发端口。通过 `adb logcat -d -s Square` 查看访问令牌。[Android 远程调试步骤](<docs/DevTools.md#android-远程调试adb-端口转发>)

```bash
adb shell am start -n com.wuldas.square.sample/.MainActivity --ez devtools true --ei devtools-port 5128
adb forward tcp:5128 tcp:5128
```

## NativeAOT 发布

NativeAOT 可以将桌面应用提前编译为原生可执行文件。安装系统所需的编译工具后，在 `MyApp` 的上一级目录运行与你系统匹配的命令。[NativeAOT 工具要求](<https://learn.microsoft.com/zh-cn/dotnet/core/deploying/native-aot/#prerequisites>)

```bash
# Windows x64
dotnet publish MyApp/MyApp.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true

# Linux x64（X11）
dotnet publish MyApp/MyApp.csproj -c Release -r linux-x64 --self-contained true -p:PublishAot=true
```

## 性能测试

两项测试于 2026 年 11 月 11 日在同一台 Windows 11 x64 电脑上运行，硬件为 Intel Core i5-12400F 和 GeForce GTX 1660 SUPER。测试使用 Square 0.3.0，各项目均以 Release 配置发布。[测试环境、测量方法和原始数据](<docs/benchmarks/2026-11-11.md>)

CPU 的 100% 相当于一个逻辑处理器满载。私有内存统计进程独占的已提交内存，工作集统计驻留在物理内存中的页面。发布体积为完整可运行目录的大小。帧率按各项目的动画或渲染事件计算，两项测试的 CPU 占用在各自表内比较。

### 布局动画

720 个方块分三组改变位置、透明度和宽度，其中宽度动画触发重新布局。每个目标预热 5 秒，测量 30 秒。Flutter 取两次测量的中位数，其余目标各测一次。JIT 版在运行时编译，NativeAOT 版在发布时预先编译为原生代码。

| 框架与配置 | CPU | 实测帧率（FPS） | 平均私有内存（MiB） | 平均工作集（MiB） | 发布体积（MiB） |
|---|---:|---:|---:|---:|---:|
| Square Software · JIT | 39.07% | 59.91 | 58.94 | 108.89 | 100.31 |
| Square Direct2D · JIT | 21.68% | 60.00 | 108.07 | 144.15 | 100.31 |
| Square Software · NativeAOT | 45.02% | 60.00 | 54.69 | 83.53 | 9.55 |
| Square Direct2D · NativeAOT | 31.97% | 60.00 | 103.98 | 108.42 | 9.55 |
| Avalonia · JIT | 26.70% | 59.97 | 125.08 | 149.03 | 102.86 |
| Avalonia · NativeAOT | 30.40% | 59.97 | 114.56 | 113.45 | 34.64 |
| Electron · 打包版 | 101.26% | 59.93 | 267.10 | 419.03 | 366.90 |
| Flutter · Release | 33.93% | 53.35 | 176.28 | 126.03 | 23.37 |

### Canvas 绘制

各项目在 1280 × 800 的画布上绘制路径、文字、图片、渐变和图层，也执行裁剪与变换。每轮预热 60 帧，测量 120 帧。每个目标运行三轮，表中是各指标的中位数。[Canvas 运行命令与依赖](<samples/Square.Sample.CanvasBenchmark/README.md>)

| 框架与配置 | CPU | 实测帧率（FPS） | 平均私有内存（MiB） | 平均工作集（MiB） | 发布体积（MiB） |
|---|---:|---:|---:|---:|---:|
| Square Software | 42.90% | 60.00 | 41.64 | 92.53 | 104.95 |
| Square Direct2D | 35.68% | 60.02 | 110.57 | 120.66 | 104.95 |
| Square Vulkan | 39.00% | 60.05 | 184.64 | 176.20 | 104.95 |
| Electron | 57.97% | 60.00 | 161.04 | 318.28 | 367.02 |
| Flutter | 45.82% | 61.57 | 172.61 | 109.70 | 23.22 |
| Avalonia | 60.25% | 59.99 | 115.76 | 141.79 | 102.36 |

### Square 在测试中的优势

- **NativeAOT 发布目录更小。** 布局动画示例使用 NativeAOT 发布后，Square 为 9.55 MiB，Avalonia 为 34.64 MiB，Square 小约 72%。同一测试中，Flutter 为 23.37 MiB，Electron 为 366.90 MiB。
- **Software 软件渲染更省内存。** 在布局动画中，Square Software NativeAOT 的平均私有内存为 54.69 MiB，Avalonia NativeAOT 为 114.56 MiB。在 Canvas 场景中，Square Software 的平均私有内存为 41.64 MiB，平均工作集为 92.53 MiB，两项均为该场景最低。
- **Windows Direct2D 的 CPU 占用较低。** 布局动画中，Square Direct2D JIT 和 Avalonia JIT 都约为 60 FPS，CPU 占用分别为 21.68% 和 26.70%。在 Canvas 场景中，Square Direct2D 的 CPU 占用中位数为 35.68%，在六个测试目标中最低。

### 运行布局动画测试

在 Windows x64 上安装 PowerShell 7、.NET 10 SDK、Node.js、Flutter Windows 桌面工具和 Visual Studio C++ 工具链。将 `flutter` 加入 `PATH` 后，在仓库根目录运行以下命令。[布局动画测试脚本](<samples/Square.Sample.PerfComparison/Run-Comparison.ps1>)

```powershell
pwsh -NoProfile -File samples/Square.Sample.PerfComparison/Run-Comparison.ps1 -Seconds 30 -WarmupSeconds 5
```

## 文档

- [入门指南](<docs/Getting-Started.md>)
- [SQV 模板语法](<docs/vue-plan.md>)与 [SQX 模板语法](<docs/Sqx-Spec.md>)
- [总体架构](<docs/Architecture.md>)与 [API 参考](<docs/API-Reference.md>)
- [CSS 支持范围](<docs/CSS-Support.md>)、[布局](<docs/Layout.md>)与[渲染](<docs/Rendering.md>)
- [DevTools 调试](<docs/DevTools.md>)与[按需绘制和节能](<docs/Power-Saving.md>)
- [Web 服务和静态 HTML](<docs/Web-Hosting.md>)
- [开发路线](<docs/Roadmap.md>)、[贡献指南](<CONTRIBUTING.md>)与[安全问题报告](<SECURITY.md>)

## NuGet 发布

维护者可在 GitHub Actions 手动运行 **Publish NuGet** 并填入待发布版本。手动运行会构建并验证 NuGet 包，验证后的包可在该次运行的产物中下载。[NuGet 发布工作流](<.github/workflows/publish.yml>)

也可以用 PowerShell 在仓库根目录打包验证，需要 .NET 10 SDK、Android 工作负载与 JDK 17。将 `x.y.z` 换成待发布版本，并使用一个空的输出目录。[打包脚本](<tools/Pack-Release.ps1>)

```powershell
pwsh -File tools/Pack-Release.ps1 -Version x.y.z -OutputDirectory artifacts/packages
```

正式发布时，为 `main` 分支上通过桌面与 Android 测试的提交创建并推送版本标签。版本 `x.y.z` 对应标签 `vx.y.z`，版本号须与构建配置中的 `Version` 一致。标签触发发布工作流，验证通过后会自动上传到 NuGet.org。[版本配置](<Directory.Build.props>)

## 许可证

[MIT](<LICENSE>)

## 版本更新

- **0.3.2** 修复条件渲染、数据绑定、输入事件和布局问题，改进字体回退与文字排版。[发布说明](<docs/releases/0.3.2.md>)
- **0.3.1** 更新 .NET 构建工具链，修复自动化测试的构建与运行问题。[发布说明](<docs/releases/0.3.1.md>)
- **0.3.0** 改进 Canvas 的路径、渐变、文字和图层绘制，以及 Windows 软件渲染向窗口提交画面的方式。[发布说明](<docs/releases/0.3.0.md>)
- **0.2.0** 优化布局、动画和代码编辑器的语法高亮，加入 Godot 集成与静态 HTML 组件挂载。[发布说明](<docs/releases/0.2.0.md>)
- **0.1.0** 首次发布框架与应用模板。[核心包](<https://www.nuget.org/packages/Wuldas.Square/0.1.0>) · [模板包](<https://www.nuget.org/packages/Wuldas.Square.Templates/0.1.0>)
