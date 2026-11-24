# 大世界视觉、自然系统与主角

## 地表与水面

近景体素、远景地表和巨洞代理共用地形配色。自然材质按绝对坐标、高度、坡度与深度选色，距离较远时弱化细节。水面依地形和水位裁切。区域海面与深水湖瓦片满足条件时加入共享网格。水下保留室外远裁剪与天空，加滤镜、低频环境声与听觉低通。[配色](../../Assets/Reol2/World/WorldMaterial.cs) · [地表着色](../../Assets/Reol2/Visuals/VoxelSurface.shader) · [水面与瓦片](../pitfalls/regional-water-tiles.md)

## 时间、天气和地质

| 系统 | 当前行为 |
| --- | --- |
| 昼夜与大气 | 世界时钟从第 1 天 09:00 开始，默认每现实秒推进 60 游戏秒。120 个游戏日是一个气候周期，天空、太阳、月亮、云和雾随时间与位置变化。 |
| 区域天气 | 温度、湿度和风受位置、高程、季节及地貌影响。区域模式可读取生成的气候场。角色附近表现阴晴、雨雪、雾和雷雨闪电。 |
| 岩层与矿迹 | 天然岩性按位置和深度变化，矿体由种子和所属区生成。`G` 查询命中处和附近实岩。可看岩色和矿迹，采料及库存结算待单独实现。 |

[时间](../../Assets/Reol2/World/WorldClock.cs) · [气候](../../Assets/Reol2/World/WorldClimate.cs) · [环境](../../Assets/Reol2/World/WorldEnvironment.cs) · [原型地质](../../Assets/Reol2/World/WorldGeology.cs)

## 主角

当前人体和动作使用 Quaternius 资产。Unity Humanoid 与 Blend Tree 用碰撞后的实际速度驱动待机、慢走、跑和冲刺。地面有蹲行、跳跃和落地动作，飞行用悬浮姿态。踩水的动画髋部对齐角色，前进游泳增加身躯方向的胶囊扫掠。足部 IK 在着地条件满足时对齐坡面，腾空和游泳时退出。[角色动画](../../Assets/Reol2/World/PlayerAnimatorDriver.cs) · [玩家控制](../../Assets/Reol2/World/ContinentExplorer.cs) · [资产许可](../../THIRD_PARTY_NOTICES.md)

## 复现

从岸边向内陆走，按 `]` 看昼夜变化，按 `F1` 到水面，按 `F2` 飞向洞口。在能命中的地面和洞壁按 `G` 看矿迹。Windows Player 可用 `-continent-benchmark -world-expansion-check` 复跑环境和动作路线。[试玩与命令](../large-world-demo.md#复现)
