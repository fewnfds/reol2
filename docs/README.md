# Reol 2 项目文档

## 当前状态

开发先验证无缝旅行、角色移动与体素修改。自然地形按格挖填，可见曲面和坡面碰撞来自同一密度场。低多边形视觉、动态仙侠世界和自由选择身份是项目方向。细节随用户取舍与实测调整。[设计方向整理](design-direction.md) · [访谈简报](preproduction/brief-reol2-2026-11-21/brief.md)

随机大世界已可在 `LargeWorld` 场景和 Windows 构建试玩。每个水平轴 `2²⁷` 米，海平面上下各 32,768 米。已验证远角挖填回访、千公里区域连续高空穿越、巨洞双入口、深洞挖填和约 11 公里的海沟下潜。两公里大陆仍用于基准。跨会话持久化、进水、材料和结构失稳有各自的后续阶段。[大世界试玩与实测](large-world-demo.md) · [两公里大陆基准](continent-demo.md)

当前试玩加入了昼夜大气、区域天气、矿藏勘探和带动作的人形主角。地表、洞壁和远景使用同一套配色，海、河、湖仍按解析水位提供潜水与游泳。[当前玩法与实测](roadmap/world-visual-nature-character.md)

## 按任务查阅

| 要了解什么 | 以哪份文档为准 |
| --- | --- |
| 大世界试玩、操作、构建和性能边界 | [随机大世界试玩与实测](large-world-demo.md) |
| 地表与水面、昼夜、自然环境、勘探和主角动画 | [大世界视觉、自然系统与主角](roadmap/world-visual-nature-character.md) |
| 试玩、验证覆盖、硬件与复现命令 | [大陆试玩与基准](continent-demo.md) |
| 完整坐标域、随机地貌、地表、深洞与流送压测 | [随机大世界方案](roadmap/large-world.md) |
| 流送、潜水、破坏、岩体和承重的阶段与验收 | [大陆与体素世界引擎](roadmap/world-engine.md) |
| 深度成本、样本归属、稀疏编码与存档用例 | [体素存储与流送选型](roadmap/voxel-storage.md) |
| 居民、聚落演算、时代变化和后续扩展 | [开发路线图](roadmap/README.md) |
| Unity 环境、素材来源、测试与轻量 CI | [引擎与开发工具](development.md) |
| 顾问快速上手、附件与接口 | [本机顾问](../tools/advisor/README.md) |

## 早期对照

两块坡面 Demo 继续用于验证挖填与角色运动。[双块原型与性能记录](prototype.md) · [平滑地形初期选型](preproduction/smooth-voxel-terrain.md) · [路线图编制记录](roadmap/review.md)
