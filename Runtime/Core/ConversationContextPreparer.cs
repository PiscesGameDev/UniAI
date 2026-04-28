using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 外部上下文采集接口。Editor 或 Runtime 可注入自己的上下文来源。
    /// </summary>
    public interface IConversationContextProvider
    {
        string Collect(int slots);
    }

    public sealed class NullConversationContextProvider : IConversationContextProvider
    {
        public static readonly NullConversationContextProvider Instance = new();

        private NullConversationContextProvider() { }

        public string Collect(int slots) => null;
    }

    /// <summary>
    /// 将 ChatSession 准备为 Runner 可消费的 AIMessage 列表。
    /// </summary>
    public sealed class ConversationContextPreparer
    {
        private readonly IConversationContextProvider _contextProvider;

        public ConversationContextPreparer(IConversationContextProvider contextProvider)
        {
            _contextProvider = contextProvider ?? NullConversationContextProvider.Instance;
        }

        public async UniTask<List<AIMessage>> PrepareAsync(
            ChatSession session,
            int contextSlots,
            ConversationRuntime runtime,
            AIConfig config,
            string modelId,
            CancellationToken ct)
        {
            var messages = session.BuildAIMessages();

            var context = _contextProvider.Collect(contextSlots);
            if (!string.IsNullOrEmpty(context))
            {
                ContextPipeline.InjectContextPair(
                    messages,
                    "Unity Context",
                    context,
                    "收到上下文信息，我会结合这些信息回答你的问题。");
            }

            if (runtime?.ContextPipeline != null && config?.General?.ContextWindow != null)
            {
                messages = await runtime.ContextPipeline.ProcessAsync(
                    messages,
                    runtime.Agent?.SystemPrompt,
                    modelId,
                    config.General.ContextWindow,
                    session,
                    ct);
            }

            return messages;
        }
    }
}
