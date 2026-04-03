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
    }
}
