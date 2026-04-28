using System;

namespace UniAI
{
    /// <summary>
    /// 根据配置创建并缓存当前对话运行环境。
    /// </summary>
    public sealed class ConversationRuntimeFactory : IDisposable
    {
        private ConversationRuntime _runtime;
        private AIConfig _lastConfig;
        private string _lastAgentId;

        public ConversationRuntime Runtime => _runtime;

        public ConversationRuntime EnsureRuntime(
            AIConfig config,
            ModelSelector modelSelector,
            AgentDefinition agent,
            ChatOrchestratorSettings settings)
        {
            var modelId = modelSelector?.EnsureValid();

            if (string.IsNullOrEmpty(modelId))
            {
                Clear();
                return null;
            }

            if (_runtime != null && !NeedsRebuild(config, agent))
            {
                _runtime.ModelId = modelId;
                ApplySettings(_runtime, config, settings);
                return _runtime;
            }

            Clear();

            try
            {
                var client = AIClient.Create(config);
                var contextPipeline = new ContextPipeline(client);
                IConversationRunner runner;

                if (agent != null)
                {
                    var agentRunner = new AIAgentRunner(client, agent);
                    runner = agentRunner;
                }
                else
                {
                    runner = new ChatRunner(client);
                }

                _runtime = new ConversationRuntime(client, runner, contextPipeline, agent, modelId);
                ApplySettings(_runtime, config, settings);
                _lastConfig = config;
                _lastAgentId = agent?.Id;
                return _runtime;
            }
            catch (Exception e)
            {
                AILogger.Warning($"Failed to create conversation runtime: {e.Message}");
                Clear();
                return null;
            }
        }

        public void UpdateModel(ModelSelector modelSelector)
        {
            if (_runtime != null)
                _runtime.ModelId = modelSelector?.EnsureValid();
        }

        public void Clear()
        {
            _runtime?.Dispose();
            _runtime = null;
            _lastConfig = null;
            _lastAgentId = null;
        }

        public void Dispose()
        {
            Clear();
        }

        private bool NeedsRebuild(AIConfig config, AgentDefinition agent)
        {
            if (_lastConfig != config)
                return true;

            var agentId = agent?.Id;
            return agentId != _lastAgentId;
        }

        private static void ApplySettings(
            ConversationRuntime runtime,
            AIConfig config,
            ChatOrchestratorSettings settings)
        {
            if (runtime?.AgentRunner == null)
                return;

            settings ??= new ChatOrchestratorSettings();
            runtime.AgentRunner.ToolTimeoutSeconds = settings.ToolTimeoutSeconds;
            runtime.AgentRunner.McpSettings = config?.General?.Mcp;
        }
    }
}
