# v11 能力与结果边界

本轮测试六个当前实现的共同绘制语义及其默认质量，不等价于完整 HTML Canvas API 一致性认证。客户区统一1280×800、DPR1；三轮独立进程，固定逻辑帧，不以有限动画帧池代替持续变化。

| 范围 | v11处理方式 |
|---|---|
| 简单闭合多边形、矩形、source-over | 共同profile；预定义内部/背景/alpha探针检查实际输出 |
| 静态对象、同值重建、新几何 | 分开测试；动态native对象必须逐帧构建，准备成本包含在调用方总CPU时间 |
| 颜色与变换变化 | 独立profile；矩阵顺序有不交换嵌套fixture |
| 真实几何clip和group opacity | 共同语义，检查clip bounds内的外部点及重叠child的分组alpha，不能用矩形或逐child透明度替代 |
| 字体/文字 | 使用同一Segoe UI文件字节和ASCII内容/固定长度；检查加载身份与非空笔画。字体AA、hinting、布局API成本不宣称相同 |
| 图像1:1 | 整数像素完整源区；实际alpha混合失败会限制该实现的图像profile资格 |
| 图像2× | 原生采样profile单列；Square公共API不提供滤波选择，Software固定nearest、Direct2D/Vulkan固定linear，不宣称六方等质量 |
| 原生开放折线 | 单列默认cap/join/AA质量；Software样式支持与native GPU不同，不按固定画质混排 |
| 线性gradient | 原生插值独立区域，mixed-dashboard因此保留native-quality标签；不根据内部不透明ROI把gradient边缘升级为已证明相同 |
| 缓存容量 | 256/4096/65536路径，同样每帧128条，预热至少60帧并覆盖池两轮；记录准备、内存与运行成本 |
| process-cold | 外部启动到首个有效桌面标记，包含运行时/输入加载；未清空OS、字体或驱动磁盘缓存 |
| cubic/quadratic native curves、洞填充规则、复杂文本fallback、filters/shadows、非source-over blend | 不属于本轮六方共同契约；不伪造Square公共API缺失的指令，也不据本轮结果声称这些功能性能相同 |

所有适配器每帧实际执行trace中的全部逻辑操作；允许框架和后端正常复用通用资源，不允许测试适配器直接复用整场景像素或省略叠加。prepared操作输入不等于prepared native资源：Square部分native资源在Draw/Present懒创建，其他API可能在layout/prepare创建。因此nativePrepare/staticPrepare分栏只作各自诊断，不能直接作为跨框架同一资源阶段排名。

外部窗口变化是共同阶段代理，不是物理scanout或GPU kernel time。GDI校准已保留：捕获约16.7ms且可能漏见，保持原observerLimited/badCoverage标志。Desktop Duplication真实校准也未达到稳定覆盖资格，失败与原始ROI保留，不归因于未经证明的desktop/affinity问题。正式运行显式选择GDI，桌面变化率仅作受限、粗分辨率诊断，不作精确GPU吞吐排名。每个条件同时运行独立的无捕获control；调用方同步耗时主表采用control（墙钟，包含API同步等待），带捕获耗时与外部观测作为配对扰动诊断。GetProcessTimes进程树字段才核算实际CPU时间；control没有marker阶段，只报告whole-lifetime CPU，不能伪造measurement CPU。两种模式顺序轮换，所有失败、遗漏序号、时间边界、observer自身CPU及private bytes均保留。未观测序号不直接叫GPU掉帧，也不把含时间量化误差的变化率称为严格数学下界。

不计算跨场景混合总分；近60Hz是显示调度上限。主要结合各场景受限窗口观测、调用方同步耗时、实际进程树CPU、准备成本和private bytes评估；private bytes是进程私有内存，不能当作总显存占用。不能从多个60FPS推断未限帧GPU极限吞吐相同。质量失败、观察覆盖不足、超时及native质量差异保留为明确资格状态，不择优删除。
