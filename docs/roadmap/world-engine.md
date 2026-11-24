# Reol 2 世界引擎

## 坐标与地图

默认大世界用种子和坐标函数生成地形。X、Z 分别为 `[-2²⁶, 2²⁶)` 米，各轴跨度约 134,218 公里。Y 为 `[-32768, 32768)` 米，海平面在零米。整数页索引和双精度世界位置通过三轴浮动原点转换为 Unity 局部坐标。全局轮廓是 257 × 257 个陆海节点，近场高程和密度在需要时求值。[世界范围](../../Assets/Reol2/World/WorldDefinition.cs) · [浮动原点](../../Assets/Reol2/World/WorldFrame.cs) · [陆海总图](../../Assets/Reol2/World/WorldAtlas.cs)

区域模式用自研生成器构造一个 16、64 或 256 公里区域。250 米网格提供气候、汇水及河流拓扑，沿细高程生成河轴与断面。河流形成连通水位后，在谷地范围内平滑尖角，再构造最终断面。湖、河、海的水面随区域瓦片生成。每一档是独立生成区域，父流域的跨区来水和水位接续还处在工作台验证阶段。[区域地貌](../../Assets/Reol2/World/RegionalLandforms.cs) · [排水](../../Assets/Reol2/World/RegionalDrainage.cs) · [河段](../../Assets/Reol2/World/RegionalRivers.cs) · [地貌阶段](H:/reol2-workbench/goals/landform-generation/todo.md)

## 编辑、碰撞与通行

- 近场采用 8 米体积页和 0.5 米三维密度采样。Marching Cubes 从密度样本插值得到地表和地下形状，画面与 MeshCollider 使用同一份发布网格。可证明整页空或实的区域跳过构网。[构网](../../Assets/Reol2/Voxels/MarchingCubes.cs) · [地形输入](../../Assets/Reol2/World/ContinentField.cs)
- 大世界支持按真实命中位置做连续挖填与两点整形，盒刷按一米格编辑。编辑样本和填料来源由当前世界会话持有，相邻页按同一世界坐标读样，覆盖区域重建后一起发布。离开并重新进入已卸载地形时，重建会读取这一会话中的改动。[编辑与发布](../../Assets/Reol2/World/ContinentTerrain.cs) · [试玩键位](../large-world-demo.md#试玩)
- 移动安全取决于已发布的碰撞。前视扫掠优先准备角色将经过的页，再处理周围的一般请求。当前预测 0.68 秒，预测位移最多 44 米，垂直分量上限 14 米。构建每帧最多检查 72 页、发布 3 个表面页，按 2.25 毫秒目标交回，实际时间取决于已开始的页。远传先准备落点邻域并核对净空。[移动与预取](../../Assets/Reol2/World/ContinentTerrain.cs) · [角色落点](../../Assets/Reol2/World/ContinentExplorer.cs)

## 近远交接

远处用分层高度瓦片，粗父瓦片在子瓦片齐套后交接。大世界地表远景容量为 224 片。巨洞由洞室与有界通道构成，已就绪的近场页接管活动范围的地表与洞口，粗洞壁补足远处及精细页边沿。近处的修改按当前编辑数据构网，远处的地表主要展示初始地貌。[高度瓦片](../../Assets/Reol2/World/WorldHorizon.cs) · [洞形](../../Assets/Reol2/World/WorldCaves.cs) · [粗洞壁](../../Assets/Reol2/World/WorldCaveHorizon.cs)

## 水位与现有边界

区域河段以同一组断面查询河床和水位。区域瓦片把水位与地形按相同三角形裁切，符合全淹没条件的瓦片可加入共享水面。角色以水柱判断浮潜，水下保留室外视距并应用滤镜和音效。挖到海平面以下的陆地干坑仍保持干燥，开挖后的自动进水处在后续阶段。[区域水体网格](../../Assets/Reol2/World/RegionalSurfaceTile.cs) · [水体排查](../pitfalls/regional-water-tiles.md)

跨会话存档、材料采集结算、岩体失稳、建筑承重和居民通行都有独立的待验证任务。当前可用的运行范围是单角色和单活动地形中心。[相关阶段](H:/reol2-workbench/goals/world-roadmap/todo.md)

## 验证

用相同种子、相机范围、路线、编辑操作和图形设置做前后对照。记录采样访问量、有效发布、重建范围、积压和 P95/P99。碰撞、帧时和含水覆盖分别记录。近远交接需要检查冷到达、稳定状态、回访和跨原点。[大世界回归命令](../large-world-demo.md#复现) · [大陆基准入口](../continent-demo.md#本机验证)
