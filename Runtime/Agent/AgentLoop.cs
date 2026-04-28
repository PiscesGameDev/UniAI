using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;

namespace UniAI
{
    internal sealed class AgentLoop
    {
        private readonly AIClient _client;
        private readonly AgentDefinition _definition;
        private readonly IReadOnlyList<AITool> _toolDefs;
        private readonly AgentToolExecutor _toolExecutor;

        public AgentLoop(
            AIClient client,
            AgentDefinition definition,
            IReadOnlyList<AITool> toolDefs,
            AgentToolExecutor toolExecutor)
        {
            _client = client;
            _definition = definition;
            _toolDefs = toolDefs;
            _toolExecutor = toolExecutor;
        }

        public async UniTask<AgentResult> RunAsync(
            List<AIMessage> messages,
            AIRequest requestOverride,
            CancellationToken ct)
        {
            var workingMessages = new List<AIMessage>(messages);
            var totalUsage = new TokenUsage();
            var maxTurns = HasTools ? _definition.MaxTurns : 1;

            for (var turn = 0; turn < maxTurns; turn++)
            {
                ct.ThrowIfCancellationRequested();

                var request = BuildRequest(workingMessages, requestOverride);
                var response = await _client.SendAsync(request, ct);

                if (!response.IsSuccess)
                    return AgentResult.Fail(response.Error, workingMessages, turn);

                AccumulateUsage(totalUsage, response.Usage);

                if (response.HasToolCalls)
                {
                    ToolCallArgumentSanitizer.Sanitize(response.ToolCalls);
                    workingMessages.Add(AgentMessageFactory.BuildAssistantMessage(
                        response.Text,
                        response.ToolCalls,
                        response.ReasoningContent));

                    foreach (var tc in response.ToolCalls)
                    {
                        var (result, isError) = await _toolExecutor.ExecuteAsync(tc, ct);
                        workingMessages.Add(AIMessage.ToolResult(tc.Id, result, isError));
                    }

                    continue;
                }

                return AgentResult.Success(response.Text, workingMessages, turn + 1, totalUsage);
            }

            return AgentResult.Fail("Exceeded maximum turns", workingMessages, maxTurns);
        }

        public IUniTaskAsyncEnumerable<AgentEvent> RunStreamAsync(
            List<AIMessage> messages,
            AIRequest requestOverride,
            CancellationToken ct)
        {
            if (!HasTools)
                return RunStreamSimple(messages, requestOverride, ct);

            return RunStreamWithTools(messages, requestOverride, ct);
        }

        private bool HasTools => _toolDefs.Count > 0;

        private IUniTaskAsyncEnumerable<AgentEvent> RunStreamSimple(
            List<AIMessage> messages,
            AIRequest requestOverride,
            CancellationToken ct)
        {
            var request = BuildRequest(new List<AIMessage>(messages), requestOverride);
            return _client.StreamAsync(request, ct)
                .Select(AgentEvent.FromChunk)
                .Where(evt => evt != null);
        }

        private IUniTaskAsyncEnumerable<AgentEvent> RunStreamWithTools(
            List<AIMessage> messages,
            AIRequest requestOverride,
            CancellationToken ct)
        {
            return UniTaskAsyncEnumerable.Create<AgentEvent>(async (writer, token) =>
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
                var linkedToken = cts.Token;

                var workingMessages = new List<AIMessage>(messages);
                var totalUsage = new TokenUsage();

                for (var turn = 0; turn < _definition.MaxTurns; turn++)
                {
                    linkedToken.ThrowIfCancellationRequested();

                    var turnResult = await RunStreamTurnAsync(
                        workingMessages,
                        requestOverride,
                        writer,
                        linkedToken);

                    AccumulateUsage(totalUsage, turnResult.Usage);

                    if (turnResult.ToolCalls.Count > 0)
                    {
                        ToolCallArgumentSanitizer.Sanitize(turnResult.ToolCalls);
                        workingMessages.Add(AgentMessageFactory.BuildAssistantMessage(
                            turnResult.Text,
                            turnResult.ToolCalls,
                            turnResult.ReasoningContent));

                        foreach (var tc in turnResult.ToolCalls)
                            await ExecuteToolAndEmitAsync(tc, turnResult.ReasoningContent, workingMessages, writer, linkedToken);

                        await writer.YieldAsync(new AgentEvent
                        {
                            Type = AgentEventType.TurnComplete,
                            TurnIndex = turn,
                            Usage = turnResult.Usage,
                            ReasoningContent = NullIfEmpty(turnResult.ReasoningContent)
                        });

                        continue;
                    }

                    await writer.YieldAsync(new AgentEvent
                    {
                        Type = AgentEventType.TurnComplete,
                        TurnIndex = turn,
                        Text = turnResult.Text,
                        Usage = totalUsage,
                        ReasoningContent = NullIfEmpty(turnResult.ReasoningContent)
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

        private async UniTask<AgentStreamTurnResult> RunStreamTurnAsync(
            List<AIMessage> workingMessages,
            AIRequest requestOverride,
            IAsyncWriter<AgentEvent> writer,
            CancellationToken ct)
        {
            var request = BuildRequest(workingMessages, requestOverride);
            var turnResult = new AgentStreamTurnResult();

            await foreach (var chunk in _client.StreamAsync(request, ct))
            {
                if (!string.IsNullOrEmpty(chunk.Error))
                {
                    await writer.YieldAsync(new AgentEvent
                    {
                        Type = AgentEventType.Error,
                        Text = chunk.Error
                    });
                }

                if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    turnResult.Text += chunk.DeltaText;
                    await writer.YieldAsync(new AgentEvent
                    {
                        Type = AgentEventType.TextDelta,
                        Text = chunk.DeltaText
                    });
                }

                if (!string.IsNullOrEmpty(chunk.ReasoningDelta))
                    turnResult.ReasoningContent += chunk.ReasoningDelta;

                if (chunk.ToolCall != null)
                    turnResult.ToolCalls.Add(chunk.ToolCall);

                if (!chunk.IsComplete)
                    continue;

                if (chunk.Usage != null)
                    turnResult.Usage = chunk.Usage;
                if (!string.IsNullOrEmpty(chunk.ReasoningContent))
                    turnResult.ReasoningContent = chunk.ReasoningContent;
            }

            return turnResult;
        }

        private async UniTask ExecuteToolAndEmitAsync(
            AIToolCall toolCall,
            string reasoningContent,
            List<AIMessage> workingMessages,
            IAsyncWriter<AgentEvent> writer,
            CancellationToken ct)
        {
            await writer.YieldAsync(new AgentEvent
            {
                Type = AgentEventType.ToolCallStart,
                ToolCall = toolCall,
                ReasoningContent = NullIfEmpty(reasoningContent)
            });

            var (result, isError) = await _toolExecutor.ExecuteAsync(toolCall, ct);

            await writer.YieldAsync(new AgentEvent
            {
                Type = AgentEventType.ToolCallResult,
                ToolCall = toolCall,
                ToolName = toolCall.Name,
                ToolResult = result,
                IsToolError = isError
            });

            workingMessages.Add(AIMessage.ToolResult(toolCall.Id, result, isError));
        }

        private AIRequest BuildRequest(List<AIMessage> messages, AIRequest overrides = null)
        {
            var request = new AIRequest
            {
                Model = overrides?.Model,
                SystemPrompt = overrides?.SystemPrompt ?? _definition.SystemPrompt,
                Messages = messages,
                Temperature = overrides != null ? overrides.Temperature : _definition.Temperature,
                MaxTokens = overrides?.MaxTokens > 0 ? overrides.MaxTokens : _definition.MaxTokens
            };

            if (_toolDefs.Count > 0)
                request.Tools = new List<AITool>(_toolDefs);

            return request;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static void AccumulateUsage(TokenUsage total, TokenUsage turn)
        {
            if (turn == null)
                return;

            total.InputTokens += turn.InputTokens;
            total.OutputTokens += turn.OutputTokens;
        }

        private sealed class AgentStreamTurnResult
        {
            public string Text = "";
            public string ReasoningContent = "";
            public readonly List<AIToolCall> ToolCalls = new();
            public TokenUsage Usage;
        }
    }
}
