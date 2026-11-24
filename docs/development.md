# 开发与验证

## Unity 环境

项目用 Unity 6.3 LTS `6000.3.29f1`、URP 17.3.0 和 C#。编辑器版本与 UPM 解析结果以项目文件为准。本机 Unity CLI 和 Unity Pipeline 为 1.0.1。编辑器需要有效的 Unity 许可证。[项目版本](../ProjectSettings/ProjectVersion.txt) · [项目依赖](../Packages/manifest.json) · [Unity CLI 文档](https://docs.unity.com/en-us/unity-cli/use-unity-cli)

在仓库根目录用 `unity open .` 打开。用 `unity status --format json` 检查 Editor 与 Pipeline 是否连接，`unity command --format json` 列出连接实例支持的命令。在新环境恢复时运行以下命令，Codex 从信任的项目读取本机 MCP 配置。

~~~powershell
unity install 6000.3.29f1
unity skill install codex --local --skill unity-cli,new-unity-project,unity-pipeline,unity-package-management
unity mcp configure codex --local --project-path (Get-Location).Path
~~~

## 顾问与提交验证

本机顾问使用 Gemini Web2API，每个请求一条 `user` 消息。阶段任务的咨询输入、回复和证据放工作台，对照使用 `--prompt-file` 与 `--out-dir`。顾问连接配置与凭据放在仓库忽略的 `.local/advisor/`。[顾问快速上手](../tools/advisor/README.md) · [工作台](H:/reol2-workbench/README.md)

GitHub Actions 静态核查纳入 Git 的资源与 `.meta`、构建场景 GUID、包 JSON，并运行顾问模拟网关测试。改动玩法后从对应场景跑 Play Mode 或 Windows Player 复核。[验证脚本](../tools/check_unity_files.py) · [项目工作约定](../AGENTS.md)

~~~powershell
python -B -X utf8 tools/check_unity_files.py
python -B -X utf8 tools/advisor/test_advisor.py
unity test . --mode PlayMode --output .local/playmode.xml --non-interactive
~~~

### 试玩角色倍速

`VoxelDemo`、`ContinentDemo` 和 `LargeWorld` 共用测试倍速。初始 1×，每次翻倍或减半，范围 0.25× 至 64×。在 HUD 点击 `+` 或 `−`，也可用主键盘和数字键盘。倍率作用于地面移动、飞行和游泳。跳跃、重力与世界时钟采用常规规则。流送未就绪时，碰撞保护仍限制实际位移。[倍速实现](../Assets/Reol2/Demo/TestMovementSpeed.cs) · [HUD 排查](pitfalls/runtime-imgui-input.md)

### Windows 本地试玩构建

三个试玩共用 `WindowsTestBuild`。普通 Windows Player 以 `REOL_TEST_BUILD` 启用倍速 HUD 和基准命令。大世界和区域试玩共用 `Builds/LargeWorld`。构建同时生成地面启动脚本 `Play-Terrain-Demo.cmd` 与河流启动脚本 `Play-River-Demo.cmd`。两公里大陆和双块原型按需从各自入口构建。[大世界](large-world-demo.md#复现) · [两公里大陆](continent-demo.md#复现) · [双块原型](prototype.md#试玩) · [防火墙排查](pitfalls/windows-player-firewall.md)

### 渲染复现包

LargeWorld 本地试玩和编辑器 Play Mode 中，`F8` 将 `frame.png` 和 `report.json` 写入一个 ZIP。Windows 默认目录为 `%USERPROFILE%\AppData\LocalLow\fewnfds\Reol 2\RenderReports`。报告包括构建、种子、画幅、相机、天气、可见瓦片、浮动原点及近期流送记录。[导出实现](../Assets/Reol2/World/WorldRenderReport.cs) · [水面回放](large-world-demo.md#复现)

## 素材与文档

当前角色与动作采用 Quaternius CC0 素材，Unity Humanoid 负责动画。第三方来源与版本记在许可声明里。项目文档随当前入口和行为更新，阶段计划、原始证据与历史日志保存在工作台。[来源与许可](../THIRD_PARTY_NOTICES.md) · [项目文档入口](README.md)
