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
    public class AIAgentRunner
    {
        private readonly AIClient _client;
        private readonly AgentDefinition _definition;
        private readonly Dictionary<string, AIToolAsset> _toolMap = new();
        private readonly List<AITool> _toolDefs = new();

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
        /// 非流式运行：返回最终结果
        /// </summary>
        public async UniTask<AgentResult> RunAsync(List<AIMessage> messages, CancellationToken ct = default)
        {
            var workingMessages = new List<AIMessage>(messages);
            var totalUsage = new TokenUsage();
            int maxTurns = _definition.HasTools ? _definition.MaxTurns : 1;

            for (int turn = 0; turn < maxTurns; turn++)
            {
                ct.ThrowIfCancellationRequested();

                var request = BuildRequest(workingMessages);
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
        /// 流式运行：yield AgentEvent
        /// </summary>
        public IUniTaskAsyncEnumerable<AgentEvent> RunStreamAsync(List<AIMessage> messages, CancellationToken ct = default)
        {
            // 无 Tool 时直接转换 Provider 流，避免额外的 UniTaskAsyncEnumerable.Create 嵌套
            if (!_definition.HasTools)
                return RunStreamSimple(messages, ct);

            return RunStreamWithTools(messages, ct);
        }

        /// <summary>
        /// 简单流式（无 Tool）：直接将 Provider 的 AIStreamChunk 转换为 AgentEvent，
        /// 不额外包裹 UniTaskAsyncEnumerable.Create，减少嵌套层数
        /// </summary>
        private IUniTaskAsyncEnumerable<AgentEvent> RunStreamSimple(List<AIMessage> messages, CancellationToken ct)
        {
            var request = BuildRequest(new List<AIMessage>(messages));
            return _client.StreamAsync(request, ct)
                .Select(ChunkToEvent)
                .Where(evt => evt != null);
        }

        private static AgentEvent ChunkToEvent(AIStreamChunk chunk)
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                return new AgentEvent { Type = AgentEventType.TextDelta, Text = chunk.DeltaText };

            if (chunk.IsComplete)
                return new AgentEvent { Type = AgentEventType.TurnComplete, TurnIndex = 0, Usage = chunk.Usage };

            // ToolCall chunks 不应出现在无 Tool 的 Agent 中，忽略
            return null;
        }

        /// <summary>
        /// 带 Tool 的流式运行：需要 UniTaskAsyncEnumerable.Create 包裹以实现多轮循环
        /// </summary>
        private IUniTaskAsyncEnumerable<AgentEvent> RunStreamWithTools(List<AIMessage> messages, CancellationToken ct)
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

                    var request = BuildRequest(workingMessages);
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

        private AIRequest BuildRequest(List<AIMessage> messages)
        {
            var request = new AIRequest
            {
                SystemPrompt = _definition.SystemPrompt,
                Messages = messages,
                Temperature = _definition.Temperature,
                MaxTokens = _definition.MaxTokens
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
            if (!_toolMap.TryGetValue(toolCall.Name, out var tool))
            {
                var error = $"Unknown tool: {toolCall.Name}";
                AILogger.Warning(error);
                return (error, true);
            }

            try
            {
                var result = await tool.ExecuteAsync(toolCall.Arguments, ct);
                AILogger.Verbose($"Tool '{toolCall.Name}' executed successfully");
                return (result, false);
            }
            catch (Exception e)
            {
                var error = $"Tool '{toolCall.Name}' failed: {e.Message}";
                AILogger.Error(error);
                return (error, true);
            }
        }

        private static void AccumulateUsage(TokenUsage total, TokenUsage turn)
        {
            if (turn == null) return;
            total.InputTokens += turn.InputTokens;
            total.OutputTokens += turn.OutputTokens;
        }
    }
}
