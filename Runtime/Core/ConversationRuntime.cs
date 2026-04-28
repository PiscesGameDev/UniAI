using System;

namespace UniAI
{
    /// <summary>
    /// 单次对话运行环境，持有当前 Client、Runner 和上下文管线。
    /// </summary>
    public sealed class ConversationRuntime : IDisposable
    {
        public AIClient Client { get; }
        public IConversationRunner Runner { get; }
        public ContextPipeline ContextPipeline { get; }
        public AgentDefinition Agent { get; }
        public AIAgentRunner AgentRunner { get; }
        public bool HasTools => AgentRunner?.HasTools == true;

        public string ModelId { get; internal set; }

        public ConversationRuntime(
            AIClient client,
            IConversationRunner runner,
            ContextPipeline contextPipeline,
            AgentDefinition agent,
            string modelId)
        {
            Client = client;
            Runner = runner;
            ContextPipeline = contextPipeline;
            Agent = agent;
            AgentRunner = runner as AIAgentRunner;
            ModelId = modelId;
        }

        public void Dispose()
        {
            if (Runner is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception e) { AILogger.Warning($"Dispose conversation runner failed: {e.Message}"); }
            }
        }
    }
}
