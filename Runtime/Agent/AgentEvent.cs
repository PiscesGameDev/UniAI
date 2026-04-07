namespace UniAI
{
    /// <summary>
    /// Agent 事件类型
    /// </summary>
    public enum AgentEventType
    {
        /// <summary>
        /// AI 输出增量文本
        /// </summary>
        TextDelta,

        /// <summary>
        /// AI 请求调用 Tool
        /// </summary>
        ToolCallStart,

        /// <summary>
        /// Tool 执行完成
        /// </summary>
        ToolCallResult,

        /// <summary>
        /// 一轮 Tool 循环完成
        /// </summary>
        TurnComplete,

        /// <summary>
        /// 错误
        /// </summary>
        Error
    }

    /// <summary>
    /// Agent 运行过程中的事件
    /// </summary>
    public class AgentEvent
    {
        public AgentEventType Type { get; set; }

        /// <summary>
        /// TextDelta: 增量文本
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// ToolCallStart: 调用信息
        /// </summary>
        public AIToolCall ToolCall { get; set; }

        /// <summary>
        /// ToolCallResult: 工具执行结果
        /// </summary>
        public string ToolResult { get; set; }

        /// <summary>
        /// ToolCallResult: 工具名称
        /// </summary>
        public string ToolName { get; set; }

        /// <summary>
        /// ToolCallResult: 是否出错
        /// </summary>
        public bool IsToolError { get; set; }

        /// <summary>
        /// TurnComplete: 轮次索引
        /// </summary>
        public int TurnIndex { get; set; }

        /// <summary>
        /// TurnComplete/Error: Token 用量
        /// </summary>
        public TokenUsage Usage { get; set; }

        /// <summary>
        /// 将流式 AIStreamChunk 转换为 AgentEvent（无 Tool 场景通用）
        /// </summary>
        internal static AgentEvent FromChunk(AIStreamChunk chunk)
        {
            if (!string.IsNullOrEmpty(chunk.DeltaText))
                return new AgentEvent { Type = AgentEventType.TextDelta, Text = chunk.DeltaText };

            if (chunk.IsComplete)
                return new AgentEvent { Type = AgentEventType.TurnComplete, TurnIndex = 0, Usage = chunk.Usage };

            return null;
        }
    }
}
