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
        /// Provider-native reasoning 增量。仅用于协议回放，不作为普通聊天文本展示。
        /// </summary>
        public string ReasoningDelta { get; set; }

        /// <summary>
        /// Provider-native reasoning 完整内容。通常仅最后一个块有值。
        /// </summary>
        public string ReasoningContent { get; set; }

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

        /// <summary>
        /// 错误信息（流式失败时填充，与 IsComplete=true 配合使用）
        /// </summary>
        public string Error { get; set; }
    }
}
