# AI Provider、模型与 effort

在「设置 → AI」中新增任意数量的 Provider。每个实例有独立名称、稳定 ID、协议、端点和凭据；可以同时连接多个使用同一协议的官方服务、中转服务或本地模型服务。协议可选 OpenAI Responses、OpenAI Chat Completions、Anthropic Messages 或 Codex app-server。

每个 Provider 设置默认模型和 token 预算，并可配置默认 effort。默认模型、聊天和任务模型均使用下拉选择器，选项结合服务端发现、当前默认模型与手动配置的模型，并按 Provider 分别展示。兼容服务未实现模型发现时，先在「模型与推理能力」中添加自定义模型 ID，再从选择器中选择；新增 Provider 或修改端点后，保存配置即可刷新服务发现结果。Codex 默认模型可选择「使用 app-server 默认模型」，任务与推理设置可选择继承默认值。在自定义模型列表填写该端点实际支持的 effort，供选择器与请求校验使用。相同模型 ID 在不同 Provider 下分别处理。

Chat 发送时使用当前选中的 Provider、模型和 effort，自动标题也沿用该选择。聊天模型列表只包含已配置的 Provider；始终可选择「Provider 默认模型」恢复默认值，Codex Provider 未配置模型时使用 app-server 默认模型，不依赖模型发现结果是否包含空 ID。「任务」页的 AI 选择只影响随后手动启动的那一次 AI 任务，不修改全局设置。当前实例的任务已有等待或正在执行的请求时，显式指定 AI 选择会返回冲突；若其他实例持有任务租约，已接受的选择会继续排队，每秒重试直至可执行。没有 AI 调用的任务不使用这些选项。

自动元数据和文件名推理使用设置页的「推理任务」选择，对应 `Inference:ProviderId`、`Inference:Model` 和 `Inference:ReasoningEffort`；留空时继承默认 Provider 及其模型配置。任务的选择在异步执行期间独立保存，执行结束后恢复，不会串到其他并发请求。

元数据和文件名推理在 OpenAI Responses 下使用严格结构化输出：每轮请求通过 `text.format` 发送现有 JSON Schema，约束最终回答的字段、类型和可空值。工具参数仍使用各工具自己的定义。模型拒绝、输出被截断或流缺少完成事件时，该次推理失败，不会把拒绝文本或未完成 JSON 当作识别结果。普通聊天不附加该格式。Codex app-server 继续使用其输出 Schema；Chat Completions 和 Anthropic 继续依靠提示词与现有 JSON 解析。自定义 Responses 端点需要支持 `text.format` 的 `json_schema` 严格模式。[OpenAI 结构化输出说明](https://developers.openai.com/api/docs/guides/structured-outputs)

## Responses 历史消息与 HTTP 错误排查

已有聊天继续发送消息时，数据库重建的 assistant 历史使用 EasyInputMessage 的字符串 `content`，保留 `role` 和 `phase`；历史工具调用及结果仍作为带标签的文字记录。这种形式减少兼容端点对 assistant 分段内容的解析歧义。一次回复中的工具后续轮次继续原样重放完整的 Responses 输出条目，包括加密推理，不受历史消息格式调整影响。[OpenAI 手动管理对话状态](https://developers.openai.com/api/docs/guides/conversation-state#manually-manage-conversation-state)

OpenAI 模型发现、Responses 和 Chat Completions 请求失败时，异常保留 HTTP 状态，并从最多 16 KiB 的标准 JSON 错误体中提取 `error.message`、`error.type`、`error.code`、`error.param`，同时附带可用的 `x-request-id`。字段有长度限制，已配置的 API key 会脱敏；不记录请求正文或未知格式的原始错误体。错误体读取最多等待 5 秒；非 JSON、过大、超时或读取失败时，仍返回 HTTP 状态及可用的请求 ID，调用方取消仍正常传播。

如果已有对话在第二轮出现 HTTP 400，先查看异常中的 `param` 和 `message` 确认端点拒绝的字段，并保留请求 ID 供服务提供方定位。仅有 `400 (Bad Request)` 无法确定具体原因；历史消息兼容性调整不意味着所有 400 都由同一参数引起。

## 部署配置

配置文件的 `AI:Providers` 是以稳定 ID 为键的字典；设置 API 使用包含 `id` 的数组。配置文件应声明 `Version: "3.0.0"`；环境覆盖使用 `SDW_CONFIG_VERSION=3.0.0`，并将冒号替换为双下划线，例如 `AI__Providers__local__Protocol=OpenAIChatCompletions`。这也确保 `Inference__Model` 按当前推理覆盖含义保留，而不是按旧结构迁移到 Provider 默认模型。

```yaml
Version: "3.0.0"
AI:
  ProvidersConfigured: true
  DefaultProviderId: openai
  Providers:
    openai:
      Name: OpenAI
      Protocol: OpenAIResponses
      BaseUrl: https://api.openai.com/v1
      ApiKey: ""
      Model: gpt-5.6-luna
      MaxTokens: 16384
    local:
      Name: 本地模型
      Protocol: OpenAIChatCompletions
      BaseUrl: http://127.0.0.1:11434/v1
      ApiKey: ""
      Model: my-model
      MaxTokens: 16384
      Models:
        - Id: my-model
          Name: 自定义推理模型
          ReasoningEfforts: [low, medium, high]
Inference:
  ProviderId: openai
  Model: gpt-5.6-luna
  RateLimitDelayMs: 1000
```

示例中的自定义模型及 effort 仅说明配置结构，应替换为服务实际支持的值。OpenAI Responses 使用 `reasoning.effort`，Chat Completions 使用 `reasoning_effort`，Anthropic 使用 `output_config.effort`，Codex 使用 turn 的 `effort`。未选择 effort 时保留服务/模型默认行为；不支持 effort 的模型不会被强制附加参数。

官方 OpenAI 的 GPT-5.6 模型通过 Chat Completions 调用工具时要求 effort 为 `none`；需要推理与工具并用时选择 Responses。GPT-6 Astra 的工具调用也要求 Responses。界面切换协议时为 GPT-5.6 选择兼容默认，服务端在保存聊天消息或将手动任务入队前校验这些限制；自定义兼容服务按其实际能力配置。[GPT-5.6 协议迁移](https://developers.openai.com/api/docs/guides/upgrading-to-gpt-5p6-sol)、[当前模型使用指南](https://developers.openai.com/api/docs/guides/latest-model)

Codex 实例使用 `Endpoint`、`BearerToken`、`PermissionProfile` 和 `TimeoutSeconds`；隔离部署与连接要求见 [容器部署](container-deployment.md#使用-codex-app-server)。

旧 `AI:Engine`、`AI:Provider`、`AI:OpenAI`、`AI:Anthropic` 和 `AI:CodexAppServer` 配置继续生效。首次从新版页面保存 Provider 列表后，已有凭据转为按实例 ID 保存；后续 AI 更新必须提交 Provider 列表，旧版单 Provider 请求不能将其降回旧配置。密钥继续使用 Data Protection 加密，GET 只返回是否配置及来源；改变凭据对应的服务源时须重新设置或清除该凭据。删除 Provider 会清除该实例的运行时凭据，显式空列表不会回退到旧配置。

## 默认模型依据（2026-09-09）

| 协议 | 新默认 | 选择依据 |
| --- | --- | --- |
| OpenAI | `gpt-5.6-luna` | 官方模型目录将 Luna 用于高频、成本敏感的任务，适合本项目日常元数据提取；需要更多能力时可另选 Terra、Sol 或 Astra。[OpenAI 模型目录](https://developers.openai.com/api/docs/models) |
| Anthropic | `claude-sonnet-5` | 当前目录中的活跃 Sonnet 型号，兼顾响应速度与能力。[Anthropic 模型目录](https://platform.claude.com/docs/en/models/overview) |

原示例中的 `claude-sonnet-4-20250514` 已于 2026-06-15 退役；退役表直接推荐 Sonnet 4.6，本次结合当前模型目录进一步更新新默认为 Sonnet 5。[Anthropic 模型退役说明](https://platform.claude.com/docs/en/about-claude/model-deprecations)

未将 `gpt-4o-mini` 标记为已退役：官方退役页未给出该通用型号的下线依据，本次替换是日常任务的默认推荐更新。[OpenAI 退役说明](https://developers.openai.com/api/docs/deprecations)

用户已经显式填写的模型 ID、endpoint 和预算不会自动改写。新增 Provider 的默认输出预算为 16384，为推理模型预留推理及最终输出空间；effort 越高通常越耗时、越多 token，可按任务选择。
