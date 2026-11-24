# 随机大世界试玩与验证

## 试玩

在本机双击 `Builds/LargeWorld/Play-River-Demo.cmd`。默认以种子 `20261121` 打开自研 64 公里区域，从河段上空开始，使用所在屏幕的原生分辨率和无边框全屏。直接运行同目录的 `Reol2World.exe` 可进入默认大世界。Unity 编辑器菜单为 `Reol 2 → Open Large World`，场景为 `Assets/Scenes/LargeWorld.unity`。[本机河流试玩](../Builds/LargeWorld/Play-River-Demo.cmd) · [坐标、流送与水位](roadmap/world-engine.md)

| 按键 | 行为 |
| --- | --- |
| `W/S`、`A/D`、`Q/E` | 前后移动、左右转向、左右横移。右键掌舵时 `A/D` 横移。 |
| 左键拖动、右键拖动、双键同按 | 环顾、掌舵、沿视线前进。滚轮缩放镜头。 |
| `Shift`、`Alt`、`C` | 冲刺或飞游加速、地面慢走、地面蹲行。 |
| `=`、`-` 或右下角 `+`、`−` | 移动倍率翻倍或减半，适用于地面、飞行和游泳。初始为 1×。 |
| `F`、`Space`、`X/C` | 切换飞行。地面按 `Space` 跳跃。空中按 `Space` 上升、`X/C` 下降。水中按 `Space` 上浮、`X/C` 下潜。 |
| 水面右键掌舵前进 | 默认沿水面游，俯角达到 25° 时下潜。 |
| `1`、`2`、`G` | 在指针或准星命中处挖、填、勘探。 |
| `3`、`Esc`、`B` | 以两个地面位置确定整形坡面，`Esc` 取消起点。`B` 切换连续笔刷和一米盒刷。 |
| `]` | 推进三个游戏小时。 |
| `F1` 至 `F7` | 去近海、巨洞西口、巨洞东口、深洞、湖盆、海沟、高空。 |
| `T`、`Home`、`Esc` | 输入世界坐标远传、回出生点、取消面板或释放拖动。 |
| `F8` | 在本机保存截图和渲染复现包。 |

## 地形精度

连续球刷半径 0.65 米，球心沿命中表面法线偏移 0.20 米。两点整形要求水平距离 2–32 米，核心宽 2 米，两侧各有 0.75 米过渡。工具检查选中的地表层与角色占用。近场渲染与碰撞共用 0.5 米密度采样的曲面。可在同一进程离开后回访改动。[地形编辑](../Assets/Reol2/World/ContinentField.cs) · [基础玩法验证](roadmap/world-engine.md#编辑碰撞与通行)

## 区域地貌与水岸

区域试玩使用自研河网、湖盆、海岸、山地和高原。河床与水面共用沿程断面，当前河道可在谷底内平滑转向。默认 64 公里版两张问题图的位置已做 Windows 前后对照。海水依据天然海床与海平面提供浮潜，挖出低于海平面的陆地干坑也保持原来的水域判定。超粗的 8,192 米瓦片仍可能漏绘少量窄支流。[当前地貌与河道实测](H:/reol2-workbench/goals/landform-generation/river-centerline-implementation.md) · [水位与瓦片排查](pitfalls/regional-water-tiles.md)

## 本机验证

2026 年 11 月 24 日最近一轮验证通过 83 项 Play Mode。Windows 复跑了 16、64、256 公里的步行、挖掘回访、飞行和游泳，以及固定河口和沿海路线。区域生成性能取决于尺寸、种子和本机条件。[区域测试报告与性能](H:/reol2-workbench/goals/landform-generation/river-centerline-implementation.md)

## 复现

从仓库根目录运行。Windows 基准使用本地试玩构建，`-continent-benchmark` 完成后自动退出。[通用开发入口](development.md#windows-本地试玩构建)

~~~powershell
unity test . --mode PlayMode --output .local/large-world-playmode.xml --non-interactive
unity build . --target StandaloneWindows64 --execute-method Reol2.Editor.ContinentAuthoring.BuildLargeWorld --output Builds/LargeWorld/Reol2World.exe --allow-dirty-build --non-interactive
~~~

| Player 启动参数 | 验证 |
| --- | --- |
| `-landforms 16/64/256` | 选择其一进入相应尺寸的区域，附加 `-world-seed <整数>` 可切换种子。 |
| `-continent-benchmark -landforms 64 -landform-check` | 区域步行、飞行、挖掘回访和游泳。 |
| `-continent-benchmark -world-controls-check` | 水面游泳、潜水、上浮和飞行控制。可附加 `-landforms 64` 复跑区域模式。 |
| `-continent-benchmark -world-long-run` | 默认大世界千公里高空与 64 公里贴地路线。 |
| `-continent-benchmark -world-seam-check` | 洞口和洞内的地表接缝。 |
| `-continent-benchmark -world-shoreline-check` | 陆地挖坑与天然近海。 |
| `-continent-benchmark -landforms 64 -world-water-tile-check <report.json>` | 按 F8 机位复现区域水面，附加 `-world-water-tile-settle` 等待瓦片队列。 |

基准可用 `-continent-image <path>` 写图、`-logFile <path>` 写日志。手动试玩遇到异常时先停在现场，按 `F8` 保留完整复现包。[复现包内容与路径](development.md#渲染复现包)
