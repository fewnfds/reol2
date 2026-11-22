# 本机顾问

在仓库根目录运行。每次咨询都是独立问答，默认经 Gemini Web2API 把同一份输入并行发给 `gemini-4-neon`（High）和 `gemini-4-neon-task`，各一次。两路均没有完整答复时，用同一份输入经 AI Studio 并行调用两路 `gemini-4-neon-search`，温度为 1.0 和 1.2。每轮都要交代背景，后续复审可把前一轮有价值的意见和新证据写进新提示词。

双模型并行只用于 `ask`。两位顾问可以读取多模态附件，交付的是文字意见。生图、朗读、配乐和视频使用 `generate`，每次选择一个支持该用途的模型或服务，只提交一次生成请求。[媒体快捷命令](gemini-web-api.md#媒体快捷命令)

## 快速上手

~~~powershell
python -B -X utf8 tools/advisor/advisor.py models
python -B -X utf8 tools/advisor/advisor.py ask --prompt "Reol 2 用 Unity 6.3，近处页提供可挖地形与碰撞。请根据源码比较两种减少冷区流送卡顿的办法，指出最可能失效的输入、可复现实验和应记录的指标。" --attach Assets/Reol2/World/ContinentTerrain.cs
~~~

1. **把问题写成独立任务。** 交代玩家遇到的现象、项目版本、现有做法、已经量到的结果、当前约束和要做的取舍。要求顾问标注代码位置、一手资料和最小复现。长问题用 UTF-8 的 `--prompt-file .local/advisor/question.md`，短问题用 `--prompt`，两者选一。
2. **给足证据。** 重复 `--attach` 附上源码、配置样例、日志或截图。UTF-8 源码和文本会带文件名拼进提示词。图片、PDF、音频、视频以相应内容段传送，也可附图片、视频或 YouTube 的 URL。需要在多轮重用同一份资料时，先上传，再用可重复的 `--file-id` 引用。只提供与问题有关且可发送到 Gemini Web 的内容，凭据留在本机。[文件上传与多模态](gemini-web-api.md#文件与附件)
3. **读结果并自己核实。** 打开输出目录的 `answers.md`，再从 `summary.json` 的 `results` 看每个模型的状态和耗时。顾问只看到了这一轮给的材料，事实、Unity API、网页引用和性能判断都用当前源码与实测复核。对两份结果有争议的点，可以附上新证据请它们各自反驳或复审。

复杂代码提交可参考这个提示结构。

~~~text
背景和目标：玩家正在做什么，如何判断结果正确。
当前事实：版本、相关入口、最小复现、日志和性能数字。
已知取舍：哪些接口或行为要保护，哪些方案可以改。
请回答：优先列真实缺陷或备选方案，给出适用条件、反例、验证步骤和一手出处。
~~~

## 配置与结果

- 默认 OpenAI 兼容 Base URL 是 `http://127.0.0.1:8007/v1`。如网关配置了密钥，在 Git 忽略的 `.local/advisor/gemini-web.local.json` 填 `base_url`、`api_key`，也可选填 `timeout_seconds`。使用 `--config <文件>` 可以显式选用另一份 Gemini Web 配置。环境变量 `REOL_ADVISOR_WEB_BASE_URL` 和 `REOL_ADVISOR_WEB_API_KEY` 优先于文件。

- AI Studio 默认用 `http://127.0.0.1:2048/v1`，读取 `.local/advisor/config.local.json` 或环境变量 `REOL_ADVISOR_BASE_URL` 和 `REOL_ADVISOR_API_KEY`。`ask --provider aistudio` 直接走备用双路，`ask --provider gemini-web` 固定走 Web。显式指定配置、模型或 Web 文件 ID 的咨询保留指定路由。媒体生成及视频续查使用 Gemini Web。
- 默认超时为每次 HTTP 请求 900 秒，可用 `--timeout` 调整。新任务默认存到 `.local/advisor/runs/`。用 `--out-dir` 指定一个尚不存在的目录，用 `--dry-run` 预览两份实际将发送的 JSON，用 `--print-answer` 同时打印合并正文。运行目录含原始输入、附件数据、回复和逐路状态。
- `complete` 表示请求的答复都完整，`partial` 表示至少一份可用，`failed` 表示没有完整答复。部分成功仍返回退出码 0，依赖双审的流程要检查 `summary.json`。遇到截断、空答或未执行的工具调用，先查看单路 `run.json` 和原始 `response.json`。用 `ask --model <模型 ID>` 可单独重跑指定模型。
- 回退时 `summary.json` 记录 `fallback_from`，四路请求和答复仍可逐个查阅。若所需的顾问答复依旧不可用，查看 `models`、各路 HTTP 状态和错误摘要，再向用户说明现有结果并询问能否恢复相应账号。

更多用法见 [Gemini-Web2API 接口指南](gemini-web-api.md)。本地模拟网关测试用 `python -B -X utf8 tools/advisor/test_advisor.py`。
