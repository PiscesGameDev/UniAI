using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// Agent 运行结果
    /// </summary>
    public class AgentResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 最终文本输出
        /// </summary>
        public string FinalText { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// 使用的 Tool 循环轮数
        /// </summary>
        public int TurnsUsed { get; set; }

        /// <summary>
        /// 累计 Token 用量
        /// </summary>
        public TokenUsage TotalUsage { get; set; }

        /// <summary>
        /// 完整对话历史（含 tool 交互）
        /// </summary>
        public List<AIMessage> Messages { get; set; }

        public static AgentResult Success(string text, List<AIMessage> messages, int turns, TokenUsage usage) => new()
        {
            IsSuccess = true,
            FinalText = text,
            Messages = messages,
            TurnsUsed = turns,
            TotalUsage = usage
        };

        public static AgentResult Fail(string error, List<AIMessage> messages = null, int turns = 0) => new()
        {
            IsSuccess = false,
            Error = error,
            Messages = messages,
            TurnsUsed = turns
        };
    }
}
