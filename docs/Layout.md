# 布局引擎

> Document Revision: 0.4
> 配套：`Architecture.md`、`CSS-Spec.md`

---

## 1. 定位

`Square.Rendering` 程序集中的 `LayoutEngine` 负责 Element Tree 的几何计算。

**Flex / Block** 使用内置的 [Yoga.Net 3.2.3 源码](https://github.com/chenrensong/Yoga.Net/tree/1c7fd4a1ede445f5a3f9d5ae9239364b09d25154)（Meta [Yoga](https://github.com/facebook/yoga) 的纯 C# 移植，AOT 友好）；**Grid** 仍使用 Square 内置算法（上游 Yoga.Net 的 CSS Grid 绑定当前结果不可靠，故未接入）。

采用 CSS 盒模型思想。

Yoga 源码位于 `src/Square/ThirdParty/Yoga`，固定于提交 `1c7fd4a1ede445f5a3f9d5ae9239364b09d25154`。类型使用内部命名空间 `Square.Rendering.Yoga`，随 `Square.dll` 编译，不再依赖或传递引入 `Yoga.Net` 包，也不作为 Square 的公开 API。应用若直接使用 `Facebook.Yoga` API，须自行引用对应外部包。

本地优化不修改 Flex 布局算法、舍入或缓存失效规则：`StyleLength` 使用不可变值类型，避免长度读取分配对象；`default(StyleLength)` 与 `Undefined()` 的解析、转换及相等性一致。诊断事件使用与现有订阅锁同步的 volatile 订阅计数；没有订阅者时不构造事件载荷、不进入发布锁，有订阅者时保留原顺序和布局统计。MIT 许可、版权与修改说明保存在 `THIRD-PARTY-NOTICES.txt`，并随 NuGet 包置于 `licenses/Square.Yoga.txt`。

上游测试保留在 `tests/Square.UI.Tests/Yoga`，原有 35 项跳过场景保持跳过；长度默认值、NaN / Infinity、百分比、关键字与相等性，以及订阅 / 取消 / 重订阅事件行为均有回归覆盖。

### 1.1 CPU 优化实测

以 Square `c850892` + `Yoga.Net 3.2.3` 为原版，固定原动画场景：Windows x64、900×1240 客户区、`Auto`、目标 60 FPS，Direct2D 保持垂直同步。原版、仅 Square 优化（原 Yoga 包）、完整优化（额外使用 Yoga fork）三组独立冻结 JIT / NativeAOT 可执行文件；每项预热 5 秒、采样 15 秒、交错执行 3 轮，共 36 次测量。下表为 CPU 中位数，100% 表示一个逻辑核心。

| 配置 | 原版 CPU | 仅 Square 优化 CPU | 完整优化 CPU |
|---|---:|---:|---:|
| Direct2D JIT | 23.14% | 23.39% | 21.21% |
| Direct2D NativeAOT | 33.89% | 29.75% | 28.79% |
| Software JIT | 40.98% | 39.94% | 38.97% |
| Software NativeAOT | 48.64% | 46.21% | 45.60% |

完整优化相对原版的 CPU 中位数下降约 4.9%～15.1%，帧提交率仍约为 60 FPS。不能把组合收益全部归给 Yoga：fork 相对仅 Square 优化组的额外中位数下降约 1.3%～9.3%，较小差异仍接近波动范围。最初仅将长度改成值类型的实验并未测得稳定 CPU 收益；后续新增事件快路径和 Square 热路径改动后才得到上述结果。

原始数据与各轮区间保存在本地 `artifacts/cpu-hotspots/cpu-next-comparison/trials.json`；不作跨平台或其他场景的收益保证。


---

## 2. 布局流程

```
Element Tree
  ↓
Measure（测量：计算期望尺寸）
  ↓
Arrange（排列：确定最终位置与尺寸）
  ↓
写入 Element.Geometry
```

---

## 3. 盒模型

```
┌───────────────────────────────────┐
│             margin                │
│  ┌─────────────────────────────┐  │
│  │           border            │  │
│  │  ┌───────────────────────┐  │  │
│  │  │         padding       │  │  │
│  │  │  ┌─────────────────┐  │  │  │
│  │  │  │     content     │  │  │  │
│  │  │  └─────────────────┘  │  │  │
│  │  └───────────────────────┘  │  │
│  └─────────────────────────────┘  │
└───────────────────────────────────┘
```

- `content`：内容区域
- `padding`：内边距
- `border`：边框
- `margin`：外边距

---

## 4. display（M1）

| 值 | 说明 | M1 |
|---|---|---|
| `block` | 块级 | ✅ |
| `flex` | 弹性 | ✅ |
| `inline` | 行内 | M2 |
| `grid` | 网格 | ✅ |
| `none` | 不渲染 | ✅ |

---

## 5. Flex 布局（M1）

### 5.1 容器属性

| 属性 | 值 |
|---|---|
| `flex-direction` | `row` `column` `row-reverse` `column-reverse` |
| `justify-content` | `flex-start` `center` `flex-end` `space-between` `space-around` |
| `align-items` | `stretch` `flex-start` `center` `flex-end` |
| `flex-wrap` | `nowrap` `wrap` |
| `gap` | 间距 |

### 5.2 子项属性

| 属性 | 说明 |
|---|---|
| `flex-grow` | 增长比例 |
| `flex-shrink` | 收缩比例 |
| `flex-basis` | 基础尺寸 |
| `align-self` | 覆盖 align-items |

### 5.3 算法

1. 确定主轴/交叉轴
2. 测量子项基础尺寸（`flex-basis`）
3. 分配剩余空间（`flex-grow`）/ 收缩（`flex-shrink`）
4. justify-content 对齐主轴
5. align-items 对齐交叉轴

### 5.4 固定尺寸与滚动面板

Square 使用 Yoga Web Defaults，但对显式主轴尺寸采用更适合桌面 UI 的默认收缩规则：

- `flex-direction: column` / `column-reverse` 中，显式 `height` 的子项默认不收缩。
- `flex-direction: row` / `row-reverse` 中，显式 `width` 的子项默认不收缩。
- 显式设置 `flex-shrink` 或 `flex` 时，以应用声明为准。
- 滚动容器的直接子项在未声明 `flex-shrink` 时保持内容尺寸，使内容能够超出视口并形成滚动范围。

固定标签栏、内部面板滚动的推荐结构：

```css
.page {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.tabs-host {
  flex-grow: 1;
  flex-shrink: 1;
  flex-basis: 0;
  min-height: 0;
}

.tabs {
  display: flex;
  flex-direction: column;
  height: 100%;
  min-height: 0;
}

.tab-list {
  height: 42px;
}

.tab-panels {
  flex: 1 1 0;
  min-height: 0;
  overflow-y: auto;
}
```

组件宿主本身也是父级 Flex 容器的子项。需要占用剩余空间时，应把 `flex-grow` / `flex-shrink` / `flex-basis` 设置到组件标签或组件宿主 class 上，而不只是设置到组件内部的第一个 `View`。

---

## 6. position（M1 基础）

| 值 | 说明 | M1 |
|---|---|---|
| `static` | 默认流式 | ✅ |
| `relative` | 相对自身 | ✅ |
| `absolute` | 相对最近定位祖先 | M2 |
| `fixed` | 相对视口 | M2 |
| `sticky` | 滚动吸顶 | M3+ |

---

## 7. 尺寸

### 7.1 单位

| 单位 | M1 |
|---|---|
| `px` | ✅ |
| `%` | ✅ |
| `auto` | ✅ |
| `rp` | ✅ |
| `vw` / `vh` | ✅ |
| `min-content` / `max-content` / `fit-content` | M2 |

### 7.2 尺寸属性

- `width` / `height`
- `min-width` / `max-width`
- `min-height` / `max-height`

---

## 8. 高 DPI

- 布局按逻辑像素
- 光栅按物理像素
- 文本、光标和选择区共享逻辑 glyph advance；累计逻辑位置映射到物理像素时再取整，避免逐字符物理取整累积误差
- 字形 coverage 按物理字号生成，保持高 DPI 清晰度
- 避免模糊

---

## 9. 内在尺寸（M2）

- `min-content`：最小内容宽度
- `max-content`：最大内容宽度
- `fit-content`：适应内容宽度

---

## 10. Grid（M2）

### 10.1 容器属性

| 属性 | 说明 |
|---|---|
| `grid-template-columns` | 列模板 |
| `grid-template-rows` | 行模板 |
| `gap` | 间距 |

### 10.2 子项属性

| 属性 | 说明 |
|---|---|
| `grid-column` | 列位置 |
| `grid-row` | 行位置 |
| `grid-column-span` | 列跨度 |
| `grid-row-span` | 行跨度 |

数值放置支持 CSS line 语义：`grid-column: 2 / 3` 表示从第 2 条线到第 3 条线（一个轨道），`span N` 表示跨度。自动放置按行扫描空闲单元格，并避开已显式放置或命名区域占用的格。

---

## 11. 后续

| 功能 | 阶段 |
|---|---|
| Container Query | M3+ |
| Subgrid | M3+ |
| intrinsic sizing 完整 | M2 |
| writing-mode | M3+ |
