# Windows 试玩程序触发防火墙确认

Unity Windows Development Player 启动 Profiler 网络发现时可能触发防火墙确认。无人值守基准使用项目菜单或对应的 Windows 构建方法生成本地试玩，三套入口共用 `WindowsTestBuild`。基准和倍速 HUD 由 `REOL_TEST_BUILD` 启用。碰到提示时先确认弹窗显示的实际 Player 路径与构建类型。[构建方式](../development.md#windows-本地试玩构建) · [共用构建规则](../../Assets/Reol2/Editor/WindowsTestBuild.cs)
