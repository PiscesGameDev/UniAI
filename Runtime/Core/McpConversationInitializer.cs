using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 管理 Agent MCP 初始化、状态文本和 Resource 注入。
    /// </summary>
    public sealed class McpConversationInitializer
    {
        private ConversationRuntime _runtime;
        private bool _started;
        private UniTask _initTask;

        public event Action StatusChanged;

        public string Status { get; private set; }

        public void EnsureStarted(
            ConversationRuntime runtime,
            ChatOrchestratorSettings settings,
            CancellationToken ct = default)
        {
            if (_runtime != runtime)
                Reset(runtime);

            if (runtime?.AgentRunner == null || runtime.Agent?.HasMcpServers != true)
            {
                SetStatus(null);
                _initTask = UniTask.CompletedTask;
                return;
            }

            settings ??= new ChatOrchestratorSettings();
            if (!settings.McpAutoConnect)
            {
                _initTask = UniTask.CompletedTask;
                return;
            }

            if (_started)
                return;

            _started = true;
            _initTask = InitializeCoreAsync(runtime, settings, ct);
        }

        public async UniTask WaitReadyAsync()
        {
            await _initTask;
        }

        public void Reset(ConversationRuntime runtime = null)
        {
            _runtime = runtime;
            _started = false;
            _initTask = UniTask.CompletedTask;
            SetStatus(null);
        }

        private async UniTask InitializeCoreAsync(
            ConversationRuntime runtime,
            ChatOrchestratorSettings settings,
            CancellationToken ct)
        {
            SetStatus("MCP: 连接中...");

            try
            {
                var agentRunner = runtime.AgentRunner;
                await agentRunner.InitializeMcpAsync(ct);

                if (settings.McpResourceInjection
                    && agentRunner.McpManager != null
                    && runtime.ContextPipeline != null)
                {
                    foreach (var provider in agentRunner.McpManager.GetResourceProviders())
                        runtime.ContextPipeline.AddProvider(provider);
                }

                var connected = agentRunner.McpManager?.Clients.Count ?? 0;
                var total = 0;
                foreach (var cfg in runtime.Agent.McpServers)
                {
                    if (cfg != null && cfg.Enabled)
                        total++;
                }

                var summary = agentRunner.McpManager?.GetConnectionSummary() ?? "未连接";
                SetStatus($"MCP: {connected}/{total} — {summary}");
            }
            catch (Exception e)
            {
                SetStatus($"MCP: 初始化失败 — {e.Message}");
                AILogger.Warning($"MCP init failed: {e}");
            }
        }

        private void SetStatus(string status)
        {
            if (Status == status)
                return;

            Status = status;
            StatusChanged?.Invoke();
        }
    }
}
