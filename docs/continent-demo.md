# 大陆试玩与基准

2026 年 11 月 21 日 · Unity 6000.3.29f1、URP、Windows。大陆是独立技术场景，配置和后续取舍见[世界引擎方案](roadmap/world-engine.md)。

## 试玩

- 运行 `Builds/ContinentDemo/Reol2Continent.exe`。保持 exe 与同目录的 `Reol2Continent_Data`、`UnityPlayer.dll` 等文件一起。Unity 编辑器内可选 `Reol 2 → Open Continent Demo`。
- Unity 场景里的新角色用 `WASD` 移动，鼠标环顾，`Alt` 慢走、`Shift` 快跑、`Space` 跳跃、按住 `C` 下蹲。`F` 切换调试飞行，飞行和潜水用 `Space/C` 升降。沿用旧构建时地面仍是旧按键。海岸出发面向内陆，转身或按住 `S` 可去海边。[大世界角色与天气](roadmap/world-visual-nature-character.md)
- 鼠标左键挖、右键填。挖填距离有限，角色占用的位置不允许填。出发点附近保留少量固定地形。按 `Home` 返回，`Esc` 释放鼠标，重新左击回到控制。
- 左上角显示坐标、就绪区块数量、网格数量和加载队列。看到 `Terrain catching up` 时，进入下一块的位移正在等待碰撞。飞到海上看大陆轮廓，再回到岸边比较近处地形。
- 地面和海床都可挖。新挖出的海床以下空间暂时干燥。挖过的地方远行回来仍在，退出程序后清空本次改动。远处只显示初始大轮廓，靠近后才能看到改动。

| 地图尺度 | 原型标高 |
| --- | --- |
| 2,048 × 2,048 米试验海域，陆块约 1.5 公里宽 | 世界 `X/Z` 约 `−1,024…1,024` |
| 岸线、海床、地下岩层 | 海平面 `Y=0`，深处海床约 `Y=−56`，基岩 `Y=−96` |
| 近处平滑体素 | 编辑 1 米一格，取样间距 0.5 米，相邻区块共用采样坐标 |

## 本机验证

### Play Mode

14 项通过，其中旧两块 Demo 5 项、大陆 9 项。覆盖四面环海、垂直潜至海床、沿坡走跑、负坐标接缝挖填后卸载再回来、出生点与基岩保护、海床开竖井至约 `Y=−95` 再上浮，以及冷海岸急转、预测起止仍在原区块时扩展走廊、超长单步安全截断与继续输入后的恢复。竖井逐格挖，下降与返回使用实际角色控制器和碰撞。

### Windows 渲染基准

Core i5-13600KF、GeForce RTX 3060、Direct3D 12。基线和当前构建各连续测两遍，使用相同地图、路线、构网与发布上限。基线用原调度和逐样本采样。每步提交一次 `1600×900` 离屏 URP 渲染，完成后验证 GPU 图像读回。常规路线模拟步长为 `1/60 s`，冷区急转用 `50 ms` 步长测试单帧要跨更远的情况。表里的帧循环时间取自关闭垂直同步的实机播放器，以独立 GPU 时间或交互场景为目标时需另测。

| 路线 | 实测移动 | 流送 P99 基线 → 当前 | 离屏帧循环 P99 当前 | 最小前视就绪 基线 → 当前 | 当前峰值就绪页 / 表面页 / 待办 |
| --- | --- | --- | --- | --- | --- |
| 岸边步行，1,200 步 | 96.02 米 | 1.907–1.913 → 1.169–1.187 ms | 2.065–2.116 ms | 3.27 → 3.27 米 | 576 / 127 / 42 |
| 低空冲岸、入内陆再折返，780 步 | 870.75 米，指令 68 米/秒 | 2.490–2.521 → 1.505–1.508 ms | 3.331–3.369 ms | 44 → 44 米 | 652 / 165 / 351 |
| 垂直潜水，1,000 步 | `Y=−0.80` 到海床 `−55.97` | 1.434–1.732 → 0.997–1.352 ms | 1.996–3.273 ms | 2.72 → 2.72 米 | 486 / 81 / 306 |
| 飞离已走路线后向冷海岸转 90°，240 步 | 816 米，指令 68 米/秒 | 2.555–2.705 → 1.623–1.654 ms | 2.610–2.864 ms | 16 → 44 米 | 649 / 94 / 366 |

两版四条路线均为 0 次加载拦停。44 米为此路线的预测测量上限。覆盖取沿前方连续分段都已就绪的长度。表中峰值待办来自普通窗口，路径缺页在每步流送后另记。当前两条飞行路线的探测走廊在这些采样点无缺页。冷区路线撤销 1,418 次普通窗口中尚未完成的请求。后续要量回程真正重建的页与未被消费的完成结果。

折返路线取样总耗时由 **270.89–271.87 ms** 降至 **71.71–72.66 ms**。当前完成 805 个表面页和 5,644 个无表面结果。抽壳 120.15–121.08 ms、碰撞烘焙 77.22–79.10 ms、发布 7.54–8.45 ms、远景切换 10.47–10.75 ms。普通队列峰值 351 页需要结合路径就绪和实际耗时判断。每帧最多处理 72 页、3 个表面页。构建以 2.25 ms 为目标，当前页处理完成后交回。

折返的 Unity 跟踪总分配峰值为 95.8–96.6 MiB。四条路线在采样的移动入口合计均为 0 B 主线程托管分配。实机长时间来回后的内存回落、多层高密洞网、已编辑地貌的远近交接仍待单独测量。[下一轮流送试验](roadmap/world-engine.md#下一步要做的流送实验)

## 复现

从仓库根目录在 PowerShell 执行。产物路径可用 `--output` 改，测试成绩写入 NUnit XML。

```powershell
unity test . --mode PlayMode --output .local/continent-playmode.xml --non-interactive
unity build . --target StandaloneWindows64 --execute-method Reol2.Editor.ContinentAuthoring.BuildWindows --output Builds/ContinentDemo/Reol2Continent.exe --allow-dirty-build --non-interactive
$continent = Start-Process -FilePath 'Builds/ContinentDemo/Reol2Continent.exe' -WorkingDirectory (Get-Location).Path -ArgumentList @('-continent-benchmark', '-screen-width', '1600', '-screen-height', '900', '-screen-fullscreen', '0', '-logFile', '.local/continent-benchmark.log', '-continent-image', '.local/continent.png') -WindowStyle Hidden -PassThru -Wait
$continent.ExitCode
```

`-continent-benchmark` 只在开发构建中有作用，基准完成后自动退出。`-continent-image` 可省略，该参数把空中俯瞰的实机画面写到指定文件。Play Mode XML、对照基准的 `baseline-1/2.log`、当前的 `final-1/2.log` 和画面保存在本机 `.local/research/continent-streaming-2026-11-21/`。
