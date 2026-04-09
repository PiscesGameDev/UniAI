using System.Threading;
using Cysharp.Threading.Tasks;

namespace UniAI
{
    /// <summary>
    /// 生成式资产 Provider 接口。
    /// 每个实现负责调用特定的 AI 生成 API（图片/音频/3D 模型等），返回原始二进制数据。
    /// </summary>
    public interface IGenerativeAssetProvider
    {
        /// <summary>唯一标识（如 "image-openai"）</summary>
        string ProviderId { get; }

        /// <summary>展示名（如 "OpenAI DALL-E 3"）</summary>
        string DisplayName { get; }

        /// <summary>支持的资产类型</summary>
        GenerativeAssetType AssetType { get; }

        /// <summary>执行生成</summary>
        UniTask<GenerateResult> GenerateAsync(GenerateRequest request, CancellationToken ct);

        /// <summary>Provider 能力描述（给 AI 看的 JSON 友好结构）</summary>
        object GetCapabilities();
    }
}
