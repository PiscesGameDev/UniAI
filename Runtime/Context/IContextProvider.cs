using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// RAG 上下文提供者接口 — 根据用户输入检索相关上下文
    /// </summary>
    public interface IContextProvider
    {
        /// <summary>
        /// 根据当前用户输入检索相关上下文
        /// </summary>
        UniTask<ContextResult> RetrieveAsync(string query, CancellationToken ct = default);
    }

    /// <summary>
    /// 上下文检索结果
    /// </summary>
    public class ContextResult
    {
        /// <summary>
        /// 检索到的上下文文本
        /// </summary>
        public string Content;

        /// <summary>
        /// 预估 token 数
        /// </summary>
        public int EstimatedTokens;

        /// <summary>
        /// 相关度（0-1）
        /// </summary>
        public float Relevance;
    }
}
