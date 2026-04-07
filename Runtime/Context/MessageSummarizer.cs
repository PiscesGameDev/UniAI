using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 消息摘要器 — 调用 AI 将历史消息压缩为简短摘要
    /// </summary>
    public class MessageSummarizer
    {
        private const string SUMMARY_SYSTEM_PROMPT =
            "你是一个对话摘要助手。请将以下对话历史压缩为简短摘要，保留关键信息（用户意图、重要决策、关键数据）。" +
            "摘要应简洁清晰，使用中文，不超过指定长度。只输出摘要内容，不要加任何前缀。";

        private readonly AIClient _client;

        public MessageSummarizer(AIClient client)
        {
            _client = client;
        }

        /// <summary>
        /// 将消息列表压缩为一段摘要文本
        /// </summary>
        public async UniTask<string> SummarizeAsync(
            IReadOnlyList<AIMessage> messages,
            int maxTokens = 512,
            CancellationToken ct = default)
        {
            if (messages == null || messages.Count == 0) return null;

            string formatted = FormatMessages(messages);

            var request = new AIRequest
            {
                SystemPrompt = SUMMARY_SYSTEM_PROMPT,
                Messages = new List<AIMessage>
                {
                    AIMessage.User($"请将以下对话压缩为摘要（不超过{maxTokens}个token）：\n\n{formatted}")
                },
                MaxTokens = maxTokens,
                Temperature = 0.3f
            };

            var response = await _client.SendAsync(request, ct);
            return response.IsSuccess ? response.Text?.Trim() : null;
        }

        private static string FormatMessages(IReadOnlyList<AIMessage> messages)
        {
            var sb = new StringBuilder();
            foreach (var msg in messages)
            {
                string role = msg.Role == AIRole.User ? "用户" : "助手";
                foreach (var content in msg.Contents)
                {
                    if (content is AITextContent text)
                    {
                        sb.AppendLine($"{role}: {text.Text}");
                    }
                    else if (content is AIToolUseContent toolUse)
                    {
                        sb.AppendLine($"助手: [调用工具 {toolUse.Name}]");
                    }
                    else if (content is AIToolResultContent toolResult)
                    {
                        sb.AppendLine($"工具结果: {Truncate(toolResult.Content, 200)}");
                    }
                }
            }
            return sb.ToString();
        }

        private static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "...";
        }
    }
}
