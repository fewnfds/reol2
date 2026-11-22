# Gemini-Web2API 接口指南

以下接口按上游路由与适配器核对。账号可用模型、思考档位和媒体能力以本机 `GET /v1/models` 和 `GET /v1/models/{model}` 为准。SDK 的 OpenAI Base URL 用 `http://127.0.0.1:8007/v1`，直接调用完整路径时只拼一次 `/v1`。[上游说明](https://github.com/Mag1cFall/Gemini-Web2API/blob/ff0d26cf9e153f4eae4800c2908abaf1b65f17a8/README.md#openai-兼容) · [上游路由](https://github.com/Mag1cFall/Gemini-Web2API/blob/ff0d26cf9e153f4eae4800c2908abaf1b65f17a8/internal/app/router.go)

## 选择入口

| 要做的事 | 入口 |
| --- | --- |
| 单次咨询、并行评审、比较设计与代码 | `advisor.py ask`。两路并发、同一份输入，自动归档状态和正文。 |
| 模型目录、非流式 JSON 请求或本机二进制下载 | `advisor.py models` 或 `advisor.py send --endpoint /v1/…`。 |
| 流式读取、上传 multipart、取消或删除服务器对象 | 直接用 SDK 或 PowerShell 下的 `curl.exe`，按下表使用对应方法。 |
| 生图、朗读、配乐、视频 | `advisor.py generate image\|speech\|music\|video`，每次提交一次生成请求。高级参数可直调 API。 |

`send` 支持 GET 和 POST。POST 使用 `--request-file` 指向 UTF-8 JSON，流式 SSE 用 SDK 或 `curl.exe -N`。若使用 Responses 后台模式，保存返回的 ID 并自行查询。`send --wait` 处理视频任务，后台 Responses 的状态与正文从原始 `response.json` 查看。媒体位置见每次调用的 `summary.json` 和 `media.json`。

~~~powershell
New-Item -ItemType Directory -Force '.local/advisor' | Out-Null
python -B -X utf8 tools/advisor/advisor.py models --out-file .local/advisor/models.json
python -B -X utf8 tools/advisor/advisor.py send --method GET --endpoint '/v1/models/gemini-4-neon'
~~~

## 文件与附件

源码和小日志优先用 `ask --attach` 拼入原文。大文件或同一 PDF、图像、音视频需要反复引用时，上传一次后用服务返回的 ID；模型对文件类型和大小的限制仍适用。内容一旦改动，上传新版并换 ID。OpenAI Files 默认存于网关的 `data/files/`，可设过期时间，按需删除，服务也有保留策略。[附件处理源码](https://github.com/Mag1cFall/Gemini-Web2API/blob/ff0d26cf9e153f4eae4800c2908abaf1b65f17a8/internal/adapter/files.go)

PowerShell 示例读取同一份 Gemini Web 配置与环境变量。将示例路径换成这一轮需要的资料。

~~~powershell
$configPath = '.local/advisor/gemini-web.local.json'
$config = if (Test-Path -LiteralPath $configPath) { Get-Content -Raw -LiteralPath $configPath | ConvertFrom-Json }
$webBase = $env:REOL_ADVISOR_WEB_BASE_URL
if (-not $webBase) { $webBase = $config.base_url }
if (-not $webBase) { $webBase = 'http://127.0.0.1:8007/v1' }
$webBase = $webBase.TrimEnd('/')
$webKey = $env:REOL_ADVISOR_WEB_API_KEY
if ($null -eq $webKey) { $webKey = $config.api_key }
$authArgs = @()
if ($webKey) { $authArgs = @('-H', "Authorization: Bearer $webKey") }

$uploadJson = curl.exe --fail-with-body -sS @authArgs -F 'purpose=user_data' -F 'file=@.local/advisor/profile.pdf' "$webBase/files"
if ($LASTEXITCODE -ne 0) { throw '检查 Gemini Web 文件上传的错误响应' }
$fileId = ($uploadJson | ConvertFrom-Json).id
python -B -X utf8 tools/advisor/advisor.py ask --prompt "请读取报告，对照源码说明最重的阶段和可复现实验。" --attach Assets/Reol2/World/ContinentTerrain.cs --file-id $fileId
~~~

直接调用时，在 Chat Completions 的用户内容数组中放 `{"type":"file","file":{"file_id":"<id>"}}`，在 Responses 的消息内容数组中放 `{"type":"input_file","file_id":"<id>"}`。按需用文件信息与内容接口确认上传版本。清理时只删除自己明确持有的文件 ID。

## 对话、思考与工具

- **Chat Completions** 适合自带完整上下文的一问一答。`messages` 支持文本和多模态内容，`stream:true` 接收增量，`reasoning_effort:"high"` 适用于目录声明支持 High 的模型。Gemini Web 不应用 OpenAI 格式的 `temperature`，因此无法借它制造两路差异。需要显式请求搜索时，可在原始 Chat 请求里加 `"web_search_options":{}`，根据返回的引用和原始来源判断是否检索成功。
- **Responses** 适合需要给后台任务取回结果或明确续接的调用。请求 `background:true` 后保存返回 ID，轮询状态，再读取完成的输出。同步请求可用 `store:false`，后台任务会写入网关存储。上游默认保留 Responses 30 天，可用 `previous_response_id` 从已保存回复重建上下文。长输入可用 `/responses/input_tokens` 估算，再用 `/responses/compact` 提取压缩上下文，工程条件、精确数字和源码证据仍应按需显式提供。
- **自定义工具** 由客户端负责执行并把结果提交给接口。顾问的 `ask` 不执行返回的函数调用。需要更多研究工具时可看网关的 Streamable HTTP MCP `http://127.0.0.1:8007/mcp`，上游列有搜索、图像、音乐、视频与 Deep Research。日常代码顾问继续用独立问答。[Chat 适配器](https://github.com/Mag1cFall/Gemini-Web2API/blob/ff0d26cf9e153f4eae4800c2908abaf1b65f17a8/internal/adapter/openai_chat.go) · [Responses 适配器](https://github.com/Mag1cFall/Gemini-Web2API/blob/ff0d26cf9e153f4eae4800c2908abaf1b65f17a8/internal/adapter/responses_handler.go)

后台 Responses 示例。首次 `send` 返回 `remote_task_pending` 时会以非零退出码提示需继续查询，检查原始响应里的 `id` 和 `status` 后再执行第二条 `send`，轮询已有 ID 可避免重复创建。

~~~powershell
'{"model":"gemini-4-neon","input":"列出验证地形流送长尾的三个实验","reasoning":{"effort":"high"},"background":true}' |
    Set-Content -Encoding utf8 '.local/advisor/background-request.json'
python -B -X utf8 tools/advisor/advisor.py send --endpoint /v1/responses --request-file .local/advisor/background-request.json
python -B -X utf8 tools/advisor/advisor.py send --method GET --endpoint '/v1/responses/<返回的 ID>'
~~~

## OpenAI 兼容路由

所有表中路径都从网关根地址开始，`{…}` 换成返回的实际 ID。列表的 `data` 可能为空。

| 方法与路径 | 行为和用法 |
| --- | --- |
| `GET /v1/models` · `GET /v1/models/{model}` | 列目录、查单个模型。重点看 `thinking_levels`、`available_account_count` 和别名字段。 |
| `POST /v1/chat/completions` | 消息对话，支持流式、文件、图像、思考参数和工具调用。 |
| `POST /v1/completions` | 兼容旧式的 `prompt` 文本补全。 |
| `POST /v1/responses` | 统一输入、多模态、工具、流式、保存及后台请求。 |
| `POST /v1/responses/input_tokens` · `POST /v1/responses/compact` | 统计输入 token，或把长对话压缩成可续接的内容。 |
| `GET /v1/responses/{response}` · `GET /v1/responses/{response}/input_items` | 读取已保存回复或该回复的输入项。 |
| `POST /v1/responses/{response}/cancel` · `DELETE /v1/responses/{response}` | 取消后台运行中的回复，或从网关存储删除回复。 |
| `POST /v1/files` · `GET /v1/files` | multipart 上传（`file` 和 `purpose`），或列出文件。 |
| `GET /v1/files/{file}` · `GET /v1/files/{file}/content` · `DELETE /v1/files/{file}` | 读元数据、下载原始字节或删除。内容可能是二进制。 |
| `POST /v1/images/generations` · `POST /v1/images/edits` | JSON 生图、multipart 图像编辑。生图可返回 `b64_json` 或 URL。编辑传 `image` 和 `prompt`，蒙版不受支持。 |
| `POST /v1/videos` · `GET /v1/videos` | 创建视频任务、列出任务。JSON 可通过 `input_reference.image_url` 或 `input_reference.file_id` 提供首帧。 |
| `GET /v1/videos/{video}` · `GET /v1/videos/{video}/content` · `DELETE /v1/videos/{video}` | 查询、下载已完成视频或删除任务。内容按返回的 `expires_at` 保存。 |
| `POST /v1/videos/extensions` · `POST /v1/videos/{video}/remix` | 在本服务已经完成的原视频会话上延长或改编。延长产出拼接后的完整成片。 |
| `POST /v1/audio/transcriptions` · `POST /v1/audio/translations` | multipart 上传 `file` 并指定 `model`，由所选对话模型转写或翻译成英文。 |
| `POST /v1/audio/speech` | JSON 的 `model`、`input` 与可选的 `response_format`，使用 Gemini 网页朗读。自定义声音和语速不生效。 |
| `GET /v1/realtime` · `POST /v1/realtime/sessions` · `POST /v1/realtime/client_secrets` | 占位接口，返回带原因的 HTTP 404。 |
| `POST /v1/moderations` · `POST /v1/embeddings` | 占位接口，返回带原因的 HTTP 404。 |

## 媒体快捷命令

Neon High 和 Neon Task 用于文本咨询，可分析附件。生成素材时按类型选用一个媒体能力，默认分别使用 `gemini-3.1-flash-image`、通过 `gemini-4-hydrogen` 访问网页朗读、`gemini-music` 和 `gemini-video`。视频提交后可多次查询状态和下载同一个任务。

~~~powershell
python -B -X utf8 tools/advisor/advisor.py generate image --prompt "低多边形海边绝壁，供配色参考" --aspect-ratio 16:9
python -B -X utf8 tools/advisor/advisor.py generate speech --prompt "山谷午后有阵雨。"
python -B -X utf8 tools/advisor/advisor.py generate music --prompt "二十秒安静的山间环境配乐"
python -B -X utf8 tools/advisor/advisor.py generate video --prompt "薄雾缓慢越过山脊" --size 1280x720
~~~

图片用 `--aspect-ratio` 设置宽高比，带图片附件时用同一个 Gemini Web 网关的 Gemini 兼容接口。配乐也使用该接口。朗读用网页默认声音。视频可以附一张首帧图，尺寸用于选取宽高比，时长以服务实际产出为准。若视频等待超时且已有任务 ID，用 `advisor.py resume <输出目录>` 继续查询和下载，避免重复创建。已保存的媒体 JSON 可用 `advisor.py extract --response-file <文件> --out-dir <新目录>` 离线提取。模型及媒体验证以实际账号目录和调用结果为准。[上游媒体参数与模型说明](https://github.com/Mag1cFall/Gemini-Web2API/blob/ff0d26cf9e153f4eae4800c2908abaf1b65f17a8/README.md)
