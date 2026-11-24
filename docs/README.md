# Reol 2 项目文档

Reol 2 是 Windows 单机第三人称 3D 修仙沙盒。当前原型验证连续旅行和可编辑地形。玩家可以飞行、游泳、挖填、两点整形和查看矿迹。[设计方向](design-direction.md)

## 试玩

本机运行 `Builds/LargeWorld/Play-Terrain-Demo.cmd`，从 64 公里地貌的地面开始试玩逐格挖填与两点整形。河流试玩从同目录的 `Play-River-Demo.cmd` 进入。两者使用种子 `20261121` 和原生分辨率无边框窗口。直接运行 `Reol2World.exe` 进入默认大世界。[操作与构建](large-world-demo.md#试玩)

## 当前技术文档

| 内容 | 文档 |
| --- | --- |
| 坐标域、地形采样、近远景、水位、编辑与流送 | [世界引擎](roadmap/world-engine.md) |
| 昼夜、天气、地表配色、勘探与角色动画 | [视觉、自然系统与主角](roadmap/world-visual-nature-character.md) |
| 流送与挖填的独立对照场景 | [两公里大陆](continent-demo.md) · [双块体素 Demo](prototype.md) |
| Unity 环境、本机构建、测试和 F8 复现包 | [开发与验证](development.md) |
| 岸线、阴影、试玩窗口和 HUD 输入排查 | [踩坑记录](pitfalls/) |
| 顾问接口、附件和单轮咨询 | [本机顾问](../tools/advisor/README.md) |
| 资产来源与许可 | [第三方声明](../THIRD_PARTY_NOTICES.md) |

阶段目标、计划、旧数据和开发日志从本机工作台查阅。[工作台索引](H:/reol2-workbench/README.md)
