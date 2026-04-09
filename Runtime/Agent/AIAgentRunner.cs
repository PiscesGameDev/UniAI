using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UniAI
{
    /// <summary>
    /// Agent 运行器 — 封装 Tool 调用循环
    /// 无 Tool 时等价于直接 Chat（一轮即结束）
    /// </summary>
    public class AIAgentRunner : IConversationRunner, IDisposable
    {
        private readonly AIClient _client;
        private readonly AgentDefinition _definition;
        private readonly Dictionary<string, ToolHandlerInfo> _localHandlers = new(StringComparer.Ordinal);
        private readonly List<AITool> _toolDefs = new();
        private McpClientManager _mcpManager;
        private UniTask? _mcpInitTask;
        private bool _mcpInitialized;

        /// <summary>
        /// 是否注册了工具
        /// </summary>
        public bool HasTools => _toolDefs.Count > 0;

        /// <summary>
        /// 已注册的本地工具处理器
        /// </summary>
        public IReadOnlyCollection<ToolHandlerInfo> LocalHandlers => _localHandlers.Values;

        /// <summary>
        /// MCP 客户端管理器（可能为 null，表示未使用 MCP）
        /// </summary>
        public McpClientManager McpManager => _mcpManager;

        /// <summary>
        /// 单个 Tool 执行超时时间（秒），0 或负数表示不限制
        /// </summary>
        public float ToolTimeoutSeconds { get; set; }

        /// <summary>
        /// MCP 运行时配置（初始化超时、Tool 调用超时），从 AIConfig.General.Mcp 传入
        /// </summary>
        public McpRuntimeConfig McpSettings { get; set; }

        public AIAgentRunner(AIClient client, AgentDefinition definition)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            // 从 Registry 按组加载本地代码工具
            if (definition.HasTools)
            {
                foreach (var handler in UniAIToolRegistry.GetHandlers(definition.ToolGroups))
                {
                    _localHandlers[handler.Name] = handler;
                    _toolDefs.Add(handler.Definition);
                }
            }
        }

        /// <summary>
        /// 异步初始化 MCP 连接：连接所有启用的 MCP Server，将其 Tools 合并到可用工具列表
        /// 没有 MCP 配置时立即返回。初始化成功后不会重复执行；失败时允许下次重试。
        /// 并发调用会共享同一次初始化过程。
        /// </summary>
        public UniTask InitializeMcpAsync(CancellationToken ct = default)
        {
            if (_mcpInitialized) return UniTask.CompletedTask;
            if (_mcpInitTask.HasValue) return _mcpInitTask.Value;

            var task = InitializeMcpCoreAsync(ct);
            _mcpInitTask = task;
            return task;
        }

        private async UniTask InitializeMcpCoreAsync(CancellationToken ct)
        {
            try
            {
                if (!_definition.HasMcpServers)
                {
                    _mcpInitialized = true;
                    return;
                }

                var manager = new McpClientManager();
                int initTimeout = McpSettings?.InitTimeoutSeconds ?? 0;
                await manager.ConnectAllAsync(_definition.McpServers, initTimeout, ct);

                manager.ToolCallTimeoutSeconds = McpSettings?.ToolCallTimeoutSeconds ?? 0;

                foreach (var tool in manager.GetAllTools())
                {
                    if (string.IsNullOrEmpty(tool.Name)) continue;

                    // MCP Tool 名与本地代码工具冲突时，本地优先
                    if (_localHandlers.ContainsKey(tool.Name))
                    {
                        AILogger.Warning($"MCP tool '{tool.Name}' shadowed by local [UniAITool]");
                        continue;
                    }
                    _toolDefs.Add(tool);
                }

                _mcpManager = manager;
                _mcpInitialized = true;
            }
            catch
            {
                // 失败时允许重试：清空缓存的 task，下次调用会重新尝试
                _mcpInitTask = null;
                throw;
            }
        }

        /// <summary>
        /// 非流式运行：返回最终结果。可选 requestOverride 覆盖 AgentDefinition 默认值
        /// </summary>
        public async UniTask<AgentResult> RunAsync(List<AIMessage> messages, AIRequest requestOverride = null, CancellationToken ct = default)
        {
            var workingMessages = new List<AIMessage>(messages);
            var totalUsage = new TokenUsage();
            int maxTurns = HasTools ? _definition.MaxTurns : 1;

            for (int turn = 0; turn < maxTurns; turn++)
            {
                ct.ThrowIfCancellationRequested();

                var request = BuildRequest(workingMessages, requestOverride);
                var response = await _client.SendAsync(request, ct);

                if (!response.IsSuccess)
                    return AgentResult.Fail(response.Error, workingMessages, turn);

                AccumulateUsage(totalUsage, response.Usage);

                if (response.HasToolCalls)
                {
                    workingMessages.Add(BuildAssistantMessage(response.Text, response.ToolCalls));

                    // 执行 Tool 并追加结果
                    foreach (var tc in response.ToolCalls)
                    {
                        var (result, isError) = await ExecuteToolAsync(tc, ct);
                        workingMessages.Add(AIMessage.ToolResult(tc.Id, result, isError));
                    }

                    continue;
                }

                // 无 Tool 调用 → 结束
                return AgentResult.Success(response.Text, workingMessages, turn + 1, totalUsage);
            }

            return AgentResult.Fail("Exceeded maximum turns", workingMessages, maxTurns);
        }

        /// <summary>
        /// 流式运行：yield AgentEvent。可选 requestOverride 覆盖 AgentDefinition 默认值
        /// </summary>
        public IUniTaskAsyncEnumerable<AgentEvent> RunStreamAsync(List<AIMessage> messages, AIRequest requestOverride = null, CancellationToken ct = default)
        {
            // 无 Tool 时直接转换 Provider 流，避免额外的 UniTaskAsyncEnumerable.Create 嵌套
            if (!HasTools)
                return RunStreamSimple(messages, requestOverride, ct);

            return RunStreamWithTools(messages, requestOverride, ct);
        }

        /// <summary>
        /// 简单流式（无 Tool）：直接将 Provider 的 AIStreamChunk 转换为 AgentEvent，
        /// 不额外包裹 UniTaskAsyncEnumerable.Create，减少嵌套层数
        /// </summary>
        private IUniTaskAsyncEnumerable<AgentEvent> RunStreamSimple(List<AIMessage> messages, AIRequest requestOverride, CancellationToken ct)
        {
            var request = BuildRequest(new List<AIMessage>(messages), requestOverride);
            return _client.StreamAsync(request, ct)
                .Select(AgentEvent.FromChunk)
                .Where(evt => evt != null);
        }

        /// <summary>
        /// 带 Tool 的流式运行：需要 UniTaskAsyncEnumerable.Create 包裹以实现多轮循环
        /// </summary>
        private IUniTaskAsyncEnumerable<AgentEvent> RunStreamWithTools(List<AIMessage> messages, AIRequest requestOverride, CancellationToken ct)
        {
            return UniTaskAsyncEnumerable.Create<AgentEvent>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var workingMessages = new List<AIMessage>(messages);
                var totalUsage = new TokenUsage();
                int maxTurns = _definition.MaxTurns;

                for (int turn = 0; turn < maxTurns; turn++)
                {
                    linkedToken.ThrowIfCancellationRequested();

                    var request = BuildRequest(workingMessages, requestOverride);
                    var responseText = "";
                    var toolCalls = new List<AIToolCall>();
                    TokenUsage turnUsage = null;

                    await foreach (var chunk in _client.StreamAsync(request, linkedToken))
                    {
                        if (!string.IsNullOrEmpty(chunk.DeltaText))
                        {
                            responseText += chunk.DeltaText;
                            await writer.YieldAsync(new AgentEvent
                            {
                                Type = AgentEventType.TextDelta,
                                Text = chunk.DeltaText
                            });
                        }

                        if (chunk.ToolCall != null)
                            toolCalls.Add(chunk.ToolCall);

                        if (chunk.IsComplete && chunk.Usage != null)
                            turnUsage = chunk.Usage;
                    }

                    AccumulateUsage(totalUsage, turnUsage);

                    if (toolCalls.Count > 0)
                    {
                        workingMessages.Add(BuildAssistantMessage(responseText, toolCalls));

                        // 执行 Tool
                        foreach (var tc in toolCalls)
                        {
                            await writer.YieldAsync(new AgentEvent
                            {
                                Type = AgentEventType.ToolCallStart,
                                ToolCall = tc
                            });

                            var (result, isError) = await ExecuteToolAsync(tc, linkedToken);

                            await writer.YieldAsync(new AgentEvent
                            {
                                Type = AgentEventType.ToolCallResult,
                                ToolCall = tc,
                                ToolName = tc.Name,
                                ToolResult = result,
                                IsToolError = isError
                            });

                            workingMessages.Add(AIMessage.ToolResult(tc.Id, result, isError));
                        }

                        await writer.YieldAsync(new AgentEvent
                        {
                            Type = AgentEventType.TurnComplete,
                            TurnIndex = turn,
                            Usage = turnUsage
                        });

                        continue;
                    }

                    // 无 Tool 调用 → 结束
                    await writer.YieldAsync(new AgentEvent
                    {
                        Type = AgentEventType.TurnComplete,
                        TurnIndex = turn,
                        Text = responseText,
                        Usage = totalUsage
                    });
                    return;
                }

                await writer.YieldAsync(new AgentEvent
                {
                    Type = AgentEventType.Error,
                    Text = "Exceeded maximum turns"
                });
            });
        }

        private AIRequest BuildRequest(List<AIMessage> messages, AIRequest overrides = null)
        {
            var request = new AIRequest
            {
                SystemPrompt = overrides?.SystemPrompt ?? _definition.SystemPrompt,
                Messages = messages,
                Temperature = overrides != null ? overrides.Temperature : _definition.Temperature,
                MaxTokens = overrides?.MaxTokens > 0 ? overrides.MaxTokens : _definition.MaxTokens
            };

            if (_toolDefs.Count > 0)
                request.Tools = _toolDefs;

            return request;
        }

        private static AIMessage BuildAssistantMessage(string text, List<AIToolCall> toolCalls)
        {
            var msg = new AIMessage { Role = AIRole.Assistant, Contents = new List<AIContent>() };
            if (!string.IsNullOrEmpty(text))
                msg.Contents.Add(new AITextContent(text));
            foreach (var tc in toolCalls)
            {
                msg.Contents.Add(new AIToolUseContent
                {
                    Id = tc.Id, Name = tc.Name, Arguments = tc.Arguments
                });
            }
            return msg;
        }

        private async UniTask<(string result, bool isError)> ExecuteToolAsync(AIToolCall toolCall, CancellationToken ct)
        {
            // 1. 本地代码工具优先
            if (_localHandlers.TryGetValue(toolCall.Name, out var handler))
                return await ExecuteLocalHandlerAsync(handler, toolCall, ct);

            // 2. MCP Tool
            if (_mcpManager != null && _mcpManager.HasTool(toolCall.Name))
            {
                try
                {
                    var mcpResult = await TimeoutHelper.WithTimeout(
                        token => _mcpManager.CallToolAsync(toolCall.Name, toolCall.Arguments, token),
                        ToolTimeoutSeconds, ct);
                    AILogger.Verbose($"MCP tool '{toolCall.Name}' executed (error={mcpResult.isError})");
                    return mcpResult;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    var timeoutError = $"MCP tool '{toolCall.Name}' timed out after {ToolTimeoutSeconds}s";
                    AILogger.Warning(timeoutError);
                    return (timeoutError, true);
                }
            }

            // 3. 未知工具
            var error = $"Unknown tool: {toolCall.Name}";
            AILogger.Warning(error);
            return (error, true);
        }

        private async UniTask<(string result, bool isError)> ExecuteLocalHandlerAsync(ToolHandlerInfo handler, AIToolCall toolCall, CancellationToken ct)
        {
            // 长耗时工具（RequiresPolling）使用自身声明的 MaxPollSeconds，
            // 绕过全局 ToolTimeoutSeconds，避免被短超时截断。
            float timeout = handler.RequiresPolling
                ? handler.MaxPollSeconds
                : ToolTimeoutSeconds;

            try
            {
                JObject args;
                if (string.IsNullOrEmpty(toolCall.Arguments))
                {
                    args = new JObject();
                }
                else
                {
                    try
                    {
                        args = JObject.Parse(toolCall.Arguments);
                    }
                    catch (JsonReaderException)
                    {
                        // 某些模型偶尔会返回拼接的多个 JSON 对象，
                        // 尝试只解析第一个完整的 JSON 对象
                        args = TryParseFirstJsonObject(toolCall.Arguments);
                        if (args == null)
                        {
                            var parseError = $"Tool '{toolCall.Name}' received malformed arguments: {toolCall.Arguments.Substring(0, Math.Min(toolCall.Arguments.Length, 200))}";
                            AILogger.Error(parseError);
                            return (parseError, true);
                        }
                        AILogger.Warning($"Tool '{toolCall.Name}' received concatenated JSON arguments, using first object only");
                    }
                }

                if (handler.RequiresPolling)
                    AILogger.Info($"Tool '{toolCall.Name}' running in polling mode (max {timeout:0}s)");

                var raw = await TimeoutHelper.WithTimeout(
                    token => handler.Invoke(args, token),
                    timeout, ct);

                var json = JsonConvert.SerializeObject(raw);
                AILogger.Verbose($"Tool '{toolCall.Name}' executed successfully");
                return (json, false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var error = $"Tool '{toolCall.Name}' timed out after {timeout}s";
                AILogger.Warning(error);
                return (error, true);
            }
            catch (Exception e)
            {
                var error = $"Tool '{toolCall.Name}' failed: {e.Message}";
                AILogger.Error(error);
                return (error, true);
            }
        }

        public void Dispose()
        {
            _mcpManager?.Dispose();
            _mcpManager = null;
        }

        /// <summary>
        /// 尝试从可能拼接的多个 JSON 对象中解析出第一个完整对象。
        /// 使用 JsonTextReader 逐 token 读取，遇到第一个对象结束即停止。
        /// </summary>
        private static JObject TryParseFirstJsonObject(string json)
        {
            try
            {
                using var reader = new JsonTextReader(new System.IO.StringReader(json))
                {
                    SupportMultipleContent = true
                };
                if (reader.Read())
                    return JObject.Load(reader);
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static void AccumulateUsage(TokenUsage total, TokenUsage turn)
        {
            if (turn == null) return;
            total.InputTokens += turn.InputTokens;
            total.OutputTokens += turn.OutputTokens;
        }
    }
}
