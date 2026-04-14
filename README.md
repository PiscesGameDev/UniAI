# UniAI

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue)](https://unity.com)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue)](LICENSE)

Unity AI 交互框架，支持多 Provider（Claude、OpenAI、Gemini、DeepSeek 等）、多模态输入、Tool Use、Agent 系统、MCP Client、生成式资产、上下文窗口管理和 SSE 流式响应。

## 特性

- **多 Provider 支持**: 内置 Claude (Messages API) 和 OpenAI (Chat Completions API) 两种协议，兼容所有 OpenAI 兼容接口（Gemini、DeepSeek、Grok、Qwen 等）
- **SSE 流式响应**: 基于 `UnityWebRequest` + `DownloadHandlerScript` 实现真正的流式接收
- **多模态输入**: 支持文本、图片（base64）和文本文件附件混合消息，Editor 支持拖拽 Unity 资产或文件选择器添加附件
- **声明式 Tool Use**: `[UniAITool]` 特性标记静态类，自动反射注册，支持 Claude tool_use + OpenAI function_calling
- **Agent 系统**: 自动多轮 Tool 调用循环编排，ScriptableObject 配置 Agent，按工具分组启用
- **MCP Client**: 作为标准 MCP 客户端连接外部 Server（Stdio 子进程 / Streamable HTTP），动态合并 Tools 与 Resources 到 Agent
- **模型注册表**: `ModelRegistry` 统一管理 30+ 预设模型元信息（能力、端点、上下文窗口），支持用户自定义扩展
- **生成式资产**: `GenerativeAssetService` 支持 AI 图片生成（DALL-E、Gemini Imagen 等），模型决定能力路由
- **上下文窗口管理**: 自动 token 预估、滑动窗口截断、AI 摘要压缩、RAG 上下文注入接口
- **对话编排**: `ChatOrchestrator` 封装完整流式对话生命周期，Runtime 自足，Editor 通过委托注入
- **统一抽象**: 一套 `AIRequest/AIResponse` 模型覆盖所有 Provider
- **Editor 工具链**: 统一管理窗口（渠道、模型、Agent、工具、MCP、设置）+ AI 对话窗口，开箱即用
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

// 从配置创建客户端
var settings = UniAISettings.Instance;
var client = AIClient.Create(settings.ToConfig());

// 单条消息完整响应
var response = await client.SendAsync(new AIRequest
{
    Model = "gpt-4o",
    SystemPrompt = "你是一个 Unity 游戏开发助手。",
    Messages = { AIMessage.User("如何优化 Draw Call？") }
});
if (response.IsSuccess) Debug.Log(response.Text);

// 完整请求
var response = await client.SendAsync(new AIRequest
{
    Model = "gpt-4o",
    SystemPrompt = "你是一个 Unity 游戏开发助手。",
    Messages = { AIMessage.User("如何优化 Draw Call？") },
});

// 结构化输出
var typed = await client.SendAsync<MyDataClass>(new AIRequest
{
    Messages = { AIMessage.User("返回 JSON 格式的角色属性") },
    ResponseFormat = AIResponseFormat.JsonSchema<MyDataClass>()
});
if (typed.IsSuccess) Debug.Log(typed.Data.Name);

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

// 多模态（文件附件）
var fileRequest = new AIRequest
{
    Messages = { AIMessage.UserWithFiles("分析这段代码", new List<AIFileContent>
    {
        new AIFileContent("PlayerController.cs", fileContent)
    }) }
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

### 3. 模型注册表

`ModelRegistry` 提供模型元信息查询，包含 30+ 内置预设：

```csharp
// 查询模型上下文窗口
int ctx = ModelRegistry.GetContextWindow("claude-opus-4-6"); // 200000

// 查询模型能力
bool canGenImage = ModelRegistry.HasCapability("dall-e-3", ModelCapability.ImageGen); // true

// 查询 API 端点
ModelEndpoint endpoint = ModelRegistry.GetEndpoint("dall-e-3"); // ImageGenerations
string path = ModelRegistry.GetEndpointPath(endpoint); // "/images/generations"

// 获取模型完整信息
ModelEntry entry = ModelRegistry.Get("gemini-2.5-flash");
// entry.Capabilities = Chat | ImageGen（多能力模型）
// entry.Vendor = "Google"
```

支持通过 `UniAISettings.CustomModels` 添加用户自定义模型，查找优先级：用户自定义 > 内置预设。

### 4. Agent 系统

Agent 是 Tool 调用循环的编排器。普通 Chat = 默认 Agent（无 Tool，一轮即结束）。

**创建自定义 Tool:**

```csharp
[UniAITool(
    Name = "read_file",
    Group = ToolGroups.Core,
    Description = "Read file content. Actions: 'read'.",
    Actions = new[] { "read" })]
internal static class ReadFileTool
{
    public static async UniTask<object> HandleAsync(JObject args, CancellationToken ct)
    {
        var action = (string)args["action"];
        if (action == "read")
        {
            var path = (string)args["path"];
            var content = await File.ReadAllTextAsync(path, ct);
            return ToolResponse.Success(new { content });
        }
        return ToolResponse.Error($"Unknown action '{action}'.");
    }

    public class ReadArgs
    {
        [ToolParam(Description = "File path to read.")]
        public string Path;
    }
}
```

工具启动时由 `UniAIToolRegistry` 自动反射注册，无需创建 ScriptableObject 资产。

**创建 Agent:** Project 中 Create > UniAI > Agent Definition，配置 SystemPrompt、Tool Groups（分组复选框）、MaxTurns，自动出现在对话窗口的 Agent 下拉菜单中。

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

### 5. 接入 MCP Server（可选）

让 Agent 连接外部 MCP Server 动态获得 Tools 和 Resources：

1. Project 中创建 Server 配置：Create > UniAI > MCP Server Config
2. 选择传输类型：
   - **Stdio**（本地子进程）：`Command = npx`，`Arguments = -y @modelcontextprotocol/server-filesystem .`
   - **HTTP**（远程服务）：填写绝对 `Base URL`
3. 点击 Inspector 的「测试连接」验证配置可用
4. 将此 SO 拖到 `AgentDefinition.McpServers` 列表

Agent 启动时自动连接所有启用的 Server，`tools/list` 和 `resources/list` 的结果会合并到 Agent 可用工具中。本地 `[UniAITool]` 同名时本地优先。

```csharp
// 代码方式测试 MCP Server
var config = AssetDatabase.LoadAssetAtPath<McpServerConfig>("Assets/MyMcp.asset");
var result = await McpClientManager.TestConnectionAsync(config, initTimeoutSeconds: 30);
Debug.Log($"Success: {result.Success}, Tools: {result.ToolCount}, Resources: {result.ResourceCount}");
```

MCP 的连接超时与 Tool 调用超时在 `Window > UniAI > Manager → 设置 → MCP 设置` 中统一配置。

### 6. 上下文窗口管理

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
- **模型限制查询**: `ModelRegistry.GetContextWindow("claude-opus-4-6")` → 200000
- **自动摘要**: 超限时用 AI 压缩旧消息为摘要，保留最近 N 条
- **RAG 注入**: 实现 `IContextProvider` 接口，检索结果自动注入消息列表

### 7. 生成式资产（可选）

通过 `ManageGenerate` Tool 或 `GenerativeAssetService` API 进行 AI 图片生成：

```csharp
// 注册 Provider（框架内置 OpenAIImageProvider）
var provider = new OpenAIImageProvider(channelEntry);
GenerativeAssetService.Instance.Register(provider);

// 或通过 Agent 的 ManageGenerate Tool 自动调用（需启用 generate 分组）
// AI 会根据模型能力自动路由到正确的生成端点
```

支持的模型包括 DALL-E 3、Gemini Imagen、Flux、Stable Diffusion 等，由 `ModelRegistry` 统一管理能力标识。

## Editor 工具

### 统一管理窗口

菜单 `Window > UniAI > Manager` 打开。左侧图标导航栏 + 右侧 Tab 内容。

**渠道 Tab:**
- 动态管理渠道列表（增删、启用/禁用）
- API Key 密码显示/隐藏 + 环境变量覆盖
- 在线获取模型列表
- 单个/批量模型连接测试
- 支持添加自定义 OpenAI 兼容渠道

**模型 Tab:**
- 查看所有内置预设模型（30+）
- 添加/编辑用户自定义模型
- 能力 badge 展示（Chat / ImageGen / ImageEdit 等）
- 上下文窗口大小展示与编辑
- 按能力/厂商筛选

**Agent Tab:**
- 查看和管理所有 Agent
- 可视化编辑 Agent 配置（名称、描述、参数、Tool Groups、System Prompt、MCP Servers）
- 从窗口直接开启与 Agent 的对话

**工具 Tab:**
- 按分组列出所有已注册 `[UniAITool]` 工具
- 内置/自定义 badge 区分
- 查看各工具的 action 列表与描述

**MCP Tab:**
- 管理 MCP Server Config 资产（增删、启用/禁用）
- Stdio / Streamable HTTP 两种传输类型
- 环境变量和请求头编辑
- 内嵌「测试连接」按钮

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
- 生成式资产内联渲染（AI 生成的图片直接显示缩略图）
- **上下文窗口管理**：长对话自动截断/摘要，防止 token 超限
- Markdown 渲染（标题、代码块、列表、加粗）
- 代码块一键复制
- 会话历史管理（自动持久化）
- 自动生成对话标题
- Unity 上下文注入：选中对象、控制台错误、工程资源
- 快捷操作：解释代码、优化建议、生成注释、修复报错
- **附件支持**: 拖拽 Unity 资产（Texture2D/Sprite/RenderTexture/TextAsset）或点击 `@` 按钮选择本地文件（图片/文本），作为多模态上下文发送给 AI
- Enter 发送，Shift+Enter 换行

## 架构

```
UniAI/
├── Runtime/
│   ├── Core/
│   │   ├── AIClient.cs              # 框架入口（多渠道故障转移）
│   │   ├── AIConfig.cs              # 配置模型（ChannelEntries + GeneralConfig）
│   │   ├── ChannelEntry.cs          # 单渠道配置（含内置预设 + 环境变量覆盖）
│   │   ├── UniAISettings.cs         # 运行时 ScriptableObject 配置（单例）
│   │   ├── ChatOrchestrator.cs      # 对话编排器（Runner/Client/Pipeline 生命周期）
│   │   ├── ModelSelector.cs         # 模型选择管理
│   │   ├── ModelRegistry.cs         # 模型注册表（30+ 预设 + 上下文窗口兜底）
│   │   ├── ModelEntry.cs            # 模型定义（能力 + 端点 + 上下文窗口）
│   │   ├── ModelCapability.cs       # 模型能力枚举 + API 端点枚举
│   │   ├── IConversationRunner.cs   # 对话运行器接口
│   │   ├── ChatRunner.cs            # 纯 Chat 运行器
│   │   ├── ModelListService.cs      # 模型列表查询服务
│   │   ├── TimeoutHelper.cs         # 通用超时包装
│   │   └── AILogger.cs              # 内部日志（API Key 脱敏）
│   ├── Models/
│   │   ├── AIRequest.cs             # 统一请求
│   │   ├── AIResponse.cs            # 统一响应 + TokenUsage + AITypedResponse<T>
│   │   ├── AIMessage.cs             # 消息（工厂方法: User/Assistant/UserWithImage/UserWithFiles）
│   │   ├── AIContent.cs             # 内容块（Text/Image/File/ToolUse/ToolResult）
│   │   ├── AITool.cs                # Tool 定义 + ToolCall
│   │   ├── AIResponseFormat.cs      # 结构化响应格式（JSON Schema）
│   │   └── AIStreamChunk.cs         # 流式响应块
│   ├── Agent/
│   │   ├── UniAIToolAttribute.cs    # [UniAITool] + [ToolParam] + ToolGroups 常量
│   │   ├── UniAIToolRegistry.cs     # 反射扫描注册表
│   │   ├── ToolSchemaGenerator.cs   # Args → JSON Schema 生成器
│   │   ├── ToolResponse.cs          # 统一返回 Success/Error
│   │   ├── ToolPathHelper.cs        # 路径安全校验
│   │   ├── AgentDefinition.cs       # Agent 配置 SO（ToolGroups + McpServers）
│   │   ├── AgentRegistry.cs         # Agent 静态注册表
│   │   ├── AIAgentRunner.cs         # Agent 运行器（Tool 循环 + MCP 合并）
│   │   ├── AgentEvent.cs            # Agent 流式事件
│   │   └── AgentResult.cs           # Agent 运行结果
│   ├── Chat/
│   │   ├── ChatSession.cs           # 会话模型 + ChatMessage + ChatAttachment
│   │   ├── ChatHistoryManager.cs    # 会话历史管理器
│   │   ├── IChatHistoryStorage.cs   # 存储接口
│   │   └── FileChatHistoryStorage.cs # 文件系统存储实现
│   ├── Context/
│   │   ├── ContextPipeline.cs       # 上下文处理管道
│   │   ├── ContextWindowConfig.cs   # 上下文窗口配置
│   │   ├── TokenEstimator.cs        # Token 预估器（中英混合 + 图片 + 文件）
│   │   ├── MessageSummarizer.cs     # 消息摘要器
│   │   └── IContextProvider.cs      # RAG 上下文提供者接口
│   ├── MCP/
│   │   ├── McpServerConfig.cs       # MCP Server 配置 SO（Stdio / HTTP）
│   │   ├── IMcpTransport.cs         # 传输层抽象接口
│   │   ├── StdioMcpTransport.cs     # 子进程 stdin/stdout
│   │   ├── HttpMcpTransport.cs      # Streamable HTTP 传输
│   │   ├── McpTransportFactory.cs   # 传输类型工厂
│   │   ├── McpClient.cs             # 单 Server 连接 + 协议握手
│   │   ├── McpClientManager.cs      # 多 Server 管理 + Tool 路由
│   │   ├── McpResourceProvider.cs   # Resource → IContextProvider 适配
│   │   ├── McpConstants.cs          # 方法名 / 内容类型常量
│   │   └── McpModels.cs             # JSON-RPC + MCP 协议数据模型
│   ├── Generative/
│   │   ├── GenerativeAssetService.cs    # Provider 注册表单例
│   │   ├── IGenerativeAssetProvider.cs  # 生成 Provider 接口
│   │   ├── GenerateRequest.cs           # 生成请求
│   │   ├── GenerateResult.cs            # 生成结果
│   │   ├── GenerativeAssetType.cs       # 资产类型枚举
│   │   └── Providers/
│   │       └── OpenAIImageProvider.cs   # OpenAI 图片生成
│   ├── Tools/
│   │   ├── ManageFile.cs            # [UniAITool] 文件操作
│   │   ├── RuntimeQuery.cs          # [UniAITool] 运行时状态查询
│   │   ├── ToolConfig.cs            # 全局配置（MaxOutputChars/SearchMaxMatches）
│   │   └── ToolCallbacks.cs         # 回调注入（OnAssetsModified）
│   ├── Providers/
│   │   ├── IAIProvider.cs           # Provider 接口
│   │   ├── ProviderBase.cs          # Provider 抽象基类
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
│   │   ├── ModelTab.cs              # 模型管理 Tab（能力 badge + 上下文窗口）
│   │   ├── AgentTab.cs              # Agent 管理 Tab
│   │   ├── ToolsTab.cs              # 工具展示 Tab（分组 + badge）
│   │   ├── SettingsTab.cs           # 设置 Tab
│   │   ├── AIConfigManager.cs       # 配置持久化
│   │   └── EditorPreferences.cs     # 编辑器偏好（ScriptableSingleton）
│   ├── Agent/
│   │   ├── AgentManager.cs          # Agent 资产扫描 + 注册到 AgentRegistry
│   │   ├── AgentDefinitionEditor.cs # Agent 自定义 Inspector
│   │   └── EditorAgentGuard.cs      # Agent 运行时 AssetDatabase 保护
│   ├── MCP/
│   │   ├── McpTab.cs                # MCP Server 管理 Tab
│   │   └── McpServerConfigEditor.cs # MCP Server 自定义 Inspector + 测试连接
│   ├── Chat/
│   │   ├── AIChatWindow*.cs         # 对话窗口 UI（partial class，多个分部）
│   │   ├── StreamingController.cs   # Editor 薄适配层 → ChatOrchestrator
│   │   ├── ChatWindowController.cs  # 业务控制器
│   │   ├── ContextCollector.cs      # Unity 上下文采集器
│   │   └── MarkdownRenderer.cs      # Markdown → IMGUI 渲染器
│   ├── Tools/                       # 内置 Editor Tools（[UniAITool]）
│   │   ├── ManageScene.cs           # 场景层级/Transform/组件/场景 IO
│   │   ├── ManageAsset.cs           # AssetDatabase 元操作
│   │   ├── ManagePrefab.cs          # 预制体生命周期
│   │   ├── ManageMaterial.cs        # 材质球与 Shader 属性编辑
│   │   ├── ManageScriptableObject.cs # 通用 SO 属性编辑
│   │   ├── ManageConsole.cs         # Unity Console 日志与编译错误
│   │   ├── ManageMenu.cs            # 执行编辑器菜单项
│   │   ├── ManageSelection.cs       # Unity Selection 读写
│   │   ├── ManageProjectSettings.cs # Tags/Layers/Physics/Time/Quality
│   │   ├── ManageTest.cs            # 运行测试
│   │   ├── ManageGenerate.cs        # 生成式资产（图片生成）
│   │   └── SceneEdit.cs             # Play Mode 安全 Undo 包装
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
| 1 (最高) | 环境变量 | `ChannelEntry.EnvVarName` + `UseEnvVar`，覆盖 ApiKey |
| 2 | UniAISettings.asset | `Assets/Resources/UniAI/`，运行时 ScriptableObject |

会话历史基于 `FileChatHistoryStorage`，每个会话一个 JSON 文件，上限 50 个。

## 许可证

Apache License 2.0 - Copyright 2025 PiscesGameDev
