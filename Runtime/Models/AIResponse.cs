using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// AI 统一响应
    /// </summary>
    public class AIResponse
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 响应文本
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string Error { get; set; }

        /// <summary>
        /// Token 使用量
        /// </summary>
        public TokenUsage Usage { get; set; }

        /// <summary>
        /// 完成原因
        /// </summary>
        public string StopReason { get; set; }

        /// <summary>
        /// 原始响应 JSON（调试用）
        /// </summary>
        public string RawResponse { get; set; }

        /// <summary>
        /// AI 请求的 Tool 调用列表
        /// </summary>
        public List<AIToolCall> ToolCalls { get; set; }

        /// <summary>
        /// 是否包含 Tool 调用
        /// </summary>
        public bool HasToolCalls => ToolCalls?.Count > 0;

        public static AIResponse Success(string text, TokenUsage usage = null, string stopReason = null, string rawResponse = null) => new()
        {
            IsSuccess = true,
            Text = text,
            Usage = usage,
            StopReason = stopReason,
            RawResponse = rawResponse
        };

        public static AIResponse Fail(string error, string rawResponse = null) => new()
        {
            IsSuccess = false,
            Error = error,
            RawResponse = rawResponse
        };
    }

    /// <summary>
    /// Token 使用量
    /// </summary>
    public class TokenUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens => InputTokens + OutputTokens;
    }
}
