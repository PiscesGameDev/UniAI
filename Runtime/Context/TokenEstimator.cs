using System.Collections.Generic;

namespace UniAI
{
    /// <summary>
    /// Token 预估器 — 基于字符比例的轻量估算，不引入 tiktoken 依赖
    /// 中文字符按 1 token/字，英文按 4 字符/token，混合取加权
    /// </summary>
    public static class TokenEstimator
    {
        private const int MESSAGE_OVERHEAD = 4; // role + 格式开销

        /// <summary>
        /// 估算单段文本的 token 数
        /// </summary>
        public static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            int cjkChars = 0;
            int otherChars = 0;

            foreach (char c in text)
            {
                if (IsCjk(c))
                    cjkChars++;
                else
                    otherChars++;
            }

            // CJK: ~1 token/字, English: ~4 chars/token
            int tokens = cjkChars + (otherChars + 3) / 4;
            return tokens > 0 ? tokens : 1;
        }

        /// <summary>
        /// 估算完整消息列表的 token 数（含 SystemPrompt）
        /// </summary>
        public static int EstimateMessages(IReadOnlyList<AIMessage> messages, string systemPrompt = null)
        {
            int total = 0;

            if (!string.IsNullOrEmpty(systemPrompt))
                total += EstimateTokens(systemPrompt) + MESSAGE_OVERHEAD;

            foreach (var msg in messages)
            {
                total += MESSAGE_OVERHEAD;
                foreach (var content in msg.Contents)
                {
                    total += EstimateContent(content);
                }
            }

            return total;
        }

        private static int EstimateContent(AIContent content)
        {
            return content switch
            {
                AITextContent text => EstimateTokens(text.Text),
                AIToolUseContent toolUse => EstimateTokens(toolUse.Name) + EstimateTokens(toolUse.Arguments) + 10,
                AIToolResultContent toolResult => EstimateTokens(toolResult.Content) + 10,
                AIImageContent => 1000, // 图片按固定值估算
                _ => 0
            };
        }

        private static bool IsCjk(char c)
        {
            // CJK Unified Ideographs + CJK Extension A + common CJK ranges
            return c >= 0x4E00 && c <= 0x9FFF
                || c >= 0x3400 && c <= 0x4DBF
                || c >= 0xF900 && c <= 0xFAFF
                || c >= 0x3000 && c <= 0x303F  // CJK 标点
                || c >= 0xFF00 && c <= 0xFFEF;  // 全角字符
        }
    }
}
