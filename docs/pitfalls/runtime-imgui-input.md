# 运行时 IMGUI 的键鼠输入

项目的 Active Input Handling 为 `Input System Package (New)`。在这个设置下，运行时 `OnGUI` 可以绘制控件，`GUI.Button` 等交互依赖的旧输入事件需要额外启用旧输入后端。[Unity Input System 的 UI 支持](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.14/manual/UISupport.html#immediate-mode-gui)

试玩倍速 HUD 由 IMGUI 绘制，通过 Input System 的 `wasPressedThisFrame` 处理点按。命中计算将鼠标的左下原点坐标转换为 GUI 的左上原点坐标，与绘制共用按钮矩形。控件收到点击时，角色控制器同时消费该指针输入，避免触发掌舵或鼠标锁定。[试玩倍速代码](../../Assets/Reol2/Demo/TestMovementSpeed.cs)

离屏 Player 回归禁用角色的逐帧输入时，窗口失焦可能留下待跳过的一帧。脚本模拟洞口快捷键前先送一帧没有操作的输入，再检查角色是否到达目标。普通游玩由每帧的角色更新处理焦点恢复。[输入处理](../../Assets/Reol2/World/ContinentExplorer.cs) · [回归驱动](../../Assets/Reol2/World/ContinentBenchmark.cs)
