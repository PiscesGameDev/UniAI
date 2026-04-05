# UniAI

[![Unity](https://img.shields.io/badge/Unity-2022.3%2B-blue)](https://unity.com)
[![License](https://img.shields.io/badge/License-MIT-green)](LICENSE)

Unity AI 交互框架，支持多 Provider（Claude、OpenAI、Gemini、DeepSeek 等）、多模态输入、Tool Use、Agent 系统和 SSE 流式响应。

## 特性

- **多 Provider 支持**: 内置 Claude (Messages API) 和 OpenAI (Chat Completions API) 两种协议，兼容所有 OpenAI 兼容接口（Gemini、DeepSeek 等）
- **SSE 流式响应**: 基于 `UnityWebRequest` + `DownloadHandlerScript` 实现真正的流式接收
- **多模态输入**: 支持文本和图片（base64）混合消息
- **Tool Use**: 支持 AI 调用开发者自定义工具（Claude tool_use + OpenAI function_calling）
- **Agent 系统**: 自动多轮 Tool 调用循环编排，ScriptableObject 配置 Agent
- **统一抽象**: 一套 `AIRequest/AIResponse` 模型覆盖所有 Provider
- **Editor 工具链**: 内置渠道管理、Agent 管理和 AI 对话窗口，开箱即用
- **零 GC 异步**: 基于 UniTask 的异步实现

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

**方式 A: Editor 配置窗口**

菜单 `Window > UniAI > Channels` 打开渠道管理窗口，填写 API Key 和其他参数。

**方式 B: 环境变量**

设置以下环境变量，框架会自动读取（优先级高于配置文件）：

| Provider | 环境变量 |
|----------|---------|
| Claude | `ANTHROPIC_API_KEY` |
| OpenAI | `OPENAI_API_KEY` |
| Gemini | `GEMINI_API_KEY` |
| DeepSeek | `DEEPSEEK_API_KEY` |

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

### 2. 发送请求

```csharp
using UniAI;

// 从配置创建客户端
var config = AIConfigManager.LoadConfig();
var client = AIClient.Create(config);

// 完整响应
var response = await client.SendAsync(new AIRequest
{
    SystemPrompt = "你是一个 Unity 游戏开发助手。",
    Messages = { AIMessage.User("如何优化 Draw Call？") },
    MaxTokens = 2048,
    Temperature = 0.7f
});

if (response.IsSuccess)
    Debug.Log(response.Text);
```

### 3. 流式响应

```csharp
var request = new AIRequest
{
    Messages = { AIMessage.User("写一个单例模式的基类") }
};

await foreach (var chunk in client.StreamAsync(request))
{
    if (!string.IsNullOrEmpty(chunk.DeltaText))
        Debug.Log(chunk.DeltaText); // 增量文本

    if (chunk.IsComplete)
        Debug.Log($"Token 用量: {chunk.Usage?.TotalTokens}");
}
```

### 4. 多模态消息（图文）

```csharp
byte[] imageData = File.ReadAllBytes("screenshot.png");

var request = new AIRequest
{
    Messages =
    {
        AIMessage.UserWithImage("这张截图中的 UI 有什么问题？", imageData, "image/png")
    }
};
```

### 5. 多轮对话

```csharp
var request = new AIRequest
{
    Messages =
    {
        AIMessage.User("什么是对象池？"),
        AIMessage.Assistant("对象池是一种设计模式，通过预先创建并复用对象来避免频繁的创建和销毁开销。"),
        AIMessage.User("在 Unity 中如何实现？")
    }
};
```

### 6. Tool Use（工具调用）

```csharp
var request = new AIRequest
{
    Messages = { AIMessage.User("读取 Assets/Scripts/Player.cs 文件的内容") },
    Tools = new List<AITool>
    {
        new AITool
        {
            Name = "read_file",
            Description = "读取指定路径的文件内容",
            ParametersSchema = @"{""type"":""object"",""properties"":{""path"":{""type"":""string""}},""required"":[""path""]}"
        }
    }
};

var response = await client.SendAsync(request);
if (response.HasToolCalls)
{
    foreach (var tc in response.ToolCalls)
        Debug.Log($"AI 请求调用工具: {tc.Name}({tc.Arguments})");
}
```

### 7. Agent 系统

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

**创建 Agent:**

1. 在 Project 中 Create > UniAI > Agent Definition
2. 配置名称、SystemPrompt、工具列表、MaxTurns 等
3. Agent 会自动出现在对话窗口的 Agent 下拉菜单中

**使用 Agent（代码方式）:**

```csharp
var runner = new AIAgentRunner(client, agentDefinition);

// 非流式
var result = await runner.RunAsync(messages);
if (result.IsSuccess)
    Debug.Log(result.FinalText);

// 流式
await foreach (var evt in runner.RunStreamAsync(messages))
{
    switch (evt.Type)
    {
        case AgentEventType.TextDelta:
            Debug.Log(evt.Text);
            break;
        case AgentEventType.ToolCallStart:
            Debug.Log($"调用工具: {evt.ToolCall.Name}");
            break;
        case AgentEventType.ToolCallResult:
            Debug.Log($"工具结果: {evt.ToolResult}");
            break;
    }
}
```

## Editor 工具

### AI 对话窗口

菜单 `Window > UniAI > Chat` 打开。

- 多模型切换，支持所有已配置渠道的模型
- Agent 选择：默认助手 + 自定义 Agent
- SSE 流式输出，实时显示
- Tool 调用过程可视化（名称、参数、结果）
- Markdown 渲染（标题、代码块、列表、加粗）
- 代码块一键复制
- 会话历史管理（自动持久化）
- 自动生成对话标题
- Unity 上下文注入：选中对象、控制台错误、工程资源
- 快捷操作：解释代码、优化建议、生成注释、修复报错
- Enter 发送，Shift+Enter 换行

### 渠道管理窗口

菜单 `Window > UniAI > Channels` 打开。

- 动态管理渠道列表（增删、启用/禁用）
- API Key 密码显示/隐藏
- 在线获取模型列表
- 单个/批量模型连接测试
- 支持添加自定义 OpenAI 兼容渠道

### Agent 管理窗口

菜单 `Window > UniAI > Agents` 打开。

- 查看和管理所有 Agent
- 可视化编辑 Agent 配置（名称、描述、参数、工具、System Prompt）
- 从窗口直接开启与 Agent 的对话

### 设置窗口

菜单 `Window > UniAI > Settings` 打开。

- 运行时参数：请求超时、日志级别
- 编辑器参数：侧边栏、历史会话上限

## 架构

```
UniAI/
├── Runtime/
│   ├── Core/
│   │   ├── AIClient.cs          # 框架入口
│   │   ├── AIConfig.cs          # 配置模型 + 渠道预设
│   │   ├── UniAISettings.cs     # 运行时 ScriptableObject 配置
│   │   ├── ModelListService.cs  # 模型列表查询服务
│   │   └── AILogger.cs          # 内部日志（API Key 脱敏）
│   ├── Models/
│   │   ├── AIRequest.cs         # 统一请求
│   │   ├── AIResponse.cs        # 统一响应 + TokenUsage
│   │   ├── AIMessage.cs         # 消息（User/Assistant + 内容块）
│   │   ├── AIContent.cs         # 内容块（Text/Image/ToolUse/ToolResult）
│   │   ├── AITool.cs            # Tool 定义 + ToolCall
│   │   └── AIStreamChunk.cs     # 流式响应块
│   ├── Agent/
│   │   ├── AIToolAsset.cs       # Tool ScriptableObject 基类
│   │   ├── AgentDefinition.cs   # Agent 配置 ScriptableObject
│   │   ├── AIAgentRunner.cs     # Agent 运行器（Tool 调用循环）
│   │   ├── AgentEvent.cs        # Agent 流式事件
│   │   └── AgentResult.cs       # Agent 运行结果
│   ├── Providers/
│   │   ├── IAIProvider.cs       # Provider 接口
│   │   ├── ProviderBase.cs      # Provider 抽象基类
│   │   ├── FallbackProvider.cs  # 多渠道故障转移
│   │   ├── Claude/              # Claude Messages API
│   │   └── OpenAI/              # OpenAI Chat Completions API
│   └── Http/
│       ├── AIHttpClient.cs      # HTTP 客户端（JSON + SSE Stream）
│       ├── HttpResult.cs        # HTTP 结果封装
│       ├── SSEDownloadHandler.cs # SSE 增量下载处理器
│       └── SSEParser.cs         # SSE 协议解析器
├── Editor/
│   ├── AIConfigManager.cs       # 配置持久化
│   ├── EditorPreferences.cs     # 编辑器偏好（ScriptableSingleton）
│   ├── AIChannelWindow.cs       # 渠道管理窗口
│   ├── AIAgentWindow.cs         # Agent 管理窗口
│   ├── UniAISettingsWindow.cs   # 设置窗口
│   ├── AgentManager.cs          # Agent 资产扫描 + 内置默认 Agent
│   ├── AgentDefinitionEditor.cs # Agent 自定义 Inspector
│   ├── EditorGUIHelper.cs       # 编辑器 GUI 工具
│   ├── Chat/
│   │   ├── AIChatWindow*.cs     # 对话窗口（partial class）
│   │   ├── ChatSession.cs       # 会话模型
│   │   ├── ChatHistory.cs       # 会话历史持久化
│   │   ├── ContextCollector.cs  # Unity 上下文采集器
│   │   └── MarkdownRenderer.cs  # Markdown → IMGUI 渲染器
│   └── Icons/                   # 编辑器图标资源
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

会话历史存储在 `ProjectSettings/UniAI/History/` 目录，每个会话一个 JSON 文件，上限 50 个。

## 许可证

MIT License - Copyright (c) 2026 PiscesGameDev
