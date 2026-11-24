# 两公里大陆试玩与基准

两公里大陆用于同机对照近场地形流送、挖填和海岸碰撞。范围为 2,048 × 2,048 米，海平面为零米，基岩标高为 −96 米。近场以 0.5 米密度采样构网，编辑沿用一米格。挖过的地形可在同一会话卸载并回访。[地形规则](roadmap/world-engine.md)

## 试玩

Unity 菜单选 `Reol 2 → Open Continent Demo`，运行 `Assets/Scenes/ContinentDemo.unity`。可按需生成 `Builds/ContinentDemo/Reol2Continent.exe`。在岸上出发，可向内陆走坡，也可转身去海边。环顾、掌舵、飞行、挖填和倍速共用大世界控制，`Home` 回到岸边出生点。新挖出的海床下方空间仍保持干燥。[共用键位](large-world-demo.md#试玩)

## 本机验证

覆盖岸边步行、沿岸飞行、垂直潜水、冷区急转和接缝挖填后回访。比较相同视距、路线和构建设置下的流送与整帧 P95/P99、最老待处理、请求取消和已发布页数。帧时由离屏 Player 的帧循环测量。[基准入口](#复现) · [阶段原始基准](H:/reol2-workbench/goals-old/world-prototype/goal-02-continent-streaming.md)

## 复现

~~~powershell
unity test . --mode PlayMode --filter Reol2.Tests.ContinentTests --output .local/continent-playmode.xml --non-interactive
unity build . --target StandaloneWindows64 --execute-method Reol2.Editor.ContinentAuthoring.BuildWindows --output Builds/ContinentDemo/Reol2Continent.exe --allow-dirty-build --non-interactive
$continent = Join-Path (Get-Location) 'Builds/ContinentDemo/Reol2Continent.exe'
$benchmarkLog = Join-Path (Get-Location) '.local/continent-benchmark.log'
$player = Start-Process -FilePath $continent -WindowStyle Hidden -WorkingDirectory (Split-Path $continent) -Wait -PassThru -ArgumentList @('-continent-benchmark', '-screen-width', '1600', '-screen-height', '900', '-screen-fullscreen', '0', '-logFile', ('"' + $benchmarkLog + '"'))
$player.ExitCode
~~~

`-continent-image <path>` 可附加空中俯瞰图。基准完成后自动退出。[本地试玩构建](development.md#windows-本地试玩构建)
