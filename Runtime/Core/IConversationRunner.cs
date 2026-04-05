using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 对话运行器抽象 — 统一 Chat（无 Tool）与 Agent（带 Tool）两种对话形态
    /// </summary>
    public interface IConversationRunner
    {
        /// <summary>
        /// 非流式运行：返回最终结果
        /// </summary>
        /// <param name="messages">对话历史</param>
        /// <param name="requestOverride">可选请求参数覆盖（SystemPrompt/Temperature/MaxTokens）</param>
        UniTask<AgentResult> RunAsync(
            List<AIMessage> messages,
            AIRequest requestOverride = null,
            CancellationToken ct = default);

        /// <summary>
        /// 流式运行：yield AgentEvent
        /// </summary>
        IUniTaskAsyncEnumerable<AgentEvent> RunStreamAsync(
            List<AIMessage> messages,
            AIRequest requestOverride = null,
            CancellationToken ct = default);
    }
}
