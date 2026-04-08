using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;

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
        private readonly Dictionary<string, AIToolAsset> _toolMap = new();
        private readonly List<AITool> _toolDefs = new();
        private McpClientManager _mcpManager;
        private bool _mcpInitialized;

        /// <summary>
        /// 是否注册了工具
        /// </summary>
        public bool HasTools => _toolDefs.Count > 0;

        /// <summary>
        /// 已注册的工具资产
        /// </summary>
        public IReadOnlyCollection<AIToolAsset> ToolAssets => _toolMap.Values;

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
        public McpConfig McpSettings { get; set; }

        public AIAgentRunner(AIClient client, AgentDefinition definition)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            if (definition.HasTools)
            {
                foreach (var tool in definition.Tools)
                {
                    if (tool == null) continue;
                    _toolMap[tool.ToolName] = tool;
                    _toolDefs.Add(tool.ToDefinition());
                }
            }
        }

        /// <summary>
        /// 异步初始化 MCP 连接：连接所有启用的 MCP Server，将其 Tools 合并到可用工具列表
        /// 没有 MCP 配置时立即返回。多次调用只会初始化一次。
        /// </summary>
        public async UniTask InitializeMcpAsync(CancellationToken ct = default)
        {
            if (_mcpInitialized) return;
            _mcpInitialized = true;

            if (!_definition.HasMcpServers) return;

            _mcpManager = new McpClientManager();

            int initTimeout = McpSettings?.InitTimeoutSeconds ?? 0;
            await _mcpManager.ConnectAllAsync(_definition.McpServers, initTimeout, ct);

            _mcpManager.ToolCallTimeoutSeconds = McpSettings?.ToolCallTimeoutSeconds ?? 0;

            foreach (var tool in _mcpManager.GetAllTools())
            {
                if (string.IsNullOrEmpty(tool.Name)) continue;

                // MCP Tool 名与本地 AIToolAsset 冲突时，本地优先
                if (_toolMap.ContainsKey(tool.Name))
                {
                    AILogger.Warning($"MCP tool '{tool.Name}' shadowed by local AIToolAsset");
                    continue;
                }
                _toolDefs.Add(tool);
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
            // 1. 本地 AIToolAsset 优先
            if (_toolMap.TryGetValue(toolCall.Name, out var tool))
                return await ExecuteLocalToolAsync(tool, toolCall, ct);

            // 2. MCP Tool
            if (_mcpManager != null && _mcpManager.HasTool(toolCall.Name))
            {
                try
                {
                    if (ToolTimeoutSeconds > 0)
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(ToolTimeoutSeconds));
                        var mcpResult = await _mcpManager.CallToolAsync(toolCall.Name, toolCall.Arguments, cts.Token);
                        AILogger.Verbose($"MCP tool '{toolCall.Name}' executed (error={mcpResult.isError})");
                        return mcpResult;
                    }
                    else
                    {
                        var mcpResult = await _mcpManager.CallToolAsync(toolCall.Name, toolCall.Arguments, ct);
                        AILogger.Verbose($"MCP tool '{toolCall.Name}' executed (error={mcpResult.isError})");
                        return mcpResult;
                    }
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

        private async UniTask<(string result, bool isError)> ExecuteLocalToolAsync(AIToolAsset tool, AIToolCall toolCall, CancellationToken ct)
        {
            try
            {
                if (ToolTimeoutSeconds > 0)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(ToolTimeoutSeconds));
                    var result = await tool.ExecuteAsync(toolCall.Arguments, cts.Token);
                    AILogger.Verbose($"Tool '{toolCall.Name}' executed successfully");
                    return (result, false);
                }
                else
                {
                    var result = await tool.ExecuteAsync(toolCall.Arguments, ct);
                    AILogger.Verbose($"Tool '{toolCall.Name}' executed successfully");
                    return (result, false);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var error = $"Tool '{toolCall.Name}' timed out after {ToolTimeoutSeconds}s";
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

        private static void AccumulateUsage(TokenUsage total, TokenUsage turn)
        {
            if (turn == null) return;
            total.InputTokens += turn.InputTokens;
            total.OutputTokens += turn.OutputTokens;
        }
    }
}
