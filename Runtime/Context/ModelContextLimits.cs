using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// 内置模型上下文窗口大小映射表
    /// 通过模型名前缀匹配，返回对应的上下文窗口 token 数
    /// </summary>
    public static class ModelContextLimits
    {
        private const int DEFAULT_CONTEXT_WINDOW = 8192;

        /// <summary>
        /// 模型前缀 → 上下文窗口大小（按前缀长度降序排列以优先匹配更具体的前缀）
        /// </summary>
        private static readonly List<(string prefix, int contextWindow)> _limits = new()
        {
            // Claude
            ("claude-opus", 200000),
            ("claude-sonnet", 200000),
            ("claude-haiku", 200000),
            ("claude-3", 200000),
            ("claude", 100000),

            // OpenAI
            ("gpt-4o", 128000),
            ("gpt-4-turbo", 128000),
            ("gpt-4-1", 1047576),
            ("gpt-4.1", 1047576),
            ("o1", 200000),
            ("o3", 200000),
            ("o4", 200000),
            ("gpt-4", 8192),
            ("gpt-3.5-turbo", 16385),

            // Google Gemini
            ("gemini-2.5", 1000000),
            ("gemini-2.0", 1048576),
            ("gemini-1.5-pro", 2097152),
            ("gemini-1.5-flash", 1048576),
            ("gemini", 32768),

            // DeepSeek
            ("deepseek-chat", 64000),
            ("deepseek-reasoner", 64000),
            ("deepseek-coder", 64000),
            ("deepseek", 32768),

            // Qwen
            ("qwen-turbo", 131072),
            ("qwen-plus", 131072),
            ("qwen-max", 32768),
            ("qwen", 8192),
        };

        /// <summary>
        /// 根据模型 ID 获取上下文窗口大小
        /// </summary>
        public static int GetContextWindow(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return DEFAULT_CONTEXT_WINDOW;

            string lower = modelId.ToLowerInvariant();
            foreach (var (prefix, contextWindow) in _limits)
            {
                if (lower.StartsWith(prefix))
                    return contextWindow;
            }

            return DEFAULT_CONTEXT_WINDOW;
        }
    }
}
