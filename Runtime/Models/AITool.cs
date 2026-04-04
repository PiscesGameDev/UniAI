namespace UniAI
{
    /// <summary>
    /// Tool 定义（传给 AI 的工具描述）
    /// </summary>
    public class AITool
    {
        /// <summary>
        /// 工具名称（唯一标识）
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 工具描述（告诉 AI 何时使用该工具）
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 参数的 JSON Schema 字符串
        /// </summary>
        public string ParametersSchema { get; set; }
    }

    /// <summary>
    /// AI 返回的 Tool 调用请求
    /// </summary>
    public class AIToolCall
    {
        /// <summary>
        /// 调用 ID（用于回填结果时关联）
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 工具名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 参数 JSON 字符串
        /// </summary>
        public string Arguments { get; set; }
    }
}
