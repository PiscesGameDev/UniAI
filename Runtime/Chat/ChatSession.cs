using System;
using System.Collections.Generic;
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
    }
}
