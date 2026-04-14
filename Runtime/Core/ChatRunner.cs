using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Linq;

namespace UniAI
{
    /// <summary>
    /// 纯 Chat 运行器 — 直接桥接 AIClient，不涉及 Tool 循环、不依赖 AgentDefinition
    /// Runtime 场景（游戏内对话）和 Editor 纯聊天模式均可使用
    /// </summary>
    public class ChatRunner : IConversationRunner
    {
        private readonly AIClient _client;

        public ChatRunner(AIClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async UniTask<AgentResult> RunAsync(
            List<AIMessage> messages,
            AIRequest requestOverride = null,
            CancellationToken ct = default)
        {
            var request = BuildRequest(messages, requestOverride);
            var response = await _client.SendAsync(request, ct);

            if (!response.IsSuccess)
                return AgentResult.Fail(response.Error, messages, 0);

            return AgentResult.Success(response.Text, messages, 1, response.Usage);
        }

        public IUniTaskAsyncEnumerable<AgentEvent> RunStreamAsync(
            List<AIMessage> messages,
            AIRequest requestOverride = null,
            CancellationToken ct = default)
        {
            var request = BuildRequest(messages, requestOverride);
            return _client.StreamAsync(request, ct)
                .Select(AgentEvent.FromChunk)
                .Where(evt => evt != null);
        }

        private static AIRequest BuildRequest(List<AIMessage> messages, AIRequest overrides)
        {
            return new AIRequest
            {
                Model = overrides?.Model,
                SystemPrompt = overrides?.SystemPrompt,
                Messages = messages,
                Temperature = overrides?.Temperature ?? 0.7f,
                MaxTokens = overrides?.MaxTokens > 0 ? overrides.MaxTokens : 4096
            };
        }
    }
}
