namespace UniAI
{
    /// <summary>
    /// 流式响应块
    /// </summary>
    public class AIStreamChunk
    {
        /// <summary>
        /// 增量文本
        /// </summary>
        public string DeltaText { get; set; }

        /// <summary>
        /// 是否为最后一个块
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Token 使用量（仅最后一个块有值）
        /// </summary>
        public TokenUsage Usage { get; set; }

        /// <summary>
        /// 流式中的 Tool 调用（增量累积完成后填充）
        /// </summary>
        public AIToolCall ToolCall { get; set; }
    }
}
