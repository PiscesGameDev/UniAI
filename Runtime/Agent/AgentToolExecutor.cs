using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace UniAI
{
    internal sealed class AgentToolExecutor
    {
        private readonly IReadOnlyDictionary<string, ToolHandlerInfo> _localHandlers;
        private readonly Func<McpClientManager> _mcpManagerProvider;
        private readonly Func<float> _toolTimeoutProvider;

        public AgentToolExecutor(
            IReadOnlyDictionary<string, ToolHandlerInfo> localHandlers,
            Func<McpClientManager> mcpManagerProvider,
            Func<float> toolTimeoutProvider)
        {
            _localHandlers = localHandlers ?? throw new ArgumentNullException(nameof(localHandlers));
            _mcpManagerProvider = mcpManagerProvider ?? throw new ArgumentNullException(nameof(mcpManagerProvider));
            _toolTimeoutProvider = toolTimeoutProvider ?? throw new ArgumentNullException(nameof(toolTimeoutProvider));
        }

        public async UniTask<(string result, bool isError)> ExecuteAsync(AIToolCall toolCall, CancellationToken ct)
        {
            if (_localHandlers.TryGetValue(toolCall.Name, out var handler))
                return await ExecuteLocalHandlerAsync(handler, toolCall, ct);

            var mcpManager = _mcpManagerProvider();
            if (mcpManager != null && mcpManager.HasTool(toolCall.Name))
                return await ExecuteMcpToolAsync(mcpManager, toolCall, ct);

            var error = $"Unknown tool: {toolCall.Name}";
            AILogger.Warning(error);
            return (error, true);
        }

        private async UniTask<(string result, bool isError)> ExecuteMcpToolAsync(
            McpClientManager mcpManager,
            AIToolCall toolCall,
            CancellationToken ct)
        {
            var timeout = _toolTimeoutProvider();

            try
            {
                var mcpResult = await TimeoutHelper.WithTimeout(
                    token => mcpManager.CallToolAsync(toolCall.Name, toolCall.Arguments, token),
                    timeout,
                    ct);
                AILogger.Verbose($"MCP tool '{toolCall.Name}' executed (error={mcpResult.isError})");
                return mcpResult;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var timeoutError = $"MCP tool '{toolCall.Name}' timed out after {timeout}s";
                AILogger.Warning(timeoutError);
                return (timeoutError, true);
            }
        }

        private async UniTask<(string result, bool isError)> ExecuteLocalHandlerAsync(
            ToolHandlerInfo handler,
            AIToolCall toolCall,
            CancellationToken ct)
        {
            var timeout = handler.RequiresPolling
                ? handler.MaxPollSeconds
                : _toolTimeoutProvider();

            try
            {
                var args = ToolCallArgumentSanitizer.Parse(toolCall);

                if (handler.RequiresPolling)
                    AILogger.Info($"Tool '{toolCall.Name}' running in polling mode (max {timeout:0}s)");

                var raw = await TimeoutHelper.WithTimeout(
                    token => handler.Invoke(args, token),
                    timeout,
                    ct);

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
    }
}
