# 双块体素原型

双块场景是平滑挖填和角色运动的最小对照。两个相邻区块构成缓坡和岩洞，按一米整数格编辑，密度采样间距 0.5 米。Marching Cubes 构建可见面和碰撞。跨边界编辑时相邻块读取同一组密度样本。[测试入口](../Assets/Reol2/Tests/PlayMode/VoxelDemoTests.cs) · [大陆对照](continent-demo.md)

## 试玩

Unity 菜单选 `Reol 2 → Open Voxel Demo`，运行 `Assets/Scenes/VoxelDemo.unity`。Windows 本地构建从 `Reol 2 → Build Windows Demo` 生成 `Builds/VoxelDemo/Reol2VoxelDemo.exe`。

| 操作 | 按键 |
| --- | --- |
| 行走、看向目标 | WASD、鼠标 |
| 跳跃 | `Space` |
| 挖土、填土 | 左键、右键 |
| 重置地形并回到起点 | `R` |
| 释放鼠标 | `Esc` |
| 移动速度翻倍、减半 | `=`、`-`，或释放鼠标后点 HUD |

沿路灯走上缓坡，挖开灯之间的岩壁并穿过。退出通道后再填回。瞄准框显示挖掘与填放目标，角色占用检查保护身体空间，路头地面保持可站立。[原型控制与编辑](../Assets/Reol2/Demo/VoxelDemoPlayer.cs)

## 性能测试

Windows 本地试玩加 `-voxel-benchmark`，预热后采集静止和跨块挖填，每帧离屏渲染，并检查 GPU 图像读回。场景用例覆盖接缝、负坐标、占用保护和改动后的碰撞。[开发工具](development.md) · [双块阶段实测](H:/reol2-workbench/goals-old/world-prototype/goal-01-smooth-voxel.md)
