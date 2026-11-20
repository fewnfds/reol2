# Reol 2

这是 Windows 单机第三人称 3D 方块沙盒。用 Unity 6.3 LTS、URP 和 C#。旧 Reol 提供玩法参考，先让角色生活、玩家挖建和世界持续变化在同一个可玩的场景里互相影响。已确认的玩法以用户决定为准，原型方案见 [docs/prototype.md](docs/prototype.md)。

## 开发与验证

- 从可观察的玩家行为和世界结果开始。把可编辑方块的状态、派生的网格与碰撞数据、角色知道的信息分别建模。引入新优化前，先记录能复现的规模、帧时和积压。
- 体素、通行与持久化任务使用 `reol2-voxel` skill。Unity 项目、包、编辑器场景、Console 和 Play Mode 优先使用本项目配置的 Unity CLI、MCP 和 Unity skills。入口与恢复安装方法见 [docs/development.md](docs/development.md)。
- 修改玩法后，从玩家入口运行相应的 Play Mode 或实机检查。纯规则可用 Edit Mode 验证。具体记录通过的场景、测试结果和验证范围。移动 Unity 资源时随同提交 `.meta` 文件，提交前检查场景和资源引用。
- 引入第三方素材、代码或生成资产时，记录来源、版本和许可。为项目创作自己的地图、角色和视觉。优先使用少量可复用、视觉一致的素材。

## 本机顾问

复杂设计和 review 可以调用 `python -X utf8 .local/advisor/advisor.py ask --prompt "问题"`。每轮交代背景、已有证据和待决策问题。顾问返回的建议由当前执行者结合项目源码和实测取舍。发送附件时只选本轮需要的材料。

本机网关配置、凭据与原始咨询记录放在 Git 忽略的 `.local/`。项目专用的 MCP 配置也留在本机。
