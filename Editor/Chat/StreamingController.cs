using System;
using Cysharp.Threading.Tasks;

namespace UniAI.Editor.Chat
{
    /// <summary>
    /// 流式响应控制器 — Editor 薄适配层，注入 Editor 行为到 ChatOrchestrator
    /// </summary>
    internal class StreamingController : IDisposable
    {
        private readonly ChatOrchestrator _orchestrator = new();

        // ─── 事件（透传） ───

        public event Action<bool> OnStreamingChanged
        {
            add => _orchestrator.OnStreamingChanged += value;
            remove => _orchestrator.OnStreamingChanged -= value;
        }

        public event Action OnScrollToBottom
        {
            add => _orchestrator.OnScrollToBottom += value;
            remove => _orchestrator.OnScrollToBottom -= value;
        }

        public event Action OnStateChanged
        {
            add => _orchestrator.OnStateChanged += value;
            remove => _orchestrator.OnStateChanged -= value;
        }

        // ─── 属性（透传） ───

        public bool IsStreaming => _orchestrator.IsStreaming;
        public string McpStatus => _orchestrator.McpStatus;

        // ─── Runner 管理 ───

        public StreamingController(ChatHistoryManager history)
        {
            _orchestrator.Configure(new ChatOrchestratorDependencies
            {
                ContextProvider = new EditorConversationContextProvider(),
                Persistence = new ChatHistorySessionPersistence(history),
                TitlePolicy = new FirstUserMessageTitlePolicy(),
                ToolExecutionGuardFactory = CreateToolExecutionGuard
            });
        }

        internal void EnsureRuntime(AIConfig config, ModelSelector modelSelector, AgentDefinition agent)
        {
            // 注入 Runtime 工具配置
            global::UniAI.Tools.ToolConfig.MaxOutputChars = EditorPreferences.instance.ToolMaxOutputChars;
            global::UniAI.Tools.ToolConfig.SearchMaxMatches = EditorPreferences.instance.SearchMaxMatches;
            global::UniAI.Tools.ToolCallbacks.OnAssetsModified = EditorAgentGuard.NotifyAssetsModified;

            var settings = new ChatOrchestratorSettings
            {
                ToolTimeoutSeconds = EditorPreferences.instance.ToolTimeout,
                McpAutoConnect = EditorPreferences.instance.McpAutoConnect,
                McpResourceInjection = EditorPreferences.instance.McpResourceInjection
            };

            _orchestrator.EnsureRuntime(config, modelSelector, agent, settings);
        }

        internal void UpdateModel(ModelSelector modelSelector)
        {
            _orchestrator.UpdateModel(modelSelector);
        }

        internal UniTask StreamResponseAsync(
            ChatSession session, ContextCollector.ContextSlot contextSlots,
            AIConfig config, string modelId)
        {
            return _orchestrator.StreamResponseAsync(new ChatStreamRequest
            {
                Session = session,
                ContextSlots = (int)contextSlots,
                Config = config,
                ModelId = modelId
            });
        }

        public void CancelStream() => _orchestrator.CancelStream();

        public void Dispose() => _orchestrator.Dispose();

        private static IDisposable CreateToolExecutionGuard()
        {
            var guard = new EditorAgentGuard();
            guard.Lock();
            return guard;
        }

        private sealed class EditorConversationContextProvider : IConversationContextProvider
        {
            public string Collect(int slots)
            {
                return ContextCollector.Collect((ContextCollector.ContextSlot)slots);
            }
        }
    }
}
