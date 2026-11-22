# 平滑体素地形研究

2026 年 11 月 21 日 · 平滑地形初期选型记录。基本挖填、走坡和大陆流送已进入 Unity 实测。当前实现与测量见[大陆试玩与基准](../continent-demo.md)，下阶段技术任务见[世界引擎方案](../roadmap/world-engine.md)。

## 先试整数格编辑与密度曲面

用整数坐标标识区块和编辑格。自然地形在规则格点采样密度，用 Marching Cubes 等算法在采样点之间插值生成地表三角形。走路和挖掘选取基于对应的碰撞面。地表可以有清楚的低多边形轮廓，也可以让有效坡度内的缓坡连续可走。地形精度和角色的可走坡度以验证场结果为准。[Voxel Tools 平滑地形文档](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/) · [Unity 6.3 角色坡度](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/CharacterController-slopeLimit.html)

两种数据方案都能保留整数格的交互。直接以密度表达自然地形，能表示同一格内不同高度、坡角、洞口和悬挑。保留每格的二值占用并用受约束的 Surface Nets 生成曲面，也能减少阶梯感，更适合要求方块始终具有明确占用语义的建造。后一种在格子粗时丢失亚格形状，薄壁、窄洞和尖角要特意试验。可先让墙、桥面等建筑件以同一格坐标吸附并保有硬边，再看天然地形与建筑接缝的效果。[Constrained Elastic SurfaceNets](https://www.merl.com/publications/TR99-24)

自然地形的一次挖填可定义为一个吸附在目标格上的局部体积操作。共享角点同时影响相邻的网格单元。用预览显示实际会改动的范围，并用固定样例判断一次编辑的边界是否符合预期。平滑表面可能跨过格界，挖填目标应按射线方向和密度或占用查询验证。坡顶、洞壁和接缝上，单凭命中点沿法线偏移再取整可能选偏。

## 已有项目经验

- **《Enshrouded》** 的开发者公开讨论过有机地貌与建筑吸附。把自然地形的形状规则用到建筑材料上，会牵涉吸附及大蓝图的操作。它适合参考地貌、低细节景观和整齐建筑怎样共处。[Keen Games 世界设计问答](https://enshrouded.com/en-US/news/enshrouded-world-design-team-reddit-ama-recap)
- **Godot Voxel Tools** 的 MIT 源码及文档展示了密度采样、分块、多级细节和实时编辑。其开发记录专门讨论碰撞生成开销和动态寻路的成本。Unity 的耗时用本项目的硬件和构建测。[平滑地形](https://voxel-tools.readthedocs.io/en/latest/smooth_terrain/) · [性能记录](https://voxel-tools.readthedocs.io/en/latest/performance/) · [导航记录](https://voxel-tools.readthedocs.io/en/latest/navigation/)
- **Eldemarkki 的 Marching Cubes Terrain** 是 MIT 的 Unity 可编辑地形示例，可参考 CPU 网格生成和区块编辑。它使用 Unity 2019.3，导入当前工程前需要兼容性验证。[项目源码](https://github.com/Eldemarkki/Marching-Cubes-Terrain)
- **reromanlee/Transvoxel 2.0.0** 是可试的 MIT Unity URP 包。固定发行版源码有 `IDensitySource`、`DensityOverride`、`InvalidateRegion`、CPU 网格生成和异步碰撞烘焙。自带 Terraform 是球刷。按格编辑、落盘，以及画面与有效碰撞的切换契约需要项目实现。源码可保留旧碰撞等待新网格烘焙，所以要测挖填后的碰撞延迟。发行版作者验证用的是 Unity 6000.5，本机版本仍待试。[2.0.0 发行版](https://github.com/reromanlee/Transvoxel/releases/tag/2.0.0) · [运行时代码](https://github.com/reromanlee/Transvoxel/blob/2.0.0/Runtime/TransvoxelTerrain.cs)

## 第一轮验收

1. 用相同分辨率的两个区块搭一条跨接缝的缓坡及一个小洞。角色连续走上坡，先后检查区块内与负坐标接缝处的地面支撑。
2. 在坡顶、洞壁与接缝显示挖填目标。挖开洞口后角色可以进入，补上后路径恢复阻挡。另验挖掉脚下的有效支撑时角色会下落，以及填土会包住角色时该编辑能被拒绝。
3. 相同世界坐标上的边界采样要一致。单次编辑更新所有依赖这些采样的区块，核对显示与碰撞怎样切换，以及连挖连填时旧结果会不会盖掉新结果。
4. 固定地图和编辑序列，记录触发到碰撞生效的延迟、主线程尖峰、网格与碰撞烘焙各自的时间。先用这些记录判断现成实现是否值得接入。远处不同分辨率区块的拼接可参考 Transvoxel，等上述交互跑通再比较。[Transvoxel 算法](https://transvoxel.org/) · [Unity 6.3 Physics.BakeMesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Physics.BakeMesh.html)
