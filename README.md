# Square

Square 是一个纯 C#、编译优先、NativeAOT 友好的实验性跨平台桌面 UI 框架。

它借鉴 Vue、HTML 与 CSS 的开发体验，但不是浏览器，也不引入 Vue 运行时或 JavaScript 引擎。`.sqv` / `.sqx` 模板会在编译期由 Roslyn Source Generator 生成普通 C# 组件。

> 项目仍处于早期开发阶段，API 与模板语法可能调整。目前主要验证 Windows / Win32 与 Linux / X11 桌面宿主。

## 截图

![Square sample screenshot](samples/E46602.png)

![Square sample screenshot](samples/QQ20260724-162114.png)

![Square sample screenshot](samples/QQ20260724-162126.png)

## 示例

`Main.sqv`：

```vue
<template>
  <View class="page">
    <Text class="title">Hello {{ Name.Value }}</Text>

    <Input :value="Name" @input="OnNameChanged" />

    <Button ref="SaveButton" @click="OnSave">
      Save
    </Button>

    <Text v-if="Saved.Value">Saved</Text>
  </View>
</template>

<script lang="csharp">
  public ObservableValue<string> Name = new("Square");
  public ObservableValue<bool> Saved = new(false);

  private void OnNameChanged(Event e)
  {
      Name.Value = ((Input)e.Target!).Value;
  }

  private void OnSave()
  {
      Saved.Value = true;
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

`Program.cs`：

```csharp
using Square.Hosting;

var window = new AppWindow("My App", 800, 600);
window.Load(new Main());

new DesktopApplication(window).Run();
```

## 运行示例

```bash
dotnet restore Square.slnx
dotnet build Square.slnx
dotnet run --project samples/Square.Sample.Vue/Square.Sample.Vue.csproj
```

主示例可选择 CPU Skia 后端：

```bash
dotnet run --project samples/Square.Sample/Square.Sample.csproj -- --backend Skia
```

运行 RichText 示例：

```bash
dotnet run --project samples/Square.Sample.RichText/Square.Sample.RichText.csproj
```

运行测试：

```bash
dotnet test Square.slnx
```

## NativeAOT 发布

Windows x64：

```bash
dotnet publish samples/Square.Sample.Vue/Square.Sample.Vue.csproj \
  -c Release \
  -r win-x64 \
  -p:SquareSamplePublishAot=true \
  --self-contained true
```

Linux x64 / X11：

```bash
dotnet publish samples/Square.Sample.Vue/Square.Sample.Vue.csproj \
  -c Release \
  -r linux-x64 \
  -p:SquareSamplePublishAot=true \
  -p:SquareTargetPlatform=X11 \
  --self-contained true
```

## 文档

- [入门指南](docs/Getting-Started.md)
- [SQV / Vue 模板语法](docs/vue-plan.md)
- [SQX 原生语法](docs/Sqx-Spec.md)
- [总体架构](docs/Architecture.md)
- [API 参考](docs/API-Reference.md)
- [CSS 规范](docs/CSS-Spec.md)
- [布局](docs/Layout.md)
- [渲染](docs/Rendering.md)
- [DevTools 调试服务](docs/DevTools.md)
- [开发路线](docs/Roadmap.md)

## 项目状态

Square 适合框架设计验证、实验和贡献开发，暂不建议用于生产项目。

贡献流程见 [`CONTRIBUTING.md`](CONTRIBUTING.md)。安全问题请按 [`SECURITY.md`](SECURITY.md) 私下报告。

## License

MIT
