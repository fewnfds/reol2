# URP 地形实时阴影的距离衰减

远坡出现固定角度的黑线时，在同一机位切换主光阴影，然后对照近处本应可见的角色阴影。URP 17.3.0 的 `GetMainLight(shadowCoord)` 只返回实时阴影采样，D3D12 反向 Z 深度下范围外的坐标仍可能落进有效采样区间。当前地形着色使用包含 `positionWS` 的平滑衰减重载，纯程序生成地形传入全白 `shadowMask`。[地形着色](../../Assets/Reol2/Visuals/VoxelSurface.shader)

Windows 本地试玩可用 `-continent-benchmark -world-terrain-shadow-check -landforms 64 -world-seed 20261121` 复跑。用 `-continent-image` 输出固定机位和稍移机位的图像，对照级联远坡及脚下阴影。[复现代码](../../Assets/Reol2/World/TerrainShadowBenchmark.cs) · [原始回归](H:/reol2-workbench/goals/landform-generation/reasearch-terrain-shadows.md)
