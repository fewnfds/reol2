# 本机顾问

## 三步发起咨询

1. 在 Reol 2 项目根目录运行 `python -X utf8 tools/advisor/advisor.py models`，确认本机网关列出 `gemini-4-neon` 和 `gemini-4-neon-task`。新机器先按下方配置网关。
2. 一次说清项目阶段、当前实现、实测证据和要判断的问题。每个相关文件写一次 `--attach`。长题目可以写进 UTF-8 文件，再用 `--prompt-file`。
3. 打开命令返回的 `answers.md`，再看 `summary.json` 中每个模型的状态和耗时。`partial` 表示只收到部分顾问的完整结果。把顾问意见当成审查线索。当前执行者应对照源码、API 文档和实际测试复核，再决定采纳、搁置或拒绝哪些建议。

```powershell
python -X utf8 tools/advisor/advisor.py ask --prompt "Reol 2 是 Unity 6.3 的第三人称可挖填 Demo。两块地形共享密度采样，挖填同步发布画面和碰撞。请找玩家能触发的边界编辑缺陷，给出代码位置、证据、复现和最小验证。" --attach Assets/Reol2/Demo/VoxelDemoTerrain.cs --attach Assets/Reol2/Voxels/DensityVolume.cs
```

UTF-8 源码和文本会以原文和路径标记并入同一份提示词。范围可覆盖几个相关文件和测量结果，足以让顾问从零理解问题。图片、PDF、音频和视频使用相应的内容段。附件是待审资料。发送前确认文件属于本轮需要的上下文。

请顾问标明每项意见的代码依据、成立条件和验证办法。它只看到本轮材料，可能漏掉跨文件约束、给出片面的取舍或误判 Unity API。面向提交的审查优先处理在当前玩家入口可复现的问题。把其他意见列为待验证的假设。

## 模型与结果

- Web 默认并行调用 `gemini-4-neon` 和 `gemini-4-neon-task`。普通 Neon 显式使用 `reasoning_effort: high`。Task 用网关提供的档位，由网关等待云端完成并返回正文。两路使用同一份问题和附件。Web 的采样温度由网页端决定。[Web2API 协议](https://github.com/Mag1cFall/Gemini-Web2API/blob/4b251f7fc46f3a1af9cd5f80528705fff7b0ee01/docs/protocol.md#思考与签名)
- `ask --provider aistudio` 并行调用两份 `gemini-4-neon-search`，温度分别为 `1.0`、`1.2`，思考强度为 `high`。`models --provider aistudio` 可查看该渠道的目录。
- 单独复核一个模型用 `ask --model gemini-4-neon-task`。可重复 `--model` 指定多个 Web 模型。普通 Neon 也接受上游的档位后缀，例如 `--model 'gemini-4-neon@think=low'`。
- 默认输出在 `.local/advisor/runs/`，可用 `--out-dir` 指定一个新目录。`prompt.md` 和每路子目录的请求、响应供复核。`--print-answer` 会显示合并的正文。`--dry-run` 只生成本地请求。HTTP 超时可用 `--timeout` 设置，默认 900 秒。

## 本机配置

工具使用 Python 3.10 或更新版本。配置文件放在 Git 忽略的 `.local/advisor/`。在 `gemini-web.local.json` 中设置以下 JSON，把占位符替换为网关的 `PROXY_API_KEY`。如果网关允许无密钥调用，`api_key` 可省略。

```json
{
  "base_url": "http://127.0.0.1:8007/v1",
  "api_key": "<本机 Gemini Web 代理密钥>"
}
```

也可使用 `REOL_ADVISOR_WEB_BASE_URL` 和 `REOL_ADVISOR_WEB_API_KEY` 覆盖。AI Studio 使用 `.local/advisor/config.local.json`，默认地址为 `http://127.0.0.1:2048/v1`，环境变量为 `REOL_ADVISOR_BASE_URL` 和 `REOL_ADVISOR_API_KEY`。两份配置可分别设置 `timeout_seconds`。`--config <文件>` 单独使用时选择 AI Studio，为 Web 自定义配置时同时传入 `--provider gemini-web`。

## 媒体与原始请求

`generate image|speech|music|video` 的生成预设走 AI Studio，用 `generate --help` 查看参数。视频任务可通过 `resume <输出目录>` 接着查询。`send --request-file` 可发手写的非流式 JSON 请求，指定 `--provider gemini-web` 可调用 Web 网关的其他接口。Web 视频的 `resume` 也须指定该渠道。`extract --response-file` 从已保存的响应提取媒体。

`python -B -X utf8 tools/advisor/test_advisor.py` 在本机模拟网关上验证路由、附件、记录和媒体处理。
