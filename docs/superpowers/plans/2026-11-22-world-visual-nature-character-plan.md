# Reol 2 世界视觉、自然系统与角色基础实施计划

> 2026 年 11 月 22 日批准时的实施记录。当前接口、取舍和实测见[大世界视觉、自然系统与主角](../../roadmap/world-visual-nature-character.md)。

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 建立一套和谐、可扩展且性能可测的大世界视觉、非生物自然系统和第三人称角色动画基础，让玩家能在具有昼夜、气候、地质资源和可信人物动作的连续大陆上移动与探索。

**Architecture:** 地形颜色、天空、气候、地质和角色表现各自拥有清晰的查询或表现边界，所有需要世界位置的规则都从确定性种子和整数化世界坐标计算。近处才创建可见对象、粒子、精细资源线索和脚部约束，远处保存区域级状态与事件时间，地形网格、碰撞和角色根节点继续使用现有的页流送与 WorldFrame 浮动原点。

**Tech Stack:** Unity 6000.3.29f1、URP 17.3.0、C#、Unity Test Framework、CharacterController、Mecanim Humanoid、内置 Humanoid Foot IK。首批角色资产采用 Quaternius CC0 素材。Animation Rigging 作为后续武器瞄准和复杂身体约束阶段的候选依赖。

**Spec:** docs/design-direction.md、docs/README.md、docs/roadmap/world-engine.md、docs/roadmap/large-world.md 以及本轮顾问记录 .local/research/world-expansion-2026-11-22/。

## Global Constraints

- Unity 版本固定为 6000.3.29f1，目标平台保持 Windows 单机，渲染使用 URP。
- 现有 ContinentTerrain、WorldFrame、近场页流送、远景瓦片、海水解析水位和 CharacterController 作为接入点继续使用。
- 世界规则使用 double 世界坐标或整数区域坐标，材质采样不能依赖浮动原点后的局部 Vector3。
- 所有远处自然状态采用确定性种子、区域摘要或事件时间线，活动范围以外不创建每格 GameObject、粒子系统或体素水状态。
- 第一轮视觉材质保持低多边形和少量可复用色板，增加细节时必须同时提供距离衰减、采样间距或屏幕像素足迹依据。
- 角色根节点负责碰撞、流送、移动和浮动原点，视觉子节点负责网格、Animator、动画平滑和脚部适配。
- 普通路线继续以 16.67 ms 总帧时作为 60 Hz 参考，现有流送构建目标 2.25 ms 和近场验收路线继续记录。
- 新增第三方资产必须在 THIRD_PARTY_NOTICES.md 记录来源、版本、许可证和仓库中实际使用的文件。
- 每个阶段都需要一个可复现的种子、路线、测试场景和性能记录，调优时一次只改变一个负载因素。

## Review Focus

- 固定频率纹理或噪声在远景 LOD、海面俯视和洞穴近景中产生混叠时，画面应保持连续，测试由视觉材质任务覆盖。
- 浮动原点重定位后同一全球坐标的颜色、天空时间和天气区域应保持一致，测试由视觉与环境任务覆盖。
- 远区时间、气候和资源查询的成本应按被访问区域增长，测试由自然系统任务覆盖。
- 角色实际速度为零、上坡、台阶、跳跃、游泳和飞行时，动画参数应与移动状态一致，测试由角色任务覆盖。
- 资产导入、Humanoid Avatar、动画引用和场景序列化出现缺失时，构建和静态检查应尽早失败，测试由角色与交付任务覆盖。

## 现有证据与本轮取舍

当前 ContinentField.WorldColor 和 SurfaceColor 使用约 170 米与 146 米的固定正弦频率，CaveColor 使用约 41 米的斜向正弦频率。WorldHorizon 在高等级远景瓦片中直接按 8 << lod 米取色，颜色采样会欠采样。ContinentOcean.shader 用两组固定频率正弦相乘形成水纹，ContinentExplorer 直接写入天空色和雾参数，当前角色由 Travel Robes.asset 占位。代码位置为 Assets/Reol2/World/ContinentField.cs、Assets/Reol2/World/WorldHorizon.cs、Assets/Reol2/World/ContinentOcean.shader、Assets/Reol2/World/ContinentExplorer.cs 和 Assets/Reol2/Editor/ContinentAuthoring.cs。

本计划采用确定性非周期哈希或梯度噪声、宏观色板和按采样间距抑制的多频段材质。宏观频带服务远景，中观变化服务普通视距，近景扰动只在可见表面需要时出现。陆地、洞壁、远景代理和近场 Marching Cubes 统一经过同一套配色规则。海面首轮以 Fresnel、天空反射色、低频非周期扰动和距离衰减为主，避免规则菱形波纹成为远景主视觉。

自然系统采用四层数据职责。视觉层负责天空、太阳、月亮、雾、云和降水表现，局部交互层负责玩家附近的能见度、温度体感、降雨声和资源露头，区域统计层负责气候、温度、湿度、风、季节、水位和地质，远处只保存确定性种子、区域状态和事件时间线。地质资源采用岩层、地质省、矿脉宿主、露头和玩家探测线索的组合，符合 Vintage Story 的探矿范式。全世界逐格天气、水流、积雪和侵蚀安排到后续有实际需求的阶段。

角色采用 Quaternius Universal Base Characters 与 Universal Animation Library 作为首选素材来源。控制器继续由当前 ContinentExplorer 驱动，Animator.applyRootMotion = false，使用实际有效速度驱动 Blend Tree，内置 Humanoid Foot IK 先覆盖坡面和低台阶。Unity Animation Rigging 后续用于武器、手部和复杂上半身约束。参考资料见 Unity CharacterController、Animator、Animation Rigging、Quaternius、Vintage Story 世界生成与探矿文档，链接集中列在文末。

## 依赖关系与实施顺序

| 顺序 | 子项目 | 依赖 | 交付物 |
| --- | --- | --- | --- |
| 0 | 基线与契约 | 当前 E7 大世界 | 可复现截图、帧时和接口约定 |
| 1 | 统一材质与远近视觉 | 0 | 陆地、洞穴、海面和远景色彩连续 |
| 2 | 时间、昼夜和大气 | 0 | 世界时钟、太阳、月亮、天空、雾 |
| 3 | 气候、季节、地质和资源 | 2 | 非生物自然查询与资源线索闭环 |
| 4 | 局部天气与水文表现 | 2、3 | 区域天气、降水、风、海岸和水下表现 |
| 5 | 人物资产与 Animator | 0、1、2 | Humanoid 主角、走跑停跳蹲和坡面表现 |
| 6 | 综合验收与文档收敛 | 1–5 | Windows 构建、性能记录和现行文档 |

实现期间优先完成 1、2、3、5 的最小闭环，再扩展 4。任何阶段遇到帧时尾部、内存驻留或队列年龄上升，都先回到该阶段的负载测量，再决定降低频率、减少驻留、改为摘要或拆分工作预算。

### Task 0: 固定基线、测试夹具和接口契约

**Files:**
- Create: Assets/Reol2/Tests/PlayMode/WorldExpansionTests.cs
- Modify: Assets/Reol2/World/ContinentBenchmark.cs
- Modify: docs/continent-demo.md
- Modify: docs/large-world-demo.md

**Interfaces:**
- Consumes: 现有 ContinentTerrain、WorldDefinition、WorldFrame、ContinentExplorer 和 ContinentStreamProbe。
- Produces: 可复现种子、路线、相机机位、颜色采样点、区域查询计数和动画状态采样格式。

- [ ] **Step 1: 建立视觉回归夹具**

  在默认种子和 14376 下记录海岸、山脊、远景、洞口、洞内横截面、海面俯视和水下视角。每个机位同时记录世界坐标、相机姿态、浮动原点、可见远景瓦片和活动页数量。

- [ ] **Step 2: 建立自然系统和角色的基线字段**

  为后续测试预留世界时间、气候查询、资源查询、有效速度、地面状态、坡度和动画参数的采样记录。基线没有这些系统时记录零值和场景版本，避免将新系统的成本混入旧数据。

- [ ] **Step 3: 运行现有路线并保存对照数据**

  ~~~powershell
  unity test . --mode PlayMode --filter Reol2.Tests.LargeWorldTests --output Logs/LargeWorldBaseline.xml
  ~~~

  Expected: 现有大世界测试通过，生成新的基线 XML 和本机性能记录。使用 unity build . --target StandaloneWindows64 --execute-method Reol2.Editor.ContinentAuthoring.BuildLargeWorld --output Builds/LargeWorld/Reol2World.exe --allow-dirty-build --non-interactive 复查开发构建入口。

### Task 1: 建立统一非周期材质和低频色板

**Files:**
- Create: Assets/Reol2/World/WorldMaterial.cs
- Modify: Assets/Reol2/World/ContinentField.cs
- Modify: Assets/Reol2/World/WorldHorizon.cs
- Modify: Assets/Reol2/World/ContinentHorizon.cs
- Modify: Assets/Reol2/World/WorldCaveHorizon.cs
- Modify: Assets/Reol2/World/ContinentTerrain.cs
- Modify: Assets/Reol2/World/ContinentOcean.shader
- Modify: Assets/Reol2/Visuals/VoxelSurface.shader
- Create: Assets/Reol2/Tests/PlayMode/WorldMaterialTests.cs

**Interfaces:**
- Consumes: WorldNoise.Hash、WorldNoise.Value、WorldDefinition.SeedBits、地表高度、法线和远景采样间距。
- Produces: WorldMaterial.Land(double x, double y, double z, Vector3 normal, float sampleSpacing, ulong seed) -> Color32、WorldMaterial.Cave(double x, double y, double z, Vector3 normal, float sampleSpacing, ulong seed) -> Color32 和 WorldMaterial.Blend(double x, double y, double z, Vector3 normal, float ground, float sampleSpacing, ulong seed) -> Color32。ContinentField 保留现有静态包装入口，现有调用者和测试可以逐步迁移。

- [ ] **Step 1: 为材质采样写确定性测试**

  覆盖同一全球坐标重复采样、负坐标、远景采样间距、两种种子和浮动原点前后的结果。增加一个固定频率回归样本，确认颜色在相隔旧正弦周期的坐标上不会重复成整条色带。

- [ ] **Step 2: 实现宏观色板和分频采样**

  先使用不依赖纹理的整数哈希或现有 WorldNoise。宏观频带约为 2–32 km，中观频带约为 100 m–1 km，近景频带约为 3–24 m。sampleSpacing 越大，越早跳过高频分量。色板由高度、坡度、法线朝向、海岸距离、洞壁深度和地质层选择，混合使用平滑权重。

- [ ] **Step 3: 接入近场、远景和洞穴代理**

  WorldHorizon 和 ContinentHorizon 传入实际瓦片采样间距，WorldCaveHorizon 传入代理采样尺度，Marching Cubes 近场传入体素间距。所有路径读取同一世界种子和同一配色函数，避免洞口和远景各自出现一套颜色。

- [ ] **Step 4: 重写海面着色**

  删除固定频率双正弦菱形图案。保留基于视角的 Fresnel、雾和水下颜色，增加一个按整数世界格哈希插值的低频扰动，并用距离和视角淡出高频。海面 shader 使用世界坐标的整数化分量或相位，浮动原点更新时保持同一全球位置的颜色连续。

- [ ] **Step 5: 验证远景和近景的视觉连续性**

  ~~~powershell
  unity test . --mode PlayMode --filter Reol2.Tests.WorldMaterialTests --output Logs/WorldMaterialTests.xml
  ~~~

  Expected: 颜色采样确定性测试通过，近场与远景使用同一色板，切换 LOD 时没有大块颜色跳变。用 Unity Pipeline 截取七个基线机位，逐帧检查远处黄灰色斑、规则波纹、洞口横条和洞内断裂。

- [ ] **Step 6: 记录性能并设定继续扩展的门槛**

  记录构网、远景构建、海面 GPU 帧时、三角形、Draw Call 和材质采样带来的增量。第一轮实现要求 C# 材质采样无每顶点分配，shader 首轮保持有限采样次数，普通路线的总帧时和流送 P99 不超过 Task 0 基线的可测增量。

### Task 2: 世界时间、昼夜、太阳、月亮和基础大气

**Files:**
- Create: Assets/Reol2/World/WorldClock.cs
- Create: Assets/Reol2/World/WorldEnvironment.cs
- Create: Assets/Reol2/World/WorldSkyVisuals.cs
- Modify: Assets/Reol2/World/ContinentExplorer.cs
- Modify: Assets/Reol2/Editor/ContinentAuthoring.cs
- Modify: Assets/Scenes/LargeWorld.unity
- Modify: Assets/Scenes/ContinentDemo.unity
- Create: Assets/Reol2/Tests/PlayMode/WorldEnvironmentTests.cs

**Interfaces:**
- Consumes: WorldDefinition.Seed、WorldFrame、玩家全球位置、洞内和水下状态。
- Produces: WorldClock.CurrentTick、WorldClock.CurrentTime -> WorldTime、WorldEnvironment.Sample(WorldPosition point) -> WorldEnvironmentSample、WorldSkyVisuals.Apply(WorldEnvironmentSample sample)。WorldEnvironmentSample 至少包含太阳方向、月亮方向、天空色、雾色、雾距离和光照强度。

- [ ] **Step 1: 写世界时钟规则测试**

  验证整数逻辑 tick 的推进、跨日、跨季、暂停、快速前进和固定种子重放。渲染帧时间只更新表现，不改变世界逻辑顺序。

- [ ] **Step 2: 实现纯规则 WorldClock**

  使用整数 tick 保存时间，提供显式 Advance(long ticks) 和只读时间快照。时间比例作为场景配置，避免把真实机器帧率写进世界历史。

- [ ] **Step 3: 实现环境快照和大气表现**

  将当前 ContinentExplorer.UpdateView 中的天空色、雾色、雾起止距离和地下水下分支迁移到环境组件。大气首轮使用太阳方向、月亮方向、天空颜色梯度、线性雾和地下/水下覆盖层，云层先使用低成本的远景层或颜色渐变。

- [ ] **Step 4: 接入场景生成和浮动原点**

  在 ContinentAuthoring 创建环境组件、太阳和月亮的表现对象。太阳和月亮使用全球逻辑时间计算方向，视觉对象挂在场景根下，更新时只读取当前局部原点进行放置。

- [ ] **Step 5: 验证昼夜、重定位和水下状态**

  ~~~powershell
  unity test . --mode PlayMode --filter Reol2.Tests.WorldEnvironmentTests --output Logs/WorldEnvironmentTests.xml
  ~~~

  Expected: 时间推进会改变太阳、天空和雾，重定位前后同一全球位置的天空状态一致，洞内和水下拥有稳定的颜色与能见度，环境更新不随远处世界尺寸增长。

### Task 3: 气候、季节、地质层和非生物资源查询

**Files:**
- Create: Assets/Reol2/World/WorldClimate.cs
- Create: Assets/Reol2/World/WorldGeology.cs
- Create: Assets/Reol2/World/WorldResourceQuery.cs
- Create: Assets/Reol2/World/WorldDiscovery.cs
- Modify: Assets/Reol2/World/WorldSurface.cs
- Modify: Assets/Reol2/World/ContinentField.cs
- Create: Assets/Reol2/Tests/PlayMode/WorldNaturalResourceTests.cs
- Modify: docs/roadmap/world-engine.md

**Interfaces:**
- Consumes: WorldDefinition.SeedBits、WorldSurface.HeightAt、海拔、坡度、海岸距离、WorldAtlas 和当前世界时间。
- Produces: WorldClimate.Sample(WorldPosition point, WorldTime time) -> ClimateSample、WorldGeology.Sample(WorldPosition point) -> GeologySample、WorldResourceQuery.Find(WorldPosition point, ResourceQueryMode mode) -> ResourceResult 和 WorldDiscovery.Observe(WorldPosition point, GeologySample geology, ResourceResult resource, WorldTime time) -> DiscoveryRecord。

  ClimateSample 包含温度、湿度、风向、风速、季节权重和气候带。GeologySample 包含地质省、岩层、宿主岩、硬度和地下水标记。ResourceResult 包含资源类型、确定性密度、宿主层、矿脉方向、露头强度和查询置信度。资源类型首批覆盖石材、土、砂、黏土、盐、煤、铁、铜、硫、灵石类占位资源和地下水。

- [ ] **Step 1: 写确定性和边界测试**

  同一 seed、同一全球坐标和同一时间必须得到相同结果。负坐标、四个远角、世界边界、海床、深洞和浮动原点都要覆盖。相邻区域的气候和地质过渡使用平滑函数，矿脉查询使用区域哈希并保证跨边界连续。

- [ ] **Step 2: 实现区域级气候场**

  用低频确定性噪声和高度、纬度、海陆距离生成气候带。每个区域只缓存一个摘要和少量派生参数，近处查询时再求局部温度、湿度和风。季节通过时间快照改变气候偏移，不为每个体素写一份温度。

- [ ] **Step 3: 实现地质省、岩层和矿脉宿主**

  地表高度决定可见层序，地下深度和地质省决定岩层及资源宿主。矿脉使用区域级种子、走向、厚度和密度参数生成，查询范围受 ResourceQueryMode 限制。远处保留配方，玩家靠近或使用探矿能力时再生成精细结果。

- [ ] **Step 4: 接入地形颜色和资源露头**

  地质样本为 WorldMaterial 提供岩层和沙土色板。玩家选择地面或洞壁时，WorldDiscovery 生成稳定的资源线索记录。第一版资源线索可以是露头颜色、碎石、矿脉方向提示和探矿读数，后续再接库存和采集动作。

- [ ] **Step 5: 验证查询成本和连续性**

  ~~~powershell
  unity test . --mode PlayMode --filter Reol2.Tests.WorldNaturalResourceTests --output Logs/WorldNaturalResourceTests.xml
  ~~~

  Expected: 查询在远角和深洞成立，跨区域不会出现断层，未访问的远区不生成资源对象。分别把活动区域扩大为 1、4、16 倍，记录查询数量、缓存字节、主线程时间和 GC Alloc，确认成本跟访问区域相关。

### Task 4: 区域天气、降水、风和有限水文表现

**Files:**
- Create: Assets/Reol2/World/WorldWeather.cs
- Create: Assets/Reol2/World/WorldWeatherVisuals.cs
- Create: Assets/Reol2/World/WorldWaterSurface.cs
- Modify: Assets/Reol2/World/WorldInlandWater.cs
- Modify: Assets/Reol2/World/ContinentExplorer.cs
- Modify: Assets/Reol2/Editor/ContinentAuthoring.cs
- Create: Assets/Reol2/Tests/PlayMode/WorldWeatherTests.cs

**Interfaces:**
- Consumes: WorldClock、WorldClimate、WorldResourceQuery、WorldSurface.WaterAt、玩家全球位置和摄像机状态。
- Produces: WorldWeather.SampleRegion(Vector2Int region, WorldTime time) -> WeatherState、WorldWeatherVisuals.Apply(WeatherState, WorldPosition) 和 WorldWaterSurface.Sample(WorldPosition) -> WaterSample。

- [ ] **Step 1: 写区域天气测试**

  验证同一地区在同一逻辑时间重放一致，天气跨区域平滑过渡，远区没有粒子对象，暂停和快进不会重复结算天气事件。

- [ ] **Step 2: 实现区域天气状态**

  使用气候场和派生种子生成晴天、阴天、雨、雪、雷暴、沙尘和雾。区域天气保存逻辑开始时间、持续时间、强度和风向，视觉对象只在玩家附近的有限范围内存在。

- [ ] **Step 3: 接入局部视觉和声音占位**

  雨雪采用有限粒子数量和距离裁剪，风用材质、雾和粒子方向体现，雷暴首轮用光照闪变和声音占位。天气强度影响能见度、环境色和水下表面表现，避免通过大范围体素修改实现天气效果。

- [ ] **Step 4: 接入解析水面和水下层**

  复用当前解析河流、湖泊、海洋水位。水面只根据区域水位、潮汐和降雨汇总值更新。局部洞穴进水继续遵守已有受影响页和传播预算，水流扩散、侵蚀和积雪体素化进入后续水文阶段。

- [ ] **Step 5: 验证天气与大世界流送叠加**

  ~~~powershell
  unity test . --mode PlayMode --filter Reol2.Tests.WorldWeatherTests --output Logs/WorldWeatherTests.xml
  ~~~

  Expected: 行走、飞行、潜水、洞穴和浮动原点重定位期间天气表现连续。比较无天气、单区域天气和 16 区域天气的 P95/P99、粒子数量、驻留内存和 GC Alloc，天气系统不能让远距离高速移动产生与世界面积相关的工作。

### Task 5: 导入统一低多边形主角资产

**Files:**
- Create: Assets/Reol2/Characters/ThirdParty/Quaternius/
- Create: Assets/Reol2/Characters/Player/PlayerBody.prefab
- Create: Assets/Reol2/Characters/Player/Materials/
- Create: Assets/Reol2/Characters/Player/Animations/
- Modify: Assets/Reol2/Editor/ContinentAuthoring.cs
- Modify: Assets/Scenes/LargeWorld.unity
- Modify: Assets/Scenes/ContinentDemo.unity
- Modify: THIRD_PARTY_NOTICES.md

**Interfaces:**
- Consumes: Quaternius Universal Base Characters、Universal Animation Library 的合法下载包，当前 CharacterController 尺寸和 ContinentExplorer 根节点。
- Produces: 一个 Humanoid Avatar、主角视觉 Prefab、Idle、Walk、Run、Crouch Idle、Crouch Walk、Jump Start、Fall Loop、Land 动画引用，以及材质和模型的许可证记录。

- [ ] **Step 1: 复核素材许可证和包内容**

  在导入前记录下载页面、版本或下载日期、压缩包校验信息和许可证文本。将实际使用的 FBX、贴图、动画和许可证文件放入第三方目录，并写入 THIRD_PARTY_NOTICES.md。

- [ ] **Step 2: 设置 Humanoid 导入和 Avatar**

  在 Unity 导入设置中确认骨骼映射、Avatar 验证、单位比例、朝向和动画循环。将角色视觉根放在 PlayerBody.prefab 中，根节点不携带世界碰撞和流送职责。

- [ ] **Step 3: 替换占位模型并保持碰撞尺寸**

  修改 ContinentAuthoring 创建带 CharacterController 的玩家根和视觉子节点。模型脚底、胶囊底部和相机眼高使用同一米制，保留当前 stepOffset = 0.22、slopeLimit = 48 作为第一轮对照值。

- [ ] **Step 4: 验证资产引用和构建**

  ~~~powershell
  python -B -X utf8 tools/check_unity_files.py
  ~~~

  Expected: 所有资产带 .meta，场景引用完整，Avatar 和动画没有 Missing。使用 Windows 构建入口确认角色资源随构建打包，构建目录同步包含 THIRD_PARTY_NOTICES.md。

### Task 6: 接入 Animator、Blend Tree、脚部 IK 和地形状态

**Files:**
- Create: Assets/Reol2/Characters/Player/PlayerAnimatorDriver.cs
- Create: Assets/Reol2/Characters/Player/PlayerLocomotion.controller
- Create: Assets/Reol2/Characters/Player/PlayerAnimatorDriver.cs.meta
- Modify: Assets/Reol2/World/ContinentExplorer.cs
- Modify: Assets/Reol2/Editor/ContinentAuthoring.cs
- Modify: Assets/Reol2/Tests/PlayMode/WorldExpansionTests.cs
- Create: Assets/Reol2/Tests/PlayMode/PlayerAnimationTests.cs

**Interfaces:**
- Consumes: ContinentExplorer 的实际移动速度、垂直速度、CharacterController.isGrounded、水下状态、飞行状态、坡度和浮动原点事件。
- Produces: PlayerAnimatorDriver 的 SetMovementState(Vector3 velocity, bool grounded, bool swimming, bool flying, bool crouching, float slopeAngle)，Animator 参数 Speed、InputMagnitude、VerticalSpeed、IsGrounded、FallTime、IsCrouching、MoveMode、Jump、SlopeAngle。

- [ ] **Step 1: 写运动状态和动画参数测试**

  覆盖静止、走、跑、急停、上坡、下坡、台阶、跳跃、坠落、落地、游泳、飞行和浮动原点重定位。实际速度为零时 Speed 必须为零，空中和游泳时脚部 IK 权重必须归零。

- [ ] **Step 2: 暴露玩家运动快照**

  在 ContinentExplorer 增加只读运动快照或等价的无分配访问入口。快照使用 motor.velocity 或经碰撞修正后的实际位移计算速度，包含当前模式、地面法线、坡度角、垂直速度和地面状态。动画驱动不读取原始输入作为唯一速度来源。

- [ ] **Step 3: 创建 Humanoid 状态机**

  用一个 Ground Locomotion 1D Blend Tree 处理 Idle、Walk、Run，用一个 Crouch Locomotion Blend Tree 处理蹲下，用 Jump Start、Fall Loop、Land 处理空中状态。普通 Stop、坡度和低台阶通过速度平滑、CharacterController 和 Foot IK 表现，减少动画状态数量。

- [ ] **Step 4: 接入脚部 IK 和视觉根分离**

  设置 Animator.applyRootMotion = false，使用内置 Humanoid Foot IK 或 OnAnimatorIK 做脚底贴合。脚部射线只在地面移动时启用，设置最大脚部位移、膝盖方向和权重变化速度，避免洞口、台阶和浮动原点重定位造成拉伸。

- [ ] **Step 5: 验证角色与体素地形**

  ~~~powershell
  unity test . --mode PlayMode --filter Reol2.Tests.PlayerAnimationTests --output Logs/PlayerAnimationTests.xml
  ~~~

  Expected: 30° 和 45° 坡面可以连续行走，0.15 m、0.22 m 台阶可以平滑通过，0.40 m 台阶需要跳跃或阻挡。脚底与地面间隙保持在约 2 cm 内，角色在水下、飞行、洞穴和重定位后没有姿态跳变。

- [ ] **Step 6: 记录动画和 IK 成本**

  用 Unity Profiler 记录 Animator、IK、角色脚本、GC Alloc 和总帧时。第一轮以动画与 IK 合计约 0.4 ms、零 GC Alloc 作为参考门槛，超出时先减少射线、更新频率或约束数量，再考虑 Animation Rigging。

### Task 7: 综合验收、文档收敛和交付

**Files:**
- Create: docs/roadmap/world-visual-nature-character.md
- Modify: docs/README.md
- Modify: docs/development.md
- Modify: docs/large-world-demo.md
- Modify: THIRD_PARTY_NOTICES.md
- Modify: AGENTS.md only when the accepted implementation changes the active project rules

**Interfaces:**
- Consumes: Task 0–6 的测试、截图、性能日志、许可证记录和实际场景入口。
- Produces: 一份现行方案与实测记录、可复现 Windows 构建、Play Mode XML、视觉对照截图和后续阶段的明确入口。

- [ ] **Step 1: 运行静态、顾问和 Unity 验证**

  ~~~powershell
  python -B -X utf8 tools/check_unity_files.py
  python -B -X utf8 tools/advisor/test_advisor.py
  unity test . --mode PlayMode --filter Reol2.Tests.WorldMaterialTests,Reol2.Tests.WorldEnvironmentTests,Reol2.Tests.WorldNaturalResourceTests,Reol2.Tests.WorldWeatherTests,Reol2.Tests.PlayerAnimationTests --output Logs/WorldExpansion.xml
  ~~~

  Expected: 资产、顾问模拟网关和指定 Play Mode 测试通过。复杂算法变更在实现阶段继续用本机双顾问 review，采纳意见前核对源码、Unity 文档和实测结果。

- [ ] **Step 2: 运行综合路线**

  以默认种子和 14376 复现岸边步行、低空飞行、海底下潜、洞穴入口、洞内移动、远角传送、千公里高空和角色坡面路线。记录 P95/P99、最大帧时、流送 P99、活动页、远景瓦片、三角形、粒子、资源缓存、Animator、内存和 GC Alloc。

- [ ] **Step 3: 更新现行文档并清理旧口径**

  将实际采用的材质分频、环境层级、资源查询、天气范围、角色资产和测试结果写入 docs/roadmap/world-visual-nature-character.md。docs/README.md 指向该入口，旧的固定正弦材质描述、过时资源假设和已经替代的实施清单随同清理。

- [ ] **Step 4: 生成 Windows 构建并做人工验收**

  ~~~powershell
  unity build . --target StandaloneWindows64 --execute-method Reol2.Editor.ContinentAuthoring.BuildLargeWorld --output Builds/LargeWorld/Reol2World.exe --allow-dirty-build --non-interactive
  ~~~

  Expected: 构建可以连续旅行、潜水、挖填、经历昼夜和天气，角色具备完整基础移动动画，远近地表材质过渡和洞穴视角没有明显周期波纹或断裂。构建结果附带许可证记录和本轮测试日志。

## 后续阶段入口

完成本计划后，再依据实测需求接入天气影响、河流与湖泊动态水位、地下水传播、侵蚀、火山与地震、自然灾害、资源采集库存、动物植物、NPC 生态和动态世界事件。它们都应复用本计划的世界时间、区域摘要、确定性种子、资源查询、近远表现和性能测量入口。

## 当前方案边界

本轮交付是实施方案文件。当前不修改 C#、Shader、场景、素材、Unity 包或第三方资源。后续进入实现时仍需在下载素材、导入 Animator、变更材质和修改场景前先复核本计划与实际工作区状态。

## 自我审查

### Spec coverage

- 视觉波纹由 Task 0、Task 1 覆盖，包含固定正弦、远景欠采样、海面和浮动原点。
- 自然世界基础由 Task 2–4 覆盖，包含昼夜、大气、气候、季节、风、天气、地质、矿脉、水位、资源露头和探矿线索。
- 主角模型和动画由 Task 5–6 覆盖，包含免费素材、Humanoid、走跑停、蹲、跳、上坡、台阶、游泳、飞行、Foot IK 和 Unity 接入方式。
- 性能优先由 Global Constraints、Review Focus、每项任务的测量步骤和 Task 7 综合路线覆盖。
- 可调整方向由分阶段交付、后续入口和实际测量门槛表达，玩法内容没有写成不可修改的绝对约束。

### Step scan

每个任务按照基线、测试、实现、接入、验证和记录拆分。涉及素材许可证、场景序列化、构网、天气对象和角色状态的步骤均有明确文件与命令，后续实现者可以在一个小目标内完成并复核。

### Type consistency

材质接口统一由 WorldMaterial 提供。环境接口统一由 WorldClock、WorldEnvironment 和 WorldSkyVisuals 提供。自然查询统一由 WorldClimate、WorldGeology、WorldResourceQuery 和 WorldDiscovery 提供。角色接口统一由 ContinentExplorer 的运动快照和 PlayerAnimatorDriver.SetMovementState(...) 提供。

### Review focus coverage

材质混叠和水纹由 WorldMaterialTests 及视觉机位覆盖。浮动原点由材质和环境测试覆盖。远区自然成本由资源和天气扩容测试覆盖。动画状态由 PlayerAnimationTests 覆盖。资源导入与构建完整性由 check_unity_files.py、许可证记录和 Windows 构建覆盖。

### Proportion

计划把需要作决定的接口、阶段、测试和性能门槛写清楚，保留算法实现空间。具体着色器代码、动画状态机资源内容、天气参数和资源概率仍以实现阶段的实测为准。

## 参考资料

- Unity CharacterController、stepOffset 和 slopeLimit 文档，https://docs.unity3d.com/6000.3/Documentation/ScriptReference/CharacterController.html
- Unity AnimatorController 文档，https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Animations.AnimatorController.html
- Unity Animation Rigging 文档，https://docs.unity3d.com/6000.0/Documentation/Manual/com.unity.animation.rigging.html
- Quaternius Universal Base Characters，https://quaternius.com/packs/universalbasecharacters.html
- Quaternius Universal Animation Library，https://quaternius.com/packs/universalanimationlibrary.html
- Vintage Story 世界生成，https://wiki.vintagestory.at/Special:MyLanguage/World_Generation
- Vintage Story 探矿，https://wiki.vintagestory.at/Guide:Prospecting
- Geometry Clipmaps，https://developer.nvidia.com/gpugems/gpugems2/part-i-geometric-complexity/chapter-2-terrain-rendering-using-gpu-based-geometry
- Hillaire 大气散射课程资料，https://blog.selfshadow.com/publications/s2020-shading-course/hillaire/s2020_pbs_hillaire_slides.pdf
