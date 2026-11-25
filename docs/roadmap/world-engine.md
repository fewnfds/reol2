# Reol 2 世界引擎

## 坐标与地图

默认大世界用种子和坐标函数生成地形。X、Z 分别为 `[-2²⁶, 2²⁶)` 米，各轴跨度约 134,218 公里。Y 为 `[-32768, 32768)` 米，海平面在零米。整数页索引和双精度世界位置通过三轴浮动原点转换为 Unity 局部坐标。全局轮廓是 257 × 257 个陆海节点，近场高程和密度在需要时求值。[世界范围](../../Assets/Reol2/World/WorldDefinition.cs) · [浮动原点](../../Assets/Reol2/World/WorldFrame.cs) · [陆海总图](../../Assets/Reol2/World/WorldAtlas.cs)

区域模式用自研生成器构造一个 16、64 或 256 公里区域。250 米网格提供气候、汇水及河流拓扑，沿细高程生成河轴与断面。河流形成连通水位后，在谷地范围内平滑尖角，再构造最终断面。湖、河、海的水面随区域瓦片生成。每一档是独立生成区域，父流域的跨区来水和水位接续还处在工作台验证阶段。[区域地貌](../../Assets/Reol2/World/RegionalLandforms.cs) · [排水](../../Assets/Reol2/World/RegionalDrainage.cs) · [河段](../../Assets/Reol2/World/RegionalRivers.cs) · [地貌阶段](H:/reol2-workbench/goals/landform-generation/todo.md)

## 编辑、碰撞与通行

- 近场采用 8 米体积页和 0.5 米三维密度采样。Marching Cubes 从密度样本插值得到地表和地下形状，画面与 MeshCollider 使用同一份发布网格。可证明整页空或实的区域跳过构网。[构网](../../Assets/Reol2/Voxels/MarchingCubes.cs) · [地形输入](../../Assets/Reol2/World/ContinentField.cs)
- 手工挖填从命中处选择一米格。圆角侧壁使用种子与整数坐标给出轻微变化，顶底平面保留相邻格的共同标高。相交晶格边的外端沿用原有占用符号，并参与插值，未动到的地形按原值构网。一次挖填检查最多 81 个样本。两点整形按设计高程写入同一密度场。[挖填采样](../../Assets/Reol2/World/ContinentField.cs) · [试玩键位](../large-world-demo.md#试玩)
- 编辑覆盖与填料来源按 8 米三维页记录。相邻页按同一世界坐标读样，覆盖区域重建后一起发布。离开后回访会用相同采样重建，Windows 大世界与区域试玩在重启前后也读取同一份改动。球形体积编辑接口用于基准和地形验证。[编辑与发布](../../Assets/Reol2/World/ContinentTerrain.cs)
- 移动安全取决于已发布的碰撞。前视扫掠优先准备角色将经过的页，再处理周围的一般请求。大世界预测 0.68 秒，预测位移最多 64 米，垂直分量上限 28 米。构建每帧最多检查 72 页、发布 3 个表面页，按 2.25 毫秒目标交回，实际时间取决于已开始的页。远传先准备落点邻域并核对净空。[移动与预取](../../Assets/Reol2/World/ContinentTerrain.cs) · [角色落点](../../Assets/Reol2/World/ContinentExplorer.cs)

## 近远交接

远处用分层高度瓦片，粗父瓦片在子瓦片齐套后交接。大世界地表远景容量为 224 片。近处的修改按当前编辑数据构网，远处的地表主要展示初始地貌。[高度瓦片](../../Assets/Reol2/World/WorldHorizon.cs)

高度瓦片随高度扩大地平线覆盖，地面和高空的名义距离分别约 20 公里和 150 公里。实际能见度还受天气与地形遮挡影响。普通干地瓦片用相邻级别的高程差、相机距离、竖直视角和画幅估计屏幕误差。细分目标约 1.5 像素，已细分瓦片在误差降至约 1 像素时才回粗。距表面 96 米内优先保留细节，预算紧张时允许更大的误差。冷瓦片、水岸与海床沿用距离规则。干地按共同采样缝合，区域海岸和湖岸保留构建水面时使用的高程。[瓦片流送](../../Assets/Reol2/World/WorldHorizon.cs) · [区域边界](../../Assets/Reol2/World/RegionalSurfaceTile.cs) · [实测](H:/reol2-workbench/goals/terrain-detail-and-persistence/results.md#近远景)

巨洞由洞室与有界通道构成。以玩家为中心约水平 4.1 公里、垂直 2 公里搜索洞穴体积，流送连通洞壁的 8 米采样网格。洞口使用额外的 1 米显示网格，洞缘由同一密度场计算。附近发布的 0.5 米曲面同时用于显示和碰撞。精细岩壁有优先显示权，粗洞壁和高度瓦片在接缝处保留重叠。洞内降低照明，同时保留室外天空和远裁剪距离，由洞壁遮挡视野。[洞形](../../Assets/Reol2/World/WorldCaves.cs) · [洞壁流送](../../Assets/Reol2/World/WorldCaveHorizon.cs) · [镜头环境](../../Assets/Reol2/World/WorldEnvironment.cs)

## 地形存档

Windows 大世界试玩按地图种子和区域尺寸保存已编辑采样，密度以原始浮点值写入并保留填料来源。原始地貌由生成器按需重算。启动时校验生成器版本和存档内容，在准备出生点碰撞前载入编辑。后台提交间隔约五秒，正常关闭会刷入待写改动。存档用原子替换提交并保留上一份备份。主档损坏时可以读取有效备份。[格式与提交](../../Assets/Reol2/World/WorldTerrainSave.cs) · [存档位置与复现](../large-world-demo.md#地形存档)

当前试玩启动时读入已编辑页，每次提交写一份压缩的编辑快照。角色仍从试玩入口指定的位置进入，必要时在附近寻找安全落点。NPC、物件和连续世界模拟各有后续存档阶段。[世界路线](H:/reol2-workbench/goals/world-roadmap/todo.md)

改变原始密度、洞穴或生成高程时，需要更新生成器版本并验证旧档迁移。存档头记录该版本，载入时按当前生成器校验。[生成器版本](../../Assets/Reol2/World/WorldDefinition.cs) · [档位身份](../../Assets/Reol2/World/WorldTerrainSave.cs)

## 水位与现有边界

区域河段以同一组断面查询河床和水位。区域瓦片把水位与地形按相同三角形裁切，符合全淹没条件的瓦片可加入共享水面。角色以水柱判断浮潜，水下保留室外视距并应用滤镜和音效。挖到海平面以下的陆地干坑仍保持干燥，开挖后的自动进水处在后续阶段。[区域水体网格](../../Assets/Reol2/World/RegionalSurfaceTile.cs) · [水体排查](../pitfalls/regional-water-tiles.md)

材料采集结算、岩体失稳、建筑承重和居民通行都有独立的待验证任务。当前可用的运行范围是单角色和单活动地形中心。[相关阶段](H:/reol2-workbench/goals/world-roadmap/todo.md)

## 验证

用相同种子、相机范围、路线、编辑操作和图形设置做前后对照。记录采样访问量、有效发布、重建范围、积压和 P95/P99。碰撞、帧时和含水覆盖分别记录。近远交接需要检查冷到达、稳定状态、回访和跨原点。[大世界回归命令](../large-world-demo.md#复现) · [大陆基准入口](../continent-demo.md#本机验证)
