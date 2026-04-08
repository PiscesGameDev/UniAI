# UniAI - AI Assistant Guide

> 本文档帮助 AI 助手快速理解 UniAI 框架的架构和开发规范。

**包名**: com.uniai.core
**版本**: 0.0.1
**Unity**: 2022.3+
**许可证**: MIT

---

## 1. 框架定位

UniAI 是 Unity 与 AI 交互的中间件，向下对接多种 AI Provider，向上为 Unity Editor 和游戏 Runtime 提供统一的 AI 调用能力。核心设计原则：

- **统一抽象**: 所有 Provider 共享 `AIRequest/AIResponse/AIStreamChunk` 模型
- **双协议覆盖**: Claude Messages API + OpenAI Chat Completions API，兼容所有 OpenAI 兼容接口
- **双运行器架构**: `ChatRunner`（纯对话）与 `AIAgentRunner`（Tool 循环）独立实现，共享 `IConversationRunner` 接口
- **Tool Use**: 支持 AI 调用开发者定义的本地工具（ScriptableObject 扩展）
- **MCP Client**: 连接外部 MCP Server（Stdio / Streamable HTTP），动态注入 Tools 与 Resources
- **上下文窗口管理**: 自动 token 预估、滑动窗口截断、摘要压缩、RAG 上下文注入
- **零业务耦合**: 纯工具层，不依赖任何业务框架

## 2. 核心架构

```
BuildAIMessages() → 全量消息
        ↓
ContextPipeline.ProcessAsync()（上下文窗口管理）
  1. IContextProvider 注入外部上下文（RAG / MCP Resource）
  2. TokenEstimator 估算总 token
  3. 超限时 → 摘要压缩旧消息 / 截断
        ↓
AIAgentRunner / ChatRunner（对话运行器）
  ├── 本地 AIToolAsset                   ← 内置工具
  └── McpClientManager（Tools 动态合并）   ← MCP 外部工具
        ↓
AIClient（API 入口）
        ↓
IAIProvider 接口
   ├── ClaudeProvider    → Claude Messages API (tool_use/tool_result)
   └── OpenAIProvider    → OpenAI Chat Completions API (function_calling/tool_calls)
         ↓
AIHttpClient（HTTP 层）
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

### 3.1 Runtime - 核心

| 类 | 路径 | 职责 |
|----|------|------|
| `AIClient` | `Runtime/Core/AIClient.cs` | 框架唯一入口，封装 Provider 调用 |
| `AIConfig` | `Runtime/Core/AIConfig.cs` | 配置模型（Provider 列表 + 通用设置 + 上下文窗口配置） |
| `UniAISettings` | `Runtime/Core/UniAISettings.cs` | 运行时 ScriptableObject 配置，提供单例访问 |
| `IConversationRunner` | `Runtime/Core/IConversationRunner.cs` | 对话运行器抽象接口（`RunAsync` + `RunStreamAsync`） |
| `ChatRunner` | `Runtime/Core/ChatRunner.cs` | 纯 Chat 运行器，直接桥接 AIClient（无 Tool 循环） |
| `ChannelEntry` | `Runtime/Core/AIConfig.cs` | 单个渠道配置（含 Enabled 开关） |
| `ChannelPresets` | `Runtime/Core/AIConfig.cs` | 内置预设（Claude/OpenAI/Gemini/DeepSeek） |
| `ModelListService` | `Runtime/Core/ModelListService.cs` | 从 Provider API 获取可用模型列表（支持 OpenAI + Claude 分页） |
| `TimeoutHelper` | `Runtime/Core/TimeoutHelper.cs` | 通用超时包装器，`WithTimeout(action, seconds, ct)` 基于链接 CTS + CancelAfter |
| `AILogger` | `Runtime/Core/AILogger.cs` | 内部日志，支持级别控制和 API Key 脱敏 |

### 3.2 Runtime - 模型

| 类 | 路径 | 职责 |
|----|------|------|
| `AIRequest` | `Runtime/Models/AIRequest.cs` | 统一请求：SystemPrompt + Messages + Model + MaxTokens + Temperature |
| `AIResponse` | `Runtime/Models/AIResponse.cs` | 统一响应：IsSuccess + Text + Error + Usage + StopReason + RawResponse |
| `AIMessage` | `Runtime/Models/AIMessage.cs` | 消息：Role (User/Assistant) + Contents 列表 |
| `AIContent` | `Runtime/Models/AIContent.cs` | 内容块基类，子类: `AITextContent`, `AIImageContent`, `AIToolUseContent`, `AIToolResultContent` |
| `AITool` | `Runtime/Models/AITool.cs` | Tool 定义（Name + Description + ParametersSchema）+ `AIToolCall`（Id + Name + Arguments） |
| `AIStreamChunk` | `Runtime/Models/AIStreamChunk.cs` | 流式响应块：DeltaText + IsComplete + Usage + ToolCall |
| `AIResponseFormat` | `Runtime/Models/AIResponseFormat.cs` | 结构化响应格式声明（JSON Schema） |
| `TokenUsage` | `Runtime/Models/AIResponse.cs:58` | Token 用量：InputTokens + OutputTokens |

### 3.3 Runtime - Provider 实现

| 类 | 路径 | 协议 |
|----|------|------|
| `IAIProvider` | `Runtime/Providers/IAIProvider.cs` | Provider 接口：`SendAsync` + `StreamAsync` |
| `ProviderBase` | `Runtime/Providers/ProviderBase.cs` | Provider 抽象基类：SendAsync 模板方法 + ParseError + SerializerSettings |
| `FallbackProvider` | `Runtime/Providers/FallbackProvider.cs` | 多渠道故障转移包装，依次尝试直到成功 |
| `ClaudeProvider` | `Runtime/Providers/Claude/ClaudeProvider.cs` | Claude Messages API (`/v1/messages`) |
| `ClaudeModels` | `Runtime/Providers/Claude/ClaudeModels.cs` | Claude 请求/响应/流式事件的 JSON 模型 |
| `OpenAIProvider` | `Runtime/Providers/OpenAI/OpenAIProvider.cs` | OpenAI Chat Completions API (`/chat/completions`) |
| `OpenAIModels` | `Runtime/Providers/OpenAI/OpenAIModels.cs` | OpenAI 请求/响应/流式事件的 JSON 模型 |

### 3.4 Runtime - HTTP 层

| 类 | 路径 | 职责 |
|----|------|------|
| `AIHttpClient` | `Runtime/Http/AIHttpClient.cs` | 静态 HTTP 客户端，`GetAsync` + `PostJsonAsync` + `PostStreamAsync` |
| `HttpResult` | `Runtime/Http/HttpResult.cs` | HTTP 结果封装 |
| `SSEDownloadHandler` | `Runtime/Http/SSEDownloadHandler.cs` | 继承 `DownloadHandlerScript`，增量接收 SSE 数据写入 Channel |
| `SSEParser` | `Runtime/Http/SSEParser.cs` | SSE 协议解析器，行解析为 `SSEEvent` |

### 3.5 Runtime - Agent 系统

| 类 | 路径 | 职责 |
|----|------|------|
| `AIToolAsset` | `Runtime/Agent/AIToolAsset.cs` | Tool ScriptableObject 基类，子类实现 `ExecuteAsync` |
| `AgentDefinition` | `Runtime/Agent/AgentDefinition.cs` | Agent 配置 SO（名称、SystemPrompt、工具集、温度、MaxTurns） |
| `AIAgentRunner` | `Runtime/Agent/AIAgentRunner.cs` | Agent 运行器，封装 Tool 调用循环（实现 `IConversationRunner`） |
| `AgentEvent` | `Runtime/Agent/AgentEvent.cs` | Agent 流式事件（TextDelta/ToolCallStart/ToolCallResult/TurnComplete/Error） |
| `AgentResult` | `Runtime/Agent/AgentResult.cs` | Agent 非流式运行结果 |

**核心设计**: `ChatRunner` 处理纯对话（无 Tool），`AIAgentRunner` 处理 Agent 对话（含 Tool 循环）。两者共享 `IConversationRunner` 接口，由上层根据是否选择 Agent 决定使用哪个运行器。

### 3.6 Runtime - Chat（会话管理）

| 类 | 路径 | 职责 |
|----|------|------|
| `ChatSession` | `Runtime/Chat/ChatSession.cs` | 会话模型（消息列表 + 摘要状态 + 预估 token）+ `ChatMessage` |
| `ChatHistoryManager` | `Runtime/Chat/ChatHistoryManager.cs` | 会话历史管理器 |
| `IChatHistoryStorage` | `Runtime/Chat/IChatHistoryStorage.cs` | 会话历史存储接口 |

**ChatSession 关键字段**:
- `Messages: List<ChatMessage>` — 对话消息列表
- `SummaryText: string` — 已生成的对话摘要（持久化，用于上下文压缩）
- `SummarizedUpToIndex: int` — 已摘要到的消息索引
- `EstimatedTokens: int` — 当前预估的上下文 token 数（不持久化）

### 3.7 Runtime - Context（上下文窗口管理）

| 类 | 路径 | 职责 |
|----|------|------|
| `ContextPipeline` | `Runtime/Context/ContextPipeline.cs` | 上下文处理管道：RAG 注入 → token 估算 → 摘要/截断 |
| `ContextWindowConfig` | `Runtime/Context/ContextWindowConfig.cs` | 上下文窗口配置（窗口大小、保留条数、摘要阈值） |
| `TokenEstimator` | `Runtime/Context/TokenEstimator.cs` | Token 预估（中英混合字符比例法，CJK 1:1，英文 4:1） |
| `ModelContextLimits` | `Runtime/Context/ModelContextLimits.cs` | 内置模型上下文窗口大小映射表（前缀匹配） |
| `MessageSummarizer` | `Runtime/Context/MessageSummarizer.cs` | 消息摘要器，调用 AI 压缩历史消息为简短摘要 |
| `IContextProvider` | `Runtime/Context/IContextProvider.cs` | RAG 上下文提供者接口 + `ContextResult` |

**ContextPipeline 处理流程**:
1. 遍历 `IContextProvider` 注入外部上下文（RAG）
2. 注入已有摘要（`ChatSession.SummaryText`）
3. `TokenEstimator.EstimateMessages()` 估算总 token
4. `ModelContextLimits.GetContextWindow(modelId)` 获取可用 token 上限
5. 未超限 → 直接返回
6. 超限且启用摘要 → `MessageSummarizer` 压缩最早消息为一条摘要
7. 超限但不启用摘要 → 从最早消息开始移除，保留至少 `MinRecentMessages`

**TokenEstimator 算法**: 中文字符按 1 token/字，英文按 4 字符/token，每条消息额外 4 token（role + 格式开销），图片固定 1000 token。

**ModelContextLimits 内置映射**: Claude → 200K, GPT-4o → 128K, Gemini-2.x → 1M, DeepSeek → 64K, 默认 → 8192。

### 3.8 Runtime - MCP（Model Context Protocol Client）

| 类 | 路径 | 职责 |
|----|------|------|
| `McpServerConfig` | `Runtime/MCP/McpServerConfig.cs` | MCP Server 连接配置 SO（Stdio / HTTP + 环境变量 / Headers），Awake/Reset/OnValidate 自动生成 Id |
| `McpTransportType` | `Runtime/MCP/McpServerConfig.cs` | 传输类型枚举（Stdio / Http） |
| `IMcpTransport` | `Runtime/MCP/IMcpTransport.cs` | 传输层抽象：`ConnectAsync` + `SendRequestAsync` + `SendNotificationAsync` |
| `StdioMcpTransport` | `Runtime/MCP/StdioMcpTransport.cs` | 子进程 stdin/stdout JSON-RPC（line-delimited），仅 Editor/Standalone |
| `HttpMcpTransport` | `Runtime/MCP/HttpMcpTransport.cs` | Streamable HTTP 传输，复用 `AIHttpClient`，socket 超时取 `UniAISettings.General.TimeoutSeconds` |
| `McpTransportFactory` | `Runtime/MCP/McpTransportFactory.cs` | 根据 `McpServerConfig.TransportType` 创建对应 Transport |
| `McpClient` | `Runtime/MCP/McpClient.cs` | 单个 Server 连接：initialize → notifications/initialized → tools/list → resources/list |
| `McpClientManager` | `Runtime/MCP/McpClientManager.cs` | 聚合多个 `McpClient`，统一 `GetAllTools/CallToolAsync/GetResourceProviders`，提供静态 `TestConnectionAsync` |
| `McpResourceProvider` | `Runtime/MCP/McpResourceProvider.cs` | MCP Resource → `IContextProvider` 适配器，基于关键词匹配的相关度过滤 |
| `McpConstants` | `Runtime/MCP/McpConstants.cs` | `McpMethods` 方法名常量 + `McpContentTypes` 内容类型常量 |
| `McpModels` | `Runtime/MCP/McpModels.cs` | JSON-RPC 2.0 消息 + MCP 协议数据模型（Tool/Resource/Content 等） |

**核心设计**:
- **双路由合并**: `AIAgentRunner.InitializeMcpAsync()` 在 Agent 启动时连接 `AgentDefinition.McpServers`，将 MCP Tools 合并到 `_toolDefs`。`ExecuteToolAsync` 先查本地 `_toolMap`，再委托 `McpClientManager.CallToolAsync` — 本地 Tool 同名时优先、MCP Tool 被遮蔽并告警。
- **懒初始化 + 并发共享**: `_mcpInitTask` 字段持有飞行中的初始化 Task，多个并发调用共享同一次初始化；失败清空以允许重试。
- **超时层次**: `McpRuntimeConfig.InitTimeoutSeconds` 包裹 connect+initialize+list 全流程，`ToolCallTimeoutSeconds` 包裹单次 tools/call，底层 HTTP socket 超时复用 UniAI 全局 `TimeoutSeconds`。所有超时通过 `TimeoutHelper.WithTimeout` 实现。
- **线程模型**: `McpClientManager` 实例方法非线程安全，需在单一主线程（UniTask 调度上下文）串行调用。
- **协议版本**: MCP `2024-11-05`，客户端声明 `tools` + `resources` 能力。

**MCP Resource 注入相关度**: `McpResourceProvider.ComputeRelevance(query)` 基于 Name/Uri/Description 的关键词命中打分，≤ 0.3 的资源不会被 `ContextPipeline` 读取，避免无用的 `resources/read`。

### 3.9 Editor - 统一管理窗口

| 类 | 路径 | 职责 |
|----|------|------|
| `UniAIManagerWindow` | `Editor/Setting/UniAIManagerWindow.cs` | 统一管理窗口（左侧图标导航栏 + 右侧 Tab 内容） |
| `ManagerTab` | `Editor/Setting/ManagerTab.cs` | Tab 页面抽象基类 |
| `ChannelTab` | `Editor/Setting/ChannelTab.cs` | 渠道管理 Tab（渠道列表 + 详情，Enabled 开关，获取模型列表） |
| `AgentTab` | `Editor/Setting/AgentTab.cs` | Agent 管理 Tab |
| `McpTab` | `Editor/MCP/McpTab.cs` | MCP Server 管理 Tab（列表 + 详情 + 连接测试） |
| `SettingsTab` | `Editor/Setting/SettingsTab.cs` | 设置 Tab（运行时 + 上下文窗口 + 编辑器偏好 + Tool + MCP 统一节） |
| `AIConfigManager` | `Editor/Setting/AIConfigManager.cs` | 配置持久化（读写 UniAISettings SO + EditorPreferences，环境变量覆盖） |
| `EditorPreferences` | `Editor/Setting/EditorPreferences.cs` | ScriptableSingleton，编辑器偏好持久化 + 环境变量静态映射 |

### 3.10 Editor - 对话窗口

| 类 | 路径 | 职责 |
|----|------|------|
| `AIChatWindow` | `Editor/Chat/AIChatWindow.cs` | 对话窗口主类（partial class，状态 + 生命周期），EditorWindow 纯 UI 壳 |
| `ChatWindowController` | `Editor/Chat/ChatWindowController.cs` | 业务控制器：会话/Client/Runner/ContextPipeline/McpClientManager 全生命周期管理，通过事件与 UI 通信 |
| `AIChatWindow.Streaming` | `Editor/Chat/AIChatWindow.Streaming.cs` | 流式响应处理（含 ContextPipeline 集成） |
| `AIChatWindow.Session` | `Editor/Chat/AIChatWindow.Session.cs` | 会话管理 + Client/Runner/ContextPipeline 初始化 |
| `AIChatWindow.ChatArea` | `Editor/Chat/AIChatWindow.ChatArea.cs` | 消息气泡渲染 |
| `AIChatWindow.InputArea` | `Editor/Chat/AIChatWindow.InputArea.cs` | 输入区域 |
| `AIChatWindow.Sidebar` | `Editor/Chat/AIChatWindow.Sidebar.cs` | 侧边栏（会话列表） |
| `AIChatWindow.Toolbar` | `Editor/Chat/AIChatWindow.Toolbar.cs` | 工具栏（模型选择、Agent 选择） |
| `AIChatWindow.Styles` | `Editor/Chat/AIChatWindow.Styles.cs` | 样式定义 |
| `EditorChatHistoryStorage` | `Editor/Chat/EditorChatHistoryStorage.cs` | 编辑器环境的 `IChatHistoryStorage` 实现（`ProjectSettings/UniAI/History/`） |
| `ContextCollector` | `Editor/Chat/ContextCollector.cs` | Unity 上下文采集：Selection / Console / Project |
| `MarkdownRenderer` | `Editor/Chat/MarkdownRenderer.cs` | Markdown → IMGUI 渲染器 |

### 3.11 Editor - Agent / MCP / Tools

| 类 | 路径 | 职责 |
|----|------|------|
| `AgentManager` | `Editor/Agent/AgentManager.cs` | Agent 资产扫描 + 内置默认 Agent（无 Tool，通用聊天助手） |
| `AgentDefinitionEditor` | `Editor/Agent/AgentDefinitionEditor.cs` | Agent 自定义 Inspector（含 MCP Servers 列表绘制） |
| `EditorAgentGuard` | `Editor/Agent/EditorAgentGuard.cs` | Agent 运行时 AssetDatabase 保护（防止 Tool 修改文件时触发重编译） |
| `McpServerConfigEditor` | `Editor/MCP/McpServerConfigEditor.cs` | `McpServerConfig` 自定义 Inspector，传输类型切换 + 环境变量/Headers ReorderableList + 「测试连接」按钮 |
| `McpTab` | `Editor/MCP/McpTab.cs` | 管理窗口的 MCP Server Tab，创建/选择/删除 + 详情内嵌 Inspector |
| `EditorGUIHelper` | `Editor/EditorGUIHelper.cs` | 编辑器 GUI 工具（Section 绘制、颜色常量） |

**内置 Editor Tools**（`Editor/Tools/`）:

| Tool | 职责 |
|------|------|
| `ReadFileTool` | 读取指定路径的文件内容 |
| `WriteFileTool` | 写入文件内容 |
| `ListFilesTool` | 列出目录下的文件 |
| `SearchFilesTool` | 搜索文件内容 |
| `RunTestsTool` | 运行 Unity 测试 |
| `RuntimeQueryTool` | 查询运行时状态 |

## 4. 数据流

### 4.1 完整响应

```
用户代码 → AIClient.SendAsync(AIRequest)
  → IAIProvider.SendAsync
    → AIHttpClient.PostJsonAsync (UnityWebRequest)
      → 等待完整响应
    → Provider 解析 JSON → AIResponse
  → 返回 AIResponse
```

### 4.2 流式响应

```
用户代码 → AIClient.StreamAsync(AIRequest)
  → IAIProvider.StreamAsync
    → AIHttpClient.PostStreamAsync
      → SSEDownloadHandler.ReceiveData (增量回调)
        → Channel<string> 写入行
      → SSEParser.ParseLine → SSEEvent
    → Provider 解析事件 → AIStreamChunk
  → yield return AIStreamChunk (IUniTaskAsyncEnumerable)
```

### 4.3 Editor 对话窗口（Agent 模式）

```
用户输入 → SendMessage()
  → 添加 ChatMessage(User) 到 ChatSession
  → StreamResponseAsync()
    → BuildAIMessages() 构建全量 AIMessage 列表
    → ContextCollector.Collect() 注入 Unity 上下文
    → ContextPipeline.ProcessAsync() 上下文窗口管理
      1. IContextProvider 注入 RAG 上下文
      2. 注入已有摘要（ChatSession.SummaryText）
      3. TokenEstimator 估算 → 超限则摘要/截断
    → IConversationRunner.RunStreamAsync() 驱动对话
      → AgentEvent.TextDelta → 增量更新 ChatMessage(Assistant).Content
      → AgentEvent.ToolCallStart → 添加 ToolCall ChatMessage（UI 展示）
      → AgentEvent.ToolCallResult → 更新 ToolCall 结果
      → AgentEvent.TurnComplete → 累计 Token 用量
      → (若有 Tool 调用，自动进入下一轮)
    → MarkdownRenderer.Draw() 渲染
  → ChatHistory.Save() 持久化
  → GenerateTitleAsync() 自动生成标题
```

## 5. 配置体系

### 5.1 配置架构

```
UniAISettings (ScriptableObject, Runtime)
├── Providers: List<ChannelEntry>       # 渠道列表
│   ├── Id, Name, Protocol, Enabled     # 标识 + 启用开关
│   ├── ApiKey, BaseUrl                 # 连接参数
│   ├── Models: List<string>            # 该渠道支持的模型列表
│   └── ApiVersion                      # Claude 专用
├── ActiveProviderId: string            # 当前激活的 Provider
└── General: GeneralConfig              # 通用设置
    ├── TimeoutSeconds: int (60)        # 全局 HTTP socket 超时（也用作 MCP HTTP 底层兜底）
    ├── LogLevel: AILogLevel (Info)
    ├── ContextWindow: ContextWindowConfig  # 上下文窗口管理
    │   ├── Enabled: bool (true)
    │   ├── MaxContextTokens: int (0=自动，模型的80%)
    │   ├── ReservedOutputTokens: int (4096)
    │   ├── MinRecentMessages: int (4)
    │   ├── EnableSummary: bool (true)
    │   └── SummaryMaxTokens: int (512)
    └── Mcp: McpRuntimeConfig            # MCP 连接参数
        ├── InitTimeoutSeconds: int (30) # connect+initialize+list 全流程超时
        └── ToolCallTimeoutSeconds: int (0) # 单次 tools/call 超时，0=不限制

EditorPreferences (ScriptableSingleton, Editor Only, 持久化到 Library/)
├── LastSelectedModelId              # 上次选择的模型
├── ShowSidebar                      # 聊天窗口侧边栏状态
├── MaxHistorySessions               # 历史会话上限
├── DefaultContextSlots              # 默认上下文槽位
├── AgentDirectory                   # Agent 创建目录
├── UserAvatar / AiAvatar            # 自定义头像
├── ToolTimeout                      # Tool 执行超时
├── ToolMaxOutputChars               # Tool 最大输出字符数
├── SearchMaxMatches                 # 搜索最大匹配数
├── McpAutoConnect (true)            # 切换 Agent 时自动连接其 MCP Server
├── McpResourceInjection (true)      # 自动将 MCP Resource 注入对话上下文
├── McpServerDirectory               # MCP Server 资产创建目录
└── 环境变量映射（静态，按预设 ID → 环境变量名）
```

### 5.2 配置优先级

```
环境变量 (EditorPreferences.EnvVarName)  ← 最高优先级，覆盖 API Key（仅 Editor）
        ↓
UniAISettings.asset (Resources/UniAI/)   ← 运行时 ScriptableObject
        ↓
自动创建默认配置                          ← 首次使用时
```

### 5.3 内置预设

| Id | 名称 | 协议 | 默认 BaseUrl | 默认 Models |
|----|------|------|-------------|------------|
| `claude` | Claude | Claude | `https://api.anthropic.com` | claude-sonnet-4-20250514, claude-opus-4-6 |
| `openai` | OpenAI | OpenAI | `https://api.openai.com/v1` | gpt-4o, gpt-4o-mini, o1 |
| `gemini` | Gemini | OpenAI | `https://generativelanguage.googleapis.com/v1beta/openai` | gemini-2.0-flash, gemini-2.5-pro |
| `deepseek` | DeepSeek | OpenAI | `https://api.deepseek.com/v1` | deepseek-chat, deepseek-reasoner |

## 6. Assembly 结构

| Assembly | 路径 | 平台 | 依赖 |
|----------|------|------|------|
| `UniAI` | `Runtime/UniAI.asmdef` | 全平台 | UniTask, UniTask.Linq |
| `UniAI.Editor` | `Editor/UniAI.Editor.asmdef` | Editor | UniAI, UniTask, UniTask.Linq |

`InternalsVisibleTo("UniAI.Editor")` 允许 Editor 代码访问 Runtime 的 internal 类型。

## 7. 协议差异处理

### Claude Messages API

- System Prompt 通过 `system` 顶级字段传递
- 认证: `x-api-key` + `anthropic-version` 请求头
- 图片: `{ type: "image", source: { type: "base64", media_type, data } }`
- Tool 定义: `tools: [{ name, description, input_schema }]`
- Tool 调用: 响应中 `content` 包含 `{ type: "tool_use", id, name, input }` 块
- Tool 结果: 请求中 `content` 包含 `{ type: "tool_result", tool_use_id, content, is_error }` 块
- 流式事件: `content_block_start` (tool_use) + `content_block_delta` (input_json_delta) 累积 arguments
- API 端点: `{BaseUrl}/v1/messages`

### OpenAI Chat Completions API

- System Prompt 作为 `messages[0]` (role=system)
- 认证: `Authorization: Bearer {ApiKey}`
- 图片: `{ type: "image_url", image_url: { url: "data:...;base64,..." } }`
- Tool 定义: `tools: [{ type: "function", function: { name, description, parameters } }]`
- Tool 调用: 响应中 `message.tool_calls: [{ id, type: "function", function: { name, arguments } }]`
- Tool 结果: `{ role: "tool", tool_call_id, content }` 消息
- 流式事件: `delta.tool_calls` 增量累积（按 index 分组）
- API 端点: `{BaseUrl}/chat/completions`

## 8. 扩展指南

### 添加新 Provider 协议

1. 在 `ProviderProtocol` 枚举中添加新协议类型
2. 创建 `Runtime/Providers/NewProvider/` 目录
3. 实现 `IAIProvider` 接口（`SendAsync` + `StreamAsync`）
4. 创建对应的 Request/Response JSON 模型
5. 在 `AIClient.Create(ChannelEntry, GeneralConfig)` 的 switch 中添加分支
6. （可选）在 `ChannelPresets` 中添加预设

### 添加新内容类型

1. 继承 `AIContent` 基类
2. 在 `AIMessage` 中添加快捷工厂方法
3. 在 `ClaudeProvider.BuildRequestBody` 和 `OpenAIProvider.BuildRequestBody` 中处理新类型的序列化

### 创建自定义 Tool

1. 继承 `AIToolAsset`，实现 `ExecuteAsync(string arguments, CancellationToken ct)`
2. 添加 `[CreateAssetMenu]` 特性
3. 在 Inspector 中填写 `ToolName`、`Description`、`ParametersSchema`（JSON Schema）
4. 创建 SO 实例并添加到 `AgentDefinition.Tools` 列表

```csharp
[CreateAssetMenu(menuName = "UniAI/Tools/Read File")]
public class ReadFileTool : AIToolAsset
{
    public override async UniTask<string> ExecuteAsync(string arguments, CancellationToken ct)
    {
        var args = JsonConvert.DeserializeObject<ReadFileArgs>(arguments);
        return await File.ReadAllTextAsync(args.Path, ct);
    }
}
```

### 创建自定义 Agent

1. 在 Project 中创建: Create > UniAI > Agent Definition
2. 配置 AgentName、SystemPrompt、Temperature、MaxTokens、MaxTurns
3. 将自定义 Tool SO 拖入 Tools 列表
4. （可选）将 MCP Server Config 拖入 McpServers 列表，Agent 启动时自动连接并合并远端 Tools/Resources
5. Agent 会自动出现在 AIChatWindow 的 Agent 下拉菜单中

### 接入外部 MCP Server

1. Project 中创建: Create > UniAI > MCP Server Config
2. 选择传输类型：
   - **Stdio**: 填写 `Command`（如 `npx`）+ `Arguments`（如 `-y @modelcontextprotocol/server-filesystem .`）+ 环境变量
   - **HTTP**: 填写 `Base URL`（必须是绝对 http(s) URL）+ 请求头
3. Inspector 右侧「测试连接」按钮验证配置可用性（走 `McpClientManager.TestConnectionAsync`）
4. 将此 SO 拖入 `AgentDefinition.McpServers`
5. 对话启动后，外部 Tools 会自动合并进 Agent 的 Tool 列表；Resources 会按相关度注入上下文

**自定义 Transport**（极少需要）: 实现 `IMcpTransport` 接口 + 在 `McpTransportFactory.Create` 中加分支。

### 添加 RAG 上下文提供者

实现 `IContextProvider` 接口，将外部知识注入对话上下文：

```csharp
public class MyRagProvider : IContextProvider
{
    public async UniTask<ContextResult> RetrieveAsync(string query, CancellationToken ct)
    {
        // 根据 query 检索相关文档
        string relevant = await SearchDocuments(query, ct);
        return new ContextResult
        {
            Content = relevant,
            EstimatedTokens = TokenEstimator.EstimateTokens(relevant),
            Relevance = 0.8f  // > 0.3 才会被注入
        };
    }
}

// 注册到 ContextPipeline
pipeline.AddProvider(new MyRagProvider());
```

## 9. 开发注意事项

- **API Key 安全**: `AIConfigManager.SaveConfig` 会在保存前清除来自环境变量的 Key，避免写入 SO。环境变量名存储在 `EditorPreferences`（Editor-only），不进入运行时资产。日志中 API Key 自动脱敏（仅显示前 8 位）。
- **配置拆分**: 运行时配置存储在 `UniAISettings` ScriptableObject（`Resources/UniAI/`），编辑器偏好基于 `ScriptableSingleton` 自动持久化到 `Library/` 目录。
- **JSON 序列化**: 使用 Newtonsoft.Json（`JsonConvert`），`NullValueHandling.Ignore` 避免发送空字段。
- **SSE 流式**: `SSEDownloadHandler` 通过 UniTask `Channel<string>` 实现生产者-消费者模式，主线程安全。
- **取消支持**: 所有异步方法接受 `CancellationToken`，流式响应可随时取消。
- **会话持久化**: 历史存储在 `ProjectSettings/UniAI/History/`，上限 50 个会话，超出自动删除最旧的。
- **上下文窗口**: `ContextPipeline` 在 `AIChatWindow.Streaming.cs` 的 `StreamResponseAsync()` 中集成，位于 `BuildAIMessages()` 之后、`RunStreamAsync()` 之前。摘要结果存储在 `ChatSession.SummaryText` 中随会话持久化。
- **Token 预估**: `TokenEstimator` 使用字符比例法（非 tiktoken），为轻量估算，误差可接受。Provider 返回的实际 `TokenUsage` 可用于后续校准。
- **EditorAgentGuard**: Agent Tool 执行期间锁定 AssetDatabase 刷新，防止 Tool 写入文件后触发重编译导致状态丢失。
- **MCP 超时层次**: `InitTimeoutSeconds` / `ToolCallTimeoutSeconds` 都由 `TimeoutHelper` 在 orchestration 层包裹，真正截止时间由 CancellationToken 传导；HTTP transport 的 socket 超时只是底层兜底，不应设得比 orchestration 超时更短。
- **MCP 懒初始化**: `AIAgentRunner._mcpInitTask` 存储飞行中的初始化 Task，并发调用共享同一次 await；失败时清空字段允许重试，成功后 `_mcpInitialized = true` 永久缓存。
- **MCP Tool 命名冲突**: 本地 `AIToolAsset` 与 MCP Tool 同名时，本地优先，MCP 版本会被丢弃并打印 `Warning`；跨 Server 的同名 Tool 由后加载者覆盖前者，也会打印 `Warning`。
- **Stdio 平台限制**: `StdioMcpTransport` 仅在 `UNITY_EDITOR || UNITY_STANDALONE` 下可用，移动平台会抛 `PlatformNotSupportedException`。
