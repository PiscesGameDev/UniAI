using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UniAI
{
    [Serializable]
    public class ChatMessage
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public AIRole Role;
        public string Content;
        public int InputTokens;
        public int OutputTokens;
        public long Timestamp;

        /// <summary>
        /// Tool 调用消息类型（仅当 IsToolCall 为 true 时有效）
        /// </summary>
        public bool IsToolCall;
        public string ToolUseId;
        public string ToolName;
        public string ToolArguments;
        public string ToolResult;
        public bool IsToolError;

        [NonSerialized] public bool IsStreaming;
    }

    [Serializable]
    public class ChatSession
    {
        public string Id;
        public string Title;
        public long CreatedAt;
        public long UpdatedAt;
        public string AgentId;
        public string ModelId;
        public List<ChatMessage> Messages = new();

        /// <summary>
        /// 已生成的对话摘要（持久化）
        /// </summary>
        public string SummaryText;

        /// <summary>
        /// 已摘要到的消息索引
        /// </summary>
        public int SummarizedUpToIndex;

        /// <summary>
        /// 当前预估的上下文 token 数（不持久化）
        /// </summary>
        [NonSerialized] public int EstimatedTokens;

        public static ChatSession Create(string modelId)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                Title = "新对话",
                CreatedAt = now,
                UpdatedAt = now,
                ModelId = modelId
            };
        }

        public int TotalInputTokens
        {
            get
            {
                int total = 0;
                foreach (var msg in Messages) total += msg.InputTokens;
                return total;
            }
        }

        public int TotalOutputTokens
        {
            get
            {
                int total = 0;
                foreach (var msg in Messages) total += msg.OutputTokens;
                return total;
            }
        }

        // ─── 消息转换 ───

        private static readonly Regex _dataImageRegex = new(
            @"!\[([^\]]*)\]\(data:image/[^)]+\)",
            RegexOptions.Compiled);

        /// <summary>
        /// 将 ChatMessage 列表转换为 AI 消息列表，供 Provider 使用
        /// </summary>
        public List<AIMessage> BuildAIMessages()
        {
            var messages = new List<AIMessage>();
            AIMessage pendingAssistant = null;

            foreach (var msg in Messages)
            {
                if (msg.IsStreaming && string.IsNullOrEmpty(msg.Content))
                    continue;

                if (msg.IsToolCall)
                {
                    if (pendingAssistant == null)
                    {
                        pendingAssistant = new AIMessage
                            { Role = AIRole.Assistant, Contents = new List<AIContent>() };
                        messages.Add(pendingAssistant);
                    }

                    pendingAssistant.Contents.Add(new AIToolUseContent
                    {
                        Id = msg.ToolUseId,
                        Name = msg.ToolName,
                        Arguments = msg.ToolArguments
                    });

                    if (!string.IsNullOrEmpty(msg.ToolResult))
                        messages.Add(AIMessage.ToolResult(msg.ToolUseId, msg.ToolResult, msg.IsToolError));

                    continue;
                }

                pendingAssistant = null;
                string content = msg.Content;

                // 剥离 Assistant 消息中的 base64 图片数据，替换为占位描述
                if (msg.Role == AIRole.Assistant && !string.IsNullOrEmpty(content)
                                                 && content.Contains("data:image/"))
                {
                    content = _dataImageRegex.Replace(content, m =>
                    {
                        string alt = m.Groups[1].Value;
                        return string.IsNullOrWhiteSpace(alt)
                            ? "[已生成图片]"
                            : $"[已生成图片: {alt}]";
                    });
                }

                if (msg.Role == AIRole.User)
                {
                    messages.Add(AIMessage.User(content));
                }
                else
                {
                    var assistantAiMsg = AIMessage.Assistant(content);
                    messages.Add(assistantAiMsg);
                    pendingAssistant = assistantAiMsg;
                }
            }

            return messages;
        }
    }
}
