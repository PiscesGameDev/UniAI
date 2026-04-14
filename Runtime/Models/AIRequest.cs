using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// AI 统一请求
    /// </summary>
    public class AIRequest
    {
        /// <summary>
        /// 系统提示词
        /// </summary>
        public string SystemPrompt { get; set; }

        /// <summary>
        /// 消息列表（支持多轮对话）
        /// </summary>
        public List<AIMessage> Messages { get; set; } = new();

        /// <summary>
        /// 模型名称，为空则使用 Provider 默认
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// 最大输出 token 数
        /// </summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>
        /// 温度 (0.0 - 1.0)
        /// </summary>
        public float Temperature { get; set; } = 0.7f;

        /// <summary>
        /// 工具定义列表
        /// </summary>
        public List<AITool> Tools { get; set; }

        /// <summary>
        /// 工具选择策略: "auto"/"any"/"none" 或指定工具名
        /// </summary>
        public string ToolChoice { get; set; }

        /// <summary>
        /// 响应格式约束（结构化输出 / JSON Mode）
        /// </summary>
        public AIResponseFormat ResponseFormat { get; set; }
    }
}
