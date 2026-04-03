namespace UniAI
{
    /// <summary>
    /// 内容块基类
    /// </summary>
    public abstract class AIContent
    {
        public abstract string Type { get; }
    }

    /// <summary>
    /// 文本内容
    /// </summary>
    public class AITextContent : AIContent
    {
        public override string Type => "text";
        public string Text { get; set; }

        public AITextContent(string text) => Text = text;
    }

    /// <summary>
    /// 图片内容（base64）
    /// </summary>
    public class AIImageContent : AIContent
    {
        public override string Type => "image";
        public byte[] Data { get; set; }
        public string MediaType { get; set; }

        public AIImageContent(byte[] data, string mediaType = "image/png")
        {
            Data = data;
            MediaType = mediaType;
        }
    }

    /// <summary>
    /// AI 发起的 Tool 调用（出现在 assistant 消息中）
    /// </summary>
    public class AIToolUseContent : AIContent
    {
        public override string Type => "tool_use";
        public string Id { get; set; }
        public string Name { get; set; }
        public string Arguments { get; set; }
    }

    /// <summary>
    /// 用户回填的 Tool 执行结果（出现在 user 消息中）
    /// </summary>
    public class AIToolResultContent : AIContent
    {
        public override string Type => "tool_result";
        public string ToolUseId { get; set; }
        public string Content { get; set; }
        public bool IsError { get; set; }
    }
}
