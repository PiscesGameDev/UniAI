using System.Collections.Generic;

namespace UniAI
{
    public enum AIRole
    {
        User,
        Assistant
    }

    /// <summary>
    /// AI 消息，包含角色和内容块列表
    /// </summary>
    public class AIMessage
    {
        public AIRole Role { get; set; }
        public List<AIContent> Contents { get; set; } = new();

        /// <summary>
        /// 快捷创建纯文本用户消息
        /// </summary>
        public static AIMessage User(string text) => new()
        {
            Role = AIRole.User,
            Contents = { new AITextContent(text) }
        };

        /// <summary>
        /// 快捷创建图文用户消息
        /// </summary>
        public static AIMessage UserWithImage(string text, byte[] imageData, string mediaType = "image/png") => new()
        {
            Role = AIRole.User,
            Contents =
            {
                new AIImageContent(imageData, mediaType),
                new AITextContent(text)
            }
        };

        /// <summary>
        /// 快捷创建助手消息
        /// </summary>
        public static AIMessage Assistant(string text) => new()
        {
            Role = AIRole.Assistant,
            Contents = { new AITextContent(text) }
        };
    }
}
