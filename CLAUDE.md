# UniAI - AI Assistant Guide

> 本文档面向参与 UniAI 开发的 AI 助手。它不是用户手册，面向用户的说明见 `README.md`。修改框架前请先读本文件，避免把模型差异、Editor 行为或生成式路由重新写回错误层级。

**包名**: `com.uniai.core`  
**版本**: `0.0.1`  
**Unity**: `2022.3+`  
**许可证**: `Apache 2.0`

---

## 1. 框架定位

UniAI 是 Unity 与 AI Provider 之间的运行时框架，同时提供 Editor 助手能力。核心目标是让 Unity 项目可以通过统一 C# API 使用 Chat、Agent、Tool、MCP、多模态输入、上下文窗口和生成式资产。

当前架构重点：

- **统一请求模型**: 上层只接触 `AIRequest`、`AIResponse`、`AIStreamChunk`、`AIMessage`、`AIContent`。
- **多协议 Provider**: 内置 Claude Messages API 与 OpenAI Chat Completions 兼容协议。
- **模型元数据驱动**: `ModelEntry` 描述能力、端点、上下文窗口、`AdapterId`、行为标志和行为参数。
- **Adapter/Dialect 扩展**: Provider 主流程保持通用，模型或厂商差异放进 adapter/dialect。
- **Agent Tool Loop**: `AIAgentRunner` 是门面，实际 loop、工具执行和 tool-call history 构造已拆分。
- **生成式路由**: 生成图片等资产由 `GenerativeProviderRouter` 按模型元数据、渠道和 adapter 决定 provider。
- **Runtime/Editor 分层**: Runtime 不依赖 `UnityEditor`；Editor 只负责 UI、配置管理和 Unity 上下文采集。

不要把 UniAI 当成单一聊天窗口项目。Runtime API 和 Editor Chat 都是一等使用场景。

## 2. 当前目录职责

```text
Runtime/
├── Core/
│   ├── AIClient / AIConfig / ChannelEntry / UniAISettings
│   ├── ChannelManager / ChannelRouteSelector / ProviderCache
│   ├── ModelRegistry / ModelEntry / ModelCapability
│   ├── Presets/                         # 内置渠道与模型预设
│   ├── Adapters/                        # AdapterAttribute / Catalog / Registry / Discovery
│   ├── ChatOrchestrator                 # 对话生命周期编排
│   ├── ConversationRuntimeFactory       # Client、Runner、ContextPipeline 生命周期
│   ├── ConversationContextPreparer      # ChatSession -> AIMessage + 上下文注入
│   ├── McpConversationInitializer       # Agent MCP 初始化与 Resource 注入
│   ├── AgentEventSessionApplier         # AgentEvent -> ChatSession
│   └── ChatSessionPolicies              # 保存和标题策略
├── Models/                              # 统一请求、响应、消息、工具、流式块
├── Providers/
│   ├── IAIProvider / IAIProviderFactory
│   ├── JsonSseProviderBase
│   ├── Claude/
│   └── OpenAI/
│       └── Chat/                        # OpenAI Chat dialect 和 request converter
├── Agent/
│   ├── AIAgentRunner                    # Agent 门面
│   ├── AgentLoop                        # stream/non-stream tool loop
│   ├── AgentToolExecutor                # 本地 Tool + MCP Tool 执行
│   ├── AgentMessageFactory              # assistant tool-call history
│   └── UniAIToolRegistry / ToolSchemaGenerator / ToolResponse
├── Chat/                                # ChatSession、历史管理和文件存储
├── Context/                             # ContextPipeline、TokenEstimator、摘要、IContextProvider
├── MCP/                                 # MCP Client、Transport、Resource Provider
├── Generative/
│   ├── GenerativeProviderRouter
│   ├── IGenerativeProviderFactory
│   ├── IGenerativeAssetProvider
│   └── Providers/OpenAI/Images/         # OpenAI Images provider + image dialect
├── Tools/                               # Runtime 可用 [UniAITool]
└── Http/                                # AIHttpClient、SSEDownloadHandler、SSEParser

Editor/
├── Setting/                             # Manager 窗口、配置 Tab、AIConfigManager
├── Chat/                                # AIChatWindow、StreamingController、ContextCollector
├── Agent/                               # Agent 资产扫描、Inspector、EditorAgentGuard
├── MCP/                                 # MCP 配置 UI
└── Tools/                               # Editor 专用 [UniAITool]
```

## 3. 关键数据流

### 3.1 普通 Chat

```text
用户代码
  -> AIClient.SendAsync / StreamAsync
  -> ChannelManager
  -> ChannelRouteSelector.BuildCandidates(modelId)
  -> ProviderCache.GetOrCreate(channel, modelId)
  -> AIProviderFactoryRegistry.CreateProvider()
  -> IAIProvider.SendAsync / StreamAsync
  -> AIHttpClient + provider-specific JSON/SSE 解析
```

`ChannelManager` 只负责候选渠道、缓存和故障转移。不要在这里写协议 switch 或模型特判。具体 provider 由 `IAIProviderFactory` 通过 adapter discovery 解析。

### 3.2 Editor 对话窗口

```text
AIChatWindow
  -> ChatWindowController
  -> StreamingController
  -> ChatOrchestrator.StreamResponseAsync(ChatStreamRequest)
     -> McpConversationInitializer.WaitReadyAsync()
     -> ConversationContextPreparer.PrepareAsync()
     -> IConversationRunner.RunStreamAsync()
     -> AgentEventSessionApplier.Apply()
     -> IChatSessionPersistence.Save()
     -> IChatTitlePolicy.GenerateTitleAsync()
```

`ChatOrchestrator` 应保持薄编排层。历史保存、标题生成、上下文来源、Tool 执行守卫都通过 `ChatOrchestratorDependencies` 注入。

### 3.3 Agent Tool Loop

```text
AIAgentRunner
  -> AgentLoop
     -> AIClient.SendAsync / StreamAsync
     -> 收集 text、tool_calls、reasoning_content
     -> AgentMessageFactory.BuildAssistantMessage()
     -> AgentToolExecutor.ExecuteAsync()
        -> 本地 [UniAITool] 优先
        -> MCP Tool 次之
     -> AIMessage.ToolResult()
     -> 下一轮请求
```

注意：

- stream 和 non-stream 路径必须保持语义一致。
- assistant tool-call history 必须保留 `ReasoningContent`，否则 DeepSeek thinking 这类模型在同一轮多次 tool call 时会失败。
- 本地 Tool 与 MCP Tool 同名时，本地 Tool 优先。

### 3.4 生成式资产

```text
ManageGenerate 或 Runtime 调用方
  -> ModelRegistry.Get(model)
  -> AIConfig.FindChannelsForModel(model)
  -> GenerativeProviderRouter.Resolve(channels, entry, model, general)
  -> IGenerativeProviderFactory
  -> IGenerativeAssetProvider.GenerateAsync()
  -> OpenAIImageDialect 或其他生成式 dialect
```

不要写 `ImageGen => OpenAIImageProvider` 这类硬路由。生成式 provider 必须由 `GenerativeProviderRouter` 根据 `Endpoint`、`AdapterId`、`ProviderProtocol` 和模型能力综合决定。

## 4. 模型元数据与 Adapter

### 4.1 ModelEntry

`ModelEntry` 是模型行为的中心：

- `Id`: 模型 ID，例如 `gpt-4o`、`gpt-image-2`、`deepseek-v4-pro`。
- `Vendor`: 供应商名，用于展示和 adapter 过滤。
- `Capabilities`: `Chat`、`VisionInput`、`ImageGen`、`ImageEdit`、`Embedding`、`Rerank` 等。
- `Endpoint`: 默认端点族，例如 `ChatCompletions`、`ImageGenerations`、`ImageEdits`。
- `ContextWindow`: token 上下文窗口，0 表示使用 `ModelRegistry` fallback。
- `AdapterId`: provider-specific adapter/dialect id。
- `Behavior`: 框架已知行为标志。
- `BehaviorTags` / `BehaviorOptions`: 用户或 adapter 私有扩展。

框架内置模型在 `Runtime/Core/Presets/BuiltInPresetCatalog.cs`。自定义模型来自 `UniAISettings.CustomModels`，查找优先级为自定义模型高于内置预设。

### 4.2 Behavior 与 BehaviorTags

`ModelBehavior` 只放框架核心已理解的通用行为，例如：

- `EmitsReasoningContent`
- `RequiresReasoningReplayForToolCalls`
- `ThinkingDefaultEnabled`
- `IgnoresTemperatureInThinking`
- `NoStreaming`
- `NoFunctionCalling`
- `RequiresMultipartForImageEdit`

如果是某个 adapter 私有的行为，不要继续扩展 `ModelBehavior`，优先使用 `BehaviorTags` 或 `BehaviorOptions`。例如：

- `chat.reasoning_content`
- `openai.images.gpt_image`
- `openai.images.no_response_format`
- `image.allowed_output_formats = png,jpeg,webp`
- `image.max_side = 3840`

### 4.3 Adapter Discovery

Adapter 工厂通过 `[Adapter(...)]` 自动发现：

```csharp
[Adapter(
    "deepseek.openai_chat.thinking",
    AdapterTarget.OpenAIChatDialect,
    "DeepSeek Thinking",
    "DeepSeek thinking mode with reasoning_content replay.",
    priority: 100,
    protocolId: "OpenAI",
    capabilities: ModelCapability.Chat,
    endpointId: "ChatCompletions")]
internal sealed class DeepSeekThinkingDialectFactory : IOpenAIChatDialectFactory
{
    public bool CanHandle(ModelEntry model) { ... }
    public IOpenAIChatDialect Create(ModelEntry model) { ... }
}
```

涉及的类型：

- `AdapterAttribute`: 声明 id、target、协议、能力、端点、优先级等元数据。
- `AdapterDiscovery`: 反射扫描 factory。
- `AdapterRegistry<TFactory>`: 每个扩展点自己的 factory 注册表。
- `AdapterCatalog`: 给 Editor 和诊断使用的统一 adapter 元数据目录。

当前扩展点：

- `ConversationProvider`: `IAIProviderFactory`
- `OpenAIChatDialect`: `IOpenAIChatDialectFactory`
- `ImageGenerationProvider`: `IGenerativeProviderFactory`
- `OpenAIImageDialect`: `IOpenAIImageDialectFactory`
- 预留: `EmbeddingProvider`、`RerankProvider`、`AudioGenerationProvider`、`VideoGenerationProvider`

## 5. Provider 与 Dialect 规则

### 5.1 Provider 主流程

Provider 负责协议形状，不负责模型身份：

- `ClaudeProvider`: Claude Messages API。
- `OpenAIProvider`: OpenAI-compatible Chat Completions。
- `OpenAIImageProvider`: OpenAI-compatible Images API。
- `JsonSseProviderBase`: JSON + SSE provider 的通用模板。

不要在 `OpenAIProvider` 主流程里写 `if (model == "deepseek-v4-pro")`。这类逻辑必须放进 `IOpenAIChatDialect`。

### 5.2 OpenAI Chat Dialect

`IOpenAIChatDialect` 负责 OpenAI-compatible Chat 的模型差异：

- 是否省略 temperature 等采样参数。
- assistant 消息回放时注入 provider-native 字段。
- 非流式响应读取 reasoning 内容。
- 流式 delta 读取 reasoning 增量。

DeepSeek thinking 的关键约束：

- thinking mode 默认开启。
- response 和 stream delta 会出现 `reasoning_content`。
- assistant 消息带 tool calls 时，下一轮请求必须回放这段 `reasoning_content`。
- 普通 assistant 消息也统一经过 `ApplyAssistantMessageExtras(..., hasToolCalls: false)`，避免未来 dialect 漏挂额外字段。

### 5.3 OpenAI Images Dialect

`IOpenAIImageDialect` 负责 OpenAI Images API 的模型差异：

- 选择 `/images/generations` 或 `/images/edits`。
- 判断 JSON 或 multipart/form-data。
- 校验 size、count、format、background 等参数。
- 构造 JSON body 或 multipart parts。
- 解析返回资产。
- 暴露模型能力说明给 Tool/UI。

`gpt-image-2` 当前通过 `openai.images.gpt-image-2` dialect 适配。图片编辑走 multipart，生成请求走 JSON。

## 6. 常见扩展方式

### 6.1 添加 OpenAI-compatible Chat 模型差异

1. 在 `BuiltInPresetCatalog` 或 `UniAISettings.CustomModels` 中补充 `ModelEntry`。
2. 如需显式适配，设置 `AdapterId`。
3. 能用通用行为表达时使用 `ModelBehavior`。
4. 私有行为使用 `BehaviorTags` / `BehaviorOptions`。
5. 实现 `IOpenAIChatDialectFactory` 和 `IOpenAIChatDialect`。
6. 工厂类添加 `[Adapter(... AdapterTarget.OpenAIChatDialect ...)]`。
7. 同时验证 stream 和 non-stream，尤其是 tool call history。

### 6.2 添加新对话 Provider 协议

1. 如协议不属于 Claude/OpenAI，新增 `ProviderProtocol`。
2. 实现 `IAIProvider`，必要时继承 `JsonSseProviderBase`。
3. 实现 `IAIProviderFactory`。
4. 工厂类添加 `[Adapter(... AdapterTarget.ConversationProvider ...)]`。
5. 如需默认渠道，添加 `ChannelPreset` 和默认模型归属。

不要修改 `ChannelManager` 增加协议 switch。Provider 解析应走 `AIProviderFactoryRegistry`。

### 6.3 添加生成式 Provider

1. 实现 `IGenerativeProviderFactory`。
2. 工厂类添加 `[Adapter(... AdapterTarget.ImageGenerationProvider ...)]` 或未来对应生成式 target。
3. 在 `CanHandle(channel, model, modelId)` 中综合判断协议、能力、端点和 adapter。
4. 实现 `IGenerativeAssetProvider`。
5. 如是 OpenAI Images 变体，优先新增 `IOpenAIImageDialect`，不要复制整个 provider。

### 6.4 添加 gpt-image 类模型

1. `Capabilities` 至少包含 `ImageGen`，支持编辑则加 `ImageEdit`。
2. `Endpoint` 设为 `ImageGenerations` 或 `ImageEdits`，不要用能力代替端点。
3. `AdapterId` 指向具体 image dialect。
4. 使用 `BehaviorTags` / `BehaviorOptions` 描述模型私有参数。
5. `CanHandle` 不要用泛化行为做身份匹配，例如不要只靠 `RequiresMultipartForImageEdit` 判断某个模型。

### 6.5 添加 Tool

1. 在 Runtime 或 Editor 对应目录创建 `internal static` 工具类。
2. 使用 `[UniAITool]` 标记 `Name`、`Group`、`Description`、`Actions`。
3. 提供 `HandleAsync(JObject args, CancellationToken ct)`。
4. 每个 action 对应 `<PascalCaseAction>Args` 嵌套类，用 `[ToolParam]` 描述字段。
5. 返回 `ToolResponse.Success(...)` 或 `ToolResponse.Error(...)`。
6. 文件路径操作必须使用 `ToolPathHelper` 或既有安全 helper。

工具不需要 ScriptableObject 注册。`UniAIToolRegistry` 会反射扫描。

### 6.6 添加上下文来源

- 对话级 Unity/业务上下文: 实现 `IConversationContextProvider`，通过 `ChatOrchestratorDependencies.ContextProvider` 注入。
- RAG/MCP Resource 类上下文: 实现 `IContextProvider`，加入 `ContextPipeline.AddProvider()`。

Editor Chat 的 Unity 上下文由 `StreamingController.EditorConversationContextProvider` 包装 `ContextCollector` 注入。

## 7. 配置和存储

```text
UniAISettings.asset (Assets/Resources/UniAI/)
├── ChannelEntries
│   ├── Name / Protocol / Enabled
│   ├── ApiKey / BaseUrl
│   ├── EnvVarName / UseEnvVar
│   ├── Models
│   └── ApiVersion
├── General
│   ├── TimeoutSeconds
│   ├── LogLevel
│   ├── ContextWindow
│   └── Mcp
└── CustomModels

EditorPreferences (Library/, Editor only)
├── LastSelectedModelId / ShowSidebar / MaxHistorySessions
├── ToolTimeout / ToolMaxOutputChars / SearchMaxMatches
├── DefaultContextSlots
├── McpAutoConnect / McpResourceInjection
└── AgentDirectory / McpServerDirectory
```

API Key 优先级：

```text
ChannelEntry.GetEffectiveApiKey()
  -> UseEnvVar && EnvVarName 有值时优先读取环境变量
  -> 否则使用 ChannelEntry.ApiKey
```

会话历史默认走 `FileChatHistoryStorage`。Editor 注入 `ChatHistorySessionPersistence`；Runtime 可替换为自己的 `IChatSessionPersistence`。

## 8. Editor 与 Runtime 边界

- Runtime assembly: `Runtime/UniAI.asmdef`，不可引用 `UnityEditor`。
- Editor assembly: `Editor/UniAI.Editor.asmdef`，可以访问 Runtime，且通过 `InternalsVisibleTo("UniAI.Editor")` 访问 internal 类型。
- Editor 专用 Tool 必须放在 `Editor/Tools/`。
- Runtime Tool 必须能在非 Editor 环境运行。
- `EditorAgentGuard` 只在 Editor 侧注入，用于 Tool 执行期间保护 AssetDatabase。

如果某个功能既要 Runtime 又要 Editor UI，先把核心能力放 Runtime，再由 Editor 包一层 UI 或策略注入。

## 9. 开发守则

- 修改前先找现有模式，优先复用本框架已有 helper、registry、factory、pipeline。
- 不要在 Provider 主流程硬编码模型名。
- 不要把 Editor 行为塞进 Runtime。
- 不要让 `ChatOrchestrator` 重新变成大类；新增职责优先抽策略或 helper。
- stream 和 non-stream 路径要同步维护。
- Tool loop 中 assistant tool-call message 必须保留 tool calls、文本和 reasoning content。
- 生成式模型路由必须通过 `GenerativeProviderRouter`。
- Adapter 工厂必须声明 `[Adapter]`，并通过 `CanHandle` 做元数据兜底匹配。
- Adapter ID 是稳定外部配置，重命名会影响用户自定义模型。
- Unity 可序列化配置类使用 public fields 是现有风格；对外运行时 API 和指标类优先使用 properties。
- JSON 使用 Newtonsoft.Json，并遵守 `NullValueHandling.Ignore` 的现有协议模型风格。
- 所有异步入口接受并传递 `CancellationToken`。
- 网络、MCP、Tool 超时使用既有配置和 `TimeoutHelper`。
- 日志通过 `AILogger`，不要输出完整 API Key。

## 10. 验证建议

代码改动后优先执行：

```powershell
dotnet build UniAI.csproj -nologo -v minimal
dotnet build UniAI.Editor.csproj -nologo -v minimal
```

按改动类型补充验证：

- OpenAI Chat dialect: 普通 chat、tool call、stream tool call、reasoning replay。
- DeepSeek thinking: 同一轮多次 tool call，不丢 `reasoning_content`。
- Images dialect: generation、edit、multipart、参数校验、返回解析。
- Channel routing: 多渠道 fallback、缓存命中、禁用渠道不参与。
- Editor Chat: 发送、停止、保存、标题生成、MCP 状态、ContextSlots。
- Tool: schema 生成、参数解析、超时、错误返回、路径安全。

文档改动只需检查行数、路径和示例是否仍对应当前 API。
