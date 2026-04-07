using System;

namespace UniAI
{
    /// <summary>
    /// 上下文窗口管理配置
    /// </summary>
    [Serializable]
    public class ContextWindowConfig
    {
        /// <summary>
        /// 是否启用上下文窗口管理
        /// </summary>
        public bool Enabled = true;

        /// <summary>
        /// 最大上下文 token 数，0 = 自动（模型上下文窗口的 80%）
        /// </summary>
        public int MaxContextTokens = 0;

        /// <summary>
        /// 预留给输出的 token 数
        /// </summary>
        public int ReservedOutputTokens = 4096;

        /// <summary>
        /// 截断时至少保留的最近消息数
        /// </summary>
        public int MinRecentMessages = 4;

        /// <summary>
        /// 是否启用摘要压缩
        /// </summary>
        public bool EnableSummary = true;

        /// <summary>
        /// 摘要的最大 token 数
        /// </summary>
        public int SummaryMaxTokens = 512;
    }
}
