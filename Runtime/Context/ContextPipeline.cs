using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 上下文处理管道 — 在消息发送前自动处理上下文窗口：
    /// 1. IContextProvider 注入外部上下文（RAG）
    /// 2. TokenEstimator 估算总 token
    /// 3. 超限时 → 摘要压缩旧消息 或 截断
    /// </summary>
    public class ContextPipeline
    {
        private readonly List<IContextProvider> _providers = new();
        private readonly MessageSummarizer _summarizer;

        public ContextPipeline(AIClient client)
        {
            _summarizer = new MessageSummarizer(client);
        }

        public void AddProvider(IContextProvider provider)
        {
            if (provider != null)
                _providers.Add(provider);
        }

        public void RemoveProvider(IContextProvider provider)
        {
            _providers.Remove(provider);
        }

        /// <summary>
        /// 在消息列表头部注入一对伪造的对话（User 上下文 + Assistant 确认），
        /// 用于向模型传递摘要、RAG 上下文等带外信息。
        /// </summary>
        internal static void InjectContextPair(List<AIMessage> messages, string tag, string content, string ack)
        {
            messages.Insert(0, AIMessage.User($"[{tag}]\n{content}"));
            messages.Insert(1, AIMessage.Assistant(ack));
        }

        /// <summary>
        /// 处理消息列表，确保不超过上下文窗口限制
        /// </summary>
        /// <param name="messages">原始消息列表（会被修改）</param>
        /// <param name="systemPrompt">系统提示词（用于 token 估算）</param>
        /// <param name="modelId">模型 ID（用于查询上下文窗口大小）</param>
        /// <param name="config">上下文窗口配置</param>
        /// <param name="session">会话（用于存储/读取摘要状态）</param>
        /// <param name="ct">取消令牌</param>
        /// <returns>处理后的消息列表</returns>
        public async UniTask<List<AIMessage>> ProcessAsync(
            List<AIMessage> messages,
            string systemPrompt,
            string modelId,
            ContextWindowConfig config,
            ChatSession session = null,
            CancellationToken ct = default)
        {
            if (config == null || !config.Enabled)
                return messages;

            // 1. 注入 RAG 上下文
            await InjectContextAsync(messages, ct);

            // 2. 注入已有摘要
            if (session != null && !string.IsNullOrEmpty(session.SummaryText))
            {
                InjectSummary(messages, session.SummaryText);
            }

            // 3. 计算可用 token
            int modelContextWindow = ModelContextLimits.GetContextWindow(modelId);
            int maxTokens = config.MaxContextTokens > 0
                ? config.MaxContextTokens
                : (int)(modelContextWindow * 0.8f);
            int availableTokens = maxTokens - config.ReservedOutputTokens;

            if (availableTokens <= 0) return messages;

            // 4. 估算当前 token
            int estimatedTokens = TokenEstimator.EstimateMessages(messages, systemPrompt);

            if (estimatedTokens <= availableTokens)
            {
                UpdateSessionTokens(session, estimatedTokens);
                return messages;
            }

            // 5. 超限处理
            AILogger.Info($"Context window exceeded: {estimatedTokens}/{availableTokens} tokens, compressing...");

            if (config.EnableSummary && messages.Count > config.MinRecentMessages + 2)
            {
                messages = await CompressWithSummaryAsync(
                    messages, systemPrompt, availableTokens, config, session, ct);
            }
            else
            {
                messages = TruncateMessages(messages, systemPrompt, availableTokens, config.MinRecentMessages);
            }

            int finalTokens = TokenEstimator.EstimateMessages(messages, systemPrompt);
            UpdateSessionTokens(session, finalTokens);
            AILogger.Info($"After compression: {finalTokens}/{availableTokens} tokens, {messages.Count} messages");

            return messages;
        }

        /// <summary>
        /// 注入 RAG 上下文
        /// </summary>
        private async UniTask InjectContextAsync(List<AIMessage> messages, CancellationToken ct)
        {
            if (_providers.Count == 0 || messages.Count == 0) return;

            // 获取最后一条用户消息作为查询
            string query = null;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role != AIRole.User) continue;
                foreach (var content in messages[i].Contents)
                {
                    if (content is AITextContent text)
                    {
                        query = text.Text;
                        break;
                    }
                }
                if (query != null) break;
            }

            if (string.IsNullOrEmpty(query)) return;

            foreach (var provider in _providers)
            {
                ct.ThrowIfCancellationRequested();
                var result = await provider.RetrieveAsync(query, ct);
                if (result != null && !string.IsNullOrEmpty(result.Content) && result.Relevance > 0.3f)
                {
                    InjectContextPair(messages, "相关上下文", result.Content, "已收到相关上下文信息，我会结合这些内容回答。");
                }
            }
        }

        /// <summary>
        /// 注入已有摘要到消息列表头部
        /// </summary>
        private static void InjectSummary(List<AIMessage> messages, string summaryText)
        {
            InjectContextPair(messages, "对话摘要", summaryText, "已了解之前的对话内容，请继续。");
        }

        /// <summary>
        /// 使用摘要压缩旧消息
        /// </summary>
        private async UniTask<List<AIMessage>> CompressWithSummaryAsync(
            List<AIMessage> messages,
            string systemPrompt,
            int availableTokens,
            ContextWindowConfig config,
            ChatSession session,
            CancellationToken ct)
        {
            // 保留最近的消息
            int keepCount = config.MinRecentMessages;
            if (keepCount >= messages.Count)
                return TruncateMessages(messages, systemPrompt, availableTokens, keepCount);

            // 分离要摘要的消息和要保留的消息
            var toSummarize = messages.GetRange(0, messages.Count - keepCount);
            var toKeep = messages.GetRange(messages.Count - keepCount, keepCount);

            // 生成摘要
            string summary = await _summarizer.SummarizeAsync(toSummarize, config.SummaryMaxTokens, ct);
            if (string.IsNullOrEmpty(summary))
            {
                // 摘要失败，回退到截断
                return TruncateMessages(messages, systemPrompt, availableTokens, keepCount);
            }

            // 更新 session 摘要状态
            if (session != null)
            {
                session.SummaryText = summary;
                session.SummarizedUpToIndex += toSummarize.Count;
            }

            // 组装新消息列表：摘要 + 保留的消息
            var result = new List<AIMessage>();
            InjectContextPair(result, "对话摘要", summary, "已了解之前的对话内容，请继续。");
            result.AddRange(toKeep);

            // 如果仍然超限，继续截断
            int estimated = TokenEstimator.EstimateMessages(result, systemPrompt);
            if (estimated > availableTokens)
                return TruncateMessages(result, systemPrompt, availableTokens, keepCount);

            return result;
        }

        /// <summary>
        /// 从最早消息开始移除，保留至少 minRecentMessages 条。
        /// O(n) 预计算每条消息 token，避免每次循环重新估算整个列表。
        /// </summary>
        private static List<AIMessage> TruncateMessages(
            List<AIMessage> messages,
            string systemPrompt,
            int availableTokens,
            int minRecentMessages)
        {
            int total = TokenEstimator.EstimateTokens(systemPrompt) + 4; // system 开销
            var perMsg = new int[messages.Count];
            for (int i = 0; i < messages.Count; i++)
            {
                perMsg[i] = TokenEstimator.EstimateMessage(messages[i]);
                total += perMsg[i];
            }

            int removeCount = 0;
            int maxRemovable = messages.Count - minRecentMessages;
            while (removeCount < maxRemovable && total > availableTokens)
            {
                total -= perMsg[removeCount];
                removeCount++;
            }

            if (removeCount > 0)
                messages.RemoveRange(0, removeCount);
            return messages;
        }

        private static void UpdateSessionTokens(ChatSession session, int estimatedTokens)
        {
            if (session != null)
                session.EstimatedTokens = estimatedTokens;
        }
    }
}
