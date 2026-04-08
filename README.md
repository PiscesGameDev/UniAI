# UniAI

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

Unity AI 交互框架，支持多 Provider（Claude、OpenAI、Gemini、DeepSeek 等）、多模态输入、Tool Use、Agent 系统、上下文窗口管理和 SSE 流式响应。

## 特性

- **多 Provider 支持**: 内置 Claude (Messages API) 和 OpenAI (Chat Completions API) 两种协议，兼容所有 OpenAI 兼容接口（Gemini、DeepSeek 等）
- **SSE 流式响应**: 基于 `UnityWebRequest` + `DownloadHandlerScript` 实现真正的流式接收
- **多模态输入**: 支持文本和图片（base64）混合消息
- **Tool Use**: 支持 AI 调用开发者自定义工具（Claude tool_use + OpenAI function_calling）
- **Agent 系统**: 自动多轮 Tool 调用循环编排，ScriptableObject 配置 Agent
- **MCP Client**: 作为标准 MCP 客户端连接外部 Server（Stdio 子进程 / Streamable HTTP），动态合并 Tools 与 Resources 到 Agent
- **上下文窗口管理**: 自动 token 预估、滑动窗口截断、AI 摘要压缩、RAG 上下文注入接口
- **统一抽象**: 一套 `AIRequest/AIResponse` 模型覆盖所有 Provider
- **Editor 工具链**: 统一管理窗口（渠道、Agent、MCP、设置）+ AI 对话窗口，开箱即用
- **零 GC 异步**: 基于 UniTask 的异步实现

## 定位

**UniAI 是 Unity 与 AI 交互的中间件** — 向下对接多种 AI Provider，向上为 Unity Editor 和游戏 Runtime 提供统一的 AI 调用能力。

| | 网页 AI 聊天 | 通用 AI Agent | AI 编码工具 | **UniAI** |
|--|---|---|---|---|
| 代表 | ChatGPT / Claude Web | OpenClaw | Codex / Claude Code | — |
| 核心价值 | 和 AI 对话 | AI 操控电脑干活 | AI 帮你写代码 | **让 Unity 项目具备 AI 能力** |
| 可编程性 | 无 | 自然语言指令 | 自然语言指令 | **C# API（嵌入游戏逻辑）** |
| 游戏 Runtime | 不支持 | 不支持 | 不支持 | **核心能力** |
| Unity 上下文 | 无 | 无 | 无 | 场景 / 组件 / Console / 工程资源 |
| 产出 | 对话内容 | 自动化任务结果 | 代码 / PR | **具备 AI 能力的游戏** |


## 环境要求

| 依赖 | 版本 |
|------|------|
| Unity | 2022.3+ |
| [UniTask](https://github.com/Cysharp/UniTask) | 2.3.3+ |
| Newtonsoft.Json | Unity 内置或手动导入 |

## 安装

### 通过 Git URL 安装（推荐）

1. 打开 Unity Editor，进入 **Window > Package Manager**
2. 点击左上角 **+** 按钮，选择 **Add package from git URL...**
3. 输入以下地址：

```
https://github.com/PiscesGameDev/UniAI.git
```

4. 点击 **Add**，等待导入完成

> 如需锁定特定版本，可在 URL 后追加 `#` + 版本标签，例如：
> ```
> https://github.com/PiscesGameDev/UniAI.git#v0.0.1
> ```

### 手动安装

将仓库克隆或下载到项目的 `Assets/` 目录下：

```bash
git clone https://github.com/PiscesGameDev/UniAI.git Assets/UniAI
```

### 依赖安装

UniAI 依赖 UniTask，请确保项目中已安装。通过 Package Manager 的 Git URL 安装：

```
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

## 快速开始

### 1. 配置 Provider

**方式 A: Editor 配置窗口** — 菜单 `Window > UniAI > Manager`，切换到渠道 Tab，填写 API Key。

**方式 B: 环境变量** — 设置 `ANTHROPIC_API_KEY` / `OPENAI_API_KEY` / `GEMINI_API_KEY` / `DEEPSEEK_API_KEY`，框架自动读取（优先级高于配置文件）。

**方式 C: 代码配置**

```csharp
var client = AIClient.Create(new ChannelEntry
{
    Protocol = ProviderProtocol.Claude,
    ApiKey = "your-api-key",
    BaseUrl = "https://api.anthropic.com",
    Models = new List<string> { "claude-sonnet-4-20250514" }
});
```

### 2. 基础用法

```csharp
using UniAI;

var config = AIConfigManager.LoadConfig();
var client = AIClient.Create(config);

// 完整响应
var response = await client.SendAsync(new AIRequest
{
    SystemPrompt = "你是一个 Unity 游戏开发助手。",
    Messages = { AIMessage.User("如何优化 Draw Call？") },
});
if (response.IsSuccess) Debug.Log(response.Text);

// 流式响应
await foreach (var chunk in client.StreamAsync(request))
{
    if (!string.IsNullOrEmpty(chunk.DeltaText))
        Debug.Log(chunk.DeltaText);
}

// 多模态（图文）
var imgRequest = new AIRequest
{
    Messages = { AIMessage.UserWithImage("这张截图有什么问题？", imageBytes, "image/png") }
};

// 多轮对话
var multiRequest = new AIRequest
{
    Messages =
    {
        AIMessage.User("什么是对象池？"),
        AIMessage.Assistant("对象池是一种复用对象的设计模式。"),
        AIMessage.User("在 Unity 中如何实现？")
    }
};
```

### 3. Agent 系统

Agent 是 Tool 调用循环的编排器。普通 Chat = 默认 Agent（无 Tool，一轮即结束）。

**创建自定义 Tool（ScriptableObject）:**

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

**创建 Agent:** Project 中 Create > UniAI > Agent Definition，配置 SystemPrompt、工具列表、MaxTurns，自动出现在对话窗口的 Agent 下拉菜单中。

**使用 Agent（代码方式）:**

```csharp
var runner = new AIAgentRunner(client, agentDefinition);

// 流式
await foreach (var evt in runner.RunStreamAsync(messages))
{
    switch (evt.Type)
    {
        case AgentEventType.TextDelta:       Debug.Log(evt.Text); break;
        case AgentEventType.ToolCallStart:   Debug.Log($"调用: {evt.ToolCall.Name}"); break;
        case AgentEventType.ToolCallResult:  Debug.Log($"结果: {evt.ToolResult}"); break;
    }
}
```

### 4. 接入 MCP Server（可选）

让 Agent 连接外部 MCP Server 动态获得 Tools 和 Resources：

1. Project 中创建 Server 配置：Create > UniAI > MCP Server Config
2. 选择传输类型：
   - **Stdio**（本地子进程）：`Command = npx`，`Arguments = -y @modelcontextprotocol/server-filesystem .`
   - **HTTP**（远程服务）：填写绝对 `Base URL`
3. 点击 Inspector 的「测试连接」验证配置可用
4. 将此 SO 拖到 `AgentDefinition.McpServers` 列表

Agent 启动时自动连接所有启用的 Server，`tools/list` 和 `resources/list` 的结果会合并到 Agent 可用工具中。本地 `AIToolAsset` 同名时本地优先。

```csharp
// 代码方式测试 MCP Server
var config = AssetDatabase.LoadAssetAtPath<McpServerConfig>("Assets/MyMcp.asset");
var result = await McpClientManager.TestConnectionAsync(config, initTimeoutSeconds: 30);
Debug.Log($"Success: {result.Success}, Tools: {result.ToolCount}, Resources: {result.ResourceCount}");
```

MCP 的连接超时与 Tool 调用超时在 `Window > UniAI > Manager → 设置 → MCP 设置` 中统一配置。

### 5. 上下文窗口管理

长对话自动管理，防止超出模型 token 上限。Editor 对话窗口默认启用，也可通过 `Window > UniAI > Manager` → 设置 Tab 调整参数。

```csharp
// 代码方式使用 ContextPipeline
var pipeline = new ContextPipeline(client);
pipeline.AddProvider(new MyRagProvider()); // 可选：RAG 上下文

var processed = await pipeline.ProcessAsync(
    messages, systemPrompt, modelId,
    config.General.ContextWindow, session);
```

核心能力：
- **Token 预估**: `TokenEstimator.EstimateMessages(messages)` — 中英混合字符比例法
- **模型限制查询**: `ModelContextLimits.GetContextWindow("claude-opus-4-6")` → 200000
- **自动摘要**: 超限时用 AI 压缩旧消息为摘要，保留最近 N 条
- **RAG 注入**: 实现 `IContextProvider` 接口，检索结果自动注入消息列表

## Editor 工具

### 统一管理窗口

菜单 `Window > UniAI > Manager` 打开。左侧图标导航栏 + 右侧 Tab 内容。

**渠道 Tab:**
- 动态管理渠道列表（增删、启用/禁用）
- API Key 密码显示/隐藏
- 在线获取模型列表
- 单个/批量模型连接测试
- 支持添加自定义 OpenAI 兼容渠道

**Agent Tab:**
- 查看和管理所有 Agent
- 可视化编辑 Agent 配置（名称、描述、参数、工具、System Prompt、MCP Servers）
- 从窗口直接开启与 Agent 的对话

**MCP Tab:**
- 管理 MCP Server Config 资产（增删、启用/禁用）
- Stdio / Streamable HTTP 两种传输类型
- 环境变量和请求头的 ReorderableList 编辑
- 内嵌「测试连接」按钮，验证握手 + tools/resources 拉取

**设置 Tab:**
- 运行时参数：请求超时、日志级别
- 上下文窗口：启用/禁用、token 限制、摘要压缩配置
- 编辑器参数：侧边栏、默认上下文、历史会话上限、头像自定义
- Tool 设置：执行超时、最大输出字符数、搜索最大匹配数
- MCP 设置：连接/Tool 调用超时、自动连接、Resource 自动注入、Server 资产目录

### AI 对话窗口

菜单 `Window > UniAI > Chat` 打开。

- 多模型切换，支持所有已配置渠道的模型
- Agent 选择：默认助手 + 自定义 Agent
- SSE 流式输出，实时显示
- Tool 调用过程可视化（名称、参数、结果）
- **上下文窗口管理**：长对话自动截断/摘要，防止 token 超限
- Markdown 渲染（标题、代码块、列表、加粗）
- 代码块一键复制
- 会话历史管理（自动持久化）
- 自动生成对话标题
- Unity 上下文注入：选中对象、控制台错误、工程资源
- 快捷操作：解释代码、优化建议、生成注释、修复报错
- Enter 发送，Shift+Enter 换行

## 架构

```
UniAI/
├── Runtime/
│   ├── Core/
│   │   ├── AIClient.cs              # 框架入口
│   │   ├── AIConfig.cs              # 配置模型 + 渠道预设 + ContextWindowConfig + McpRuntimeConfig
│   │   ├── UniAISettings.cs         # 运行时 ScriptableObject 配置
│   │   ├── IConversationRunner.cs   # 对话运行器接口
│   │   ├── ChatRunner.cs            # 纯 Chat 运行器
│   │   ├── ModelListService.cs      # 模型列表查询服务
│   │   ├── TimeoutHelper.cs         # 通用超时包装（链接 CTS + CancelAfter）
│   │   └── AILogger.cs              # 内部日志（API Key 脱敏）
│   ├── Models/
│   │   ├── AIRequest.cs             # 统一请求
│   │   ├── AIResponse.cs            # 统一响应 + TokenUsage
│   │   ├── AIMessage.cs             # 消息（User/Assistant + 内容块）
│   │   ├── AIContent.cs             # 内容块（Text/Image/ToolUse/ToolResult）
│   │   ├── AITool.cs                # Tool 定义 + ToolCall
│   │   ├── AIResponseFormat.cs      # 结构化响应格式（JSON Schema）
│   │   └── AIStreamChunk.cs         # 流式响应块
│   ├── Agent/
│   │   ├── AIToolAsset.cs           # 本地 Tool ScriptableObject 基类
│   │   ├── AgentDefinition.cs       # Agent 配置 SO（含 McpServers 列表）
│   │   ├── AIAgentRunner.cs         # Agent 运行器（本地 Tool + MCP Tool 路由）
│   │   ├── AgentEvent.cs            # Agent 流式事件
│   │   └── AgentResult.cs           # Agent 运行结果
│   ├── Chat/
│   │   ├── ChatSession.cs           # 会话模型（含摘要状态）
│   │   ├── ChatHistoryManager.cs    # 会话历史管理器
│   │   └── IChatHistoryStorage.cs   # 会话存储接口
│   ├── Context/
│   │   ├── ContextPipeline.cs       # 上下文处理管道
│   │   ├── ContextWindowConfig.cs   # 上下文窗口配置
│   │   ├── TokenEstimator.cs        # Token 预估器
│   │   ├── ModelContextLimits.cs    # 模型上下文窗口映射
│   │   ├── MessageSummarizer.cs     # 消息摘要器
│   │   └── IContextProvider.cs      # RAG 上下文提供者接口
│   ├── MCP/
│   │   ├── McpServerConfig.cs       # MCP Server 配置 SO（Stdio / HTTP）
│   │   ├── IMcpTransport.cs         # 传输层抽象接口
│   │   ├── StdioMcpTransport.cs     # 子进程 stdin/stdout（Editor/Standalone）
│   │   ├── HttpMcpTransport.cs      # Streamable HTTP 传输
│   │   ├── McpTransportFactory.cs   # 传输类型工厂
│   │   ├── McpClient.cs             # 单 Server 连接 + 协议握手
│   │   ├── McpClientManager.cs      # 多 Server 管理 + Tool 路由 + TestConnectionAsync
│   │   ├── McpResourceProvider.cs   # Resource → IContextProvider 适配器
│   │   ├── McpConstants.cs          # 方法名 / 内容类型常量
│   │   └── McpModels.cs             # JSON-RPC + MCP 协议数据模型
│   ├── Providers/
│   │   ├── IAIProvider.cs           # Provider 接口
│   │   ├── ProviderBase.cs          # Provider 抽象基类
│   │   ├── FallbackProvider.cs      # 多渠道故障转移
│   │   ├── Claude/                  # Claude Messages API
│   │   └── OpenAI/                  # OpenAI Chat Completions API
│   ├── Http/
│   │   ├── AIHttpClient.cs          # HTTP 客户端（JSON + SSE Stream）
│   │   ├── HttpResult.cs            # HTTP 结果封装
│   │   ├── SSEDownloadHandler.cs    # SSE 增量下载处理器
│   │   └── SSEParser.cs             # SSE 协议解析器
│   └── Assembly/
│       └── AssemblyInfo.cs          # InternalsVisibleTo("UniAI.Editor")
├── Editor/
│   ├── EditorGUIHelper.cs           # 编辑器 GUI 工具
│   ├── Setting/
│   │   ├── UniAIManagerWindow.cs    # 统一管理窗口（图标导航 + Tab）
│   │   ├── ManagerTab.cs            # Tab 页面基类
│   │   ├── ChannelTab.cs            # 渠道管理 Tab
│   │   ├── AgentTab.cs              # Agent 管理 Tab
│   │   ├── SettingsTab.cs           # 设置 Tab（运行时 + 上下文 + 编辑器 + MCP）
│   │   ├── AIConfigManager.cs       # 配置持久化
│   │   └── EditorPreferences.cs     # 编辑器偏好（ScriptableSingleton）
│   ├── Agent/
│   │   ├── AgentManager.cs          # Agent 资产扫描 + 内置默认 Agent
│   │   ├── AgentDefinitionEditor.cs # Agent 自定义 Inspector
│   │   └── EditorAgentGuard.cs      # Agent 运行时 AssetDatabase 保护
│   ├── MCP/
│   │   ├── McpTab.cs                # MCP Server 管理 Tab
│   │   └── McpServerConfigEditor.cs # MCP Server 自定义 Inspector + 测试连接
│   ├── Chat/
│   │   ├── AIChatWindow*.cs         # 对话窗口 UI（partial class，8 个分部）
│   │   ├── ChatWindowController.cs  # 业务控制器（会话 / Client / Runner / MCP 生命周期）
│   │   ├── EditorChatHistoryStorage.cs # 编辑器会话存储实现
│   │   ├── ContextCollector.cs      # Unity 上下文采集器
│   │   └── MarkdownRenderer.cs      # Markdown → IMGUI 渲染器
│   ├── Tools/                       # 内置 Editor Tools
│   │   ├── ReadFileTool.cs          # 读取文件
│   │   ├── WriteFileTool.cs         # 写入文件
│   │   ├── ListFilesTool.cs         # 列出目录文件
│   │   ├── SearchFilesTool.cs       # 搜索文件内容
│   │   ├── RunTestsTool.cs          # 运行测试
│   │   └── RuntimeQueryTool.cs      # 运行时状态查询
│   └── Icons/                       # 编辑器图标资源
└── package.json
```

## 扩展自定义 Provider

实现 `IAIProvider` 接口即可：

```csharp
public class MyProvider : IAIProvider
{
    public string Name => "MyProvider";

    public async UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct)
    {
        // 实现请求逻辑
    }

    public IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct)
    {
        // 实现流式逻辑
    }
}

// 使用
var client = new AIClient(new MyProvider());
```

## 配置存储

| 优先级 | 来源 | 说明 |
|--------|------|------|
| 1 (最高) | 环境变量 | `ANTHROPIC_API_KEY` 等，仅 Editor 生效 |
| 2 | UniAISettings.asset | `Assets/Resources/UniAI/`，运行时 ScriptableObject |

会话历史存储在 `Library/UniAI/History/` 目录，每个会话一个 JSON 文件，上限 50 个。

## 许可证

MIT License - Copyright (c) 2026 PiscesGameDev
