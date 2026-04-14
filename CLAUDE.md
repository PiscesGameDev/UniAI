# UniAI - AI Assistant Guide

> 本文档帮助 AI 助手快速理解 UniAI 框架的架构和开发规范。

**包名**: com.uniai.core
**版本**: 0.0.1
**Unity**: 2022.3+
**许可证**: Apache 2.0

---

## 1. 框架定位

UniAI 是 Unity 与 AI 交互的中间件，向下对接多种 AI Provider，向上为 Unity Editor 和游戏 Runtime 提供统一的 AI 调用能力。核心设计原则：

- **统一抽象**: 所有 Provider 共享 `AIRequest/AIResponse/AIStreamChunk` 模型
- **双协议覆盖**: Claude Messages API + OpenAI Chat Completions API，兼容所有 OpenAI 兼容接口
- **双运行器架构**: `ChatRunner`（纯对话）与 `AIAgentRunner`（Tool 循环）独立实现，共享 `IConversationRunner` 接口
- **声明式工具系统**: `[UniAITool]` 特性标记静态类，自动反射发现注册，无需 ScriptableObject
- **MCP Client**: 连接外部 MCP Server（Stdio / Streamable HTTP），动态注入 Tools 与 Resources
- **模型注册表**: `ModelRegistry` 统一管理模型元信息（能力、端点、上下文窗口）
- **生成式资产**: `GenerativeAssetService` 支持 AI 图片生成等多模态输出
- **对话编排**: `ChatOrchestrator` 封装完整流式对话生命周期，Editor 通过委托注入平台行为
- **多模态输入**: 文本、图片（base64）、文件附件混合消息
- **上下文窗口管理**: 自动 token 预估、滑动窗口截断、摘要压缩、RAG 上下文注入
- **零业务耦合**: 纯工具层，不依赖任何业务框架

## 2. 核心架构

```
BuildAIMessages() → 全量消息（含附件 → AIContent 子类）
        ↓
ContextPipeline.ProcessAsync()（上下文窗口管理）
  1. IContextProvider 注入外部上下文（RAG / MCP Resource）
  2. TokenEstimator 估算总 token
  3. 超限时 → 摘要压缩旧消息 / 截断
        ↓
ChatOrchestrator（对话编排器，Runtime 自足）
  ├── GuardFactory: Func<IDisposable>    ← Editor 注入 EditorAgentGuard
  └── ContextCollector: Func<int,string> ← Editor 注入 Unity 上下文采集
        ↓
AIAgentRunner / ChatRunner（对话运行器）
  ├── UniAIToolRegistry（[UniAITool] 反射注册表） ← 内置工具
  └── McpClientManager（Tools 动态合并）            ← MCP 外部工具
        ↓
AIClient（API 入口，路由模式委托 ChannelManager 处理渠道缓存和故障转移）
        ↓
IAIProvider 接口
   ├── ClaudeProvider    → Claude Messages API (tool_use/tool_result)
   └── OpenAIProvider    → OpenAI Chat Completions API (function_calling/tool_calls)
         ↓
AIHttpClient（内部静态 HTTP 客户端）
   ├── PostJsonAsync     → 完整响应
   └── PostStreamAsync   → SSE 流式响应
         ↓
SSEDownloadHandler + SSEParser（SSE 协议层）

── 独立的 MCP 传输通道（与 AI Provider HTTP 无关） ──
McpClient → IMcpTransport
   ├── StdioMcpTransport  → 子进程 stdin/stdout（JSON-RPC / line-delimited）
   └── HttpMcpTransport   → Streamable HTTP（复用 AIHttpClient）
```

## 3. 关键类与文件

### 3.1 Runtime/Core — 核心入口

| 类 | 路径 | 职责 |
|----|------|------|
| `AIClient` | `Runtime/Core/AIClient.cs` | 框架唯一入口。路由模式: `Create(config)` — 模型从 `request.Model` 解析，委托 `ChannelManager` 处理缓存和故障转移；直连模式: `Create(entry, modelId, general)` 用于测试连接。便捷方法: `ChatAsync` / `ChatStreamAsync` / `SendAsync<T>` |
| `ChannelManager` | `Runtime/Core/ChannelManager.cs` | 全局静态渠道管理器: Provider 缓存（modelId → 上次成功的渠道/Provider）+ 故障转移 + 渠道验证。`SendAsync` / `StreamAsync` / `CreateProvider` / `Invalidate` / `InvalidateAll` |
| `AIConfig` | `Runtime/Core/AIConfig.cs` | 配置模型。`ChannelEntries` 渠道列表 + `ActiveChannelId` + `GeneralConfig`（含 `ContextWindowConfig` + `McpRuntimeConfig`） |
| `ChannelEntry` | `Runtime/Core/ChannelEntry.cs` | 单个渠道配置。`Protocol` (Claude/OpenAI) + `ApiKey` + `BaseUrl` + `Models` + `EnvVarName`/`UseEnvVar` 环境变量覆盖。内置预设: `Claude()` / `OpenAI()` / `Gemini()` / `DeepSeek()` |
| `UniAISettings` | `Runtime/Core/UniAISettings.cs` | 运行时 ScriptableObject。`Instance` 单例从 `Resources/UniAI/` 加载。含 `ChannelEntries` + `General` + `CustomModels`（补充 `ModelRegistry`） |
| `ChatOrchestrator` | `Runtime/Core/ChatOrchestrator.cs` | 对话编排器。管理 Runner/Client/ContextPipeline 生命周期。`StreamResponseAsync` 驱动完整流式对话（含 MCP 等待、上下文管理、事件分发）。Editor 通过 `GuardFactory` / `ContextCollector` 委托注入行为 |
| `ModelSelector` | `Runtime/Core/ModelSelector.cs` | 模型选择管理。`RebuildCache(config)` 建立模型列表，`ResolveForAgent(agent)` 解析 Agent 指定模型，`RestoreFromSession(session)` 恢复会话模型 |
| `ModelRegistry` | `Runtime/Core/ModelRegistry.cs` | 静态模型注册表。内置 30+ 预设（OpenAI/Anthropic/Google/DeepSeek/Meta/xAI/Alibaba）+ 用户 `CustomModels`。`GetContextWindow(modelId)` 精确查 → 前缀兜底表 → 8192 默认。`HasCapability(modelId, cap)` 能力查询 |
| `ModelEntry` | `Runtime/Core/ModelEntry.cs` | 模型定义：`Id` / `Vendor` / `Capabilities` (Flags) / `Endpoint` (唯一) / `ContextWindow` / `Description` / `Icon` |
| `ModelCapability` | `Runtime/Core/ModelCapability.cs` | `[Flags]` 枚举: `Chat` / `ImageGen` / `ImageEdit` / `AudioGen` / `VideoGen` |
| `ModelEndpoint` | `Runtime/Core/ModelCapability.cs` | API 端点枚举: `ChatCompletions` / `ImageGenerations` / `ImageEdits` / `AudioGenerations` / `VideoGenerations` |
| `IConversationRunner` | `Runtime/Core/IConversationRunner.cs` | 对话运行器接口: `RunAsync` + `RunStreamAsync` |
| `ChatRunner` | `Runtime/Core/ChatRunner.cs` | 纯 Chat 运行器，直接桥接 AIClient（无 Tool 循环） |
| `ModelListService` | `Runtime/Core/ModelListService.cs` | 从 Provider API 获取可用模型列表（支持 OpenAI + Claude 分页） |
| `TimeoutHelper` | `Runtime/Core/TimeoutHelper.cs` | 通用超时包装器，基于链接 CTS + CancelAfter |
| `AILogger` | `Runtime/Core/AILogger.cs` | `internal` 日志，支持级别控制和 API Key 脱敏 |

### 3.2 Runtime/Models — 统一请求/响应模型

| 类 | 路径 | 职责 |
|----|------|------|
| `AIRequest` | `Runtime/Models/AIRequest.cs` | 统一请求: `SystemPrompt` + `Messages` + `Model` + `MaxTokens` + `Temperature` + `Tools` + `ResponseFormat` |
| `AIResponse` | `Runtime/Models/AIResponse.cs` | 统一响应: `IsSuccess` + `Text` + `Error` + `Usage` (TokenUsage) + `StopReason` + `ToolCalls`。`AITypedResponse<T>` 支持结构化输出反序列化 |
| `AIMessage` | `Runtime/Models/AIMessage.cs` | 消息: `Role` (User/Assistant) + `Contents` 列表。工厂方法: `User()` / `Assistant()` / `UserWithImage()` / `UserWithFiles()` / `ToolResult()` |
| `AIContent` | `Runtime/Models/AIContent.cs` | 内容块基类，子类: `AITextContent` / `AIImageContent`(byte[] + mediaType) / `AIFileContent`(fileName + text) / `AIToolUseContent` / `AIToolResultContent` |
| `AITool` | `Runtime/Models/AITool.cs` | Tool 定义: `Name` + `Description` + `ParametersSchema`。`AIToolCall`: `Id` + `Name` + `Arguments` |
| `AIStreamChunk` | `Runtime/Models/AIStreamChunk.cs` | 流式响应块: `DeltaText` + `IsComplete` + `Usage` + `ToolCall` |
| `AIResponseFormat` | `Runtime/Models/AIResponseFormat.cs` | 结构化响应格式声明（JSON Schema） |

### 3.3 Runtime/Providers — Provider 实现

| 类 | 路径 | 协议 |
|----|------|------|
| `IAIProvider` | `Runtime/Providers/IAIProvider.cs` | Provider 接口: `SendAsync` + `StreamAsync` + `Name` |
| `ProviderBase` | `Runtime/Providers/ProviderBase.cs` | 抽象基类: 模板方法 + `ProviderConfig`（ApiKey/BaseUrl/Model/TimeoutSeconds/ApiVersion） |
| `ClaudeProvider` | `Runtime/Providers/Claude/ClaudeProvider.cs` | Claude Messages API (`/v1/messages`) |
| `ClaudeModels` | `Runtime/Providers/Claude/ClaudeModels.cs` | Claude JSON 模型 |
| `OpenAIProvider` | `Runtime/Providers/OpenAI/OpenAIProvider.cs` | OpenAI Chat Completions API (`/chat/completions`) |
| `OpenAIModels` | `Runtime/Providers/OpenAI/OpenAIModels.cs` | OpenAI JSON 模型 |

### 3.4 Runtime/Http — HTTP 层

| 类 | 路径 | 职责 |
|----|------|------|
| `AIHttpClient` | `Runtime/Http/AIHttpClient.cs` | `internal static` HTTP 客户端: `GetAsync` + `PostJsonAsync` + `PostStreamAsync` |
| `HttpResult` | `Runtime/Http/HttpResult.cs` | HTTP 结果封装 |
| `SSEDownloadHandler` | `Runtime/Http/SSEDownloadHandler.cs` | 继承 `DownloadHandlerScript`，增量写入 UniTask `Channel<string>` |
| `SSEParser` | `Runtime/Http/SSEParser.cs` | SSE 协议解析器 |

### 3.5 Runtime/Agent — Agent 与工具系统

| 类 | 路径 | 职责 |
|----|------|------|
| `UniAIToolAttribute` | `Runtime/Agent/UniAIToolAttribute.cs` | 工具声明特性。`Name` / `Group` / `Description` / `HasActions`(bool) / `Actions`(string[]) / `RequiresPolling` / `MaxPollSeconds`。`ToolParamAttribute`: 字段级 schema 描述 |
| `ToolGroups` | `Runtime/Agent/UniAIToolAttribute.cs` | 分组常量: `Core` / `Scene` / `Asset` / `Editor` / `Testing` / `Runtime` / `Generate` |
| `UniAIToolRegistry` | `Runtime/Agent/UniAIToolRegistry.cs` | 反射扫描所有程序集中 `[UniAITool]` 静态类，按组注册 `HandleAsync` 委托 |
| `ToolSchemaGenerator` | `Runtime/Agent/ToolSchemaGenerator.cs` | 从工具嵌套的 `<PascalCaseAction>Args` 类自动生成 JSON Schema（支持 Args 继承链） |
| `ToolResponse` | `Runtime/Agent/ToolResponse.cs` | 统一返回: `Success(data, message)` / `Error(error, code)` |
| `ToolPathHelper` | `Runtime/Agent/ToolPathHelper.cs` | 路径安全校验（拒绝 `..` 穿越与项目外路径） |
| `AgentDefinition` | `Runtime/Agent/AgentDefinition.cs` | Agent 配置 SO。`AgentName` / `SystemPrompt` / `SpecifyModel` / `Temperature` / `MaxTokens` / `MaxTurns` / `ToolGroups`(List\<string\>) / `McpServers`(List\<McpServerConfig\>) |
| `AgentRegistry` | `Runtime/Agent/AgentRegistry.cs` | 静态注册表。`Register` / `TryGet(id)` / `Unregister`。Editor 由 `AgentManager` 自动扫描注册；Runtime 可手动操作 |
| `AIAgentRunner` | `Runtime/Agent/AIAgentRunner.cs` | Agent 运行器。按 `ToolGroups` 从注册表拉取工具，封装多轮 Tool 调用循环。`InitializeMcpAsync()` 连接 MCP Server |
| `AgentEvent` | `Runtime/Agent/AgentEvent.cs` | Agent 流式事件: `TextDelta` / `ToolCallStart` / `ToolCallResult` / `TurnComplete` / `Error` |
| `AgentResult` | `Runtime/Agent/AgentResult.cs` | Agent 非流式运行结果 |

**工具系统核心设计**:
- **声明式**: `[UniAITool]` 标记 `internal static` 类，启动时自动反射发现，无需 SO 资产
- **聚合 + action 派发**: 一个工具类含多个 action（如 `manage_scene` 有 15 个 action），通过 `action` 字段分发。`HasActions=false` 用于单功能工具
- **Args 嵌套类**: 每个 action 对应 `<PascalCaseAction>Args` 嵌套类，`[ToolParam]` 标注字段，`ToolSchemaGenerator` 自动合并为带 `action` 枚举的 JSON Schema
- **分组而非列表**: `AgentDefinition.ToolGroups` 存储分组名，启用某组即解锁全部工具，新增工具只需加文件

### 3.6 Runtime/Chat — 会话管理

| 类 | 路径 | 职责 |
|----|------|------|
| `ChatSession` | `Runtime/Chat/ChatSession.cs` | 会话模型: `Messages`(List\<ChatMessage\>) + `SummaryText` + `SummarizedUpToIndex` + `EstimatedTokens`。`BuildAIMessages()` 转换为 `List<AIMessage>` 供 Provider 使用 |
| `ChatMessage` | `Runtime/Chat/ChatSession.cs` | 消息: `Role`/`Content`/`InputTokens`/`OutputTokens`/`Timestamp` + Tool 调用字段 + `Attachments`(List\<ChatAttachment\>) |
| `ChatAttachment` | `Runtime/Chat/ChatSession.cs` | 附件: `Type`(Image/File) + `FileName` + `Content`(base64 或文本) + `MediaType`。`BuildAIMessages()` 中自动转为 `AIImageContent`/`AIFileContent` |
| `ChatHistoryManager` | `Runtime/Chat/ChatHistoryManager.cs` | 会话历史管理器 |
| `IChatHistoryStorage` | `Runtime/Chat/IChatHistoryStorage.cs` | 存储接口 |
| `FileChatHistoryStorage` | `Runtime/Chat/FileChatHistoryStorage.cs` | 文件系统存储实现，支持最大会话数限制，超限自动删除最旧 |

### 3.7 Runtime/Context — 上下文窗口管理

| 类 | 路径 | 职责 |
|----|------|------|
| `ContextPipeline` | `Runtime/Context/ContextPipeline.cs` | 上下文处理管道: RAG 注入 → 摘要注入 → token 估算 → 超限摘要/截断 |
| `ContextWindowConfig` | `Runtime/Context/ContextWindowConfig.cs` | 配置: `Enabled` / `MaxContextTokens`(0=自动) / `ReservedOutputTokens`(4096) / `MinRecentMessages`(4) / `EnableSummary` / `SummaryMaxTokens`(512) |
| `TokenEstimator` | `Runtime/Context/TokenEstimator.cs` | Token 预估: CJK 1:1，英文 4:1，图片 1000，文件按文本内容+文件名+10 |
| `MessageSummarizer` | `Runtime/Context/MessageSummarizer.cs` | 消息摘要器，调用 AI 压缩历史消息 |
| `IContextProvider` | `Runtime/Context/IContextProvider.cs` | RAG 上下文提供者接口 + `ContextResult` |

### 3.8 Runtime/MCP — Model Context Protocol Client

| 类 | 路径 | 职责 |
|----|------|------|
| `McpServerConfig` | `Runtime/MCP/McpServerConfig.cs` | MCP Server 配置 SO（Stdio / HTTP + 环境变量 / Headers） |
| `IMcpTransport` | `Runtime/MCP/IMcpTransport.cs` | 传输层抽象: `ConnectAsync` + `SendRequestAsync` + `SendNotificationAsync` |
| `StdioMcpTransport` | `Runtime/MCP/StdioMcpTransport.cs` | 子进程 stdin/stdout JSON-RPC（仅 Editor/Standalone） |
| `HttpMcpTransport` | `Runtime/MCP/HttpMcpTransport.cs` | Streamable HTTP 传输 |
| `McpTransportFactory` | `Runtime/MCP/McpTransportFactory.cs` | 传输类型工厂 |
| `McpClient` | `Runtime/MCP/McpClient.cs` | 单 Server 连接: initialize → tools/list → resources/list |
| `McpClientManager` | `Runtime/MCP/McpClientManager.cs` | 多 Server 管理: `GetAllTools` / `CallToolAsync` / `GetResourceProviders` / `TestConnectionAsync` |
| `McpResourceProvider` | `Runtime/MCP/McpResourceProvider.cs` | Resource → `IContextProvider` 适配（关键词匹配相关度过滤，≤0.3 不注入） |
| `McpConstants` | `Runtime/MCP/McpConstants.cs` | 方法名 / 内容类型常量 |
| `McpModels` | `Runtime/MCP/McpModels.cs` | JSON-RPC 2.0 + MCP 协议数据模型 |

### 3.9 Runtime/Generative — 生成式资产

| 类 | 路径 | 职责 |
|----|------|------|
| `GenerativeAssetService` | `Runtime/Generative/GenerativeAssetService.cs` | Provider 注册表单例。`Register` / `GetDefault(type)` / `GetByType(type)` |
| `IGenerativeAssetProvider` | `Runtime/Generative/IGenerativeAssetProvider.cs` | 生成 Provider 接口: `GenerateAsync(request)` → `GenerateResult` |
| `OpenAIImageProvider` | `Runtime/Generative/Providers/OpenAIImageProvider.cs` | OpenAI DALL-E / 兼容 API 图片生成 |
| `GenerateRequest` | `Runtime/Generative/GenerateRequest.cs` | 生成请求 |
| `GenerateResult` | `Runtime/Generative/GenerateResult.cs` | 生成结果 |
| `GenerativeAssetType` | `Runtime/Generative/GenerativeAssetType.cs` | 枚举: `Image` / `Audio` / `Model3D` / `Video` |

**核心设计**: 模型决定能力，而非渠道。`ManageGenerate` Tool 根据 `model` 参数查 `ModelRegistry.HasCapability(model, ImageGen)` 路由到正确的 Provider。

### 3.10 Runtime/Tools — 运行时工具

| 类 | 路径 | 职责 |
|----|------|------|
| `ManageFile` | `Runtime/Tools/ManageFile.cs` | `[UniAITool]` 文件操作: read / write / list / search |
| `RuntimeQuery` | `Runtime/Tools/RuntimeQuery.cs` | `[UniAITool]` 运行时状态查询: scene / find / inspect / component / find_type |
| `ToolConfig` | `Runtime/Tools/ToolConfig.cs` | 全局配置: `MaxOutputChars`(50000) / `SearchMaxMatches`(100)。Editor 侧启动时覆盖 |
| `ToolCallbacks` | `Runtime/Tools/ToolCallbacks.cs` | 回调注入: `OnAssetsModified`。Editor 注入 `EditorAgentGuard.NotifyAssetsModified` |

### 3.11 Editor — 管理窗口

| 类 | 路径 | 职责 |
|----|------|------|
| `UniAIManagerWindow` | `Editor/Setting/UniAIManagerWindow.cs` | 统一管理窗口（菜单 `Window > UniAI > Manager`），左侧图标导航 + 右侧 Tab |
| `ChannelTab` | `Editor/Setting/ChannelTab.cs` | 渠道管理: 增删、Enabled 开关、API Key、模型列表、连接测试 |
| `AgentTab` | `Editor/Setting/AgentTab.cs` | Agent 管理 |
| `ModelTab` | `Editor/Setting/ModelTab.cs` | 模型管理: 内置 + 自定义模型展示，能力/端点/上下文窗口编辑，多能力 badge |
| `ToolsTab` | `Editor/Setting/ToolsTab.cs` | 工具展示: 按分组列出所有已注册 `[UniAITool]`，内置/自定义 badge |
| `McpTab` | `Editor/MCP/McpTab.cs` | MCP Server 管理: 增删、Stdio/HTTP 配置、连接测试 |
| `SettingsTab` | `Editor/Setting/SettingsTab.cs` | 设置: 运行时 + 上下文窗口 + 编辑器偏好 + Tool + MCP |
| `AIConfigManager` | `Editor/Setting/AIConfigManager.cs` | 配置持久化（UniAISettings SO + EditorPreferences） |
| `EditorPreferences` | `Editor/Setting/EditorPreferences.cs` | `ScriptableSingleton`，编辑器偏好持久化到 Library/ |

### 3.12 Editor — 对话窗口

| 类 | 路径 | 职责 |
|----|------|------|
| `AIChatWindow` | `Editor/Chat/AIChatWindow.cs` | 对话窗口主类（partial class），菜单 `Window > UniAI > Chat` |
| `AIChatWindow.ChatArea` | `Editor/Chat/AIChatWindow.ChatArea.cs` | 消息气泡渲染 + 附件缩略图/文件标签内联渲染 |
| `AIChatWindow.InputArea` | `Editor/Chat/AIChatWindow.InputArea.cs` | 输入区 + `@` 附件按钮(文件选择器) + 拖拽 Unity 资产 + 附件预览条 |
| `AIChatWindow.Sidebar` | `Editor/Chat/AIChatWindow.Sidebar.cs` | 侧边栏（会话列表） |
| `AIChatWindow.Toolbar` | `Editor/Chat/AIChatWindow.Toolbar.cs` | 工具栏（模型/Agent 选择） |
| `AIChatWindow.Styles` | `Editor/Chat/AIChatWindow.Styles.cs` | 样式定义 |
| `StreamingController` | `Editor/Chat/StreamingController.cs` | Editor 薄适配层，注入 `EditorAgentGuard` + `ContextCollector` 到 `ChatOrchestrator` |
| `ChatWindowController` | `Editor/Chat/ChatWindowController.cs` | 业务控制器: 会话/Client/Runner 生命周期 |
| `ContextCollector` | `Editor/Chat/ContextCollector.cs` | Unity 上下文采集: `Selection` / `Console` / `Project` 三个槽位 |
| `MarkdownRenderer` | `Editor/Chat/MarkdownRenderer.cs` | Markdown → IMGUI 渲染 |

### 3.13 Editor — Agent / Tools

| 类 | 路径 | 职责 |
|----|------|------|
| `AgentManager` | `Editor/Agent/AgentManager.cs` | Agent 资产扫描 + 内置默认 Agent + 注册到 `AgentRegistry` |
| `AgentDefinitionEditor` | `Editor/Agent/AgentDefinitionEditor.cs` | Agent 自定义 Inspector |
| `EditorAgentGuard` | `Editor/Agent/EditorAgentGuard.cs` | Tool 执行期间锁定 AssetDatabase 刷新，防止重编译 |
| `SceneEdit` | `Editor/Tools/SceneEdit.cs` | Play Mode 安全 Undo 包装 |

**内置聚合工具**（`Editor/Tools/` + `Runtime/Tools/`）:

| Tool | Group | 位置 | Actions |
|------|-------|------|---------|
| `ManageFile` | core | Runtime | read / write / list / search |
| `RuntimeQuery` | runtime | Runtime | scene / find / inspect / component / find_type |
| `ManageScene` | scene | Editor | create_empty / create_primitive / create_camera / create_light / destroy / set_transform / set_active / set_parent / rename / add_component / remove_component / set_property / save_scene / open_scene / new_scene |
| `ManageAsset` | asset | Editor | create_folder / copy / move / rename / delete / refresh / find / dependencies / guid_to_path / path_to_guid |
| `ManagePrefab` | asset | Editor | create_from_gameobject / instantiate / unpack / apply_overrides / revert_overrides |
| `ManageMaterial` | asset | Editor | create / set_shader / set_color / set_float / set_int / set_vector / set_texture |
| `ManageScriptableObject` | asset | Editor | list_fields / get / set |
| `ManageConsole` | editor | Editor | get_recent / get_errors / get_warnings / get_compile_errors / count / clear |
| `ManageMenu` | editor | Editor | execute / list |
| `ManageSelection` | editor | Editor | get / set / clear / get_assets |
| `ManageProjectSettings` | editor | Editor | list_tags / add_tag / remove_tag / list_layers / set_layer / get_physics / set_physics / get_time / set_time / get_quality / set_quality |
| `ManageTest` | testing | Editor | run_editmode / run_playmode / run_both |
| `ManageGenerate` | generate | Editor | generate / list_models |

## 4. 数据流

### 4.1 完整响应

```
用户代码 → AIClient.SendAsync(AIRequest)
  → IAIProvider.SendAsync
    → AIHttpClient.PostJsonAsync (UnityWebRequest)
    → Provider 解析 JSON → AIResponse
  → 返回 AIResponse
```

### 4.2 流式响应

```
用户代码 → AIClient.StreamAsync(AIRequest)
  → IAIProvider.StreamAsync
    → AIHttpClient.PostStreamAsync
      → SSEDownloadHandler.ReceiveData → Channel<string>
      → SSEParser.ParseLine → SSEEvent
    → Provider 解析事件 → AIStreamChunk
  → yield return AIStreamChunk (IUniTaskAsyncEnumerable)
```

### 4.3 Editor 对话窗口（Agent 模式）

```
用户输入 → ChatWindowController.SendMessage(text, contextSlots, attachments)
  → 添加 ChatMessage(User, attachments) 到 ChatSession
  → StreamingController.StreamResponseAsync()
    → ChatOrchestrator.StreamResponseAsync()
      → 等待 MCP 初始化完成（如有）
      → BuildAIMessages()（附件 → AIContent 子类）
      → ContextCollector 注入 Unity 上下文
      → ContextPipeline.ProcessAsync() 上下文窗口管理
      → IConversationRunner.RunStreamAsync() 驱动对话
        → AgentEvent.TextDelta → 增量更新 Assistant 消息
        → AgentEvent.ToolCallStart → 添加 ToolCall 消息
        → AgentEvent.ToolCallResult → 更新结果
        → AgentEvent.TurnComplete → 累计 Token
      → MarkdownRenderer.Draw() 渲染
  → ChatHistory.Save() 持久化
  → GenerateTitleAsync() 自动标题
```

## 5. 配置体系

### 5.1 配置架构

```
UniAISettings (ScriptableObject, Resources/UniAI/)
├── ChannelEntries: List<ChannelEntry>   # 渠道列表
│   ├── Id, Name, Protocol, Enabled      # 标识 + 启用开关
│   ├── ApiKey, BaseUrl                  # 连接参数
│   ├── EnvVarName, UseEnvVar            # 环境变量覆盖
│   ├── Models: List<string>             # 支持的模型列表
│   └── ApiVersion                       # Claude 专用
├── General: GeneralConfig               # 通用设置
│   ├── TimeoutSeconds: int (60)
│   ├── LogLevel: AILogLevel (Info)
│   ├── ContextWindow: ContextWindowConfig
│   │   ├── Enabled (true), MaxContextTokens (0=自动)
│   │   ├── ReservedOutputTokens (4096), MinRecentMessages (4)
│   │   └── EnableSummary (true), SummaryMaxTokens (512)
│   └── Mcp: McpRuntimeConfig
│       ├── InitTimeoutSeconds (30)
│       └── ToolCallTimeoutSeconds (60)
└── CustomModels: List<ModelEntry>       # 用户自定义模型（补充 ModelRegistry）

EditorPreferences (ScriptableSingleton, Library/，仅 Editor)
├── LastSelectedModelId, ShowSidebar, MaxHistorySessions (50)
├── UserAvatar, AiAvatar
├── AgentDirectory ("Assets/Agents")
├── ToolTimeout (30s), ToolMaxOutputChars (50000), SearchMaxMatches (100)
├── DefaultContextSlots
├── McpAutoConnect (true), McpResourceInjection (true)
└── McpServerDirectory ("Assets/Agents/MCP")
```

### 5.2 配置优先级

```
环境变量 (ChannelEntry.EnvVarName + UseEnvVar)  ← 最高，覆盖 ApiKey
        ↓
UniAISettings.asset (Resources/UniAI/)           ← 运行时 SO
        ↓
自动创建默认配置                                  ← 首次使用时
```

### 5.3 内置渠道预设

| 预设 | 协议 | BaseUrl | 默认 Models | EnvVarName |
|------|------|---------|------------|------------|
| Claude | Claude | `https://api.anthropic.com` | claude-sonnet-4-20250514, claude-opus-4-6 | `ANTHROPIC_API_KEY` |
| OpenAI | OpenAI | `https://api.openai.com/v1` | gpt-4o, gpt-4o-mini, o1 | `OPENAI_API_KEY` |
| Gemini | OpenAI | `https://generativelanguage.googleapis.com/v1beta/openai` | gemini-2.0-flash, gemini-2.5-pro | `GEMINI_API_KEY` |
| DeepSeek | OpenAI | `https://api.deepseek.com/v1` | deepseek-chat, deepseek-reasoner | `DEEPSEEK_API_KEY` |

## 6. Assembly 结构

| Assembly | 路径 | 平台 | 依赖 |
|----------|------|------|------|
| `UniAI` | `Runtime/UniAI.asmdef` | 全平台 | UniTask, UniTask.Linq |
| `UniAI.Editor` | `Editor/UniAI.Editor.asmdef` | Editor Only | UniAI, UniTask, UniTask.Linq |

`InternalsVisibleTo("UniAI.Editor")` — Editor 可访问 Runtime 的 `internal` 类型（如 `AIHttpClient`, `AILogger`）。

## 7. 协议差异处理

### Claude Messages API

- System Prompt: `system` 顶级字段
- 认证: `x-api-key` + `anthropic-version` 请求头
- 图片: `{ type: "image", source: { type: "base64", media_type, data } }`
- 文件附件: 降级为 `{ type: "text", text: "[File: {fileName}]\n{content}" }`
- Tool 定义: `tools: [{ name, description, input_schema }]`
- Tool 调用: `content[{ type: "tool_use", id, name, input }]`
- Tool 结果: `content[{ type: "tool_result", tool_use_id, content, is_error }]`
- 流式: `content_block_start` (tool_use) + `content_block_delta` (input_json_delta)
- 端点: `{BaseUrl}/v1/messages`

### OpenAI Chat Completions API

- System Prompt: `messages[0]` (role=system)
- 认证: `Authorization: Bearer {ApiKey}`
- 图片: `{ type: "image_url", image_url: { url: "data:...;base64,..." } }`
- 文件附件: 降级为 `{ type: "text", text: "[File: {fileName}]\n{content}" }`
- Tool 定义: `tools: [{ type: "function", function: { name, description, parameters } }]`
- Tool 调用: `message.tool_calls: [{ id, function: { name, arguments } }]`
- Tool 结果: `{ role: "tool", tool_call_id, content }`
- 流式: `delta.tool_calls` 增量累积（按 index 分组）
- 端点: `{BaseUrl}/chat/completions`

## 8. 扩展指南

### 添加新 Provider 协议

1. `ProviderProtocol` 枚举新增类型
2. 创建 `Runtime/Providers/NewProvider/` 目录，实现 `IAIProvider`
3. 在 `ChannelManager.CreateProvider` switch 中添加分支
4. （可选）在 `ChannelEntry` 中添加预设工厂方法

### 添加新内容类型

1. 继承 `AIContent` 基类
2. `AIMessage` 添加工厂方法
3. `ClaudeProvider.ConvertMessages` + `OpenAIProvider.ConvertMessages` 处理序列化
4. `TokenEstimator.EstimateContent` 添加估算分支
5. （如需 Editor 附件）`ChatAttachmentType` 新增枚举 → `BuildAIMessages` 转换 → `InputArea` 拖拽/选择 → `ChatArea` 渲染

### 创建自定义 Tool

```csharp
[UniAITool(
    Name = "my_tool",
    Group = ToolGroups.Core,
    Description = "Demo. Actions: 'greet'.",
    Actions = new[] { "greet" })]
internal static class MyTool
{
    public static UniTask<object> HandleAsync(JObject args, CancellationToken ct)
    {
        var action = (string)args["action"];
        return action switch
        {
            "greet" => UniTask.FromResult<object>(
                ToolResponse.Success(new { msg = $"Hello {args["name"]}" })),
            _ => UniTask.FromResult<object>(ToolResponse.Error($"Unknown action '{action}'."))
        };
    }

    public class GreetArgs
    {
        [ToolParam(Description = "Target name.")]
        public string Name;
    }
}
```

启动时 `UniAIToolRegistry` 自动反射注册，无需创建 SO、无需改 AgentDefinition。

### 创建自定义 Agent

1. Project → Create > UniAI > Agent Definition
2. 配置 `AgentName` / `SystemPrompt` / `Temperature` / `MaxTokens` / `MaxTurns`
3. 在 Inspector 的 Tool Groups 中勾选分组（core/scene/asset/editor/testing/runtime/generate）
4. （可选）MCP Servers 列表拖入 `McpServerConfig` SO
5. 自动出现在对话窗口 Agent 下拉菜单

### 接入 MCP Server

1. Project → Create > UniAI > MCP Server Config
2. Stdio: `Command` + `Arguments` + 环境变量；HTTP: `Base URL` + Headers
3. Inspector「测试连接」验证
4. 拖入 `AgentDefinition.McpServers`

### 添加 RAG 上下文

实现 `IContextProvider` 接口，注册到 `ContextPipeline.AddProvider()`。`Relevance > 0.3` 时内容被注入。

## 9. 开发注意事项

- **API Key 安全**: `ChannelEntry.GetEffectiveApiKey()` 管理环境变量优先级。`AILogger` 自动脱敏（仅显示前 8 位）
- **配置拆分**: 运行时 `UniAISettings` SO (`Resources/UniAI/`) + 编辑器 `EditorPreferences` (`Library/`)
- **JSON 序列化**: Newtonsoft.Json，`NullValueHandling.Ignore`
- **SSE 流式**: `SSEDownloadHandler` → UniTask `Channel<string>` 生产者-消费者模式
- **取消支持**: 所有异步方法接受 `CancellationToken`
- **会话持久化**: `FileChatHistoryStorage`，上限 50 个会话
- **ChatOrchestrator**: Runtime 自足，Editor 通过 `StreamingController` 注入 `GuardFactory`(EditorAgentGuard) + `ContextCollector`
- **EditorAgentGuard**: Tool 执行期间锁定 AssetDatabase，防止写入文件时重编译
- **MCP 超时层次**: `InitTimeoutSeconds`(30s) 包裹全流程，`ToolCallTimeoutSeconds`(60s) 包裹单次调用，底层 HTTP socket 超时复用全局 `TimeoutSeconds`(60s)
- **MCP Tool 命名冲突**: 本地 `[UniAITool]` 优先，同名 MCP Tool 被丢弃并 Warning
- **Stdio 平台限制**: `StdioMcpTransport` 仅 `UNITY_EDITOR || UNITY_STANDALONE`
- **ModelRegistry 查找优先级**: 用户 `CustomModels` > 内置预设 > 前缀兜底表 > 默认 8192
- **多模态附件**: `ChatAttachment` 随 `ChatMessage` 持久化。Image=base64+MediaType，File=文本内容。`BuildAIMessages()` 自动转 `AIContent` 子类
