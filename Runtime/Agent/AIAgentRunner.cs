using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// Agent 运行器门面。
    /// 负责 Tool 注册和 MCP 生命周期；多轮状态机由 AgentLoop 负责。
    /// </summary>
    public class AIAgentRunner : IConversationRunner, IDisposable
    {
        private readonly AIClient _client;
        private readonly AgentDefinition _definition;
        private readonly Dictionary<string, ToolHandlerInfo> _localHandlers = new(StringComparer.Ordinal);
        private readonly List<AITool> _toolDefs = new();
        private readonly AgentLoop _loop;
        private McpClientManager _mcpManager;
        private UniTask? _mcpInitTask;
        private bool _mcpInitialized;

        public bool HasTools => _toolDefs.Count > 0;

        public IReadOnlyCollection<ToolHandlerInfo> LocalHandlers => _localHandlers.Values;

        public McpClientManager McpManager => _mcpManager;

        public float ToolTimeoutSeconds { get; set; }

        public McpRuntimeConfig McpSettings { get; set; }

        public AIAgentRunner(AIClient client, AgentDefinition definition)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));

            if (definition.HasTools)
            {
                foreach (var handler in UniAIToolRegistry.GetHandlers(definition.ToolGroups))
                {
                    _localHandlers[handler.Name] = handler;
                    _toolDefs.Add(handler.Definition);
                }
            }

            var toolExecutor = new AgentToolExecutor(
                _localHandlers,
                () => _mcpManager,
                () => ToolTimeoutSeconds);
            _loop = new AgentLoop(_client, _definition, _toolDefs, toolExecutor);
        }

        /// <summary>
        /// Initializes MCP once and merges MCP tools into the available tool definitions.
        /// Failed initialization can be retried by calling this method again.
        /// </summary>
        public UniTask InitializeMcpAsync(CancellationToken ct = default)
        {
            if (_mcpInitialized)
                return UniTask.CompletedTask;
            if (_mcpInitTask.HasValue)
                return _mcpInitTask.Value;

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
                var initTimeout = McpSettings?.InitTimeoutSeconds ?? 0;
                await manager.ConnectAllAsync(_definition.McpServers, initTimeout, ct);

                manager.ToolCallTimeoutSeconds = McpSettings?.ToolCallTimeoutSeconds ?? 0;

                foreach (var tool in manager.GetAllTools())
                {
                    if (string.IsNullOrEmpty(tool.Name))
                        continue;

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
                _mcpInitTask = null;
                throw;
            }
        }

        public UniTask<AgentResult> RunAsync(
            List<AIMessage> messages,
            AIRequest requestOverride = null,
            CancellationToken ct = default)
        {
            return _loop.RunAsync(messages, requestOverride, ct);
        }

        public IUniTaskAsyncEnumerable<AgentEvent> RunStreamAsync(
            List<AIMessage> messages,
            AIRequest requestOverride = null,
            CancellationToken ct = default)
        {
            return _loop.RunStreamAsync(messages, requestOverride, ct);
        }

        public void Dispose()
        {
            _mcpManager?.Dispose();
            _mcpManager = null;
        }
    }
}
