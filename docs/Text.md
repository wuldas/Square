# 字体与排版

> Document Revision: 0.3
> 配套：`Architecture.md`、`Graphics.md`

---

## 1. 定位

`Square.Text` 为独立文本模块，负责文本测量、排版、渲染支持。

字体系统优先采用纯 C# 实现。

---

## 2. 职责

| 功能 | M1 | M7 |
|---|---|---|
| Unicode | ✅ | ✅ |
| Font Manager | ✅ 基础 | ✅ 完整 |
| Glyph 缓存 | ✅ | ✅ |
| 单行排版 | ✅ | ✅ |
| 多行排版 | ✅ 基础 | ✅ 完整 |
| 文本测量 | ✅ | ✅ |
| 命中测试 | ✅ 基础 | ✅ 完整 |
| Caret | M3 | ✅ |
| Selection | M3 | ✅ |
| Line Break | ✅ 基础 | ✅ 完整 |
| Font Fallback | M7 | ✅ |
| BiDi | M7 | ✅ |

---

## 3. Font Manager

### 3.1 职责

- 加载系统字体
- 字体匹配（family / weight / style）
- 字体缓存

### 3.2 M1 实现

- 读取系统字体目录
- 按 family name 匹配
- 最小缓存

### 3.3 接口

```csharp
public sealed class FontManager
{
    public static FontManager Instance { get; }
    public Font Match(string family, float size, FontWeight weight, FontStyle style);
    public IReadOnlyList<string> AvailableFamilies { get; }
}
```

---

## 4. Glyph

### 4.1 Glyph 缓存

- 按 family、物理字号、weight、style 和字符码点建立键
- Software Backend 缓存 glyph coverage，DPI 变化或 RenderContext 释放时清理上下文缓存
- Vulkan Backend 使用独立 atlas 元数据缓存；coverage 上传 GPU 后不在 `SystemGlyphRasterizer` 中长期缓存
- Direct2D Backend 的主文本路径缓存有界 DirectWrite layout；只有暂不支持选项的回退路径使用有界 A8 glyph cache
- 缓存边界和跨子系统共享仍在演进，当前不是全局 LRU

### 4.2 Glyph 信息

```csharp
public readonly struct GlyphInfo
{
    public int CodePoint;
    public float AdvanceWidth;
    public float AdvanceHeight;
    public Rect Bounds;
    public float LeftBearing;
    public float TopBearing;
}
```

---

## 5. Text Layout

### 5.1 单行（M1）

```csharp
public sealed class TextLayout
{
    public string Text;
    public Font Font;
    public Size MaxSize;
    public TextAlignment Alignment;
    public Size Measure();
    public IReadOnlyList<GlyphRun> GetRuns();
}
```

### 5.2 多行（M1 基础）

- 按宽度自动换行
- 换行宽度比较采用与 DirectWrite 一致的 `1/64px` 容差，避免字形 advance 和字距的浮点累加误差导致刚好容纳的文本意外换行。
- 行高 = `font.Size * line-height`
- 对齐：left / center / right

### 5.3 完整排版（M7）

- BiDi 算法
- Font Fallback
- 复杂脚本整形
- 段落分割

---

## 6. 命中测试

### 6.1 M1 基础

- 文本坐标 → 字符索引
- 字符索引 → 文本坐标

### 6.2 M7 完整

- 多行命中测试
- 跨行选择

---

## 7. Caret 与 Selection

### 7.1 当前实现

- Caret 位置计算
- Caret 绘制
- 单行和多行编辑器选择
- DOM `Range` 文本选择
- CSS `selection-background` / `selection-background-color` / `selection-color`
- 选择背景使用逻辑 advance，并扩展到首尾 glyph 的实际墨迹边界
- Caret、命中测试、水平滚动和选择区共享同一套 glyph advance

### 7.2 后续完整排版

- 复杂脚本整形后的 cluster 级选择
- BiDi 视觉顺序选择
- 跨 fallback font run 的统一 cluster 边界

---

## 8. Line Break

### 6.1 M1 基础

- 按宽度断行
- 空格断词

### 8.2 M7 完整

- Unicode Line Break Algorithm (UAX #14)
- CJK 断行规则
- 非断行字符

---

## 9. Font Fallback（M7）

- 缺字时自动回退到备用字体
- 回退链可配置

---

## 10. BiDi（M7）

- Unicode BiDi Algorithm (UAX #9)
- 段落方向自动检测
- 嵌入方向覆盖

---

## 11. 与渲染集成

- `IRenderContext.DrawText(TextLayout, Point, Brush)`
- Software Backend：读取灰度 coverage，并直接混合到整数物理像素
- Vulkan Backend：将 coverage 上传为白色 RGB、coverage alpha 的 atlas 区域；glyph 周围使用透明白 padding，避免线性过滤产生暗边
- Direct2D Backend：DirectWrite snapshot 同时提供测量、换行、cluster、BiDi、命中、selection/caret 和 `DrawTextLayout`
- DirectWrite selection rect 会按同一视觉行的连续 cluster 合并，避免逐字符背景矩形在亚像素边界产生可见接缝
- Direct2D 暂不支持选项的回退路径才将 coverage 上传为 A8 bitmap，并继续按物理像素对齐 baseline
- Software 与 Vulkan 按逻辑 glyph advance 累计字符位置；DirectWrite 使用 shaped cluster advance，三者都保持逻辑 DPI 坐标
- Vulkan 普通 DPI 文本保持物理像素对齐的 glyph 原点和 bearing，使 atlas texel 与 framebuffer pixel 保持一对一映射
- Vulkan 旋转、斜切或额外缩放文本保留浮点 quad 与线性过滤，以支持任意变换

---

## 12. 字体清晰度约束

系统 glyph rasterizer 在 Windows 使用 GDI `GGO_GRAY8_BITMAP` 生成 0 到 64 的灰度 coverage，并归一化到 0 到 255。Software、Vulkan 与 Direct2D fallback 路径共享该结果；Direct2D 主路径不再为每个 glyph 创建 A8 bitmap。

已经抗锯齿的 coverage 不应在一对一显示时再次落在半像素位置，否则线性采样会把相邻 coverage 再平均一次，使笔画变软。为保持 Software/Vulkan 一致：

- 布局继续使用逻辑像素，glyph 按 `font.Size * DpiScale` 栅格化
- 普通 DPI 文本使用逻辑 advance 累计位置，并在每个 glyph 落点映射到 framebuffer 时舍入
- glyph bearing 和 coverage 使用物理字号栅格器结果，advance 不直接使用逐字符物理整数值
- atlas UV 使用 allocation 边界，不额外添加全局半 texel 偏移
- 不对整个共享 atlas 强制使用 nearest filter，以免降低 Bitmap 缩放质量
