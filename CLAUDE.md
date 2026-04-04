# UniAI - AI Assistant Guide

> 本文档帮助 AI 助手快速理解 UniAI 框架的架构和开发规范。

**包名**: com.uniai.core
**版本**: 0.0.1
**Unity**: 2022.3+
**许可证**: MIT

---

## 1. 框架定位

UniAI 是一个轻量级 Unity AI 交互框架，提供统一的多 Provider AI API 调用能力。核心设计原则：

- **统一抽象**: 所有 Provider 共享 `AIRequest/AIResponse/AIStreamChunk` 模型
- **双协议覆盖**: Claude Messages API + OpenAI Chat Completions API，兼容所有 OpenAI 兼容接口
- **Agent 统一通道**: 所有对话统一走 Agent 通道，普通 Chat = 内置默认 Agent（无 Tool，一轮即结束）
- **Tool Use**: 支持 AI 调用开发者定义的工具（ScriptableObject 扩展）
- **零业务耦合**: 纯工具层，不依赖任何业务框架

## 2. 核心架构

```
AIAgentRunner（Agent 入口）
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
```

## 3. 关键类与文件

### 3.1 Runtime - 核心

| 类 | 路径 | 职责 |
|----|------|------|
| `AIClient` | `Runtime/Core/AIClient.cs` | 框架唯一入口，封装 Provider 调用 |
| `AIConfig` | `Runtime/Core/AIConfig.cs` | 配置模型（Provider 列表 + 通用设置） |
| `ProviderEntry` | `Runtime/Core/AIConfig.cs:59` | 单个 Provider 配置条目 |
| `ProviderPresets` | `Runtime/Core/AIConfig.cs:111` | 内置预设（Claude/OpenAI/Gemini/DeepSeek） |
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
| `AIAgentRunner` | `Runtime/Agent/AIAgentRunner.cs` | Agent 运行器，封装 Tool 调用循环 |
| `AgentEvent` | `Runtime/Agent/AgentEvent.cs` | Agent 流式事件（TextDelta/ToolCallStart/ToolCallResult/TurnComplete/Error） |
| `AgentResult` | `Runtime/Agent/AgentResult.cs` | Agent 非流式运行结果 |

**核心设计**: 普通 Chat = 默认 Agent（无 Tool，MaxTurns=1）。所有对话统一走 `AIAgentRunner`，无 Tool 时循环只跑一轮。

### 3.6 Editor

| 类 | 路径 | 职责 |
|----|------|------|
| `AIConfigManager` | `Editor/AIConfigManager.cs` | 配置持久化（优先级: 环境变量 > UserSettings JSON > EditorPrefs） |
| `AISettingsWindow` | `Editor/AISettingsWindow.cs` | 渠道管理窗口（双面板: 渠道列表 + 详情，支持获取模型列表） |
| `ModelListService` | `Editor/ModelListService.cs` | 从 Provider API 获取可用模型列表（支持 OpenAI + Claude 分页） |
| `AgentManager` | `Editor/AgentManager.cs` | Agent 资产扫描 + 内置默认 Agent（无 Tool，通用聊天助手） |
| `AIChatWindow` | `Editor/Chat/AIChatWindow*.cs` | AI 对话窗口（partial class，支持 Agent 选择 + Tool 调用渲染） |
| `ChatSession` | `Editor/Chat/ChatSession.cs` | 会话模型 + `ChatMessage` |
| `ChatHistory` | `Editor/Chat/ChatHistory.cs` | 会话历史持久化（`ProjectSettings/UniAI/History/`） |
| `ContextCollector` | `Editor/Chat/ContextCollector.cs` | Unity 上下文采集：Selection / Console / Project |
| `MarkdownRenderer` | `Editor/Chat/MarkdownRenderer.cs` | Markdown → IMGUI 渲染器 |

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
    → ContextCollector.Collect() 注入 Unity 上下文
    → AIAgentRunner.RunStreamAsync() 驱动 Agent Loop
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

### 5.1 配置优先级

```
环境变量 (ANTHROPIC_API_KEY 等)   ← 最高优先级，覆盖 API Key
        ↓
UserSettings/UniAISettings.json   ← 项目级配置文件（不入 Git）
        ↓
EditorPrefs (UniAI_Config)        ← 编辑器级备份
```

### 5.2 AIConfig 结构

```
AIConfig
├── Providers: List<ProviderEntry>   # 渠道列表
│   ├── Id, Name, Protocol           # 标识（Protocol 决定 API 调用方式）
│   ├── ApiKey, BaseUrl              # 连接参数
│   ├── Models: List<string>         # 该渠道支持的模型列表
│   ├── ApiVersion                   # Claude 专用
│   ├── IconName, EnvVarName         # UI 和环境变量
├── ActiveProviderId: string         # 当前激活的 Provider
└── General: GeneralConfig           # 通用设置
    ├── TimeoutSeconds: int (60)
    └── LogLevel: AILogLevel (Info)
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
5. 在 `AIClient.Create(ProviderEntry, GeneralConfig)` 的 switch 中添加分支
6. （可选）在 `ProviderPresets` 中添加预设

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
4. Agent 会自动出现在 AIChatWindow 的 Agent 下拉菜单中

## 9. 开发注意事项

- **API Key 安全**: `AIConfigManager.SaveConfig` 会在保存前清除来自环境变量的 Key，避免写入文件。日志中 API Key 自动脱敏（仅显示前 8 位）。
- **JSON 序列化**: 使用 Newtonsoft.Json（`JsonConvert`），`NullValueHandling.Ignore` 避免发送空字段。
- **SSE 流式**: `SSEDownloadHandler` 通过 UniTask `Channel<string>` 实现生产者-消费者模式，主线程安全。
- **取消支持**: 所有异步方法接受 `CancellationToken`，流式响应可随时取消。
- **会话持久化**: 历史存储在 `ProjectSettings/UniAI/History/`，上限 50 个会话，超出自动删除最旧的。
