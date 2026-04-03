using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// AI 模型提供者接口
    /// </summary>
    public interface IAIProvider
    {
        /// <summary>
        /// 提供者名称（如 "Claude", "OpenAI"）
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 发送请求并获取完整响应
        /// </summary>
        UniTask<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default);

        /// <summary>
        /// 发送请求并流式获取响应
        /// </summary>
        IUniTaskAsyncEnumerable<AIStreamChunk> StreamAsync(AIRequest request, CancellationToken ct = default);
    }
}
