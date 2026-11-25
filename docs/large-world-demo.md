# 随机大世界试玩与验证

## 试玩

双击 `Builds/LargeWorld/Play-Terrain-Demo.cmd`，从地面开始测试逐格挖填。将鼠标指向角色前方的地面，按 `1` 挖一格，再挖同一标高的相邻格。朝坑底或侧壁按 `2` 填土。用 `3` 分别标定一段缓坡的两个端点，再沿新坡面走一趟。

同目录的 `Play-River-Demo.cmd` 从河段上空开始。两种入口采用种子 `20261121`、64 公里区域、原生分辨率无边框窗口。Windows 试玩启动器使用 Direct3D 11，避开本机 Direct3D 12 的穿洞回归退出崩溃。用 `Reol2World.exe -force-d3d11` 可进入默认大世界。Unity 编辑器菜单为 `Reol 2 → Open Large World`，场景为 `Assets/Scenes/LargeWorld.unity`。[地面试玩](../Builds/LargeWorld/Play-Terrain-Demo.cmd) · [河流试玩](../Builds/LargeWorld/Play-River-Demo.cmd) · [坐标、流送与水位](roadmap/world-engine.md)

| 按键 | 行为 |
| --- | --- |
| `W/S`、`A/D`、`Q/E` | 前后移动、左右转向、左右横移。右键掌舵时 `A/D` 横移。 |
| 左键拖动、右键拖动、双键同按 | 环顾、掌舵、沿视线前进。滚轮缩放镜头。 |
| `Shift`、`Alt`、`C` | 冲刺或飞游加速、地面慢走、地面蹲行。 |
| `=`、`-` 或右下角 `+`、`−` | 移动倍率翻倍或减半，适用于地面、飞行和游泳。初始为 1×。 |
| `F`、`Space`、`X/C` | 切换飞行。地面按 `Space` 跳跃。空中按 `Space` 上升、`X/C` 下降。水中按 `Space` 上浮、`X/C` 下潜。 |
| 水面右键掌舵前进 | 默认沿水面游，俯角达到 25° 时下潜。 |
| `1`、`2`、`G` | 沿指针或准星挖一格、填一格、勘探。线框显示当前挖掘目标。 |
| `3`、`Esc` | 以两个地面位置确定整形坡面，`Esc` 取消起点。 |
| `]` | 推进三个游戏小时。 |
| `F1` 至 `F7` | 去近海、巨洞西口、巨洞东口、深洞、湖盆、海沟、高空。 |
| `T`、`Home`、`Esc` | 输入世界坐标远传、回出生点、取消面板或释放拖动。 |
| `F8` | 在本机保存截图和渲染复现包。 |

## 地形精度

手工挖填以一米格为操作单位。样本跨格边界插值，形成圆钝的边角和随世界种子确定的轻微变化。同一标高的相邻挖格共享平底。两点整形要求水平距离 2–32 米，核心宽 2 米，两侧各有 0.75 米过渡。工具检查选中的地表层与角色占用。近场渲染与碰撞共用 0.5 米密度采样的曲面。[地形编辑](../Assets/Reol2/World/ContinentField.cs) · [基础玩法验证](roadmap/world-engine.md#编辑碰撞与通行)

## 地形存档

在 Windows 试玩挖土、填土或整形后，HUD 会提示自动保存。正常退出并用相同种子和地图大小重开，再回到原位置，地形改动会恢复。地面和河流入口的 64 公里图使用同一存档。默认世界有独立存档。出生位置被填住时，启动会寻找周围的安全落点。

如果退出时保存失败，游戏会继续运行并在 HUD 提示。恢复磁盘空间或写入权限后，再关闭窗口即可重试。出现这条提示时按 `F9`，可以用上一次成功的存档退出。

存档目录为 `%USERPROFILE%\AppData\LocalLow\fewnfds\Reol 2\WorldSaves`，主档扩展名为 `.r2terrain`，上一份完整提交使用 `.bak`。如果档位正在被另一进程使用，试玩会标明保存不可用并锁住地形编辑。编辑器 Play Mode 和自动基准可用 `-world-save-root <目录>` 指定隔离档位。[存档与限制](roadmap/world-engine.md#地形存档)

## 洞穴试玩

`F2` 和 `F3` 分别在巨洞西口和东口外进入飞行，镜头沿当前地貌的入口通道瞄准。按住右键并用 `W` 向视线方向飞入，从洞内转身可沿开口看到室外景物。洞内照明变暗，岩壁和转弯决定可见范围。`F4` 可查看独立的封闭深洞。[洞穴流送与遮挡](roadmap/world-engine.md#近远交接) · [视野排查](pitfalls/cave-visibility.md)

飞行时可前后移动观察同一洞口的轮廓，再按 `X` 下降，接近地面时按 `F` 落脚。按 `F7` 可从空中查看山脊、海岸和远景。地形名义视距随高度从约 20 公里增至 150 公里。[近远景精度与视距](roadmap/world-engine.md#近远交接)

## 区域地貌与水岸

区域试玩使用自研河网、湖盆、海岸、山地和高原。河床与水面共用沿程断面，当前河道可在谷底内平滑转向。默认 64 公里版两张问题图的位置已做 Windows 前后对照。海水依据天然海床与海平面提供浮潜，挖出低于海平面的陆地干坑也保持原来的水域判定。超粗的 8,192 米瓦片仍可能漏绘少量窄支流。[当前地貌与河道实测](H:/reol2-workbench/goals/landform-generation/river-centerline-implementation.md) · [水位与瓦片排查](pitfalls/regional-water-tiles.md)

## 本机验证

Play Mode 覆盖指针挖填、跨四个地形页的坑底、填土来源、行走碰撞和区块卸载回访。Windows 以相同种子复跑巨洞接缝挖填、20 米坡面与区域步行、飞行和游泳。[挖填实测](H:/reol2-workbench/goals/grid-excavation/results.md) · [河道旧基线](H:/reol2-workbench/goals/landform-generation/river-centerline-implementation.md)

当前构建通过 92 项 Play Mode 用例。DX11 Player 在 900p 和 1440p 验证洞口和落脚，默认地图与 64 公里区域分别通过写入、重启再挖及再次重启检查。[地形近远景与存档报告](H:/reol2-workbench/goals/terrain-detail-and-persistence/results.md)

## 复现

从仓库根目录运行。Windows 基准使用本地试玩构建，`-continent-benchmark` 完成后自动退出。用与试玩启动器相同的 `-force-d3d11` 复现窗口模式。[通用开发入口](development.md#windows-本地试玩构建)

~~~powershell
unity test . --mode PlayMode --output .local/large-world-playmode.xml --non-interactive
unity build . --target StandaloneWindows64 --execute-method Reol2.Editor.ContinentAuthoring.BuildLargeWorld --output Builds/LargeWorld/Reol2World.exe --allow-dirty-build --non-interactive
~~~

| Player 启动参数 | 验证 |
| --- | --- |
| `-landforms 16/64/256` | 选择其一进入相应尺寸的区域，附加 `-world-seed <整数>` 可切换种子。 |
| `-continent-benchmark -landforms 64 -landform-check` | 区域步行、飞行、挖掘回访和游泳。 |
| `-continent-benchmark -world-sculpt-check` | 巨洞编辑、连续坡度、逐格挖开坡面和填回。附加 `-sculpt-grid` 执行旧版同路线的单格编辑性能对照。 |
| `-continent-benchmark -world-controls-check` | 水面游泳、潜水、上浮和飞行控制。可附加 `-landforms 64` 复跑区域模式。 |
| `-continent-benchmark -world-long-run` | 默认大世界千公里高空与 64 公里贴地路线。 |
| `-continent-benchmark -world-seam-check` | 东西洞口双向渲染、完整穿洞路线、主洞室和深洞的遮挡。 |
| `-continent-benchmark -landforms 64 -world-terrain-lod-check` | 种子 `20261121` 的东洞口在固定机位切换流送范围、洞底实际落脚、山地瓦片共边。 |
| `-continent-benchmark -world-save-root <目录> -world-persistence-check write` | 空档测试写入。使用同一目录再启动 `read` 和 `verify`，核对重启后碰撞、填料来源和再次挖掘。 |
| `-continent-benchmark -world-shoreline-check` | 陆地挖坑与天然近海。 |
| `-continent-benchmark -landforms 64 -world-water-tile-check <report.json>` | 按 F8 机位复现区域水面，附加 `-world-water-tile-settle` 等待瓦片队列。 |

基准可用 `-continent-image <path>` 写图、`-logFile <path>` 写日志。手动试玩遇到异常时先停在现场，按 `F8` 保留完整复现包。[复现包内容与路径](development.md#渲染复现包)
