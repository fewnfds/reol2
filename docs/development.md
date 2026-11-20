# 引擎与开发工具

## Unity

采用 Unity 6.3 LTS `6000.3.29f1`、Universal 3D（URP）和 C#，先面向 Windows 单机。版本固定在 `ProjectSettings/ProjectVersion.txt`。6.3 LTS 支持到 2027 年 12 月。Unity Personal 可以免费使用，对自行开发游戏的个人，资格门槛是过去 12 个月与使用 Unity 相关的总财务额不超过 20 万美元。UE 的游戏许可也可以零成本起步。选 Unity 的主要理由是 C# 规则便于单测和审查，官方 CLI 可以用同一套工具管理项目、驱动编辑器、测试和构建。这个项目的主要技术成本在运行时方块编辑、碰撞、寻路和存档联动。

- [Unity 6 发布与支持](https://unity.com/releases/unity-6/support) · [6000.3.29f1 发布说明](https://unity.com/releases/editor/whats-new/6000.3.29f1) · [Unity Editor 条款](https://unity.com/legal/editor-terms-of-service/software) · [UE 许可](https://www.unrealengine.com/license)

## Codex 开发工具

| 工具 | 用途 |
| --- | --- |
| Unity CLI `1.0.1` | 编辑器管理、包、测试和构建。CLI 安装后可独立于 Hub 使用。 |
| Unity Pipeline `1.0.1` + Unity MCP | Pipeline 是项目依赖。CLI 通过 stdio 提供 MCP，连接运行中的项目编辑器，查询场景和日志，操作 Play Mode 并截图。 |
| Unity 官方 skills | 项目本机安装 `unity-cli`、`new-unity-project`、`unity-pipeline`、`unity-package-management`，授权范围见 Unity Companion License。 |
| `reol2-voxel` | 仓库自带的体素游戏开发与验证 skill。 |

官方 CLI 和 Unity MCP 可免费使用，无需 Unity AI 订阅。编辑器需要有效许可证。激活后从仓库根目录运行 `unity open .`，首次导入会生成 `Packages/packages-lock.json`。MCP 在编辑器关闭时可查询 CLI 状态和本机项目。编辑器连接以 `unity status --format json` 显示就绪为准。用 `unity command --format json` 查看当前 Pipeline 命令，然后检查 Console、Play Mode 中推进的帧和画面。能用可重复测试观察的玩法行为，优先记录到 Unity Test Framework 中。

在另一台机器恢复 Unity Agent 工具时，先安装 Unity CLI，然后从仓库根目录运行以下命令。`mcp configure` 会把当前项目路径写进本机的项目级 Codex 配置。Codex 会从受信任的项目加载项目级配置。在新会话中检查 `unity` 是否出现在 MCP 工具列表。

```powershell
unity install 6000.3.29f1
unity skill install codex --local --skill unity-cli,new-unity-project,unity-pipeline,unity-package-management
unity mcp configure codex --local --project-path (Get-Location).Path
```

- [Unity CLI 1.0](https://unity.com/the-unity-cli-1.0-is-here) · [官方 CLI 安装与用法](https://docs.unity.com/en-us/unity-cli/use-unity-cli) · [Unity MCP 接入](https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli) · [Unity Skills](https://github.com/Unity-Technologies/skills)

### 开源替代方案（2026 年 11 月 20 日核验）

| 方案 | 许可与近期版本 | 适用场景 |
| --- | --- | --- |
| [IvanMurzak Unity-MCP](https://github.com/IvanMurzak/Unity-MCP/releases/tag/1.13.0) | Apache-2.0。`1.13.0` 发布于 2026 年 11 月 19 日 | 同时提供 MCP、CLI 和 skills，适合需要自行扩展或维护本地工具时。 |
| [MCP for Unity（CoplayDev）](https://github.com/CoplayDev/unity-mcp/releases/tag/v10.3.0) | MIT。`v10.3.0` 发布于 2026 年 10 月 4 日 | 场景、Prefab、Console、Unity Test Framework 和 Blender 接入。 |

## 美术与代码参考

首个原型用程序生成的方块地形和颜色材质。角色和环境道具从 [Quaternius Cube World Kit](https://quaternius.com/packs/cubeworldkit.html) 起步，它提供 CC0 的带动画角色、动物与道具，有 FBX 格式。需要更细的人物基础时可看 [Universal Base Characters](https://quaternius.com/packs/universalbasecharacters.html)，少量道具可从 [Kenney Voxel Kit](https://kenney.nl/assets/voxel-kit) 选，同为 CC0。缺少特定修仙道具时，可以先做简单几何体，再视需要用 Blender 或 AI 生成静态道具。

体素实现可查 [HELLO_MY_WORLD](https://github.com/kojeomstudio/HELLO_MY_WORLD) 的 Unity 6 C# 分块和寻路代码。通用 C# 游戏架构可看 [Unity 游戏编程模式示例](https://github.com/Unity-Technologies/game-programming-patterns-demo)。用作参考时先核对对应代码和素材的许可。
